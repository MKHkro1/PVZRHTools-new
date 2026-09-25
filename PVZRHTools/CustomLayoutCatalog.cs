using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using HandyControl.Controls;

namespace PVZRHTools;

/// <summary>
/// 可移动功能目录（2026-09-25 新增「自由度排版」功能区）。
///
/// 设计要点（为什么是"工厂重建"而不是"移动现有控件"）：
///   WPF 里把已实例化的控件从一个 TabItem 搬到另一个 TabItem，要经历
///   RemoveLogicalChild/AddLogicalChild，而本工程的控件绑定的是**窗口级 DataContext**（ModifierViewModel）、
///   还挂着 Click/SelectionChanged 等 code-behind 事件与 x:Name 字段引用。
///   直接搬家会出现：① 模板内元素名（PART_*）解析错乱；② x:Name 生成的字段仍指向旧实例；
///   ③ 部分 HandyControl 控件（NumericUpDown/ComboBox）重挂时丢皮肤。
///   ⇒ 定稿做法：**每个功能提供一份"构建函数"**，原页面与自定义面板各自实例化一份。
///   两份都是对同一个 VM 属性的 TwoWay 绑定 ⇒ 状态天然同步，且互不干扰（不动原页面一根汗毛）。
///
/// 作用域纪律：目录里只放**纯设置类**功能（勾选/数值/下拉），不含"作用于游戏的一次性动作按钮"
/// （如生成僵尸、清场），因为那类按钮放在自定义面板里会与玩家预期的"设置面板"语义不符。
/// </summary>
public static class CustomLayoutCatalog
{
    /// <summary>一个可移动功能。</summary>
    /// <param name="Id">稳定标识（用于持久化；**不要改已有 id**，否则老存档的收藏会失效）。</param>
    /// <param name="DisplayKey">显示名语言键。</param>
    /// <param name="SourceKey">来源页面语言键（供选择器分组与"去原页面"提示）。</param>
    /// <param name="Build">构建一份绑定到 VM 的控件。</param>
    public sealed record Feature(string Id, string DisplayKey, string SourceKey, Func<FrameworkElement> Build)
    {
        /// <summary>选择器里显示的名字（本地化；找不到键时退回键名，避免空白项）。</summary>
        public string DisplayName => Application.Current?.TryFindResource(DisplayKey) as string ?? DisplayKey;

        public override string ToString() => DisplayName;
    }

    private static readonly List<Feature> _all = new();

    public static IReadOnlyList<Feature> All => _all;

    public static Feature? Find(string id) => _all.FirstOrDefault(f => f.Id == id);

    /// <summary>按来源页面分组（选择器用）。</summary>
    public static IEnumerable<IGrouping<string, Feature>> Grouped()
        => _all.GroupBy(f => f.SourceKey);

    // ---------------------------------------------------------------------
    // 构建辅助
    // ---------------------------------------------------------------------

    /// <summary>
    /// 绑到「窗口级 ViewModel」（2026-09-25 修复：面板卡片里的控件此前用裸 Binding(path)，
    /// 会在 DataContext=CustomPanelItem 上解析失败 ⇒ 勾了不生效/按了没反应，与原页面不同步）。
    /// 显式走 窗口 DataContext：FindAncestor(Window) 在控件挂进视觉树后必然解析到主窗口，
    /// 其 DataContext 就是 ModifierViewModel —— 与原页面控件同一份属性源，天然双向同步。
    /// </summary>
    private static Binding VmBinding(string path, BindingMode mode = BindingMode.TwoWay)
        => new Binding("DataContext." + path)
        {
            Mode = mode,
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(System.Windows.Window), 1)
        };

    private static CheckBox Chk(string contentKey, string path, string? tooltipKey = null)
    {
        var cb = new CheckBox
        {
            Content = new DynamicResourceExtension(contentKey).ProvideValue(null!),
            MinHeight = 24,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 6),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        cb.SetResourceReference(Control.ForegroundProperty, "TextForegroundBrush");
        cb.SetBinding(ToggleButton_IsCheckedProperty, VmBinding(path));
        if (tooltipKey is not null)
            cb.SetResourceReference(FrameworkElement.ToolTipProperty, tooltipKey);
        return cb;
    }

    private static readonly DependencyProperty ToggleButton_IsCheckedProperty =
        System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty;

    private static NumericUpDown Num(string path, double min = 0, double max = 9999,
        int decimals = 0, double increment = 1, string? tooltipKey = null, double width = 110)
    {
        var n = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            DecimalPlaces = decimals,
            Increment = increment,
            Width = width,
            Height = 28,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 6)
        };
        n.SetBinding(NumericUpDown.ValueProperty, VmBinding(path));
        if (tooltipKey is not null)
            n.SetResourceReference(FrameworkElement.ToolTipProperty, tooltipKey);
        return n;
    }

    /// <summary>一行：[勾选开关] + [数值]，勾选控制数值的启用态（可给 enablePath）。</summary>
    private static FrameworkElement Row(FrameworkElement first, FrameworkElement second)
    {
        var sp = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        };
        sp.Children.Add(first);
        sp.Children.Add(second);
        return sp;
    }

    private static System.Windows.Controls.ComboBox Cmb(string itemsPath, string selectedPath, double width = 260,
        string? searchPath = null, bool editable = true)
    {
        var c = new System.Windows.Controls.ComboBox
        {
            SelectedValuePath = "Key",
            DisplayMemberPath = "Value",
            Width = width,
            Height = 28,
            IsEditable = editable,
            IsTextSearchEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        };
        c.SetBinding(ItemsControl.ItemsSourceProperty, VmBinding(itemsPath, BindingMode.OneWay));
        c.SetBinding(Selector_SelectedValueProperty, VmBinding(selectedPath));
        c.SetResourceReference(FrameworkElement.StyleProperty, "ComboBoxExtend");
        if (searchPath is not null)
            TextSearch.SetTextPath(c, searchPath);
        return c;
    }

    private static readonly DependencyProperty Selector_SelectedValueProperty =
        System.Windows.Controls.Primitives.Selector.SelectedValueProperty;

    private static FrameworkElement Wrap(params FrameworkElement[] children)
    {
        var wp = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var c in children) wp.Children.Add(c);
        return wp;
    }

    private static FrameworkElement Titled(string headerKey, FrameworkElement body)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var t = new TextBlock
        {
            Text = (string)Application.Current.TryFindResource(headerKey)!,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, "LabelForegroundBrush");
        sp.Children.Add(t);
        sp.Children.Add(body);
        return sp;
    }

    // ---------------------------------------------------------------------
    // 目录内容（2026-09-25 首批：常用设置类功能）
    // ---------------------------------------------------------------------

    static CustomLayoutCatalog()
    {
        // ---- 基本特性 ----
        Add("dev-mode", "Tab1.Page1.DevMode", "Tab1.Page1.Header",
            () => Chk("Tab1.Page1.DevMode", "DeveloperMode"));
        Add("glove-nocd", "Tab1.Page1.GloveNoCD", "Tab1.Page1.Header",
            () => Chk("Tab1.Page1.GloveNoCD", "GloveNoCD"));
        Add("hammer-nocd", "Tab1.Page1.HammerNoCD", "Tab1.Page1.Header",
            () => Chk("Tab1.Page1.HammerNoCD", "HammerNoCD"));
        Add("planting-nocd", "Tab1.Page1.PlantingNoCD", "Tab1.Page1.Header",
            () => Chk("Tab1.Page1.PlantingNoCD", "PlantingNoCD"));
        Add("free-planting", "Tab1.Page1.FreePlanting", "Tab1.Page1.Header",
            () => Chk("Tab1.Page1.FreePlanting", "FreePlanting"));
        Add("unlock-fusions", "Tab1.Page1.UnlockAllFusions", "Tab1.Page1.Header",
            () => Chk("Tab1.Page1.UnlockAllFusions", "UnlockAllFusions"));
        Add("wheel-nocd", "Tab1.Page1.WheelNoCD", "Tab1.Page1.Header",
            () => Chk("Tab1.Page1.WheelNoCD", "WheelNoCD"));

        // ---- 冷却锁定 ----
        Add("lock-wheel-cd", "Tab1.Page1.LockWheelCD", "Tab1.Page1.Header",
            () => Row(Chk("Tab1.Page1.LockWheelCD", "WheelFullCDEnabled"),
                      Num("WheelFullCD", 0, 9999, 1, 0.1, width: 96)));
        Add("lock-glove-cd", "Tab1.Page1.LockGloveCD", "Tab1.Page1.Header",
            () => Row(Chk("Tab1.Page1.LockGloveCD", "GloveFullCDEnabled"),
                      Num("GloveFullCD", 0, 9999, 1, 0.1, width: 96)));
        Add("lock-hammer-cd", "Tab1.Page1.LockHammerCD", "Tab1.Page1.Header",
            () => Row(Chk("Tab1.Page1.LockHammerCD", "HammerFullCDEnabled"),
                      Num("HammerFullCD", 0, 9999, 1, 0.1, width: 96)));

        // ---- 关卡特性附加 ----
        Add("column-planting", "Tab1.Page4.ColumnPlanting", "Tab1.Page4.Header",
            () => Chk("Tab1.Page4.ColumnPlanting", "ColumnPlanting"));
        Add("seed-rain", "Tab1.Page4.SeedRain", "Tab1.Page4.Header",
            () => Chk("Tab1.Page4.SeedRain", "SeedRain"));
        Add("remove-fusion-limit", "Tab1.Page4.RemoveFusionLimit", "Tab1.Page4.Header",
            () => Chk("Tab1.Page4.RemoveFusionLimit", "RemoveFusionLimit",
                      "Tab1.Page4.RemoveFusionLimit.Tooltip"));
        Add("scaredy-dream", "Tab1.Page4.ScaredyDream", "Tab1.Page4.Header",
            () => Chk("Tab1.Page4.ScaredyDream", "ScaredyDream"));

        // ---- 数值修改 ----
        Add("next-wave-cd", "Tab1.Page1.NextWaveCD", "Tab1.Page3.Header",
            () => Num("NewZombieUpdateCD", 0, 30, 1, 0.1, "Tab1.Page1.NextWaveCD.Tootip", width: 240));
        Add("game-speed", "Tab1.Page1.GameSpeed", "Tab1.Page3.Header",
            () => Chk("Tab1.Page1.GameSpeedEnabled", "GameSpeedEnabled"));

        // ---- 场地特性 ----
        Add("no-ice-road", "Tab1.Page5.NoIceRoad", "Tab1.Page5.Header",
            () => Chk("Tab1.Page5.NoIceRoad", "NoIceRoad"));
        Add("disable-ice-effect", "Tab1.Page5.DisableIceEffect", "Tab1.Page5.Header",
            () => Chk("Tab1.Page5.DisableIceEffect", "DisableIceEffect"));
        Add("item-exist-forever", "Tab1.Page5.ItemExistForever", "Tab1.Page5.Header",
            () => Chk("Tab1.Page5.ItemExistForever", "ItemExistForever"));
        Add("card-no-init", "Tab1.Page5.CardNoInit", "Tab1.Page5.Header",
            () => Chk("Tab1.Page5.CardNoInit", "CardNoInit"));
        Add("jackbox-not-explode", "Tab1.Page5.JackboxNotExplode", "Tab1.Page5.Header",
            () => Chk("Tab1.Page5.JackboxNotExplode", "JackboxNotExplode"));
        Add("garlic-day", "Tab1.Page5.GarlicDay", "Tab1.Page5.Header",
            () => Chk("Tab1.Page5.GarlicDay", "GarlicDay"));
        Add("unlimited-sunlight", "Tab1.Page5.UnlimitedSunlight", "Tab1.Page5.Header",
            () => Chk("Tab1.Page5.UnlimitedSunlight", "UnlimitedSunlight",
                      "Tab1.Page5.UnlimitedSunlight.Tooltip"));
        Add("unlock-red-card", "Tab1.Page5.UnlockRedCardPlants", "Tab1.Page5.Header",
            () => Chk("Tab1.Page5.UnlockRedCardPlants", "UnlockRedCardPlants",
                      "Tab1.Page5.UnlockRedCardPlants.Tooltip"));
        Add("pot-smashing-fix", "Tab1.Page5.PotSmashingFix", "Tab1.Page5.Header",
            () => Chk("Tab1.Page5.PotSmashingFix", "PotSmashingFix",
                      "Tab1.Page5.PotSmashingFix.Tooltip"));

        // ---- 植物特性（免疫类，最常被玩家挑出来放一起） ----
        Add("fast-shooting", "Tab1.Page6.FastShooting", "Tab1.Page6.Header",
            () => Chk("Tab1.Page6.FastShooting", "FastShooting"));
        Add("hard-plant", "Tab1.Page6.HardPlant", "Tab1.Page6.Header",
            () => Chk("Tab1.Page6.HardPlant", "HardPlant"));
        Add("curse-immunity", "Tab1.Page6.CurseImmunity", "Tab1.Page6.Header",
            () => Chk("Tab1.Page6.CurseImmunity", "CurseImmunity", "Tab1.Page6.CurseImmunity.Tooltip"));
        Add("crush-immunity", "Tab1.Page6.CrushImmunity", "Tab1.Page6.Header",
            () => Chk("Tab1.Page6.CrushImmunity", "CrushImmunity", "Tab1.Page6.CrushImmunity.Tooltip"));
        Add("trample-immunity", "Tab1.Page6.TrampleImmunity", "Tab1.Page6.Header",
            () => Chk("Tab1.Page6.TrampleImmunity", "TrampleImmunity", "Tab1.Page6.TrampleImmunity.Tooltip"));
        Add("pickaxe-immunity", "Tab1.Page6.PickaxeImmunity", "Tab1.Page6.Header",
            () => Chk("Tab1.Page6.PickaxeImmunity", "PickaxeImmunity", "Tab1.Page6.PickaxeImmunity.Tooltip"));
        Add("no-hole", "Tab1.Page6.NoHole", "Tab1.Page6.Header",
            () => Chk("Tab1.Page6.NoHole", "NoHole"));
        Add("chomper-nocd", "Tab1.Page6.ChomperNoCD", "Tab1.Page6.Header",
            () => Chk("Tab1.Page6.ChomperNoCD", "ChomperNoCD"));
        Add("cobcannon-nocd", "Tab1.Page6.CobCannonNoCD", "Tab1.Page6.Header",
            () => Chk("Tab1.Page6.CobCannonNoCD", "CobCannonNoCD"));
        Add("magnet-nut", "Tab1.Page6.MagnetNutUnlimited", "Tab1.Page6.Header",
            () => Chk("Tab1.Page6.MagnetNutUnlimited", "MagnetNutUnlimited",
                      "Tab1.Page6.MagnetNutUnlimited.Tooltip"));

        // ---- 僵尸特性 ----
        Add("zombie-damage-limit", "Tab1.Page6_5.ZombieDamageLimit200", "Tab1.Page6_5.Header",
            () => Row(Chk("Tab1.Page6_5.ZombieDamageLimit200", "ZombieDamageLimit200"),
                      Num("ZombieDamageLimitValue", 1, 99999, 0, 1)));
        Add("zombie-speed", "Tab1.Page6_5.ZombieSpeedModify", "Tab1.Page6_5.Header",
            () => Row(Chk("Tab1.Page6_5.ZombieSpeedModify", "ZombieSpeedModifyEnabled"),
                      Num("ZombieSpeedMultiplier", 0.1, 10, 2, 0.1)));
        Add("zombie-attack", "Tab1.Page6_5.ZombieAttackMultiplier", "Tab1.Page6_5.Header",
            () => Row(Chk("Tab1.Page6_5.ZombieAttackMultiplier", "ZombieAttackMultiplierEnabled"),
                      Num("ZombieAttackMultiplier", 0.1, 100, 2, 0.5)));
        Add("zombie-health", "Tab1.Page6_5.ZombieHealthMultiplier", "Tab1.Page6_5.Header",
            () => Row(Chk("Tab1.Page6_5.ZombieHealthMultiplier", "ZombieHealthMultiplierEnabled"),
                      Num("ZombieHealthMultiplier", 0.1, 10, 2, 0.1)));
        Add("zombie-immune-freeze", "Tab1.Page6_5.ZombieImmuneFreeze", "Tab1.Page6_5.ZombieImmune.GroupHeader",
            () => Chk("Tab1.Page6_5.ZombieImmuneFreeze", "ZombieImmuneFreeze"));
        Add("zombie-immune-cold", "Tab1.Page6_5.ZombieImmuneCold", "Tab1.Page6_5.ZombieImmune.GroupHeader",
            () => Chk("Tab1.Page6_5.ZombieImmuneCold", "ZombieImmuneCold"));
        Add("zombie-immune-butter", "Tab1.Page6_5.ZombieImmuneButter", "Tab1.Page6_5.ZombieImmune.GroupHeader",
            () => Chk("Tab1.Page6_5.ZombieImmuneButter", "ZombieImmuneButter"));
        Add("zombie-immune-poison", "Tab1.Page6_5.ZombieImmunePoison", "Tab1.Page6_5.ZombieImmune.GroupHeader",
            () => Chk("Tab1.Page6_5.ZombieImmunePoison", "ZombieImmunePoison"));
        Add("zombie-immune-mind", "Tab1.Page6_5.ZombieImmuneMindControl", "Tab1.Page6_5.ZombieImmune.GroupHeader",
            () => Chk("Tab1.Page6_5.ZombieImmuneMindControl", "ZombieImmuneMindControl"));
        Add("zombie-immune-devour", "Tab1.Page6_5.ZombieImmuneDevour", "Tab1.Page6_5.ZombieImmune.GroupHeader",
            () => Chk("Tab1.Page6_5.ZombieImmuneDevour", "ZombieImmuneDevour"));

        // ---- 其他特性（高频开关） ----
        Add("auto-cut-fruit", "Tab1.Page7.AutoCutFruit", "Tab1.Page7.Header",
            () => Chk("Tab1.Page7.AutoCutFruit", "AutoCutFruit"));
        Add("random-card", "Tab1.Page7.RandomCard", "Tab1.Page7.Header",
            () => Chk("Tab1.Page7.RandomCard", "RandomCard"));
        Add("column-glove", "Tab1.Page7.ColumnGlove", "Tab1.Page7.Header",
            () => Chk("Tab1.Page7.ColumnGlove", "ColumnGlove"));
        Add("unlimited-card-slots", "Tab1.Page7.UnlimitedCardSlots", "Tab1.Page7.Header",
            () => Chk("Tab1.Page7.UnlimitedCardSlots", "UnlimitedCardSlots",
                      "Tab1.Page7.UnlimitedCardSlots.Tooltip"));
        Add("auto-rhythm", "Tab1.Page7.AutoRhythmGame", "Tab1.Page7.Header",
            () => Chk("Tab1.Page7.AutoRhythmGame", "AutoRhythmGame", "Tab1.Page7.AutoRhythmGame.Tooltip"));
        Add("star-up-buff", "Tab1.Page7.StarUpBuff", "Tab1.Page7.Header",
            () => Chk("Tab1.Page7.StarUpBuff", "StarUpBuff", "Tab1.Page7.StarUpBuff.Tooltip"));
        Add("enable-all-cards", "Tab1.Page7.EnableAllCards", "Tab1.Page7.Header",
            () => Chk("Tab1.Page7.EnableAllCards", "EnableAllCards", "Tab1.Page7.EnableAllCards.Tooltip"));

        // ---- 游戏状态调整（锁阳光/锁钱：纯设置类） ----
        Add("lock-sun", "Tab2.Page2.LockSun", "Tab2.Page2.Header",
            () => Chk("Tab2.Page2.LockSun", "LockSun"));
        Add("lock-money", "Tab2.Page2.LockMoney", "Tab2.Page2.Header",
            () => Chk("Tab2.Page2.LockMoney", "LockMoney"));
        Add("stop-summon", "Tab2.Page2.StopSummon", "Tab2.Page2.Header",
            () => Chk("Tab2.Page2.StopSummon", "StopSummon"));
        Add("no-fail", "Tab2.Page2.NoFail", "Tab2.Page2.Header",
            () => Chk("Tab2.Page2.NoFail", "NoFail"));
        Add("unlimited-refresh", "Tab2.Page2.UnlimitedRefresh", "Tab2.Page2.Header",
            () => Chk("Tab2.Page2.UnlimitedRefresh", "UnlimitedRefresh"));
        Add("unlimited-score", "Tab2.Page2.UnlimitedScore", "Tab2.Page2.Header",
            () => Chk("Tab2.Page2.UnlimitedScore", "UnlimitedScore"));

        // ---- 诸神进化（高频开关） ----
        Add("god-unlimited-refresh", "Tab13.UnlimitedRefresh", "Tab13.Section.Refresh",
            () => Chk("Tab13.UnlimitedRefresh", "GodEvolutionUnlimitedRefresh"));
        Add("god-free-quality", "Tab13.FreeUpgradeQuality", "Tab13.Section.Refresh",
            () => Chk("Tab13.FreeUpgradeQuality", "GodEvolutionFreeUpgradeQuality"));
        Add("god-super-upgrade", "Tab13.SuperUpgrade", "Tab13.Section.Attributes",
            () => Chk("Tab13.SuperUpgrade", "GodEvolutionSuperUpgrade"));
        Add("god-uncrashable", "Tab13.Uncrashable", "Tab13.Section.Attributes",
            () => Chk("Tab13.Uncrashable", "GodEvolutionUncrashable"));
        Add("god-lucky", "Tab13.Lucky", "Tab13.Section.Attributes",
            () => Row(Chk("Tab13.Lucky", "GodEvolutionLuckyEnabled"),
                      Num("GodEvolutionLucky", -9999, 9999, 2, 0.5, width: 100)));
        Add("god-difficulty", "Tab13.Difficulty", "Tab13.Section.Attributes",
            () => Row(Chk("Tab13.Difficulty", "GodEvolutionDifficultyEnabled"),
                      Num("GodEvolutionDifficulty", 0, 99, 0, 1, width: 80)));

        // ---- 杂项 ----
        Add("topmost-sprite", "Tab7.TopMostSprite", "Tab7.Section.General",
            () => Chk("Tab7.TopMostSprite", "TopMostSprite"));
        Add("enable-animations", "Tab7.EnableAnimations", "Tab7.Section.General",
            () => Chk("Tab7.EnableAnimations", "EnableAnimations", "Tab7.EnableAnimations.Tooltip"));

        // ★ 品质权重组合块：勾选 + 普通/白银/黄金/钻石四个权重（四控件一行放不下，两行布局）
        Add("god-quality-weights", "Tab13.QualityWeightEnabled", "Tab13.Section.Quality",
            QualityWeights);

        // ==================================================================
        // 扩容批次（2026-09-25「支持修改器基本所有功能」，共 109 条，由
        // .tools\gen_catalog.ps1 按 MainWindow.xaml 文档顺序扫描生成：
        // 复选框 45 + 勾选数值配对 11 + 独立数值 8 + 命令按钮 46。
        // 排除项：依赖相邻输入框/下拉才有意义的组合按钮（写入阵型/生成类/类型选择）、
        //         面板自身的编排命令、一次性数值输入（血量/子弹/盲盒/礼盒锁定）。
        // ==================================================================
        Add("game-speed-value", "Tab1.Page1.GameSpeed", "Tab1.Page3.Header", () => Num("GameSpeed"));
        Add("unlock-all-plants", "Tab1.Page1.UnlockAllPlants", "Tab1.Page3.Header", () => Btn("Tab1.Page1.UnlockAllPlants", "UnlockAllPlants"));
        Add("row", "Tab2.Page1.Row", "Tab2.Page1.Header", () => Num("Row"));
        Add("col", "Tab2.Page1.Col", "Tab2.Page1.Header", () => Num("Col"));
        Add("times", "Tab2.Page1.RepeatTimes", "Tab2.Page1.Header", () => Num("Times"));
        Add("pv-p-pot-range", "Tab2.Page1.PvPPotRange", "Tab2.Page1.Header", () => Chk("Tab2.Page1.PvPPotRange", "PvPPotRange"));
        Add("is-mind-ctrl", "Tab2.Page1.IsMindCtrl", "Tab2.Page1.Header", () => Chk("Tab2.Page1.IsMindCtrl", "IsMindCtrl"));
        Add("abyss-cheat", "Tab0.AbyssCheat", "Tab2.Page1.Header", () => Btn("Tab0.AbyssCheat", "AbyssCheat"));
        Add("load-custom-plant-data", "Tab0.LoadCustomPlantData", "Tab2.Page1.Header", () => Btn("Tab0.LoadCustomPlantData", "LoadCustomPlantData"));
        Add("sun", "Tab2.Page2.ChangeSun", "Tab2.Page2.Header", () => Btn("Tab2.Page2.ChangeSun", "Sun"));
        Add("new-sun", "Tab2.Page2.ChangeSun", "Tab2.Page2.Header", () => Num("NewSun"));
        Add("new-money", "Tab2.Page2.ChangeMoney", "Tab2.Page2.Header", () => Num("NewMoney"));
        Add("next-wave", "Tab2.Page2.NextWave", "Tab2.Page2.Header", () => Btn("Tab2.Page2.NextWave", "NextWave"));
        Add("clear-all-plants", "Tab2.Page2.ClearAllPlants", "Tab2.Page2.Header", () => Btn("Tab2.Page2.ClearAllPlants", "ClearAllPlants"));
        Add("kill-all-zombies", "Tab2.Page2.KillAllZombies", "Tab2.Page2.Header", () => Btn("Tab2.Page2.KillAllZombies", "KillAllZombies"));
        Add("clear-ice-roads", "Tab2.Page2.ClearIce", "Tab2.Page2.Header", () => Btn("Tab2.Page2.ClearIce", "ClearIceRoads"));
        Add("mind-ctrl", "Tab2.Page2.MindCtrlAll", "Tab2.Page2.Header", () => Btn("Tab2.Page2.MindCtrlAll", "MindCtrl"));
        Add("clear-all-holes", "Tab2.Page2.ClearAllHoles", "Tab2.Page2.Header", () => Btn("Tab2.Page2.ClearAllHoles", "ClearAllHoles"));
        Add("set-award", "Tab2.Page2.SetAward", "Tab2.Page2.Header", () => Btn("Tab2.Page2.SetAward", "SetAward"));
        Add("destroy-award", "Tab2.Page2.DestroyAward", "Tab2.Page2.Header", () => Btn("Tab2.Page2.DestroyAward", "DestroyAward"));
        Add("cancel-game-lose", "Tab0.CancelGameLose", "Tab2.Page2.Header", () => Btn("Tab0.CancelGameLose", "CancelGameLose"));
        Add("start-mower", "Tab2.Page2.StartMower", "Tab2.Page2.Header", () => Btn("Tab2.Page2.StartMower", "StartMower"));
        Add("flag-wave-buff-enabled", "Tab2.Page2.FlagWaveBuffEnabled", "Tab2.Page2.Header", () => Chk("Tab2.Page2.FlagWaveBuffEnabled", "FlagWaveBuffEnabled"));
        Add("convey-belt-modify", "Tab2.Page1.ConveyBeltModify", "Tab2.Page2.Header", () => Chk("Tab2.Page1.ConveyBeltModify", "ConveyBeltModify"));
        Add("super-present", "Tab1.Page2.SuperPresent", "Tab1.Page2.Header", () => Chk("Tab1.Page2.SuperPresent", "SuperPresent"));
        Add("ultimate-ramdom-zombie", "Tab1.Page2.UltimateRamdomZombie", "Tab1.Page2.Header", () => Chk("Tab1.Page2.UltimateRamdomZombie", "UltimateRamdomZombie"));
        Add("present-fast-open", "Tab1.Page2.PresentFastOpen", "Tab1.Page2.Header", () => Chk("Tab1.Page2.PresentFastOpen", "PresentFastOpen"));
        Add("immune-force-deduct", "Tab1.Page6.ImmuneForceDeduct", "Tab1.Page6.Header", () => Chk("Tab1.Page6.ImmuneForceDeduct", "ImmuneForceDeduct"));
        Add("undead-bullet", "Tab1.Page6.UndeadBullet", "Tab1.Page6.Header", () => Chk("Tab1.Page6.UndeadBullet", "UndeadBullet"));
        Add("old-obsidian-bullet", "Tab1.Page6.OldObsidianBullet", "Tab1.Page6.Header", () => Chk("Tab1.Page6.OldObsidianBullet", "OldObsidianBullet"));
        Add("dev-lour", "Tab1.Page6.DevLour", "Tab1.Page6.Header", () => Chk("Tab1.Page6.DevLour", "DevLour"));
        Add("ultimate-super-gatling", "Tab1.Page6.UltimateSuperGatling", "Tab1.Page6.Header", () => Chk("Tab1.Page6.UltimateSuperGatling", "UltimateSuperGatling"));
        Add("hypono-emperor-no-cd", "Tab1.Page6.HyponoEmperorNoCD", "Tab1.Page6.Header", () => Chk("Tab1.Page6.HyponoEmperorNoCD", "HyponoEmperorNoCD"));
        Add("mine-no-cd", "Tab1.Page6.MineNoCD", "Tab1.Page6.Header", () => Chk("Tab1.Page6.MineNoCD", "MineNoCD"));
        Add("plant-upgrade", "Tab1.Page6.PlantUpgrade", "Tab1.Page6.Header", () => Chk("Tab1.Page6.PlantUpgrade", "PlantUpgrade"));
        Add("super-star-no-cd", "Tab1.Page6.SuperStarNoCD", "Tab1.Page6.Header", () => Chk("Tab1.Page6.SuperStarNoCD", "SuperStarNoCD"));
        Add("obtain-all-plant-skins", "Tab1.Page6.ObtainAllPlantSkins", "Tab1.Page6.Header", () => Btn("Tab1.Page6.ObtainAllPlantSkins", "ObtainAllPlantSkins"));
        Add("apply-all-plant-skins", "Tab1.Page6.ApplyAllPlantSkins", "Tab1.Page6.Header", () => Btn("Tab1.Page6.ApplyAllPlantSkins", "ApplyAllPlantSkins"));
        Add("plant-speed-multiplier", "Tab1.Page6.PlantSpeedMultiplier", "Tab1.Page6.Header", () => Row(Chk("Tab1.Page6.PlantSpeedMultiplier", "PlantSpeedMultiplierEnabled"), Num("PlantSpeedMultiplier")));
        Add("apply-plant-speed-ratio", "Tab1.Page6.ApplyPlantSpeedRatio", "Tab1.Page6.Header", () => Btn("Tab1.Page6.ApplyPlantSpeedRatio", "ApplyPlantSpeedRatio"));
        Add("plant-attack-multiplier", "Tab1.Page6.PlantAttackMultiplier", "Tab1.Page6.Header", () => Row(Chk("Tab1.Page6.PlantAttackMultiplier", "PlantAttackMultiplierEnabled"), Num("PlantAttackMultiplier")));
        Add("apply-plant-attack-ratio", "Tab1.Page6.ApplyPlantAttackRatio", "Tab1.Page6.Header", () => Btn("Tab1.Page6.ApplyPlantAttackRatio", "ApplyPlantAttackRatio"));
        Add("plant-health-multiplier", "Tab1.Page6.PlantHealthMultiplier", "Tab1.Page6.Header", () => Row(Chk("Tab1.Page6.PlantHealthMultiplier", "PlantHealthMultiplierEnabled"), Num("PlantHealthMultiplier")));
        Add("apply-plant-health-ratio", "Tab1.Page6.ApplyPlantHealthRatio", "Tab1.Page6.Header", () => Btn("Tab1.Page6.ApplyPlantHealthRatio", "ApplyPlantHealthRatio"));
        Add("zombie-bullet-reflect-chance", "Tab1.Page6_5.ZombieBulletReflect", "Tab1.Page6_5.Header", () => Row(Chk("Tab1.Page6_5.ZombieBulletReflect", "ZombieBulletReflectEnabled"), Num("ZombieBulletReflectChance")));
        Add("zombie-revive-debuff-chance", "Tab1.Page6_5.ZombieReviveDebuffCustom", "Tab1.Page6_5.Header", () => Row(Chk("Tab1.Page6_5.ZombieReviveDebuffCustom", "ZombieReviveDebuffCustomEnabled"), Num("ZombieReviveDebuffChance")));
        Add("zombie-free-revive-chance", "Tab1.Page6_5.ZombieFreeRevive", "Tab1.Page6_5.Header", () => Row(Chk("Tab1.Page6_5.ZombieFreeRevive", "ZombieFreeReviveEnabled"), Num("ZombieFreeReviveChance")));
        Add("zombie-status-coexist", "Tab1.Page6_5.ZombieStatusCoexist", "Tab1.Page6_5.Header", () => Chk("Tab1.Page6_5.ZombieStatusCoexist", "ZombieStatusCoexist"));
        Add("zombie-immune-jalaed", "Tab1.Page6_5.ZombieImmuneJalaed", "Tab1.Page6_5.ZombieImmune.GroupHeader", () => Chk("Tab1.Page6_5.ZombieImmuneJalaed", "ZombieImmuneJalaed"));
        Add("zombie-immune-embered", "Tab1.Page6_5.ZombieImmuneEmbered", "Tab1.Page6_5.ZombieImmune.GroupHeader", () => Chk("Tab1.Page6_5.ZombieImmuneEmbered", "ZombieImmuneEmbered"));
        Add("zombie-immune-knockback", "Tab1.Page6_5.ZombieImmuneKnockback", "Tab1.Page6_5.ZombieImmune.GroupHeader", () => Chk("Tab1.Page6_5.ZombieImmuneKnockback", "ZombieImmuneKnockback"));
        Add("zombie-health-ratio", "Tab1.Page6_5.ZombieHealthMultiplier", "Tab1.Page6_5.ZombieImmune.GroupHeader", () => Num("ZombieHealthRatio"));
        Add("apply-zombie-health-ratio", "Tab1.Page6_5.ApplyZombieHealthRatio", "Tab1.Page6_5.ZombieImmune.GroupHeader", () => Btn("Tab1.Page6_5.ApplyZombieHealthRatio", "ApplyZombieHealthRatio"));
        Add("random-bullet", "Tab1.Page7.RandomBullet", "Tab1.Page7.Header", () => Chk("Tab1.Page7.RandomBullet", "RandomBullet"));
        Add("hard-bullet", "Tab1.Page7.HardBullet", "Tab1.Page7.Header", () => Chk("Tab1.Page7.HardBullet", "HardBullet"));
        Add("plants-all-upgrade", "Tab1.Page7.PlantsAllUpgrade", "Tab1.Page7.Header", () => Chk("Tab1.Page7.PlantsAllUpgrade", "PlantsAllUpgrade"));
        Add("plants-all-star-up", "Tab1.Page7.PlantsAllStarUp", "Tab1.Page7.Header", () => Chk("Tab1.Page7.PlantsAllStarUp", "PlantsAllStarUp"));
        Add("unlock-all-almanac", "Tab1.Page7.UnlockAllAlmanac", "Tab1.Page7.Header", () => Btn("Tab1.Page7.UnlockAllAlmanac", "UnlockAllAlmanac"));
        Add("clear-all-bullets", "Tab1.Page7.ClearAllBullets", "Tab1.Page7.Header", () => Btn("Tab1.Page7.ClearAllBullets", "ClearAllBullets"));
        Add("remove-all-graves", "Tab1.Page7.RemoveAllGraves", "Tab1.Page7.Header", () => Btn("Tab1.Page7.RemoveAllGraves", "RemoveAllGraves"));
        Add("kill-non-mind-controlled-zombies", "Tab1.Page7.KillNonMindControlledZombies", "Tab1.Page7.Header", () => Btn("Tab1.Page7.KillNonMindControlledZombies", "KillNonMindControlledZombies"));
        Add("kill-mind-controlled-zombies", "Tab1.Page7.KillMindControlledZombies", "Tab1.Page7.Header", () => Btn("Tab1.Page7.KillMindControlledZombies", "KillMindControlledZombies"));
        Add("kill-zombies-on-row", "Tab1.Page7.KillZombiesOnRow", "Tab1.Page7.Header", () => Btn("Tab1.Page7.KillZombiesOnRow", "KillZombiesOnRow"));
        Add("apply-abyss-tickets", "Tab1.Page7.ApplyAbyssTickets", "Tab1.Page7.Header", () => Btn("Tab1.Page7.ApplyAbyssTickets", "ApplyAbyssTickets"));
        Add("spawn-pet-gargantuar", "Tab1.Page7.SpawnPetGargantuar", "Tab1.Page7.Header", () => Btn("Tab1.Page7.SpawnPetGargantuar", "SpawnPetGargantuar"));
        Add("spawn-pet-football", "Tab1.Page7.SpawnPetFootball", "Tab1.Page7.Header", () => Btn("Tab1.Page7.SpawnPetFootball", "SpawnPetFootball"));
        Add("spawn-pet-snow-boss", "Tab1.Page7.SpawnPetSnowBoss", "Tab1.Page7.Header", () => Btn("Tab1.Page7.SpawnPetSnowBoss", "SpawnPetSnowBoss"));
        Add("spawn-pet-jackbox", "Tab1.Page7.SpawnPetJackbox", "Tab1.Page7.Header", () => Btn("Tab1.Page7.SpawnPetJackbox", "SpawnPetJackbox"));
        Add("spawn-pet-drown", "Tab1.Page7.SpawnPetDrown", "Tab1.Page7.Header", () => Btn("Tab1.Page7.SpawnPetDrown", "SpawnPetDrown"));
        Add("spawn-pet-horse", "Tab1.Page7.SpawnPetHorse", "Tab1.Page7.Header", () => Btn("Tab1.Page7.SpawnPetHorse", "SpawnPetHorse"));
        Add("spawn-pet-imp", "Tab1.Page7.SpawnPetImp", "Tab1.Page7.Header", () => Btn("Tab1.Page7.SpawnPetImp", "SpawnPetImp"));
        Add("spawn-pet-kirov", "Tab1.Page7.SpawnPetKirov", "Tab1.Page7.Header", () => Btn("Tab1.Page7.SpawnPetKirov", "SpawnPetKirov"));
        Add("set-star-adv-star", "Tab1.Page7.SetStarAdvStar", "Tab1.Page7.Header", () => Btn("Tab1.Page7.SetStarAdvStar", "SetStarAdvStar"));
        Add("set-star-adv-star-hard", "Tab1.Page7.SetStarAdvStarHard", "Tab1.Page7.Header", () => Btn("Tab1.Page7.SetStarAdvStarHard", "SetStarAdvStarHard"));
        Add("star-adv-free-buff", "Tab1.Page7.StarAdvFreeBuff", "Tab1.Page7.Header", () => Chk("Tab1.Page7.StarAdvFreeBuff", "StarAdvFreeBuff"));
        Add("gao-shu-mode", "Tab2.Page1.GaoShuMode", "Tab2.Header", () => Chk("Tab2.Page1.GaoShuMode", "GaoShuMode"));
        Add("clear-on-writing-field", "Tab2.Page1.ClearOnWritingField", "Tab2.Page1.PlantLineup", () => Chk("Tab2.Page1.ClearOnWritingField", "ClearOnWritingField"));
        Add("clear-on-writing-zombies", "Tab2.Page1.ClearOnWritingZombies", "Tab2.Page1.ZombieLineUp", () => Chk("Tab2.Page1.ClearOnWritingZombies", "ClearOnWritingZombies"));
        Add("clear-on-writing-mix", "Tab2.Page1.ClearOnWritingMix", "Tab2.Page1.MixLineUp", () => Chk("Tab2.Page1.ClearOnWritingMix", "ClearOnWritingMix"));
        Add("clear-on-writing-vases", "Tab2.Page1.ClearOnWritingVases", "Tab2.Page1.VaseLineUp", () => Chk("Tab2.Page1.ClearOnWritingVases", "ClearOnWritingVases"));
        Add("zombie-sea-enabled", "Tab2.Page3.ZombieSeaEnabled", "Tab2.Page3.Header", () => Chk("Tab2.Page3.ZombieSeaEnabled", "ZombieSeaEnabled"));
        Add("zombie-sea-cd", "Tab2.Page3.FpCreate", "Tab2.Page3.Header", () => Num("ZombieSeaCD"));
        Add("zombie-sea-low-enabled", "Tab2.Page3.ZombieSeaLowEnabled", "Tab2.Page3.Header", () => Chk("Tab2.Page3.ZombieSeaLowEnabled", "ZombieSeaLowEnabled"));
        Add("set-zombie-idle", "Tab2.SetZombieIdle", "Tab2.Page3.Header", () => Btn("Tab2.SetZombieIdle", "SetZombieIdle"));
        Add("need-save", "Tab7.SaveAndResume", "Tab7.Section.General", () => Chk("Tab7.SaveAndResume", "NeedSave"));
        Add("kill-upgrade", "Tab8.KillUpgrade", "Tab8.Header", () => Chk("Tab8.KillUpgrade", "KillUpgrade"));
        Add("random-upgrade-mode", "Tab1.Page7.RandomUpgradeMode", "Tab8.Header", () => Chk("Tab1.Page7.RandomUpgradeMode", "RandomUpgradeMode"));
        Add("mn-entry-enabled", "Tab9.PlantEntry.MNEntry", "Tab9.PlantEntry.Header", () => Chk("Tab9.PlantEntry.MNEntry", "MNEntryEnabled"));
        Add("manual-snapshot", "Tab12.ManualSnapshot", "Tab12.Header", () => Btn("Tab12.ManualSnapshot", "ManualSnapshot"));
        Add("restore-last-snapshot", "Tab12.RestoreLast", "Tab12.Header", () => Btn("Tab12.RestoreLast", "RestoreLastSnapshot"));
        Add("refresh-snapshot-info", "Tab12.RefreshSnapshotInfo", "Tab12.Header", () => Btn("Tab12.RefreshSnapshotInfo", "RefreshSnapshotInfo"));
        Add("god-evolution-refresh-count", "Tab13.RefreshCount", "Tab13.Section.Refresh", () => Row(Chk("Tab13.RefreshCount", "GodEvolutionRefreshCountEnabled"), Num("GodEvolutionRefreshCount")));
        Add("god-evolution-max-plant-count", "Tab13.MaxPlantCount", "Tab13.Section.Attributes", () => Row(Chk("Tab13.MaxPlantCount", "GodEvolutionMaxPlantCountEnabled"), Num("GodEvolutionMaxPlantCount")));
        Add("god-evolution-option-count", "Tab13.OptionCount", "Tab13.Section.Attributes", () => Row(Chk("Tab13.OptionCount", "GodEvolutionOptionCountEnabled"), Num("GodEvolutionOptionCount")));
        Add("god-evolution-upgrade-buff-chance", "Tab13.UpgradeBuffChance", "Tab13.Section.Attributes", () => Row(Chk("Tab13.UpgradeBuffChance", "GodEvolutionUpgradeBuffChanceEnabled"), Num("GodEvolutionUpgradeBuffChance")));
        Add("god-evolution-force-super-quality", "Tab13.ForceSuperQuality", "Tab13.Section.Attributes", () => Chk("Tab13.ForceSuperQuality", "GodEvolutionForceSuperQuality"));
        Add("god-evolution-damage-multiplier", "Tab13.DamageMultiplier", "Tab13.Section.Attributes", () => Row(Chk("Tab13.DamageMultiplier", "GodEvolutionDamageMultiplierEnabled"), Num("GodEvolutionDamageMultiplier")));
        Add("god-evolution-force-mission-buff", "Tab1.Page7.GodEvolutionForceMissionBuff", "Tab13.Section.UnlockBoost", () => Chk("Tab1.Page7.GodEvolutionForceMissionBuff", "GodEvolutionForceMissionBuff"));
        Add("god-evolution-force-tactical-buff", "Tab1.Page7.GodEvolutionForceTacticalBuff", "Tab13.Section.UnlockBoost", () => Chk("Tab1.Page7.GodEvolutionForceTacticalBuff", "GodEvolutionForceTacticalBuff"));
        Add("god-evolution-cheat-hard", "Tab1.Page7.GodEvolutionCheatHard", "Tab13.Section.UnlockBoost", () => Chk("Tab1.Page7.GodEvolutionCheatHard", "GodEvolutionCheatHard"));
        Add("god-evolution-force-expert-buff", "Tab1.Page7.GodEvolutionForceExpertBuff", "Tab13.Section.UnlockBoost", () => Chk("Tab1.Page7.GodEvolutionForceExpertBuff", "GodEvolutionForceExpertBuff"));
        Add("god-evolution-force-star-up-buff", "Tab1.Page7.GodEvolutionForceStarUpBuff", "Tab13.Section.UnlockBoost", () => Chk("Tab1.Page7.GodEvolutionForceStarUpBuff", "GodEvolutionForceStarUpBuff"));
        Add("god-evolution-force-mutation-buff", "Tab1.Page7.GodEvolutionForceMutationBuff", "Tab13.Section.UnlockBoost", () => Chk("Tab1.Page7.GodEvolutionForceMutationBuff", "GodEvolutionForceMutationBuff"));
        Add("god-evolution-force-iridescent-buff", "Tab1.Page7.GodEvolutionForceIridescentBuff", "Tab13.Section.UnlockBoost", () => Chk("Tab1.Page7.GodEvolutionForceIridescentBuff", "GodEvolutionForceIridescentBuff"));
        Add("god-evolution-force-random-buff", "Tab1.Page7.GodEvolutionForceRandomBuff", "Tab13.Section.UnlockBoost", () => Chk("Tab1.Page7.GodEvolutionForceRandomBuff", "GodEvolutionForceRandomBuff"));
        Add("set-god-coin", "Tab1.Page7.ApplyGodCoin", "Tab13.Section.UnlockBoost", () => Btn("Tab1.Page7.ApplyGodCoin", "SetGodCoin"));
        Add("god-evolution-unlock-all", "Tab1.Page7.GodEvolutionUnlockAll", "Tab13.Section.UnlockBoost", () => Btn("Tab1.Page7.GodEvolutionUnlockAll", "GodEvolutionUnlockAll"));
        Add("apply-god-evolution-now", "Tab13.ApplyNow", "Tab13.Section.UnlockBoost", () => Btn("Tab13.ApplyNow", "ApplyGodEvolutionNow"));
        Add("reset-god-evolution-defaults", "Tab13.ResetDefaults", "Tab13.Section.UnlockBoost", () => Btn("Tab13.ResetDefaults", "ResetGodEvolutionDefaults"));

        // ==================================================================
        // 全覆盖批次 v2（2026-09-25「全部功能覆盖」）：补 86 项动作按钮/输入/生成类配套
        // 生成器 .tools\gen_catalog2.ps1（脚本断言插入，勿手改）
        // ==================================================================
        Add("create-plant", "Tab2.Page1.CreatePlant", "Tab2.Page1.Header", () => Row(Cmb("Plants2", "PlantType", 170), Btn("Tab2.Page1.CreatePlant", "CreatePlant")));
        Add("create-card", "Tab2.Page1.CreateCard", "Tab2.Page1.Header", () => Btn("Tab2.Page1.CreateCard", "CreateCard"));
        Add("simple-presents", "Tab2.Page1.SimplePresents", "Tab2.Page1.Header", () => Btn("Tab2.Page1.SimplePresents", "SimplePresents"));
        Add("create-zombie", "Tab2.Page1.CreateZombie", "Tab2.Page1.Header", () => Btn("Tab2.Page1.CreateZombie", "CreateZombie"));
        Add("plant-vase", "Tab2.Page1.PlantVase", "Tab2.Page1.Header", () => Btn("Tab2.Page1.PlantVase", "PlantVase"));
        Add("zombie-vase", "Tab2.Page1.ZombieVase", "Tab2.Page1.Header", () => Btn("Tab2.Page1.ZombieVase", "ZombieVase"));
        Add("random-vase", "Tab2.Page1.RandomVase", "Tab2.Page1.Header", () => Btn("Tab2.Page1.RandomVase", "RandomVase"));
        Add("create-item", "Tab2.Page1.CreateItem", "Tab2.Page1.Header", () => Btn("Tab2.Page1.CreateItem", "CreateItem"));
        Add("create-passive-mateorite", "Tab2.Page1.CreatePassiveMateorite", "Tab2.Page1.Header", () => Btn("Tab2.Page1.CreatePassiveMateorite", "CreatePassiveMateorite"));
        Add("create-active-mateorite", "Tab2.Page1.CreateActiveMateorite", "Tab2.Page1.Header", () => Btn("Tab2.Page1.CreateActiveMateorite", "CreateActiveMateorite"));
        Add("create-ultimate-mateorite", "Tab2.Page1.CreateUltimateMateorite", "Tab2.Page1.Header", () => Btn("Tab2.Page1.CreateUltimateMateorite", "CreateUltimateMateorite"));
        Add("create-solar-meteorite", "Tab2.Page1.CreateSolarMeteorite", "Tab2.Page1.Header", () => Btn("Tab2.Page1.CreateSolarMeteorite", "CreateSolarMeteorite"));
        Add("txt-jumpwavevalue", "Tab2.Page2.JumpWave", "Tab2.Page2.Header", () => Txt("Tab2.Page2.JumpWave", "JumpWaveValue"));
        Add("set-jump-wave", "Tab2.Page2.JumpWave", "Tab2.Page2.Header", () => Btn("Tab2.Page2.JumpWave", "SetJumpWave"));
        Add("create-mower", "Tab2.Page2.CreateMower", "Tab2.Page2.Header", () => Btn("Tab2.Page2.CreateMower", "CreateMower"));
        Add("level-name", "Tab2.Page2.ChangeLevelName", "Tab2.Page2.Header", () => Btn("Tab2.Page2.ChangeLevelName", "LevelName"));
        Add("txt-newlevelname", "Tab2.Page2.ChangeLevelName", "Tab2.Page2.Header", () => Txt("Tab2.Page2.ChangeLevelName", "NewLevelName"));
        Add("showing-text", "Tab2.Page2.ShowText", "Tab2.Page2.Header", () => Btn("Tab2.Page2.ShowText", "ShowingText"));
        Add("txt-showtext", "Tab2.Page2.ShowText", "Tab2.Page2.Header", () => Txt("Tab2.Page2.ShowText", "ShowText"));
        Add("health-zombie", "Tab1.Page3.HealthZombie", "Tab1.Page3.Header", () => Btn("Tab1.Page3.HealthZombie", "HealthZombie"));
        Add("health1st", "Tab1.Page3.Health1st", "Tab1.Page3.Header", () => Btn("Tab1.Page3.Health1st", "Health1st"));
        Add("health2nd", "Tab1.Page3.Health2nd", "Tab1.Page3.Header", () => Btn("Tab1.Page3.Health2nd", "Health2nd"));
        Add("bullet-damage", "Tab1.Page3.BulletDamage", "Tab1.Page3.Header", () => Btn("Tab1.Page3.BulletDamage", "BulletDamage"));
        Add("lock-bullet", "Tab1.Page3.LockBullet", "Tab1.Page3.Header", () => Btn("Tab1.Page3.LockBullet", "LockBullet"));
        Add("txt-locklightlevel", "Tab1.Page7.LockLightLevel", "Tab1.Page7.Header", () => Txt("Tab1.Page7.LockLightLevel", "LockLightLevel"));
        Add("txt-killzombiesrow", "Tab1.Page7.KillZombiesOnRow", "Tab1.Page7.Header", () => Txt("Tab1.Page7.KillZombiesOnRow", "KillZombiesRow"));
        Add("txt-abysswoodenticket", "Tab1.Page7.ApplyAbyssTickets", "Tab1.Page7.Header", () => Txt("Tab1.Page7.ApplyAbyssTickets", "AbyssWoodenTicket"));
        Add("txt-abysssilverticket", "Tab1.Page7.ApplyAbyssTickets", "Tab1.Page7.Header", () => Txt("Tab1.Page7.ApplyAbyssTickets", "AbyssSilverTicket"));
        Add("txt-abyssgoldticket", "Tab1.Page7.ApplyAbyssTickets", "Tab1.Page7.Header", () => Txt("Tab1.Page7.ApplyAbyssTickets", "AbyssGoldTicket"));
        Add("txt-abyssdiamondticket", "Tab1.Page7.ApplyAbyssTickets", "Tab1.Page7.Header", () => Txt("Tab1.Page7.ApplyAbyssTickets", "AbyssDiamondTicket"));
        Add("cheat-key_cheat-mode", "cheatmode", "Tab1.Page7.Header", () => BtnRaw("cheatmode", "CheatKey_CheatMode"));
        Add("cheat-key_more-sun", "moresun", "Tab1.Page7.Header", () => BtnRaw("moresun", "CheatKey_MoreSun"));
        Add("cheat-key_big-cannon", "bigcannon", "Tab1.Page7.Header", () => BtnRaw("bigcannon", "CheatKey_BigCannon"));
        Add("cheat-key_ir-winner", "irwinner", "Tab1.Page7.Header", () => BtnRaw("irwinner", "CheatKey_IrWinner"));
        Add("cheat-key_clear-plant", "clearplant", "Tab1.Page7.Header", () => BtnRaw("clearplant", "CheatKey_ClearPlant"));
        Add("cheat-key_clear-zombie", "clearzombie", "Tab1.Page7.Header", () => BtnRaw("clearzombie", "CheatKey_ClearZombie"));
        Add("cheat-key_mys-money", "mysmoney", "Tab1.Page7.Header", () => BtnRaw("mysmoney", "CheatKey_MysMoney"));
        Add("cheat-key_give-card", "givecard", "Tab1.Page7.Header", () => BtnRaw("givecard", "CheatKey_GiveCard"));
        Add("cheat-key_reload", "reload", "Tab1.Page7.Header", () => BtnRaw("reload", "CheatKey_Reload"));
        Add("cheat-key_debug", "debug", "Tab1.Page7.Header", () => BtnRaw("debug", "CheatKey_Debug"));
        Add("cheat-key_up-up", "upup", "Tab1.Page7.Header", () => BtnRaw("upup", "CheatKey_UpUp"));
        Add("cheat-key_kill", "kill", "Tab1.Page7.Header", () => BtnRaw("kill", "CheatKey_Kill"));
        Add("cheat-key_report", "report", "Tab1.Page7.Header", () => BtnRaw("report", "CheatKey_Report"));
        Add("cheat-key_mission-a", "missiona", "Tab1.Page7.Header", () => BtnRaw("missiona", "CheatKey_MissionA"));
        Add("cheat-key_mission-b", "missionb", "Tab1.Page7.Header", () => BtnRaw("missionb", "CheatKey_MissionB"));
        Add("cheat-key_shoot-hard", "shoothard", "Tab1.Page7.Header", () => BtnRaw("shoothard", "CheatKey_ShootHard"));
        Add("cheat-key_open-b-live", "openblive", "Tab1.Page7.Header", () => BtnRaw("openblive", "CheatKey_OpenBLive"));
        Add("txt-staradvstar", "Tab1.Page7.SetStarAdvStar", "Tab1.Page7.Header", () => Txt("Tab1.Page7.SetStarAdvStar", "StarAdvStar"));
        Add("txt-staradvstarhard", "Tab1.Page7.SetStarAdvStarHard", "Tab1.Page7.Header", () => Txt("Tab1.Page7.SetStarAdvStarHard", "StarAdvStarHard"));
        Add("copy-field-scripts", "Tab2.Page1.ReadField", "Tab2.Page1.PlantLineup", () => Btn("Tab2.Page1.ReadField", "CopyFieldScripts"));
        Add("write-field", "Tab2.Page1.WriteField", "Tab2.Page1.PlantLineup", () => Btn("Tab2.Page1.WriteField", "WriteField"));
        Add("txt-fieldstring", "Tab2.Page1.WriteField", "Tab2.Page1.PlantLineup", () => Txt("Tab2.Page1.WriteField", "FieldString"));
        Add("copy-zombie-scripts", "Tab2.Page1.ReadField", "Tab2.Page1.ZombieLineUp", () => Btn("Tab2.Page1.ReadField", "CopyZombieScripts"));
        Add("write-zombies", "Tab2.Page1.WriteField", "Tab2.Page1.ZombieLineUp", () => Btn("Tab2.Page1.WriteField", "WriteZombies"));
        Add("txt-zombiefieldstring", "Tab2.Page1.ZombieLineUp", "Tab2.Page1.ZombieLineUp", () => Txt("Tab2.Page1.ZombieLineUp", "ZombieFieldString"));
        Add("copy-mix-scripts", "Tab2.Page1.ReadField", "Tab2.Page1.MixLineUp", () => Btn("Tab2.Page1.ReadField", "CopyMixScripts"));
        Add("write-mix", "Tab2.Page1.WriteField", "Tab2.Page1.MixLineUp", () => Btn("Tab2.Page1.WriteField", "WriteMix"));
        Add("txt-mixfieldstring", "Tab2.Page1.WriteField", "Tab2.Page1.MixLineUp", () => Txt("Tab2.Page1.WriteField", "MixFieldString"));
        Add("copy-vases-scripts", "Tab2.Page1.ReadField", "Tab2.Page1.VaseLineUp", () => Btn("Tab2.Page1.ReadField", "CopyVasesScripts"));
        Add("write-vases", "Tab2.Page1.WriteField", "Tab2.Page1.VaseLineUp", () => Btn("Tab2.Page1.WriteField", "WriteVases"));
        Add("txt-vasesfieldstring", "Tab2.Page1.VaseLineUp", "Tab2.Page1.VaseLineUp", () => Txt("Tab2.Page1.VaseLineUp", "VasesFieldString"));
        Add("travel-buff-select-all", "Tab3.All", "Tab3.Page1.Header", () => Btn("Tab3.All", "TravelBuffSelectAll"));
        Add("travel-buff-unselect-all", "Tab3.None", "Tab3.Page1.Header", () => Btn("Tab3.None", "TravelBuffUnselectAll"));
        Add("in-game-buff-select-all", "Tab3.All", "Tab3.Page2.Header", () => Btn("Tab3.All", "InGameBuffSelectAll"));
        Add("in-game-buff-unselect-all", "Tab3.None", "Tab3.Page2.Header", () => Btn("Tab3.None", "InGameBuffUnselectAll"));
        Add("debuff-select-all", "Tab3.All", "Tab3.Page3.Header", () => Btn("Tab3.All", "DebuffSelectAll"));
        Add("debuff-unselect-all", "Tab3.None", "Tab3.Page3.Header", () => Btn("Tab3.None", "DebuffUnselectAll"));
        Add("in-game-invest-buff-select-all", "Tab3.All", "Tab3.Page5.Header", () => Btn("Tab3.All", "InGameInvestBuffSelectAll"));
        Add("in-game-invest-buff-unselect-all", "Tab3.None", "Tab3.Page5.Header", () => Btn("Tab3.None", "InGameInvestBuffUnselectAll"));
        Add("in-game-debuff-select-all", "Tab3.All", "Tab3.Page4.Header", () => Btn("Tab3.All", "InGameDebuffSelectAll"));
        Add("in-game-debuff-unselect-all", "Tab3.None", "Tab3.Page4.Header", () => Btn("Tab3.None", "InGameDebuffUnselectAll"));
        Add("pv-e", "Tab8.KillUpgrade", "Tab8.Header", () => Btn("Tab8.KillUpgrade", "PvE"));
        Add("txt-flagwave1customtext", "Tab10.Wave1.Header", "Tab10.Wave1.Header", () => Txt("Tab10.Wave1.Header", "FlagWave1CustomText"));
        Add("txt-flagwave2customtext", "Tab10.Wave2.Header", "Tab10.Wave2.Header", () => Txt("Tab10.Wave2.Header", "FlagWave2CustomText"));
        Add("txt-flagwave3customtext", "Tab10.Wave3.Header", "Tab10.Wave3.Header", () => Txt("Tab10.Wave3.Header", "FlagWave3CustomText"));
        Add("txt-flagwave4customtext", "Tab10.Wave4.Header", "Tab10.Wave4.Header", () => Txt("Tab10.Wave4.Header", "FlagWave4CustomText"));
        Add("txt-flagwave5customtext", "Tab10.Wave5.Header", "Tab10.Wave5.Header", () => Txt("Tab10.Wave5.Header", "FlagWave5CustomText"));
        Add("txt-flagwave6customtext", "Tab10.Wave6.Header", "Tab10.Wave6.Header", () => Txt("Tab10.Wave6.Header", "FlagWave6CustomText"));
        Add("txt-flagwave7customtext", "Tab10.Wave7.Header", "Tab10.Wave7.Header", () => Txt("Tab10.Wave7.Header", "FlagWave7CustomText"));
        Add("txt-flagwave8customtext", "Tab10.Wave8.Header", "Tab10.Wave8.Header", () => Txt("Tab10.Wave8.Header", "FlagWave8CustomText"));
        Add("txt-flagwave9customtext", "Tab10.Wave9.Header", "Tab10.Wave9.Header", () => Txt("Tab10.Wave9.Header", "FlagWave9CustomText"));
        Add("txt-flagwave10customtext", "Tab10.Wave10.Header", "Tab10.Wave10.Header", () => Txt("Tab10.Wave10.Header", "FlagWave10CustomText"));
        Add("txt-particleid", "Tab1.Page3.Header", "Tab11.EffectPlay.Header", () => Txt("Tab1.Page3.Header", "ParticleId"));
        Add("txt-soundid", "Tab1.Page3.Header", "Tab11.EffectPlay.Header", () => Txt("Tab1.Page3.Header", "SoundId"));
        Add("txt-snapshotinfo", "Tab12.ManualSnapshot", "Tab12.Header", () => Txt("Tab12.ManualSnapshot", "SnapshotInfo"));
        Add("txt-newgodcoin", "Tab1.Page7.ApplyGodCoin", "Tab13.Section.UnlockBoost", () => Txt("Tab1.Page7.ApplyGodCoin", "NewGodCoin"));
       }

    /// <summary>命令按钮（仅限自身 Command 无参数的自包含动作；组合型按钮见类头「作用域纪律」）。
    /// ★ 面板按钮的 <paramref name="commandPath"/> 是父目录登记的【方法名】（去掉「Command」后缀，与 XAML
    ///   Command="{Binding XxxCommand}" 一致）；[RelayCommand] 生成的公开属性是「方法名 + Command」，
    ///   绑定路径必须补上后缀，否则解析不到 ⇒ 按钮 enabled 但点击无效果（2026-09-25 实测踩坑）。</summary>
    private static Button Btn(string contentKey, string commandPath)
    {
        var b = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 96,
            Height = 28,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(14, 0, 14, 0)
        };
        b.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty, contentKey);
        b.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
            VmBinding(commandPath + "Command", BindingMode.OneWay));
        return b;
    }

    /// <summary>品质权重组合块：勾选开关 + 四档权重（每档 = 小标签 + 数值，两行 Wrap 布局）。</summary>
    private static FrameworkElement QualityWeights()
    {
        var sp = new StackPanel();
        sp.Children.Add(Chk("Tab13.QualityWeightEnabled", "GodEvolutionQualityWeightEnabled"));
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        wrap.Children.Add(WeightLabeled("Tab13.QualityDefault", "GodEvolutionQualityDefault"));
        wrap.Children.Add(WeightLabeled("Tab13.QualitySilver", "GodEvolutionQualitySilver"));
        wrap.Children.Add(WeightLabeled("Tab13.QualityGold", "GodEvolutionQualityGold"));
        wrap.Children.Add(WeightLabeled("Tab13.QualityDiamond", "GodEvolutionQualityDiamond"));
        sp.Children.Add(wrap);
        return sp;
    }

    private static FrameworkElement WeightLabeled(string labelKey, string path)
    {
        var p = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 16, 6)
        };
        var tb = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        tb.SetResourceReference(TextBlock.TextProperty, labelKey);
        p.Children.Add(tb);
        p.Children.Add(Num(path, 0, 9999, 1, 1, width: 70));
        return p;
    }

    /// <summary>文本输入（关卡名/字幕/阵容码/跳波/门票等）：绑 VM 的 Text 属性，卡片标题=功能名。</summary>
    private static System.Windows.Controls.TextBox Txt(string contentKey, string path, string? tooltipKey = null, double width = 220)
    {
        var tx = new System.Windows.Controls.TextBox
        {
            Width = width,
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 6)
        };
        tx.SetBinding(System.Windows.Controls.TextBox.TextProperty, VmBinding(path));
        if (tooltipKey is not null)
            tx.SetResourceReference(FrameworkElement.ToolTipProperty, tooltipKey);
        return tx;
    }

    /// <summary>原始文本按钮（CheatKey 调试键等无语言键的英文按钮）：Content 直接取文本。</summary>
    private static Button BtnRaw(string text, string commandPath)
    {
        var b = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 96,
            Height = 28,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(14, 0, 14, 0)
        };
        b.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
            VmBinding(commandPath + "Command", BindingMode.OneWay));
        return b;
    }

    private static void Add(string id, string displayKey, string sourceKey, Func<FrameworkElement> build)
        => _all.Add(new Feature(id, displayKey, sourceKey, build));
}
