using System.Reflection;
using Il2CppInterop.Runtime.Injection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ToolModBepInEx;

/// <summary>
/// #20 「对着植物按 H 出现的数据窗口内容消失」修复组件。
/// 移植自参考版 REF\ToolMod\Components\PlantStatisticsModifier.cs（275 行，完整重写数据窗口）：
/// 在 PlantDataMenu（H 键数据窗口）的 GameObject 上自建 ScrollRect + 属性行，
/// 按 Plant 的公开 int/float/bool 可读写属性逐行生成「属性名 + 输入框/Toggle」，
/// 并在 Update() 中把游戏侧的值变化同步回控件 —— 彻底替换原版窗口的内容渲染路径。
///
/// 挂接：参考版只有 ClassInjector 注册（ModCore.cs:59）而没有明确的 AddComponent 挂接点
///（其 PlantDataMenuPatch 是空类）；本目标按任务约定改用现有补丁模式挂接 ——
/// 见 PatchMgr.cs 的 PlantDataMenuStartAttachPatch（PlantDataMenu.Start 的 Postfix）。
///
/// 与参考版的两处刻意差异（均为加固，正常路径行为一致）：
/// ① 两个控件预制体改为「带失败闩的懒加载」：参考版用静态字段初始化器直接
///    Resources.Load + 逐层 GetChild，一旦路径失效会在类型初始化时抛异常并毒化整个类型
///    （之后每次访问都抛 TypeInitializationException）；这里首次访问只尝试一次，
///    失败后永久返回 null 并由调用方跳过对应行，失败路径不再重试。
/// ② Start 前加 childCount 守卫，避免结构不符时每开一次窗口抛一次越界。
/// </summary>
public class PlantStatisticsModifier : MonoBehaviour
{
    public PlantStatisticsModifier() : base(ClassInjector.DerivedConstructorPointer<PlantStatisticsModifier>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    public PlantStatisticsModifier(IntPtr ptr) : base(ptr)
    {
    }

    public Plant TargetPlant => GetComponent<PlantDataMenu>().plant;

    // ── 控件预制体：带失败闩的懒加载（见类注释 ①） ──────────────────────
    private static GameObject? _inputFieldPrefab;
    private static bool _inputFieldTried;

    public static GameObject? InputFieldPrefab
    {
        get
        {
            if (!_inputFieldTried)
            {
                _inputFieldTried = true; // 失败闩：无论成败只尝试一次，失败后每次访问直接返回 null
                try
                {
                    var root = Resources.Load<GameObject>("ui\\prefabs\\UIConfigMenu");
                    if (root != null)
                        _inputFieldPrefab = root.transform.GetChild(2).GetChild(0).GetChild(0).gameObject;
                }
                catch
                {
                    _inputFieldPrefab = null;
                }
            }

            return _inputFieldPrefab;
        }
    }

    private static GameObject? _togglePrefab;
    private static bool _toggleTried;

    public static GameObject? TogglePrefab
    {
        get
        {
            if (!_toggleTried)
            {
                _toggleTried = true; // 失败闩：同上
                try
                {
                    _togglePrefab = Resources.Load<GameObject>("ui\\prefabs\\sample\\Toggle");
                }
                catch
                {
                    _togglePrefab = null;
                }
            }

            return _togglePrefab;
        }
    }

    public void Start()
    {
        // #20 结构守卫：参考版直接 transform.GetChild(1)，结构不符时每次开窗都会抛越界。
        if (transform.childCount < 2) return;

        // ── 仿 PlantDamageMenu.prefab 的 ScrollRect 结构 ────────────────
        var scrollView = new GameObject("PlantStatsScrollView");
        scrollView.transform.SetParent(transform.GetChild(1));
        var svRt = scrollView.AddComponent<RectTransform>();
        svRt.sizeDelta = new Vector2(5.5f, 5.5f);
        svRt.anchoredPosition = Vector2.zero;

        // ScrollRect
        var scrollRect = scrollView.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 50;

        // Viewport (仿 prefab: anchor=0,0 pivot=0,1 sizeDelta=0,0)
        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollView.transform);

        var vpRt = viewport.AddComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.zero;
        vpRt.pivot = new Vector2(0, 1);
        vpRt.sizeDelta = Vector2.zero;
        vpRt.anchoredPosition = Vector2.zero;

        viewport.AddComponent<Mask>();
        var vpImage = viewport.AddComponent<Image>();
        vpImage.color = new Color(0, 0, 0, 0.5f);
        vpImage.raycastTarget = true;

        // Content (仿 prefab: anchor=0,1~1,1 pivot=0,1)
        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform);

        var contentRt = content.AddComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = Vector2.one;
        contentRt.pivot = new Vector2(0, 1);
        contentRt.sizeDelta = Vector2.zero;
        contentRt.anchoredPosition = Vector2.zero;

        scrollRect.viewport = vpRt;
        scrollRect.content = contentRt;

        // ── GridLayoutGroup 直接挂 Content 上 ───────────────────────────
        // 使用 PlantDamageMenu 的已验证值（参考版口径）
        var glg = content.AddComponent<GridLayoutGroup>();
        glg.constraint = GridLayoutGroup.Constraint.Flexible;
        glg.spacing = new Vector2(0, 10);
        glg.padding = new RectOffset { left = 0, top = 0, right = 0, bottom = 0 };
        glg.childAlignment = TextAnchor.UpperLeft;
        glg.cellSize = new Vector2(100, 40);

        // ── 填充属性行 ──────────────────────────────────────────────────
        BuildUi(content.transform);

        // ── 手动设置 content 高度 (仿 prefab, 不用 ContentSizeFitter) ───
        int rows = _bindings.Count;
        if (rows > 0)
        {
            float h = rows * 40 + (rows - 1) * 10;
            contentRt.sizeDelta = new Vector2(0, h);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  UI 构建
    // ═══════════════════════════════════════════════════════════════════

    private void BuildUi(Transform contentParent)
    {
        var plant = TargetPlant;
        if (plant == null) return;

        var props = typeof(Plant).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        if (props == null || props.Length == 0) return;

        foreach (var prop in props)
        {
            var pt = prop.PropertyType;
            if (pt != typeof(int) && pt != typeof(float) && pt != typeof(bool)) continue;
            if (!prop.CanRead || !prop.CanWrite) continue;
            if (prop.GetIndexParameters().Length > 0) continue;
            if (IsUnityBaseProperty(prop)) continue;

            AddPropertyRow(contentParent, prop);
        }
    }

    private static bool IsUnityBaseProperty(PropertyInfo prop) => prop.Name switch
    {
        "name" or "hideFlags" or "enabled" or "isActiveAndEnabled" or
        "gameObject" or "transform" or "tag" or "rigidbody" or
        "rigidbody2D" or "camera" or "light" or "animation" or
        "renderer" or "audio" or "particleSystem" or "particleEmitter" or
        "collider" or "collider2D" or "hingeJoint" or "runInEditMode" or
        "hasTransform" or "canvas" or "allowPrefabMode" or "isPartOfPrefabInstance" or
        "Component" or "TheArchitect" or "WasMoved" => true,
        _ => false,
    };

    private void AddPropertyRow(Transform parent, PropertyInfo prop)
    {
        // 控件预制体不可用时整行跳过（失败闩保证不会逐行/逐帧重试加载）
        bool isBool = prop.PropertyType == typeof(bool);
        var neededPrefab = isBool ? TogglePrefab : InputFieldPrefab;
        if (neededPrefab == null) return;

        var row = new GameObject($"Row_{prop.Name}");
        row.transform.SetParent(parent);

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = true;
        hlg.childForceExpandHeight = true;
        hlg.spacing = 20;
        hlg.padding = new RectOffset { left = 0, top = 0, right = 0, bottom = 0 };

        // Label
        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(row.transform);

        var labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.text              = prop.Name;
        labelText.fontSize          = 20;
        labelText.color             = Color.white;
        labelText.alignment         = TextAlignmentOptions.Left;

        var labelLe = labelObj.AddComponent<LayoutElement>();
        labelLe.minWidth       = 40;
        labelLe.preferredWidth = 50;
        labelLe.flexibleWidth  = 0;

        // Value
        if (isBool)
        {
            var tObj = Object.Instantiate(neededPrefab, row.transform);
            if (tObj == null) return;
            tObj.name = $"Value_{prop.Name}";
            var toggle = tObj.GetComponent<Toggle>();
            if (toggle == null) return;

            try { toggle.isOn = (bool)prop.GetValue(TargetPlant)!; }
            catch { toggle.isOn = false; }

            toggle.onValueChanged.AddListener((UnityAction<bool>)(v =>
            {
                try { if (TargetPlant != null) prop.SetValue(TargetPlant, v); }
                catch { }
            }));

            _bindings.Add(new PropBinding(prop, toggle: toggle));
        }
        else
        {
            var iObj = Object.Instantiate(neededPrefab, row.transform);
            if (iObj == null) return;
            iObj.name = $"Value_{prop.Name}";
            var inputField = iObj.GetComponent<TMP_InputField>();
            if (inputField == null) return;

            try
            {
                var val = prop.GetValue(TargetPlant);
                inputField.text = val?.ToString() ?? "0";
            }
            catch { inputField.text = "0"; }

            inputField.contentType = prop.PropertyType == typeof(int)
                ? TMP_InputField.ContentType.IntegerNumber
                : TMP_InputField.ContentType.DecimalNumber;

            inputField.onValueChanged.AddListener((UnityAction<string>)(s =>
            {
                if (TargetPlant == null) return;
                try
                {
                    if (prop.PropertyType == typeof(int))
                        { if (int.TryParse(s, out var v))  prop.SetValue(TargetPlant, v); }
                    else if (prop.PropertyType == typeof(float))
                        { if (float.TryParse(s, out var v)) prop.SetValue(TargetPlant, v); }
                }
                catch { }
            }));

            var inputLe = iObj.AddComponent<LayoutElement>();
            inputLe.minWidth      = 40;
            inputLe.flexibleWidth = 1;

            _bindings.Add(new PropBinding(prop, inputField: inputField));
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  值同步（窗口存活期间每帧把游戏侧变化同步回控件）
    // ═══════════════════════════════════════════════════════════════════

    public void Update()
    {
        var plant = TargetPlant;
        if (plant == null) return;

        foreach (var b in _bindings)
        {
            try
            {
                var cur = b.prop.GetValue(plant);
                if (Equals(cur, b.lastValue)) continue;
                b.lastValue = cur;

                if (b.toggle != null)
                {
                    if (b.toggle.isOn != (bool)cur!) b.toggle.isOn = (bool)cur!;
                }
                else if (b.inputField != null)
                {
                    var str = cur?.ToString() ?? "";
                    if (b.inputField.text != str) b.inputField.text = str;
                }
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Binding 记录
    // ═══════════════════════════════════════════════════════════════════

    private readonly List<PropBinding> _bindings = new();

    private sealed class PropBinding
    {
        public readonly PropertyInfo prop;
        public readonly TMP_InputField? inputField;
        public readonly Toggle? toggle;
        public object? lastValue;

        public PropBinding(PropertyInfo prop, TMP_InputField? inputField = null, Toggle? toggle = null)
        {
            this.prop       = prop;
            this.inputField = inputField;
            this.toggle     = toggle;
        }
    }
}
