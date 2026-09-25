using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PVZRHTools;

/// <summary>
/// 自定义面板里的一项（2026-09-25「自由度排版」功能区）。
///
/// 每一项 = 目录里某个功能的**一份独立实例**（见 CustomLayoutCatalog 的设计说明）。
/// 控件在 <see cref="View"/> 里缓存：多次切换页面/列数时不重建，避免 NumericUpDown 闪烁与丢拖动状态。
/// </summary>
public partial class CustomPanelItem : ObservableObject
{
    /// <summary>目录功能 id（持久化用）。</summary>
    public string FeatureId { get; }

    /// <summary>显示名（已本地化，创建时取一次；切语言后由面板整体重建刷新）。</summary>
    public string Title { get; }

    /// <summary>来源页面名（已本地化）。</summary>
    public string SourceTitle { get; }

    /// <summary>构建好的控件实例（延迟到首次取用时构建）。</summary>
    public FrameworkElement View => _view ??= _build();

    private FrameworkElement? _view;
    private readonly Func<FrameworkElement> _build;

    /// <summary>所属面板（用于调用上移/下移/移除）。</summary>
    public CustomPanelViewModel? Owner { get; internal set; }

    public CustomPanelItem(CustomLayoutCatalog.Feature feature)
    {
        FeatureId = feature.Id;
        Title = Localize(feature.DisplayKey);
        SourceTitle = Localize(feature.SourceKey);
        _build = feature.Build;
    }

    /// <summary>找不到语言键时退回键名本身（不让界面出现空白项）。</summary>
    internal static string Localize(string key)
        => Application.Current?.TryFindResource(key) as string ?? key;
}

/// <summary>
/// 「我的面板」视图模型：收藏项集合 + 列数 + 增删排序。
/// 持久化落在 ModifierSaveModel.CustomPanelItems / CustomPanelColumns（见 ModifierViewModel.SaveModel）。
/// </summary>
public partial class CustomPanelViewModel : ObservableObject
{
    public ObservableCollection<CustomPanelItem> Items { get; } = new();

    /// <summary>面板列数：1 / 2 / 3（0 = 自动跟随窗口宽度）。</summary>
    [ObservableProperty] public partial int Columns { get; set; } = 2;

    public bool IsEmpty => Items.Count == 0;

    /// <summary>选择器已无候选（用于显示「已全部加入」提示）。</summary>
    public bool IsPickListEmpty => Available.Count == 0;

    /// <summary>尚未加入面板的可选功能（按来源分组后供选择器显示）。</summary>
    public ObservableCollection<CustomLayoutCatalog.Feature> Available { get; } = new();

    /// <summary>选择器当前选中的待添加功能。</summary>
    [ObservableProperty] public partial CustomLayoutCatalog.Feature? PendingAdd { get; set; }

    /// <summary>
    /// 搜索关键词（2026-09-25 新增：目录扩到 175+ 项后靠下拉翻页找太慢）。
    /// 过滤依据 = 功能显示名 + 来源页名，忽略大小写；每敲一个字符即时刷新候选。
    /// </summary>
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;

    partial void OnSearchTextChanged(string value) => RefreshAvailable();

    /// <summary>选择器下方的提示语：空 = 正常（有候选）；非空 = 「已全部加入」或「没有匹配的功能」。</summary>
    public string PickerHint
    {
        get
        {
            if (Available.Count > 0) return string.Empty;
            return string.IsNullOrWhiteSpace(SearchText)
                ? CustomPanelItem.Localize("Custom.Picker.Empty")
                : CustomPanelItem.Localize("Custom.Picker.NoMatch");
        }
    }

    public CustomPanelViewModel()
    {
        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            RefreshAvailable();
        };

        // ★ 构造即填充候选：没有存档时不会走 LoadFrom，若只在那里刷新，
        //   选择器会一直是空的（实测现象：面板为空却显示「已把全部功能加入面板」）。
        RefreshAvailable();
    }

    /// <summary>面板发生变化时通知宿主（用于标脏存档）。</summary>
    public Action? Changed { get; set; }

    /// <summary>载入存档里的收藏（找不到的 id 静默丢弃——目录改名/删项后不应崩）。</summary>
    public void LoadFrom(System.Collections.Generic.IEnumerable<string>? ids, int columns)
    {
        Items.Clear();
        if (ids is not null)
        {
            foreach (var id in ids)
            {
                var f = CustomLayoutCatalog.Find(id);
                if (f is null) continue;
                if (Items.Any(i => i.FeatureId == id)) continue; // 去重
                Items.Add(new CustomPanelItem(f) { Owner = this });
            }
        }

        Columns = columns is >= 1 and <= 3 ? columns : 2;
        RefreshAvailable();
    }

    /// <summary>导出为持久化列表。</summary>
    public List<string> ToIdList() => Items.Select(i => i.FeatureId).ToList();

    // ==================================================================
    // 多配置存档（2026-09-25）：命名槽位，保存/加载/删除多套面板布局
    // ==================================================================

    public ObservableCollection<string> PresetNames { get; } = new();

    /// <summary>当前选中的槽位名。</summary>
    [ObservableProperty] public partial string? SelectedPreset { get; set; }

    /// <summary>「保存为新配置」输入的名字。</summary>
    [ObservableProperty] public partial string NewPresetName { get; set; } = string.Empty;

    private List<CustomPanelPreset> _presets = new();

    /// <summary>多配置数据导出（写盘用）。</summary>
    public List<CustomPanelPreset> ExportPresets() => _presets.Select(p => new CustomPanelPreset
    {
        Name = p.Name,
        Items = new List<string>(p.Items),
        Columns = p.Columns
    }).ToList();

    private void RefreshPresetNames()
    {
        var sel = SelectedPreset;
        PresetNames.Clear();
        foreach (var p in _presets) PresetNames.Add(p.Name);
        if (sel is not null && PresetNames.Contains(sel)) SelectedPreset = sel;
        else SelectedPreset = PresetNames.Count > 0 ? PresetNames[0] : null;
        OnPropertyChanged(nameof(SelectedPreset));
    }

    [RelayCommand]
    private void SaveNewPreset()
    {
        var name = NewPresetName?.Trim() ?? string.Empty;
        if (name.Length == 0) return;

        // 同名覆盖：玩家再次保存同一名字 = 更新该套配置
        var idx = -1;
        for (var i = 0; i < _presets.Count; i++) if (_presets[i].Name == name) { idx = i; break; }
        var preset = new CustomPanelPreset { Name = name, Items = ToIdList(), Columns = Columns };
        if (idx >= 0) _presets[idx] = preset; else _presets.Add(preset);

        RefreshPresetNames();
        SelectedPreset = name;
        OnPropertyChanged(nameof(SelectedPreset));
        Changed?.Invoke();
    }

    [RelayCommand]
    private void LoadPreset()
    {
        if (SelectedPreset is not { } name) return;
        var p = _presets.FirstOrDefault(x => x.Name == name);
        if (p is null) return;

        Items.Clear();
        foreach (var id in p.Items)
        {
            var f = CustomLayoutCatalog.Find(id);
            if (f is null) continue;
            if (Items.Any(i => i.FeatureId == id)) continue;
            Items.Add(new CustomPanelItem(f) { Owner = this });
        }
        Columns = p.Columns is >= 1 and <= 3 ? p.Columns : 2;

        RefreshPresetNames();
        SelectedPreset = name;
        OnPropertyChanged(nameof(SelectedPreset));
        Changed?.Invoke(); // 把载入结果也落盘为「当前面板」
    }

    [RelayCommand]
    private void DeletePreset()
    {
        if (SelectedPreset is not { } name) return;
        _presets.RemoveAll(x => x.Name == name);
        RefreshPresetNames();
        Changed?.Invoke();
    }

    /// <summary>
    /// 载入存档（含多配置列表）。<paramref name="presets"/> 为空则保留现状。
    /// </summary>
    public void LoadPresets(IEnumerable<CustomPanelPreset>? presets)
    {
        if (presets is null) return;
        _presets = presets.ToList();
        RefreshPresetNames();
    }

    private void RefreshAvailable()
    {
        var kw = SearchText?.Trim() ?? string.Empty;
        Available.Clear();
        foreach (var f in CustomLayoutCatalog.All)
        {
            if (Items.Any(i => i.FeatureId == f.Id)) continue;
            if (kw.Length > 0 && !Matches(f, kw)) continue;
            Available.Add(f);
        }

        if (PendingAdd is not null && !Available.Contains(PendingAdd))
            PendingAdd = null;

        // 默认选中第一项：玩家打开面板后可直接点「加入面板」，少一步操作
        if (PendingAdd is null && Available.Count > 0)
            PendingAdd = Available[0];

        OnPropertyChanged(nameof(Available));
        OnPropertyChanged(nameof(IsPickListEmpty));
        OnPropertyChanged(nameof(PickerHint));
    }

    /// <summary>关键词匹配：功能名或来源页名包含即命中（忽略大小写；中文按子串直接比）。</summary>
    private static bool Matches(CustomLayoutCatalog.Feature f, string kw)
    {
        static bool Hit(string? s, string kw)
            => !string.IsNullOrEmpty(s) &&
               s.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0;
        return Hit(f.DisplayName, kw) || Hit(CustomPanelItem.Localize(f.SourceKey), kw);
    }

    [RelayCommand]
    private void AddFeature(CustomLayoutCatalog.Feature? feature)
    {
        // ★ 用命令参数（= 下拉当前选中项）而不是内部 PendingAdd：
        //   实测两处坑——① 集合刷新会把 PendingAdd 重置成 null；
        //   ② 下拉的 TwoWay 回写会在刷新后把 null 又写回来，导致「点了加入面板却什么都没发生」。
        //   参数由 XAML 直接绑 ComboBox.SelectedItem，拿到的就是玩家看到的那一项。
        if (feature is null) return;
        if (Items.Any(i => i.FeatureId == feature.Id)) return;

        Items.Add(new CustomPanelItem(feature) { Owner = this });
        RefreshAvailable();
        Changed?.Invoke();
    }
    [RelayCommand]
    private void Remove(CustomPanelItem? item)
    {
        if (item is null) return;
        Items.Remove(item);
        Changed?.Invoke();
    }

    [RelayCommand]
    private void MoveUp(CustomPanelItem? item)
    {
        if (item is null) return;
        var i = Items.IndexOf(item);
        if (i <= 0) return;
        Items.Move(i, i - 1);
        Changed?.Invoke();
    }

    [RelayCommand]
    private void MoveDown(CustomPanelItem? item)
    {
        if (item is null) return;
        var i = Items.IndexOf(item);
        if (i < 0 || i >= Items.Count - 1) return;
        Items.Move(i, i + 1);
        Changed?.Invoke();
    }

    [RelayCommand]
    private void ClearAll()
    {
        if (Items.Count == 0) return;
        Items.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// 显式保存当前面板配置（2026-09-25「保存配置」按钮）：
    /// 布局本就在每次改动时自动落盘（Changed → ModifierViewModel.PersistCustomPanel），
    /// 此方法给玩家一个明确的保存入口，行为与自动保存一致、幂等。
    /// </summary>
    public void SaveNow() => Changed?.Invoke();

    partial void OnColumnsChanged(int value) => Changed?.Invoke();
}
