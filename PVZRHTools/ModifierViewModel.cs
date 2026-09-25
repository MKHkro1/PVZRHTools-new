using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastHotKeyForWPF;
using HandyControl.Tools.Extension;
using ToolModData;
using System.Collections.Generic;
using System.Linq;

namespace PVZRHTools;

[Serializable]
public class HotkeyUI : IAutoHotKeyProperty
{
    public event HotKeyEventHandler? Handler;

    public uint CurrentKeyA { get; set; }

    public Key CurrentKeyB { get; set; }

    [JsonIgnore] public int PoolID { get; set; }
}

[Serializable]
public partial class HotkeyUIVM : ObservableObject, IAutoHotKeyUpdate, IAutoHotKeyProperty
{
    public HotkeyUIVM(HotkeyUI HotkeyUI)
    {
        this.HotkeyUI = HotkeyUI;
        Clear = new RelayCommand(() => (CurrentKeyA, CurrentKeyB) = (0, 0));
    }

    [JsonIgnore] public RelayCommand Clear { get; init; }

    [JsonIgnore] public RelayCommand? Command { get; set; }

    [ObservableProperty] public partial HotkeyUI HotkeyUI { get; set; }

    [JsonIgnore] public string Text { get; init; } = "";

    public event HotKeyEventHandler? Handler;

    public uint CurrentKeyA
    {
        get => HotkeyUI.CurrentKeyA;
        set => SetProperty(HotkeyUI.CurrentKeyA, value, HotkeyUI, (t, e) => t.CurrentKeyA = e);
    }

    public Key CurrentKeyB
    {
        get => HotkeyUI.CurrentKeyB;
        set => SetProperty(HotkeyUI.CurrentKeyB, value, HotkeyUI, (t, e) => t.CurrentKeyB = e);
    }

    public void RemoveSame()
    {
    }

    public void UpdateHotKey()
    {
        GlobalHotKey.Add(CurrentKeyA, KeyHelper.KeyToNormalKeys[CurrentKeyB], (_, _) => Command!.Execute(null));
    }

    public void UpdateText()
    {
    }

    [JsonIgnore]
    public int PoolID
    {
        get => HotkeyUI.PoolID;
        set => HotkeyUI.PoolID = value;
    }
}

public partial class InGameHotkeyUI(string text, KeyCode code) : ObservableObject
{
    [ObservableProperty] public partial KeyCode KeyCode { get; set; } = code;

    public string KeyText { get; set; } = text;
}

public partial class InGameHotkeyUIVM(InGameHotkeyUI InGameHotkeyUI) : ObservableObject
{
    [ObservableProperty] public partial InGameHotkeyUI InGameHotkeyUI { get; set; } = InGameHotkeyUI;

    public KeyCode KeyCode
    {
        get => InGameHotkeyUI.KeyCode;
        set { SetProperty(InGameHotkeyUI.KeyCode, value, InGameHotkeyUI, (t, e) => t.KeyCode = e); }
    }

    public void NotifyKeyCodeChanged() => OnPropertyChanged(nameof(KeyCode));
}

public partial class ModifierViewModel : ObservableObject
{
    private ModifierSaveModel? _loadedSaveModel;
    private bool _inGameHotkeysLoadedFromSave;

    [ObservableProperty] public partial bool IsLoading { get; set; } = true;

    /// <summary>关于修改器覆盖层（5.3.1 #3）：窗口内嵌弹层开关</summary>
    [ObservableProperty] public partial bool IsAboutOpen { get; set; }

    /// <summary>标题栏「关于」按钮与覆盖层关闭按钮共用（开↔关）</summary>
    [RelayCommand]
    public void ShowAbout()
    {
        IsAboutOpen = !IsAboutOpen;
    }

    /// <summary>左侧栏收起（5.3.1 #5 收起半边；图标半边判 SUPERSEDED 未做）</summary>
    [ObservableProperty] public partial bool SidebarCollapsed { get; set; }

    /// <summary>
    /// 自定义面板（2026-09-25 新增「自由度排版」功能区）：
    /// 玩家把常用功能收藏到自己的页面，可调顺序、可选 1/2/3 列。
    /// 面板项的控件由 CustomLayoutCatalog 的构建函数**独立实例化**，与原页面互不影响（见该文件设计说明）。
    /// </summary>
    public CustomPanelViewModel CustomPanel { get; } = new();

    /// <summary>标题栏侧栏切换按钮</summary>
    [RelayCommand]
    public void ToggleSidebar()
    {
        SidebarCollapsed = !SidebarCollapsed;
    }

    public ModifierViewModel()
    {
        Plants = new Dictionary<int, string>
        {
            { -1, "-1 : 不修改" }
        };
        Bullets2 = new Dictionary<int, string>
        {
            { -2, "-2 : 不修改" },
            { -1, "-1 : 随机子弹" }
        };
        Health1sts = [];
        Health2nds = [];
        HealthPlants = [];
        HealthZombies = [];
        
        // 如果 InitData 还没有加载，先初始化空列表，等待 ReloadBuffsFromInitData 被调用
        if (App.InitData != null)
        {
            foreach (var kp in App.InitData.Value.Plants) Plants.Add(kp.Key, kp.Value);

        foreach (var h1 in App.InitData.Value.FirstArmors) Health1sts.Add(h1.Key, -1);

        foreach (var h2 in App.InitData.Value.SecondArmors) Health2nds.Add(h2.Key, -1);

        foreach (var h3 in App.InitData.Value.Plants) HealthPlants.Add(h3.Key, -1);

        foreach (var h4 in App.InitData.Value.Zombies) HealthZombies.Add(h4.Key, -1);

        foreach (var b in Bullets) Bullets2.Add(b.Key, b.Key + " : " + b.Value);
        }

        GameSpeed = 1;
        ZombieSeaTypes = [];
        ZombieSeaCD = 40;
        ConveyBeltTypes = [];
        FieldString = "";
        ZombieFieldString = "";
        VasesFieldString = "";
        NewLevelName = "";
        ShowText = "";
        BulletDamageType = 0;
        LockPresent = -1;
        LockWheat = -1;
        LockPresent1 = -1;
        LockPresent2 = -1;
        LockPresent3 = -1;
        LockPresent4 = -1;
        LockPresent5 = -1;
        LockBulletType = -2;
        ZombieSeaTypes = [];
        TravelBuffs = [];
        InGameBuffs = [];
        InvestBuffs = [];
        InGameInvestBuffs = [];
        Debuffs = [];
        InGameDebuffs = [];
        Times = 1;
        NewZombieUpdateCD = 30;
        // PvE 斗蛐蛐布阵：盲盒僵尸置顶（默认不生效）
        PvEBlindBoxZombie1 = -1;
        PvEBlindBoxZombie2 = -1;
        PvEBlindBoxZombie3 = -1;
        PvEBlindBoxZombie4 = -1;
        PvEBlindBoxZombie5 = -1;
        PvEBlindBoxZombie6 = -1;

        // 如果 InitData 还没有加载，先初始化空列表，等待 ReloadBuffsFromInitData 被调用
        if (App.InitData != null)
        {
            var bi = 0;
            // 高级词条
            foreach (var b in App.InitData.Value.AdvBuffs)
            {
                TravelBuffs.Add(new TravelBuffVM(new TravelBuff(bi, b, false, false)));
                InGameBuffs.Add(new TravelBuffVM(new TravelBuff(bi, b, true, false)));
                bi++;
            }

            // 究极词条
            foreach (var b in App.InitData.Value.UltiBuffs)
            {
                TravelBuffs.Add(new TravelBuffVM(new TravelBuff(bi, b, false, false)));
                InGameBuffs.Add(new TravelBuffVM(new TravelBuff(bi, b, true, false)));
                bi++;
            }

            // 投资词条：单独分区，不再归类到植物词条里
            if (App.InitData.Value.InvestBuffs is not null)
            {
                foreach (var b in App.InitData.Value.InvestBuffs)
                {
                    InvestBuffs.Add(new TravelBuffVM(new TravelBuff(bi, b, false, false)));
                    InGameInvestBuffs.Add(new TravelBuffVM(new TravelBuff(bi, b, true, false)));
                    bi++;
                }
            }

            // 负面词条单独分区
            var di = 0;
            foreach (var d in App.InitData.Value.Debuffs)
            {
                Debuffs.Add(new TravelBuffVM(new TravelBuff(di, d, true, true)));
                InGameDebuffs.Add(new TravelBuffVM(new TravelBuff(di, d, true, true)));
                di++;
            }

            IsLoading = false;
        }

        // 创建合并列表，包含所有三种buff（用于旗帜波词条选择）
        AllInGameBuffs = new BindingList<TravelBuffVM>();
        if (App.InitData != null)
        {
            foreach (var buff in InGameBuffs)
                AllInGameBuffs.Add(buff);
            foreach (var invest in InGameInvestBuffs)
                AllInGameBuffs.Add(invest);
            foreach (var debuff in InGameDebuffs)
                AllInGameBuffs.Add(debuff);
        }

        // 自定义面板（「自由度排版」）：面板一变动就落盘。
        // ★ 不走 SyncAll()：后者被「保存设置(NeedSave)」开关门控，而布局是玩家对界面的偏好，
        //   与"是否把设置同步进游戏"无关 ⇒ 必须独立持久化，否则改了布局重启就丢。
        CustomPanel.Changed = PersistCustomPanel;

        TravelBuffs.ListChanged += (sender, e) => SyncTravelBuffs();
        InGameBuffs.ListChanged += (sender, e) => 
        {
            SyncInGameBuffs();
            // 当列表项添加时，绑定 PropertyChanged 事件
            if (e.ListChangedType == System.ComponentModel.ListChangedType.ItemAdded && e.NewIndex >= 0 && e.NewIndex < InGameBuffs.Count)
            {
                BindTravelBuffVMPropertyChanged(InGameBuffs[e.NewIndex]);
            }
        };
        Debuffs.ListChanged += (_, _) => SyncTravelBuffs();
        InvestBuffs.ListChanged += (_, _) => SyncTravelBuffs();
        InGameInvestBuffs.ListChanged += (_, e) =>
        {
            SyncInGameBuffs();
            // 当列表项添加时，绑定 PropertyChanged 事件
            if (e.ListChangedType == System.ComponentModel.ListChangedType.ItemAdded && e.NewIndex >= 0 && e.NewIndex < InGameInvestBuffs.Count)
            {
                BindTravelBuffVMPropertyChanged(InGameInvestBuffs[e.NewIndex]);
            }
        };
        InGameDebuffs.ListChanged += (_, e) => 
        {
            SyncInGameBuffs();
            // 当列表项添加时，绑定 PropertyChanged 事件
            if (e.ListChangedType == System.ComponentModel.ListChangedType.ItemAdded && e.NewIndex >= 0 && e.NewIndex < InGameDebuffs.Count)
            {
                BindTravelBuffVMPropertyChanged(InGameDebuffs[e.NewIndex]);
            }
        };
        
        // 为现有的 InGameBuffs 项绑定 PropertyChanged 事件
        foreach (var buff in InGameBuffs)
        {
            BindTravelBuffVMPropertyChanged(buff);
        }

        // 为现有的 InGameInvestBuffs 项绑定 PropertyChanged 事件
        foreach (var invest in InGameInvestBuffs)
        {
            BindTravelBuffVMPropertyChanged(invest);
        }
        
        // 为现有的 InGameDebuffs 项绑定 PropertyChanged 事件
        foreach (var debuff in InGameDebuffs)
        {
            BindTravelBuffVMPropertyChanged(debuff);
        }
        
        Hotkeys = [];
        foreach (var (h, hui) in from h in KeyCommands let hui = new HotkeyUI() select (h, hui))
            Hotkeys.Add(new HotkeyUIVM(hui)
            {
                Command = new RelayCommand(h.Item2),
                Text = h.Item1
            });

        InGameHotkeys = [];
    }

    public ModifierViewModel(List<HotkeyUIVM> hotkeys) : this()
    {
        var hi = 0;
        Hotkeys = [];
        foreach (var (h, hui) in from h in KeyCommands let hui = new HotkeyUI() select (h, hui))
        {
            Hotkeys.Add(new HotkeyUIVM(hotkeys[hi].HotkeyUI)
            {
                Command = new RelayCommand(h.Item2),
                Text = h.Item1
            });
            hi++;
        }
    }

    public ModifierViewModel(ModifierSaveModel s)
    {
        Plants = new Dictionary<int, string>
        {
            { -1, "-1 : 不修改" }
        };
        Bullets2 = new Dictionary<int, string>
        {
            { -2, "-2 : 不修改" },
            { -1, "-1 : 随机子弹" }
        };
        Health1sts = [];
        Health2nds = [];
        HealthPlants = [];
        HealthZombies = [];
        foreach (var kp in App.InitData!.Value.Plants) Plants.Add(kp.Key, kp.Value);

        foreach (var h1 in App.InitData.Value.FirstArmors) Health1sts.Add(h1.Key, -1);

        foreach (var h2 in App.InitData.Value.SecondArmors) Health2nds.Add(h2.Key, -1);

        foreach (var h3 in App.InitData.Value.Plants) HealthPlants.Add(h3.Key, -1);

        foreach (var h4 in App.InitData.Value.Zombies) HealthZombies.Add(h4.Key, -1);

        foreach (var b in Bullets) Bullets2.Add(b.Key, b.Key + " : " + b.Value);

        InGameBuffs = [];
        InGameInvestBuffs = [];
        InGameDebuffs = [];
        BuffRefreshNoLimit = s.BuffRefreshNoLimit;
        UnlimitedRefresh = s.UnlimitedRefresh;
        UnlimitedScore = s.UnlimitedScore;
        CardNoInit = s.CardNoInit;
        ChomperNoCD = s.ChomperNoCD;
        SuperStarNoCD = s.SuperStarNoCD;
        AutoCutFruit = s.AutoCutFruit;
        RandomCard = s.RandomCard;
        ClearOnWritingField = s.ClearOnWritingField;
        ClearOnWritingZombies = s.ClearOnWritingZombies;
        ClearOnWritingVases = s.ClearOnWritingVases;
        GaoShuMode = s.GaoShuMode;
        CobCannonNoCD = s.CobCannonNoCD;
        Col = s.Col;
        ColumnPlanting = s.ColumnPlanting;
        ConveyBeltModify = s.ConveyBeltModify;
        ConveyBeltTypes =
            [.. from cbt in s.ConveyBeltTypes select new KeyValuePair<int, string>(cbt, Plants2[cbt])];
        Debuffs = [.. s.Debuffs];
        InvestBuffs = s.InvestBuffs is not null ? [.. s.InvestBuffs] : new BindingList<TravelBuffVM>();
        DeveloperMode = s.DeveloperMode;
        DevLour = s.DevLour;
        Exchange = s.Exchange;
        FastShooting = s.FastShooting;
        FieldString = s.FieldString;
        FreeCD = s.FreeCD;
        FreePlanting = s.FreePlanting;
        GameSpeed = s.GameSpeed;
        GameSpeedEnabled = s.GameSpeedEnabled;
        GarlicDay = s.GarlicDay;
        GloveNoCD = s.GloveNoCD;
        HammerNoCD = s.HammerNoCD;
        WheelNoCD = s.WheelNoCD;
        HardPlant = s.HardPlant;
        ImmuneForceDeduct = s.ImmuneForceDeduct;
        CurseImmunity = s.CurseImmunity;
        CrushImmunity = s.CrushImmunity;
        TrampleImmunity = s.TrampleImmunity;
        HyponoEmperorNoCD = s.HyponoEmperorNoCD;
        IsMindCtrl = s.IsMindCtrl;
        ItemExistForever = s.ItemExistForever;
        ItemType = s.ItemType;
        JackboxNotExplode = s.JackboxNotExplode;
        LockBulletType = s.LockBulletType;
        LockMoney = s.LockMoney;
        LockPresent = s.LockPresent;
        LockWheat = s.LockWheat;
        LockSun = s.LockSun;
        MineNoCD = s.MineNoCD;
        NeedSave = s.NeedSave;
        NewLevelName = s.NewLevelName;
        NewMoney = s.NewMoney;
        NewSun = s.NewSun;
        NoFail = s.NoFail;
        NoHole = s.NoHole;
        NoIceRoad = s.NoIceRoad;
        DisableIceEffect = s.DisableIceEffect;
        UnlockRedCardPlants = s.UnlockRedCardPlants;
        PlantingNoCD = s.PlantingNoCD;
        PlantType = s.PlantType;
        PresentFastOpen = s.PresentFastOpen;
        Row = s.Row;
        ScaredyDream = s.ScaredyDream;
        SeedRain = s.SeedRain;
        Shooting1 = s.Shooting1;
        Shooting2 = s.Shooting2;
        Shooting3 = s.Shooting3;
        Shooting4 = s.Shooting4;
        ShowText = s.ShowText;
        StopSummon = s.StopSummon;
        SuperPresent = s.SuperPresent;
        Times = s.Times;
        TopMostSprite = s.TopMostSprite;
        EnableAnimations = s.AnimationDefaultMigrated ? s.EnableAnimations : true;
        IsDarkMode = s.IsDarkMode;
        CustomPanel.LoadFrom(s.CustomPanelItems, s.CustomPanelColumns);
        CustomPanel.LoadPresets(s.CustomPanelPresets);
        TravelBuffs = [.. s.TravelBuffs];
        InvestBuffs = s.InvestBuffs is not null ? [.. s.InvestBuffs] : new BindingList<TravelBuffVM>();
        UltimateRamdomZombie = s.UltimateRamdomZombie;
        UltimateSuperGatling = s.UltimateSuperGatling;
        ZombieHealthMultiplierEnabled = s.ZombieHealthMultiplierEnabled;
        ZombieHealthMultiplier = (float)s.ZombieHealthMultiplier;
        ZombieHealthRatio = (float)s.ZombieHealthRatio;
        PlantSpeedMultiplierEnabled = s.PlantSpeedMultiplierEnabled;
        PlantSpeedMultiplier = (float)s.PlantSpeedMultiplier;
        PlantAttackMultiplierEnabled = s.PlantAttackMultiplierEnabled;
        PlantAttackMultiplier = (float)s.PlantAttackMultiplier;
        PlantHealthMultiplierEnabled = s.PlantHealthMultiplierEnabled;
        PlantHealthMultiplier = (float)s.PlantHealthMultiplier;
        AutoRhythmGame = s.AutoRhythmGame;
        UndeadBullet = s.UndeadBullet;
        OldObsidianBullet = s.OldObsidianBullet;
        UnlockAllFusions = s.UnlockAllFusions;
        WheelNoCD = s.WheelNoCD;
        VasesFieldString = s.VasesFieldString;
        ZombieFieldString = s.ZombieFieldString;
        ZombieSeaCD = s.ZombieSeaCD;
        ZombieSeaEnabled = s.ZombieSeaEnabled;
        ZombieType = s.ZombieType;
        ZombieSeaTypes = [.. from zst in s.ZombieSeaTypes select new KeyValuePair<int, string>(zst, Zombies[zst])];
        ZombieSeaLowEnabled = s.ZombieSeaLowEnabled;
        HammerFullCD = s.HammerFullCD;
        HammerFullCDEnabled = s.HammerFullCDEnabled;
        GloveFullCD = s.GloveFullCD;
        GloveFullCDEnabled = s.GloveFullCDEnabled;
        NewZombieUpdateCD = s.NewZombieUpdateCD;
        PlantUpgrade = s.PlantUpgrade;
        // PvE 斗蛐蛐布阵：盲盒僵尸置顶，兼容旧存档（旧存档中字段默认为0）
        PvEBlindBoxZombie1 = s.PvEBlindBoxZombie1;
        PvEBlindBoxZombie2 = s.PvEBlindBoxZombie2;
        PvEBlindBoxZombie3 = s.PvEBlindBoxZombie3;
        PvEBlindBoxZombie4 = s.PvEBlindBoxZombie4;
        PvEBlindBoxZombie5 = s.PvEBlindBoxZombie5;
        PvEBlindBoxZombie6 = s.PvEBlindBoxZombie6;
        GodEvolutionUnlimitedRefresh = s.GodEvolutionUnlimitedRefresh;
        GodEvolutionFreeUpgradeQuality = s.GodEvolutionFreeUpgradeQuality;
        GodEvolutionLuckyEnabled = s.GodEvolutionLuckyEnabled;
        GodEvolutionLucky = s.GodEvolutionLucky;
        GodEvolutionDifficultyEnabled = s.GodEvolutionDifficultyEnabled;
        GodEvolutionDifficulty = s.GodEvolutionDifficulty;
        GodEvolutionRefreshCountEnabled = s.GodEvolutionRefreshCountEnabled;
        GodEvolutionRefreshCount = s.GodEvolutionRefreshCount;
        GodEvolutionMaxPlantCountEnabled = s.GodEvolutionMaxPlantCountEnabled;
        GodEvolutionMaxPlantCount = s.GodEvolutionMaxPlantCount;
        GodEvolutionOptionCountEnabled = s.GodEvolutionOptionCountEnabled;
        GodEvolutionOptionCount = s.GodEvolutionOptionCount;
        GodEvolutionUpgradeBuffChanceEnabled = s.GodEvolutionUpgradeBuffChanceEnabled;
        GodEvolutionUpgradeBuffChance = s.GodEvolutionUpgradeBuffChance;
        GodEvolutionSuperUpgrade = s.GodEvolutionSuperUpgrade;
        GodEvolutionForceSuperQuality = s.GodEvolutionForceSuperQuality;
        GodEvolutionUncrashable = s.GodEvolutionUncrashable;
        GodEvolutionQualityWeightEnabled = s.GodEvolutionQualityWeightEnabled;
        GodEvolutionQualityDefault = s.GodEvolutionQualityDefault;
        GodEvolutionQualitySilver = s.GodEvolutionQualitySilver;
        GodEvolutionQualityGold = s.GodEvolutionQualityGold;
        GodEvolutionQualityDiamond = s.GodEvolutionQualityDiamond;
        GodEvolutionDamageMultiplierEnabled = s.GodEvolutionDamageMultiplierEnabled;
        GodEvolutionDamageMultiplier = s.GodEvolutionDamageMultiplier;
        GodEvolutionForceMissionBuff = s.GodEvolutionForceMissionBuff;
        GodEvolutionForceTacticalBuff = s.GodEvolutionForceTacticalBuff;
        GodEvolutionCheatHard = s.GodEvolutionCheatHard;
        GodEvolutionForceExpertBuff = s.GodEvolutionForceExpertBuff;
        GodEvolutionForceStarUpBuff = s.GodEvolutionForceStarUpBuff;
        GodEvolutionForceMutationBuff = s.GodEvolutionForceMutationBuff;
        GodEvolutionForceIridescentBuff = s.GodEvolutionForceIridescentBuff;
        GodEvolutionForceRandomBuff = s.GodEvolutionForceRandomBuff;
        StarAdvStar = s.StarAdvStar;
        StarAdvStarHard = s.StarAdvStarHard;
        StarAdvFreeBuff = s.StarAdvFreeBuff;
        WheelFullCD = s.WheelFullCD;
        WheelFullCDEnabled = s.WheelFullCDEnabled;
        RemoveFusionLimit = s.RemoveFusionLimit;
        var bi = 0;
        foreach (var b in App.InitData.Value.AdvBuffs)
        {
            try
            {
                InGameBuffs.Add(new TravelBuffVM(new TravelBuff(bi, b, true, false)));
                TravelBuffs.Add(new TravelBuffVM(new TravelBuff(bi, b, true, false)));
                if (bi < s.TravelBuffs.Count)
                    TravelBuffs[bi].TravelBuff.Enabled = s.TravelBuffs[bi].Enabled;
            }
            catch
            {
            }

            bi++;
        }

        foreach (var b in App.InitData.Value.UltiBuffs)
        {
            try
            {
                InGameBuffs.Add(new TravelBuffVM(new TravelBuff(bi, b, true, false)));
                TravelBuffs.Add(new TravelBuffVM(new TravelBuff(bi, b, true, false)));
                if (bi < s.TravelBuffs.Count)
                    TravelBuffs[bi].TravelBuff.Enabled = s.TravelBuffs[bi].Enabled;
            }
            catch
            {
            }

            bi++;
        }

        // 初始化 InGameInvestBuffs：与 InvestBuffs 对应
        foreach (var ib in InvestBuffs)
        {
            InGameInvestBuffs.Add(new TravelBuffVM(
                new TravelBuff(ib.TravelBuff.Index, ib.TravelBuff.Text, true, false)
                {
                    Enabled = ib.TravelBuff.Enabled
                }));
        }

        var di = 0;
        foreach (var d in App.InitData.Value.Debuffs)
            InGameDebuffs.Add(new TravelBuffVM(new TravelBuff(di, d, true, true)));

        TravelBuffs.ListChanged += (sender, e) => SyncTravelBuffs();
        InGameBuffs.ListChanged += (sender, e) => 
        {
            SyncInGameBuffs();
            if (e.ListChangedType == System.ComponentModel.ListChangedType.ItemAdded && e.NewIndex >= 0 && e.NewIndex < InGameBuffs.Count)
            {
                BindTravelBuffVMPropertyChanged(InGameBuffs[e.NewIndex]);
            }
        };
        Debuffs.ListChanged += (_, _) => SyncTravelBuffs();
        InGameInvestBuffs.ListChanged += (_, e) =>
        {
            SyncInGameBuffs();
            if (e.ListChangedType == System.ComponentModel.ListChangedType.ItemAdded && e.NewIndex >= 0 && e.NewIndex < InGameInvestBuffs.Count)
            {
                BindTravelBuffVMPropertyChanged(InGameInvestBuffs[e.NewIndex]);
            }
        };
        InGameDebuffs.ListChanged += (_, e) => 
        {
            SyncInGameBuffs();
            if (e.ListChangedType == System.ComponentModel.ListChangedType.ItemAdded && e.NewIndex >= 0 && e.NewIndex < InGameDebuffs.Count)
            {
                BindTravelBuffVMPropertyChanged(InGameDebuffs[e.NewIndex]);
            }
        };
        
        foreach (var buff in InGameBuffs)
        {
            BindTravelBuffVMPropertyChanged(buff);
        }
        foreach (var invest in InGameInvestBuffs)
        {
            BindTravelBuffVMPropertyChanged(invest);
        }
        foreach (var debuff in InGameDebuffs)
        {
            BindTravelBuffVMPropertyChanged(debuff);
        }
        
        var hi = 0;
        Hotkeys = [];
        foreach (var (h, hui) in from h in KeyCommands let hui = new HotkeyUI() select (h, hui))
        {
            Hotkeys.Add(new HotkeyUIVM(s.Hotkeys[hi].HotkeyUI)
            {
                Command = new RelayCommand(h.Item2),
                Text = h.Item1
            });
            hi++;
        }

        _loadedSaveModel = s;
    }

    public void ApplyPendingSaveIfNeeded()
    {
        if (App.PendingSaveModel is not ModifierSaveModel pending)
        {
            return;
        }

        App.PendingSaveModel = null;
        if (App.InitData == null)
        {
            App.PendingSaveModel = pending;
            return;
        }

        ApplySavedSettings(pending);
        _loadedSaveModel = pending;
        if (NeedSave)
        {
            SyncAll();
        }
    }

    private void ApplySavedSettings(ModifierSaveModel s)
    {
        NeedSync = false;
        BuffRefreshNoLimit = s.BuffRefreshNoLimit;
        UnlimitedRefresh = s.UnlimitedRefresh;
        UnlimitedScore = s.UnlimitedScore;
        CardNoInit = s.CardNoInit;
        ChomperNoCD = s.ChomperNoCD;
        SuperStarNoCD = s.SuperStarNoCD;
        AutoCutFruit = s.AutoCutFruit;
        RandomCard = s.RandomCard;
        ClearOnWritingField = s.ClearOnWritingField;
        ClearOnWritingZombies = s.ClearOnWritingZombies;
        ClearOnWritingVases = s.ClearOnWritingVases;
        ClearOnWritingMix = s.ClearOnWritingMix;
        GaoShuMode = s.GaoShuMode;
        CobCannonNoCD = s.CobCannonNoCD;
        Col = s.Col;
        ColumnPlanting = s.ColumnPlanting;
        ConveyBeltModify = s.ConveyBeltModify;
        DeveloperMode = s.DeveloperMode;
        DevLour = s.DevLour;
        Exchange = s.Exchange;
        FastShooting = s.FastShooting;
        FieldString = s.FieldString;
        FreeCD = s.FreeCD;
        FreePlanting = s.FreePlanting;
        GameSpeed = s.GameSpeed;
        GameSpeedEnabled = s.GameSpeedEnabled;
        GarlicDay = s.GarlicDay;
        GloveNoCD = s.GloveNoCD;
        HammerNoCD = s.HammerNoCD;
        WheelNoCD = s.WheelNoCD;
        HardPlant = s.HardPlant;
        ImmuneForceDeduct = s.ImmuneForceDeduct;
        CurseImmunity = s.CurseImmunity;
        CrushImmunity = s.CrushImmunity;
        TrampleImmunity = s.TrampleImmunity;
        HyponoEmperorNoCD = s.HyponoEmperorNoCD;
        IsMindCtrl = s.IsMindCtrl;
        ItemExistForever = s.ItemExistForever;
        ItemType = s.ItemType;
        JackboxNotExplode = s.JackboxNotExplode;
        LockBulletType = s.LockBulletType;
        LockMoney = s.LockMoney;
        LockPresent = s.LockPresent;
        LockWheat = s.LockWheat;
        LockSun = s.LockSun;
        MineNoCD = s.MineNoCD;
        NeedSave = s.NeedSave;
        NewLevelName = s.NewLevelName;
        NewMoney = s.NewMoney;
        NewSun = s.NewSun;
        NoFail = s.NoFail;
        NoHole = s.NoHole;
        NoIceRoad = s.NoIceRoad;
        DisableIceEffect = s.DisableIceEffect;
        UnlockRedCardPlants = s.UnlockRedCardPlants;
        PlantingNoCD = s.PlantingNoCD;
        PlantType = s.PlantType;
        PresentFastOpen = s.PresentFastOpen;
        Row = s.Row;
        ScaredyDream = s.ScaredyDream;
        SeedRain = s.SeedRain;
        Shooting1 = s.Shooting1;
        Shooting2 = s.Shooting2;
        Shooting3 = s.Shooting3;
        Shooting4 = s.Shooting4;
        ShowText = s.ShowText;
        StopSummon = s.StopSummon;
        SuperPresent = s.SuperPresent;
        Times = s.Times;
        TopMostSprite = s.TopMostSprite;
        EnableAnimations = s.AnimationDefaultMigrated ? s.EnableAnimations : true;
        IsDarkMode = s.IsDarkMode;
        CustomPanel.LoadFrom(s.CustomPanelItems, s.CustomPanelColumns);
        CustomPanel.LoadPresets(s.CustomPanelPresets);
        UltimateRamdomZombie = s.UltimateRamdomZombie;
        UltimateSuperGatling = s.UltimateSuperGatling;
        ZombieHealthMultiplierEnabled = s.ZombieHealthMultiplierEnabled;
        ZombieHealthMultiplier = (float)s.ZombieHealthMultiplier;
        ZombieHealthRatio = (float)s.ZombieHealthRatio;
        PlantSpeedMultiplierEnabled = s.PlantSpeedMultiplierEnabled;
        PlantSpeedMultiplier = (float)s.PlantSpeedMultiplier;
        PlantAttackMultiplierEnabled = s.PlantAttackMultiplierEnabled;
        PlantAttackMultiplier = (float)s.PlantAttackMultiplier;
        PlantHealthMultiplierEnabled = s.PlantHealthMultiplierEnabled;
        PlantHealthMultiplier = (float)s.PlantHealthMultiplier;
        AutoRhythmGame = s.AutoRhythmGame;
        UndeadBullet = s.UndeadBullet;
        OldObsidianBullet = s.OldObsidianBullet;
        UnlockAllFusions = s.UnlockAllFusions;
        VasesFieldString = s.VasesFieldString;
        ZombieFieldString = s.ZombieFieldString;
        MixFieldString = s.MixFieldString;
        ZombieSeaCD = s.ZombieSeaCD;
        ZombieSeaEnabled = s.ZombieSeaEnabled;
        ZombieType = s.ZombieType;
        ZombieSeaLowEnabled = s.ZombieSeaLowEnabled;
        HammerFullCD = s.HammerFullCD;
        HammerFullCDEnabled = s.HammerFullCDEnabled;
        GloveFullCD = s.GloveFullCD;
        GloveFullCDEnabled = s.GloveFullCDEnabled;
        NewZombieUpdateCD = s.NewZombieUpdateCD;
        PlantUpgrade = s.PlantUpgrade;
        PvEBlindBoxZombie1 = s.PvEBlindBoxZombie1;
        PvEBlindBoxZombie2 = s.PvEBlindBoxZombie2;
        PvEBlindBoxZombie3 = s.PvEBlindBoxZombie3;
        PvEBlindBoxZombie4 = s.PvEBlindBoxZombie4;
        PvEBlindBoxZombie5 = s.PvEBlindBoxZombie5;
        PvEBlindBoxZombie6 = s.PvEBlindBoxZombie6;
        GodEvolutionUnlimitedRefresh = s.GodEvolutionUnlimitedRefresh;
        GodEvolutionFreeUpgradeQuality = s.GodEvolutionFreeUpgradeQuality;
        GodEvolutionLuckyEnabled = s.GodEvolutionLuckyEnabled;
        GodEvolutionLucky = s.GodEvolutionLucky;
        GodEvolutionDifficultyEnabled = s.GodEvolutionDifficultyEnabled;
        GodEvolutionDifficulty = s.GodEvolutionDifficulty;
        GodEvolutionRefreshCountEnabled = s.GodEvolutionRefreshCountEnabled;
        GodEvolutionRefreshCount = s.GodEvolutionRefreshCount;
        GodEvolutionMaxPlantCountEnabled = s.GodEvolutionMaxPlantCountEnabled;
        GodEvolutionMaxPlantCount = s.GodEvolutionMaxPlantCount;
        GodEvolutionOptionCountEnabled = s.GodEvolutionOptionCountEnabled;
        GodEvolutionOptionCount = s.GodEvolutionOptionCount;
        GodEvolutionUpgradeBuffChanceEnabled = s.GodEvolutionUpgradeBuffChanceEnabled;
        GodEvolutionUpgradeBuffChance = s.GodEvolutionUpgradeBuffChance;
        GodEvolutionSuperUpgrade = s.GodEvolutionSuperUpgrade;
        GodEvolutionForceSuperQuality = s.GodEvolutionForceSuperQuality;
        GodEvolutionUncrashable = s.GodEvolutionUncrashable;
        GodEvolutionQualityWeightEnabled = s.GodEvolutionQualityWeightEnabled;
        GodEvolutionQualityDefault = s.GodEvolutionQualityDefault;
        GodEvolutionQualitySilver = s.GodEvolutionQualitySilver;
        GodEvolutionQualityGold = s.GodEvolutionQualityGold;
        GodEvolutionQualityDiamond = s.GodEvolutionQualityDiamond;
        GodEvolutionDamageMultiplierEnabled = s.GodEvolutionDamageMultiplierEnabled;
        GodEvolutionDamageMultiplier = s.GodEvolutionDamageMultiplier;
        GodEvolutionForceMissionBuff = s.GodEvolutionForceMissionBuff;
        GodEvolutionForceTacticalBuff = s.GodEvolutionForceTacticalBuff;
        GodEvolutionCheatHard = s.GodEvolutionCheatHard;
        GodEvolutionForceExpertBuff = s.GodEvolutionForceExpertBuff;
        GodEvolutionForceStarUpBuff = s.GodEvolutionForceStarUpBuff;
        GodEvolutionForceMutationBuff = s.GodEvolutionForceMutationBuff;
        GodEvolutionForceIridescentBuff = s.GodEvolutionForceIridescentBuff;
        GodEvolutionForceRandomBuff = s.GodEvolutionForceRandomBuff;
        StarAdvStar = s.StarAdvStar;
        StarAdvStarHard = s.StarAdvStarHard;
        StarAdvFreeBuff = s.StarAdvFreeBuff;
        WheelFullCD = s.WheelFullCD;
        WheelFullCDEnabled = s.WheelFullCDEnabled;
        RemoveFusionLimit = s.RemoveFusionLimit;

        if (s.ConveyBeltTypes is { Count: > 0 })
        {
            ConveyBeltTypes = [];
            foreach (var cbt in s.ConveyBeltTypes)
            {
                if (Plants2.ContainsKey(cbt))
                {
                    ConveyBeltTypes.Add(new KeyValuePair<int, string>(cbt, Plants2[cbt]));
                }
            }
        }

        if (s.ZombieSeaTypes is { Count: > 0 })
        {
            ZombieSeaTypes = [];
            foreach (var zst in s.ZombieSeaTypes)
            {
                if (Zombies.ContainsKey(zst))
                {
                    ZombieSeaTypes.Add(new KeyValuePair<int, string>(zst, Zombies[zst]));
                }
            }
        }

        RestoreSavedTravelBuffStates(s);
        NeedSync = true;
    }

    private void RestoreSavedTravelBuffStates(ModifierSaveModel s)
    {
        if (s.TravelBuffs is { Count: > 0 })
        {
            for (var i = 0; i < TravelBuffs.Count && i < s.TravelBuffs.Count; i++)
            {
                TravelBuffs[i].Enabled = s.TravelBuffs[i].Enabled;
            }
        }

        if (s.Debuffs is { Count: > 0 })
        {
            for (var i = 0; i < Debuffs.Count && i < s.Debuffs.Count; i++)
            {
                Debuffs[i].Enabled = s.Debuffs[i].Enabled;
            }
        }

        if (s.InvestBuffs is { Count: > 0 })
        {
            for (var i = 0; i < InvestBuffs.Count && i < s.InvestBuffs.Count; i++)
            {
                InvestBuffs[i].Enabled = s.InvestBuffs[i].Enabled;
            }
        }
    }

    #region Commands
    [RelayCommand]
    public void PvE()
    {
        App.DataSync.Value.SendData(new InGameActions { PvE = true });
    }
    [RelayCommand]
    public void AbyssCheat()
    {
        App.DataSync.Value.SendData(new InGameActions { AbyssCheat = true });
    }

    // ---- 功能 #18：冒险秘境抽奖券（4 种券各自可填任意数值）----
    [ObservableProperty] public partial int AbyssWoodenTicket { get; set; }
    [ObservableProperty] public partial int AbyssSilverTicket { get; set; }
    [ObservableProperty] public partial int AbyssGoldTicket { get; set; }
    [ObservableProperty] public partial int AbyssDiamondTicket { get; set; }

    /// <summary>把四种抽奖券的数值一次性下发给存档（AbyssManager.Data）</summary>
    [RelayCommand]
    public void ApplyAbyssTickets()
    {
        App.DataSync.Value.SendData(new InGameActions
        {
            AbyssWoodenTicket = AbyssWoodenTicket,
            AbyssSilverTicket = AbyssSilverTicket,
            AbyssGoldTicket = AbyssGoldTicket,
            AbyssDiamondTicket = AbyssDiamondTicket
        });
    }

    // ---- 星辉冒险修改（REF AbyssAndTreasureView「星辉冒险修改」同组）----
    /// <summary>普通难度星星数输入框（点「修改」按钮才下发，避免每敲一个数字就写一次存档）</summary>
    [ObservableProperty] public partial int StarAdvStar { get; set; }

    /// <summary>困难难度星星数输入框（点「修改」按钮才下发）</summary>
    [ObservableProperty] public partial int StarAdvStarHard { get; set; }

    /// <summary>星辉冒险词条免费点亮（状态开关，随变化即时下发）</summary>
    [ObservableProperty] public partial bool StarAdvFreeBuff { get; set; }

    [RelayCommand]
    public void SetStarAdvStar()
    {
        App.DataSync.Value.SendData(new InGameActions { SetStarAdvStar = StarAdvStar });
    }

    [RelayCommand]
    public void SetStarAdvStarHard()
    {
        App.DataSync.Value.SendData(new InGameActions { SetStarAdvStarHard = StarAdvStarHard });
    }

    partial void OnStarAdvFreeBuffChanged(bool value)
    {
        App.DataSync.Value.SendData(new InGameActions { StarAdvFreeBuff = value });
    }

    [RelayCommand]
    public void UnlockAllPlants()
    {
        App.DataSync.Value.SendData(new InGameActions { UnlockAllPlants = true });
    }

    [RelayCommand]
    public void SpawnPetGargantuar()
    {
        App.DataSync.Value.SendData(new InGameActions { SpawnPetGargantuar = true });
    }

    [RelayCommand]
    public void SpawnPetFootball()
    {
        App.DataSync.Value.SendData(new InGameActions { SpawnPetFootball = true });
    }

    [RelayCommand]
    public void SpawnPetSnowBoss()
    {
        App.DataSync.Value.SendData(new InGameActions { PetSnowBoss = true });
    }
    
    [RelayCommand]
    public void SpawnPetJackbox()
    {
        App.DataSync.Value.SendData(new InGameActions { SpawnPetJackbox = true });
    }
    
    [RelayCommand]
    public void SpawnPetDrown()
    {
        App.DataSync.Value.SendData(new InGameActions { SpawnPetDrown = true });
    }

    [RelayCommand]
    public void SpawnPetHorse()
    {
        App.DataSync.Value.SendData(new InGameActions { SpawnPetHorse = true });
    }

    [RelayCommand]
    public void SpawnPetImp()
    {
        App.DataSync.Value.SendData(new InGameActions { SpawnPetImp = true });
    }

    [RelayCommand]
    public void SpawnPetKirov()
    {
        App.DataSync.Value.SendData(new InGameActions { SpawnPetKirov = true });
    }

    // 游戏作弊码按钮组（照抄 REF CommonSettingsViewModel 17 个 CheatKey 命令）：
    // 一次性发送 key 串，游戏侧 CheatKey 组件收到即执行对应作弊码
    [RelayCommand]
    public void CheatKey_CheatMode() => App.DataSync.Value.SendData(new InGameActions { ExecuteCheatKey = "cheatmode" });

    [RelayCommand]
    public void CheatKey_MoreSun() => App.DataSync.Value.SendData(new InGameActions { ExecuteCheatKey = "moresun" });

    [RelayCommand]
    public void CheatKey_BigCannon() => App.DataSync.Value.SendData(new InGameActions { ExecuteCheatKey = "bigcannon" });

    [RelayCommand]
    public void CheatKey_IrWinner() => App.DataSync.Value.SendData(new InGameActions { ExecuteCheatKey = "irwinner" });

    [RelayCommand]
    public void CheatKey_ClearPlant() => App.DataSync.Value.SendData(new InGameActions { ExecuteCheatKey = "clearplant" });

    [RelayCommand]
    public void CheatKey_ClearZombie() => App.DataSync.Value.SendData(new InGameActions { ExecuteCheatKey = "clearzombie" });

    [RelayCommand]
    public void CheatKey_MysMoney() => App.DataSync.Value.SendData(new InGameActions { ExecuteCheatKey = "mysmoney" });

    [RelayCommand]
    public void CheatKey_GiveCard() => App.DataSync.Value.SendData(new InGameActions { ExecuteCheatKey = "givecard" });

    [RelayCommand]
    public void CheatKey_Reload() => App.DataSync.Value.SendData(new InGameActions { ExecuteCheatKey = "reload" });

    [RelayCommand]
    public void CheatKey_Debug() => App.DataSync.Value.SendData(new InGameActions { ExecuteCheatKey = "debug" });

    [RelayCommand]
    public void CheatKey_UpUp() => App.DataSync.Value.SendData(new InGameActions { ExecuteCheatKey = "upup" });

    [RelayCommand]
    public void CheatKey_Kill() => App.DataSync.Value.SendData(new InGameActions { ExecuteCheatKey = "kill" });

    [RelayCommand]
    public void CheatKey_Report() => App.DataSync.Value.SendData(new InGameActions { ExecuteCheatKey = "report" });

    [RelayCommand]
    public void CheatKey_MissionA() => App.DataSync.Value.SendData(new InGameActions { ExecuteCheatKey = "missiona" });

    [RelayCommand]
    public void CheatKey_MissionB() => App.DataSync.Value.SendData(new InGameActions { ExecuteCheatKey = "missionb" });

    [RelayCommand]
    public void CheatKey_ShootHard() => App.DataSync.Value.SendData(new InGameActions { ExecuteCheatKey = "shoothard" });

    [RelayCommand]
    public void CheatKey_OpenBLive() => App.DataSync.Value.SendData(new InGameActions { ExecuteCheatKey = "openblive" });

    [RelayCommand]
    public void BulletDamage()
    {
        App.DataSync.Value.SendData(new ValueProperties
            { BulletsDamage = new KeyValuePair<int, int>(BulletDamageType, (int)BulletDamageValue) });
    }

    [RelayCommand]
    public void ClearAllHoles()
    {
        App.DataSync.Value.SendData(new InGameActions { ClearAllHoles = true });
    }

    [RelayCommand]
    public void ClearAllPlants()
    {
        App.DataSync.Value.SendData(new InGameActions { ClearAllPlants = true });
    }

    [RelayCommand]
    public void ClearIceRoads()
    {
        App.DataSync.Value.SendData(new InGameActions { ClearAllIceRoads = true });
    }

    [RelayCommand]
    public void CopyFieldScripts()
    {
        App.DataSync.Value.SendData(new InGameActions { ReadField = true, GaoShuMode = GaoShuMode });
    }

    [RelayCommand]
    public void CopyVasesScripts()
    {
        App.DataSync.Value.SendData(new InGameActions { ReadVases = true, GaoShuMode = GaoShuMode });
    }

    [RelayCommand]
    public void CopyZombieScripts()
    {
        App.DataSync.Value.SendData(new InGameActions { ReadZombies = true, GaoShuMode = GaoShuMode });
    }

    [RelayCommand]
    public void CopyMixScripts()
    {
        App.DataSync.Value.SendData(new InGameActions { ReadMix = true });
    }

    [RelayCommand]
    public void CreateActiveMateorite()
    {
        App.DataSync.Value.SendData(new InGameActions { CreateActiveMateorite = true });
    }

    [RelayCommand]
    public void CreateCard()
    {
        App.DataSync.Value.SendData(new InGameActions { Card = true, PlantType = PlantType });
    }

    [RelayCommand]
    public void CreateItem()
    {
        App.DataSync.Value.SendData(new InGameActions { ItemType = ItemType });
    }

    [RelayCommand]
    public void CreateMower()
    {
        App.DataSync.Value.SendData(new InGameActions { CreateMower = true });
    }

    [RelayCommand]
    public void CreatePassiveMateorite()
    {
        App.DataSync.Value.SendData(new InGameActions { CreatePassiveMateorite = true });
    }

    [RelayCommand]
    public void CreatePlant()
    {
        App.DataSync.Value.SendData(new InGameActions
        {
            Row = (int)Row,
            Column = (int)Col,
            Times = (int)Times,
            PlantType = PlantType
        });
    }

    [RelayCommand]
    public void CreateUltimateMateorite()
    {
        App.DataSync.Value.SendData(new InGameActions { CreateUltimateMateorite = true });
    }

    [RelayCommand]
    public void CreateZombie()
    {
        App.DataSync.Value.SendData(new InGameActions
        {
            Row = (int)Row,
            Column = (int)Col,
            Times = (int)Times,
            ZombieType = ZombieType,
            SummonMindControlledZombies = IsMindCtrl
        });
    }

    [RelayCommand]
    public void DebuffSelectAll()
    {
        NeedSync = false;
        foreach (var t in Debuffs) t.Enabled = true;

        NeedSync = true;
        SyncTravelBuffs();
    }

    [RelayCommand]
    public void DebuffUnselectAll()
    {
        NeedSync = false;
        foreach (var t in Debuffs) t.Enabled = false;

        NeedSync = true;
        SyncTravelBuffs();
    }

    [RelayCommand]
    public void Health1st()
    {
        App.DataSync.Value.SendData(new ValueProperties
            { FirstArmorsHealth = new KeyValuePair<int, int>(Health1stType, (int)Health1stValue) });
    }

    [RelayCommand]
    public void Health2nd()
    {
        App.DataSync.Value.SendData(new ValueProperties
            { SecondArmorsHealth = new KeyValuePair<int, int>(Health2ndType, (int)Health2ndValue) });
    }

    [RelayCommand]
    public void HealthPlant()
    {
        App.DataSync.Value.SendData(new ValueProperties
            { PlantsHealth = new KeyValuePair<int, int>(HealthPlantType, (int)HealthPlantValue) });
    }

    [RelayCommand]
    public void HealthZombie()
    {
        App.DataSync.Value.SendData(new ValueProperties
            { ZombiesHealth = new KeyValuePair<int, int>(HealthZombieType, (int)HealthZombieValue) });
    }

    [RelayCommand]
    public void InGameBuffSelectAll()
    {
        if (!App.inited) return;
        NeedSync = false;
        foreach (var t in InGameBuffs) t.Enabled = true;

        NeedSync = true;
        SyncInGameBuffs();
    }

    [RelayCommand]
    public void InGameBuffUnselectAll()
    {
        if (!App.inited) return;
        NeedSync = false;
        foreach (var t in InGameBuffs) t.Enabled = false;

        NeedSync = true;
        SyncInGameBuffs();
    }

    [RelayCommand]
    public void InGameDebuffSelectAll()
    {
        if (!App.inited) return;
        NeedSync = false;
        foreach (var t in InGameDebuffs) t.Enabled = true;

        NeedSync = true;
        SyncInGameBuffs();
    }

    [RelayCommand]
    public void InGameDebuffUnselectAll()
    {
        if (!App.inited) return;
        NeedSync = false;
        foreach (var t in InGameDebuffs) t.Enabled = false;

        NeedSync = true;
        SyncInGameBuffs();
    }

    [RelayCommand]
    public void InGameInvestBuffSelectAll()
    {
        if (!App.inited) return;
        NeedSync = false;
        foreach (var t in InGameInvestBuffs) t.Enabled = true;

        NeedSync = true;
        SyncInGameBuffs();
    }

    [RelayCommand]
    public void InGameInvestBuffUnselectAll()
    {
        if (!App.inited) return;
        NeedSync = false;
        foreach (var t in InGameInvestBuffs) t.Enabled = false;

        NeedSync = true;
        SyncInGameBuffs();
    }

    private static readonly (string Text, KeyCode DefaultKey)[] InGameHotkeyDefaults =
    [
        ("高级时停 TimeStop", KeyCode.Alpha6),
        ("卡槽栏置顶 TopMostCardBank", KeyCode.Tab),
        ("显示CD信息 ShowCDInfo", KeyCode.BackQuote),
        ("图鉴种植：植物 AlmanacCreatePlant", KeyCode.N),
        ("图鉴种植：僵尸 AlmanacCreateZombie", KeyCode.M),
        ("图鉴种植：僵尸是否魅惑 AlmanacZombieMindCtrl", KeyCode.LeftControl),
        ("图鉴种植：植物罐子 AlmanacCreatePlantVase", KeyCode.J),
        ("图鉴种植：僵尸罐子 AlmanacCreateZombieVase", KeyCode.K),
        ("随机卡槽 RandomCard", KeyCode.R)
    ];

    public bool ShouldAcceptGameInGameHotkeys() => !_inGameHotkeysLoadedFromSave;

    public void RestoreInGameHotkeys(List<int>? savedCodes, List<int>? gameCodes)
    {
        if (savedCodes is { Count: > 0 })
        {
            InitInGameHotkeys(savedCodes, fromSave: true);
            SyncInGameHotkeys();
            return;
        }

        if (gameCodes is { Count: > 0 })
        {
            InitInGameHotkeys(gameCodes);
            return;
        }

        InitInGameHotkeys([]);
    }

    public void InitInGameHotkeys(List<int> keycodes, bool fromSave = false)
    {
        keycodes ??= [];
        _inGameHotkeysLoadedFromSave = fromSave && keycodes.Count > 0;
        InGameHotkeys = [];
        for (var i = 0; i < InGameHotkeyDefaults.Length; i++)
        {
            var defaultItem = InGameHotkeyDefaults[i];
            var resolvedKey = i < keycodes.Count ? (KeyCode)keycodes[i] : defaultItem.DefaultKey;
            InGameHotkeys.Add(new InGameHotkeyUIVM(new InGameHotkeyUI(defaultItem.Text, resolvedKey)));
        }

        InGameHotkeys.ListChanged += (_, e) =>
        {
            if (e.ListChangedType == ListChangedType.ItemAdded && e.NewIndex >= 0 &&
                e.NewIndex < InGameHotkeys.Count)
            {
                BindInGameHotkeyPropertyChanged(InGameHotkeys[e.NewIndex]);
            }

            SyncInGameHotkeys();
        };

        foreach (var hotkey in InGameHotkeys)
        {
            BindInGameHotkeyPropertyChanged(hotkey);
        }

        OnPropertyChanged(nameof(InGameHotkeys));
        foreach (var hotkey in InGameHotkeys)
        {
            hotkey.NotifyKeyCodeChanged();
        }
    }

    private void BindInGameHotkeyPropertyChanged(InGameHotkeyUIVM hotkey)
    {
        hotkey.PropertyChanged -= OnInGameHotkeyPropertyChanged;
        hotkey.PropertyChanged += OnInGameHotkeyPropertyChanged;
    }

    private void OnInGameHotkeyPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(InGameHotkeyUIVM.KeyCode))
        {
            SyncInGameHotkeys();
        }
    }

    private List<int> CollectInGameHotkeyCodes()
    {
        List<int> keys = [];
        if (InGameHotkeys != null)
        {
            foreach (var igh in InGameHotkeys)
            {
                keys.Add((int)igh.InGameHotkeyUI.KeyCode);
            }
        }

        return keys;
    }

    [RelayCommand]
    public void KillAllZombies()
    {
        App.DataSync.Value.SendData(new InGameActions { ClearAllZombies = true });
    }

    [RelayCommand]
    public void CancelGameLose()
    {
        App.DataSync.Value.SendData(new InGameActions { CancelGameLose = true });
    }

    [RelayCommand]
    public void LevelName()
    {
        App.DataSync.Value.SendData(new InGameActions { ChangeLevelName = NewLevelName });
    }

    [RelayCommand]
    public void LoadCustomPlantData()
    {
        App.DataSync.Value.SendData(new InGameActions { LoadCustomPlantData = true });
    }

    [RelayCommand]
    public async Task ManualSnapshot()
    {
        App.DataSync.Value.SendData(new InGameActions { ManualSnapshot = true });
        await Task.Delay(400);
        RefreshSnapshotInfo();
    }

    [RelayCommand]
    public void RestoreLastSnapshot()
    {
        App.DataSync.Value.SendData(new InGameActions { RestoreLastSnapshot = true });
        RefreshSnapshotInfo();
    }

    // 跨会话恢复功能已移除

    // --- 快照信息显示 ---
    [ObservableProperty] public partial string SnapshotInfo { get; set; } = "";

    [RelayCommand]
    public void RefreshSnapshotInfo()
    {
        try
        {
            var baseDir = App.IsBepInEx ? "." : ".";
            var path = System.IO.Path.Combine(baseDir, "PVZRHTools", "Snapshots", "LatestSnapshot.json");
            if (!File.Exists(path)) { SnapshotInfo = "暂无快照文件"; return; }
            var json = File.ReadAllText(path);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var r = doc.RootElement;
            int GetInt(string name, int def = 0) => r.TryGetProperty(name, out var e) && e.TryGetInt32(out var v) ? v : def;
            float GetFloat(string name, float def = 0f) => r.TryGetProperty(name, out var e) && e.TryGetSingle(out var v) ? v : def;
            bool GetBool(string name, bool def = false) => r.TryGetProperty(name, out var e) && e.ValueKind == System.Text.Json.JsonValueKind.True ? true :
                                                           r.TryGetProperty(name, out e) && e.ValueKind == System.Text.Json.JsonValueKind.False ? false : def;
            int GetArrayCount(string name) => r.TryGetProperty(name, out var e) && e.ValueKind == System.Text.Json.JsonValueKind.Array ? e.GetArrayLength() : 0;
            DateTime GetDate(string name)
            {
                if (r.TryGetProperty(name, out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String &&
                    DateTime.TryParse(e.GetString(), out var d)) return d;
                return DateTime.MinValue;
            }
            string BoardTagText()
            {
                if (!r.TryGetProperty("BoardTag", out var bt) || bt.ValueKind != System.Text.Json.JsonValueKind.Object) return "";
                bool sr = bt.TryGetProperty("IsSeedRain", out var v1) && v1.ValueKind == System.Text.Json.JsonValueKind.True;
                bool col = bt.TryGetProperty("IsColumn", out var v2) && v2.ValueKind == System.Text.Json.JsonValueKind.True;
                bool sd = bt.TryGetProperty("IsScaredyDream", out var v3) && v3.ValueKind == System.Text.Json.JsonValueKind.True;
                string B(bool x) => x ? "是" : "否";
                return $"BoardTag：种子雨={B(sr)}  列种={B(col)}  胆小菇梦境={B(sd)}";
            }

            var sb = new System.Text.StringBuilder();
            var captured = GetDate("CapturedAt");
            if (captured != DateTime.MinValue) sb.AppendLine($"捕获时间：{captured.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            var bt = GetInt("BoardType");
            var lvl = GetInt("LevelNumber");
            var round = GetInt("SurvivalRound");
            var wave = GetInt("Wave");
            var maxWave = GetInt("MaxWave");
            var isHuge = GetBool("IsHugeWave");
            var tnw = GetFloat("TimeUntilNextWave");
            var wInt = GetFloat("WaveInterval");
            var sun = GetInt("Sun");
            var money = GetInt("Money");
            sb.AppendLine($"关卡类型：{bt}    关卡编号：{lvl}    生存轮次：{round}");
            sb.AppendLine($"波次：{wave}/{maxWave}    巨大波：{(isHuge ? "是" : "否")}");
            sb.AppendLine($"距下波时间：{tnw:F2}    波间隔：{wInt:F2}");
            sb.AppendLine($"阳光：{sun}    金币：{money}");
            var btText = BoardTagText();
            if (!string.IsNullOrEmpty(btText)) sb.AppendLine(btText);
            sb.AppendLine("高级词条：" + GetArrayCount("AdvOn") + "    究极词条：" + GetArrayCount("UltiOn") + "    负面词条：" + GetArrayCount("DebuffOn") + "    投资词条：" + GetArrayCount("InvestOn"));
            sb.AppendLine("格子物品：" + GetArrayCount("GridItems") + "    罐子：" + GetArrayCount("Vases"));
            sb.AppendLine("卡槽：" + GetArrayCount("CardBank") + "    卡片冷却：" + GetArrayCount("CardCDs") + "    掉落物冷却：" + GetArrayCount("DroppedCDs"));
            sb.AppendLine("植物血量：" + GetArrayCount("PlantHealths") + "    僵尸血量：" + GetArrayCount("ZombieHealths"));
            sb.AppendLine("随机种子：" + GetInt("RandomSeed"));
            SnapshotInfo = sb.ToString();
        }
        catch (Exception ex)
        {
            SnapshotInfo = $"读取失败: {ex.Message}";
        }
    }

    [RelayCommand]
    public void LockBullet()
    {
        App.DataSync.Value.SendData(new ValueProperties { LockBulletType = LockBulletType });
    }

    [RelayCommand]
    public void MindCtrl()
    {
        App.DataSync.Value.SendData(new InGameActions { MindControlAllZombies = true });
    }

    [RelayCommand]
    public void Money()
    {
        // 同 Sun()：未勾选「锁定金币」时不下发数值。
        if (!LockMoney) return;
        App.DataSync.Value.SendData(new InGameActions { CurrentMoney = (int)NewMoney });
    }

    // ---- 4.0 新增：一次性动作按钮（不是开关，所以用 RelayCommand 而不是 CheckBox）----
    /// <summary>图鉴全解锁 - 把所有植物写进 config.meetPlants / meetPlant_runTime</summary>
    [RelayCommand]
    public void UnlockAllAlmanac()
    {
        App.DataSync.Value.SendData(new BasicProperties { UnlockAllAlmanac = true });
    }

    /// <summary>清除全部子弹</summary>
    [RelayCommand]
    public void ClearAllBullets()
    {
        App.DataSync.Value.SendData(new InGameActions { ClearAllBullets = true });
    }

    /// <summary>清除全部墓碑（一次性；REF RemoveAllGraves 同语义）</summary>
    [RelayCommand]
    public void RemoveAllGraves()
    {
        App.DataSync.Value.SendData(new InGameActions { RemoveAllGraves = true });
    }

    /// <summary>生成太阳陨石（一次性；REF CreateSolarMeteorite = itemPrefab[47]）</summary>
    [RelayCommand]
    public void CreateSolarMeteorite()
    {
        App.DataSync.Value.SendData(new InGameActions { CreateSolarMeteorite = true });
    }

    /// <summary>跳转到指定波的输入框（点「跳转」按钮才下发）</summary>
    [ObservableProperty] public partial int JumpWaveValue { get; set; }

    /// <summary>跳转到指定波（一次性；REF SetJumpWave 同语义）</summary>
    [RelayCommand]
    public void SetJumpWave()
    {
        App.DataSync.Value.SendData(new InGameActions { JumpWave = JumpWaveValue });
    }

    /// <summary>秒杀非魅惑僵尸（玩家侧/敌方僵尸）</summary>
    [RelayCommand]
    public void KillNonMindControlledZombies()
    {
        App.DataSync.Value.SendData(new InGameActions { KillNonMindControlledZombies = true });
    }

    /// <summary>秒杀魅惑僵尸（被魅惑、归玩家一侧的僵尸）</summary>
    [RelayCommand]
    public void KillMindControlledZombies()
    {
        App.DataSync.Value.SendData(new InGameActions { KillMindControlledZombies = true });
    }

    /// <summary>秒杀指定行僵尸（UI 1-based；协议侧会 -1 转成 theZombieRow 的 0-based）</summary>
    [RelayCommand]
    public void KillZombiesOnRow()
    {
        App.DataSync.Value.SendData(new InGameActions { KillZombiesOnRow = true, Row = KillZombiesRow });
    }

    /// <summary>一键解锁诸神进化（植物 / 路线 / 战术上限）</summary>
    [RelayCommand]
    public void GodEvolutionUnlockAll()
    {
        App.DataSync.Value.SendData(new GodEvolutionProperties { UnlockAll = true });
    }

    /// <summary>诸神币修改 - 直接写入 ShootingManager.Data.godCoins（存档数据，写即持久）</summary>
    [RelayCommand]
    public void SetGodCoin()
    {
        App.DataSync.Value.SendData(new GodEvolutionProperties { GodCoin = NewGodCoin });
    }

    // 冒险秘境抽奖券（4 个券）的 UI 统一走 ApplyAbyssTicketsCommand（一次性合并下发），
    // 不再提供逐个券的单独命令 —— 保持单一入口，避免两套写法并存。

    [RelayCommand]
    public void NextWave()
    {
        // RelayCommand/按钮回调通常要求 void；这里用 fire-and-forget 的异步脉冲发送。
        _ = NextWaveAsync();
    }

    private async Task NextWaveAsync()
    {
        // NextWave 必须作为“脉冲”发送（true -> false），否则 UI 侧/传输层可能会去重，导致只能生效一次
        try
    {
        App.DataSync.Value.SendData(new InGameActions { NextWave = true });
            // 给一小段时间让游戏端 Update 消费到该值
            await Task.Delay(50);
            App.DataSync.Value.SendData(new InGameActions { NextWave = false });
        }
        catch
        {
            // 静默：避免 UI 按钮抛异常影响其他功能
        }
    }

    [RelayCommand]
    public void PlantVase()
    {
        App.DataSync.Value.SendData(new InGameActions
        {
            PlantVase = true,
            PlantType = PlantType,
            Row = (int)Row,
            Column = (int)Col
        });
    }

    public void Save()
    {
        ModifierSaveModel s = new()
        {
            BuffRefreshNoLimit = BuffRefreshNoLimit,
            UnlimitedRefresh = UnlimitedRefresh,
            UnlimitedScore = UnlimitedScore,
            CardNoInit = CardNoInit,
            ChomperNoCD = ChomperNoCD,
            SuperStarNoCD = SuperStarNoCD,
            ClearOnWritingField = ClearOnWritingField,
            ClearOnWritingVases = ClearOnWritingVases,
            ClearOnWritingZombies = ClearOnWritingZombies,
            ClearOnWritingMix = ClearOnWritingMix,
            CobCannonNoCD = CobCannonNoCD,
            Col = Col,
            ColumnPlanting = ColumnPlanting,
            ConveyBeltModify = ConveyBeltModify,
            ConveyBeltTypes = [],
            Debuffs = [.. Debuffs],
            DeveloperMode = DeveloperMode,
            DevLour = DevLour,
            Exchange = Exchange,
            FastShooting = FastShooting,
            FieldString = FieldString,
            FreeCD = FreeCD,
            FreePlanting = FreePlanting,
            GameSpeed = GameSpeed,
            GameSpeedEnabled = GameSpeedEnabled,
            GarlicDay = GarlicDay,
            GloveNoCD = GloveNoCD,
            HammerNoCD = HammerNoCD,
            WheelNoCD = WheelNoCD,
            HardPlant = HardPlant,
            ImmuneForceDeduct = ImmuneForceDeduct,
            CurseImmunity = CurseImmunity,
            CrushImmunity = CrushImmunity,
            TrampleImmunity = TrampleImmunity,
            HyponoEmperorNoCD = HyponoEmperorNoCD,
            IsMindCtrl = IsMindCtrl,
            ItemExistForever = ItemExistForever,
            ItemType = ItemType,
            JackboxNotExplode = JackboxNotExplode,
            LockBulletType = LockBulletType,
            LockMoney = LockMoney,
            LockPresent = LockPresent,
            LockSun = LockSun,
            MineNoCD = MineNoCD,
            NeedSave = NeedSave,
            NewLevelName = NewLevelName,
            NewMoney = NewMoney,
            NewSun = NewSun,
            NoFail = NoFail,
            NoHole = NoHole,
            NoIceRoad = NoIceRoad,
            DisableIceEffect = DisableIceEffect,
            UnlockRedCardPlants = UnlockRedCardPlants,
            PlantingNoCD = PlantingNoCD,
            PlantType = PlantType,
            PresentFastOpen = PresentFastOpen,
            Row = Row,
            ScaredyDream = ScaredyDream,
            SeedRain = SeedRain,
            Shooting1 = Shooting1,
            Shooting2 = Shooting2,
            Shooting3 = Shooting3,
            Shooting4 = Shooting4,
            ShowText = ShowText,
            StopSummon = StopSummon,
            SuperPresent = SuperPresent,
            Times = Times,
            TopMostSprite = TopMostSprite,
            EnableAnimations = EnableAnimations,
            AnimationDefaultMigrated = true,
            IsDarkMode = IsDarkMode,
            TravelBuffs = [.. TravelBuffs],
            UltimateRamdomZombie = UltimateRamdomZombie,
            UltimateSuperGatling = UltimateSuperGatling,
            ZombieHealthMultiplierEnabled = ZombieHealthMultiplierEnabled,
            ZombieHealthMultiplier = ZombieHealthMultiplier,
            ZombieHealthRatio = ZombieHealthRatio,
            PlantSpeedMultiplierEnabled = PlantSpeedMultiplierEnabled,
            PlantSpeedMultiplier = PlantSpeedMultiplier,
            PlantAttackMultiplierEnabled = PlantAttackMultiplierEnabled,
            PlantAttackMultiplier = PlantAttackMultiplier,
            PlantHealthMultiplierEnabled = PlantHealthMultiplierEnabled,
            PlantHealthMultiplier = PlantHealthMultiplier,
            CustomPanelItems = CustomPanel.ToIdList(),
            CustomPanelColumns = CustomPanel.Columns,
            AutoRhythmGame = AutoRhythmGame,
            UndeadBullet = UndeadBullet,
            OldObsidianBullet = OldObsidianBullet,
            UnlockAllFusions = UnlockAllFusions,
            VasesFieldString = VasesFieldString,
            ZombieFieldString = ZombieFieldString,
            MixFieldString = MixFieldString,
            GaoShuMode = GaoShuMode,
            ZombieSeaCD = ZombieSeaCD,
            ZombieSeaEnabled = ZombieSeaEnabled,
            ZombieSeaTypes = [],
            ZombieType = ZombieType,
            ZombieSeaLowEnabled = ZombieSeaLowEnabled,
            GloveFullCD = GloveFullCD,
            WheelFullCD = WheelFullCD,
            WheelFullCDEnabled = WheelFullCDEnabled,
            RemoveFusionLimit = RemoveFusionLimit,
            GloveFullCDEnabled = GloveFullCDEnabled,
            HammerFullCD = HammerFullCD,
            HammerFullCDEnabled = HammerFullCDEnabled,
            Hotkeys = Hotkeys,
            InGameHotkeyCodes = CollectInGameHotkeyCodes(),
            NewZombieUpdateCD = NewZombieUpdateCD,
            PlantUpgrade = PlantUpgrade,
            PvEBlindBoxZombie1 = PvEBlindBoxZombie1,
            PvEBlindBoxZombie2 = PvEBlindBoxZombie2,
            PvEBlindBoxZombie3 = PvEBlindBoxZombie3,
            PvEBlindBoxZombie4 = PvEBlindBoxZombie4,
            PvEBlindBoxZombie5 = PvEBlindBoxZombie5,
            PvEBlindBoxZombie6 = PvEBlindBoxZombie6,
            GodEvolutionUnlimitedRefresh = GodEvolutionUnlimitedRefresh,
            GodEvolutionFreeUpgradeQuality = GodEvolutionFreeUpgradeQuality,
            GodEvolutionLuckyEnabled = GodEvolutionLuckyEnabled,
            GodEvolutionLucky = GodEvolutionLucky,
            GodEvolutionDifficultyEnabled = GodEvolutionDifficultyEnabled,
            GodEvolutionDifficulty = GodEvolutionDifficulty,
            GodEvolutionRefreshCountEnabled = GodEvolutionRefreshCountEnabled,
            GodEvolutionRefreshCount = GodEvolutionRefreshCount,
            GodEvolutionMaxPlantCountEnabled = GodEvolutionMaxPlantCountEnabled,
            GodEvolutionMaxPlantCount = GodEvolutionMaxPlantCount,
            GodEvolutionOptionCountEnabled = GodEvolutionOptionCountEnabled,
            GodEvolutionOptionCount = GodEvolutionOptionCount,
            GodEvolutionUpgradeBuffChanceEnabled = GodEvolutionUpgradeBuffChanceEnabled,
            GodEvolutionUpgradeBuffChance = GodEvolutionUpgradeBuffChance,
            GodEvolutionSuperUpgrade = GodEvolutionSuperUpgrade,
            GodEvolutionForceSuperQuality = GodEvolutionForceSuperQuality,
            GodEvolutionUncrashable = GodEvolutionUncrashable,
            GodEvolutionQualityWeightEnabled = GodEvolutionQualityWeightEnabled,
            GodEvolutionQualityDefault = GodEvolutionQualityDefault,
            GodEvolutionQualitySilver = GodEvolutionQualitySilver,
            GodEvolutionQualityGold = GodEvolutionQualityGold,
            GodEvolutionQualityDiamond = GodEvolutionQualityDiamond,
            GodEvolutionDamageMultiplierEnabled = GodEvolutionDamageMultiplierEnabled,
            GodEvolutionDamageMultiplier = GodEvolutionDamageMultiplier,
            GodEvolutionForceMissionBuff = GodEvolutionForceMissionBuff,
            GodEvolutionForceTacticalBuff = GodEvolutionForceTacticalBuff,
            GodEvolutionCheatHard = GodEvolutionCheatHard,
            GodEvolutionForceExpertBuff = GodEvolutionForceExpertBuff,
            GodEvolutionForceStarUpBuff = GodEvolutionForceStarUpBuff,
            GodEvolutionForceMutationBuff = GodEvolutionForceMutationBuff,
            GodEvolutionForceIridescentBuff = GodEvolutionForceIridescentBuff,
            GodEvolutionForceRandomBuff = GodEvolutionForceRandomBuff,
            StarAdvStar = StarAdvStar,
            StarAdvStarHard = StarAdvStarHard,
            StarAdvFreeBuff = StarAdvFreeBuff,
        };
        if (ZombieSeaTypes.Count > 0) s.ZombieSeaTypes.AddRange(from zst in ZombieSeaTypes select zst.Key);

        if (ConveyBeltTypes.Count > 0) s.ConveyBeltTypes.AddRange(from cbt in ConveyBeltTypes select cbt.Key);

        ModifierPaths.EnsureSaveSettingsDirectory();
        File.WriteAllText(ModifierPaths.GetSaveSettingsPath(),
            JsonSerializer.Serialize(s, ModifierSaveModelSGC.Default.ModifierSaveModel));
    }

    [RelayCommand]
    public void SetAward()
    {
        App.DataSync.Value.SendData(new InGameActions { SetAward = true });
    }

    [RelayCommand]
    public void DestroyAward()
    {
        App.DataSync.Value.SendData(new InGameActions { DestroyAward = true });
    }

    [RelayCommand]
    public void SetZombieIdle()
    {
        App.DataSync.Value.SendData(new InGameActions { SetZombieIdle = true });
    }

    [RelayCommand]
    public void ShowingText()
    {
        App.DataSync.Value.SendData(new InGameActions { ShowText = ShowText });
    }

    [RelayCommand]
    public void SimplePresents()
    {
        App.DataSync.Value.SendData(new InGameActions
        {
            WriteField = "H4sIAAAAAAAACjPQMdIxMjWzNoTSQBJMG0NpEyhtCqYNrM2gtDmUtoDSlhAaABg+1o9PAAAA",
            ClearOnWritingField = ClearOnWritingField,
            GaoShuMode = true,
            ClearAllZombies = true
        });
    }

    [RelayCommand]
    public void StartMower()
    {
        App.DataSync.Value.SendData(new InGameActions { StartMower = true });
    }

    [RelayCommand]
    public void Sun()
    {
        // ★ 未勾选「锁定阳光」时不下发 CurrentSun（保持 null）。
        // 否则 DataProcessor 的 `if (iga.CurrentSun is not null) Board.Instance.theSun = ...`
        // 会把数值直接应用到棋盘上 —— 这正是「未勾选时改数值也会应用」的根因。
        if (!LockSun) return;
        App.DataSync.Value.SendData(new InGameActions { CurrentSun = (int)NewSun });
    }

    /// <summary>
    /// 自定义面板改动后立刻落盘（只改 CustomPanelItems/Columns 两个字段，其余字段从当前文件读回后合并写，
    /// 避免用一份可能过期的内存模型整体覆盖别的设置）。
    /// 失败一律吞掉：布局存不下不该影响玩家继续用修改器。
    /// </summary>
    private void PersistCustomPanel()
    {
        try
        {
            var path = ModifierPaths.GetSaveSettingsPath();
            ModifierSaveModel model;
            if (File.Exists(path))
            {
                try
                {
                    model = JsonSerializer.Deserialize(File.ReadAllText(path),
                        ModifierSaveModelSGC.Default.ModifierSaveModel);
                }
                catch
                {
                    // 存档损坏：不冒险覆盖，直接放弃本次持久化（下次正常保存时会带上面板）
                    return;
                }
            }
            else
            {
                model = new ModifierSaveModel();
            }

            model.CustomPanelItems = CustomPanel.ToIdList();
            model.CustomPanelColumns = CustomPanel.Columns;
            model.CustomPanelPresets = CustomPanel.ExportPresets();
            File.WriteAllText(path,
                JsonSerializer.Serialize(model, ModifierSaveModelSGC.Default.ModifierSaveModel));
        }
        catch
        {
            // 忽略：布局持久化失败不应中断使用
        }
    }

    public void SyncAll()
    {
        if (!NeedSave) return;
        List<bool> adv = [];
        List<bool> ulti = [];
        List<bool> invest = [];
        List<bool> deb = [];
        int advLen = App.InitData!.Value.AdvBuffs?.Length ?? 0;
        int ultiLen = App.InitData.Value.UltiBuffs?.Length ?? 0;

        foreach (var buff in TravelBuffs)
        {
            int idx = buff.TravelBuff.Index;
            if (idx < advLen)
                adv.Add(buff.TravelBuff.Enabled);
            else if (idx < advLen + ultiLen)
                ulti.Add(buff.TravelBuff.Enabled);
        }

        // 投资词条单独分区
        foreach (var ib in InvestBuffs)
            invest.Add(ib.TravelBuff.Enabled);

        foreach (var d in Debuffs) deb.Add(d.TravelBuff.Enabled);

        InGameActions iga = new()
        {
            BuffRefreshNoLimit = BuffRefreshNoLimit,
            NoFail = NoFail,
            ConveyBeltTypes = [.. from p in ConveyBeltTypes select p.Key],
            StopSummon = StopSummon,
            ZombieSeaCD = (int)ZombieSeaCD,
            ZombieSeaEnabled = ZombieSeaEnabled,
            ZombieSeaTypes = [],
            ZombieType = ZombieType,
            GaoShuMode = GaoShuMode
        };
        iga.ZombieSeaTypes.AddRange(from zst in ZombieSeaTypes select zst.Key);
        // 星辉冒险「天赋免费点亮」是状态量：连上修改器时随 SyncAll 整包补发一次，
        // 否则游戏重启后 WPF 侧存档里的开关状态无法回灌游戏侧静态位
        iga.StarAdvFreeBuff = StarAdvFreeBuff;
        SyncAll syncAll = new()
        {
            BasicProperties = new BasicProperties
            {
                CardNoInit = CardNoInit,
                ChomperNoCD = ChomperNoCD,
                CobCannonNoCD = CobCannonNoCD,
                DeveloperMode = DeveloperMode,
                DevLour = DevLour,
                FastShooting = FastShooting,
                FreePlanting = FreePlanting,
                GameSpeed = (int)GameSpeed,
                GameSpeedEnabled = GameSpeedEnabled,
                GarlicDay = GarlicDay,
                GloveNoCD = GloveNoCD,
                HammerNoCD = HammerNoCD,
            WheelNoCD = WheelNoCD,
                HardPlant = HardPlant,
                ImmuneForceDeduct = ImmuneForceDeduct,
                CurseImmunity = CurseImmunity,
                CrushImmunity = CrushImmunity,
                TrampleImmunity = TrampleImmunity,
                HyponoEmperorNoCD = HyponoEmperorNoCD,
                ItemExistForever = ItemExistForever,
                JackboxNotExplode = JackboxNotExplode,
                LockPresent = LockPresent,
                MineNoCD = MineNoCD,
                NoHole = NoHole,
                NoIceRoad = NoIceRoad,
                DisableIceEffect = DisableIceEffect,
                UnlockRedCardPlants = UnlockRedCardPlants,
                PlantingNoCD = PlantingNoCD,
                PresentFastOpen = PresentFastOpen,
                SuperPresent = SuperPresent,
                UltimateRamdomZombie = UltimateRamdomZombie,
                AutoRhythmGame = AutoRhythmGame,
                UndeadBullet = UndeadBullet,
                OldObsidianBullet = OldObsidianBullet,
                UnlockAllFusions = UnlockAllFusions,
                GloveFullCD = GloveFullCDEnabled ? (int)GloveFullCD : -1,
                WheelFullCD = WheelFullCDEnabled ? (int)WheelFullCD : -1,
                HammerFullCD = HammerFullCDEnabled ? (int)HammerFullCD : -1,
                NewZombieUpdateCD = NewZombieUpdateCD,
                PlantUpgrade = PlantUpgrade
            },
            InGameActions = iga,
            TravelBuffs = new SyncTravelBuff
            {
                AdvTravelBuff = adv,
                UltiTravelBuff = ulti,
                Debuffs = deb,
                InvestTravelBuff = invest.Count > 0 ? invest : null
            },
            ValueProperties = new ValueProperties { LockBulletType = LockBulletType },
            GameModes = new GameModes
            {
                ScaredyDream = ScaredyDream,
                ColumnPlanting = ColumnPlanting,
                SeedRain = SeedRain,
                RemoveFusionLimit = RemoveFusionLimit
            }
        };

        App.DataSync.Value.SendData(syncAll);
        SyncGodEvolution();
    }

    /// <summary>
    /// 为 TravelBuffVM 绑定 PropertyChanged 事件，当 Enabled 属性变化时触发同步
    /// </summary>
    private void BindTravelBuffVMPropertyChanged(TravelBuffVM buff)
    {
        buff.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == "Enabled")
            {
                // 防止游戏->修改器同步时触发回传（会在跨关卡/连续关卡时把游戏真实词条覆盖掉）
                // - NeedSync=false：批量更新UI期间不回传
                // - DataSync.Enabled=false：DataSync 正在处理来自游戏的同步，不回传
                if (!NeedSync) return;
                if (!DataSync.Enabled) return;
                SyncInGameBuffs();
            }
        };
    }

    public void SyncInGameBuffs()
    {
        // 仅在允许同步、且不处于游戏->修改器同步更新期间时才发送，避免反馈环覆盖游戏状态
        if (!NeedSync) return;
        if (!DataSync.Enabled) return;
        if (!App.inited) return;

        List<bool> adv = [];
        List<bool> ulti = [];
        List<bool> invest = [];
        List<bool> deb = [];
        int advLen2 = App.InitData!.Value.AdvBuffs?.Length ?? 0;
        int ultiLen2 = App.InitData.Value.UltiBuffs?.Length ?? 0;
        int investLen2 = App.InitData.Value.InvestBuffs?.Length ?? 0;

        foreach (var buff in InGameBuffs)
        {
            int idx = buff.TravelBuff.Index;
            if (idx < advLen2)
            {
                // 高级词条：直接使用索引
                adv.Add(buff.TravelBuff.Enabled);
            }
            else if (idx < advLen2 + ultiLen2)
            {
                // 究极词条：需要转换为究极词条数组的索引（从0开始）
                int ultiIndex = idx - advLen2;
                // 确保 ulti 列表大小足够
                while (ulti.Count <= ultiIndex)
                {
                    ulti.Add(false);
                }
                ulti[ultiIndex] = buff.TravelBuff.Enabled;
            }
        }

        // 投资词条单独分区，顺序与 InitData.InvestBuffs 保持一致
        foreach (var ib in InGameInvestBuffs)
                {
            invest.Add(ib.TravelBuff.Enabled);
        }

        // 确保列表大小正确（填充缺失的项）
        while (adv.Count < advLen2)
        {
            adv.Add(false);
        }
        while (ulti.Count < ultiLen2)
        {
            ulti.Add(false);
        }
        if (investLen2 > 0)
        {
            while (invest.Count < investLen2)
            {
                invest.Add(false);
            }
        }

        foreach (var d in InGameDebuffs) deb.Add(d.TravelBuff.Enabled);

        App.DataSync.Value.SendData(new SyncTravelBuff
        {
            AdvInGame = adv,
            UltiInGame = ulti,
            DebuffsInGame = deb,
            InvestInGame = invest.Count > 0 ? invest : null
        });
    }

    public void SyncInGameHotkeys()
    {
        DataSync.Enabled = false;
        List<int> keys = [];
        foreach (var igh in InGameHotkeys) keys.Add((int)igh.InGameHotkeyUI.KeyCode);

        DataSync.Enabled = true;

        App.DataSync.Value.SendData(new InGameHotkeys { KeyCodes = keys });
    }

    public void SyncFlagWaveBuffs()
    {
        var idsStr = FlagWaveBuffIds != null ? string.Join(", ", FlagWaveBuffIds) : "null";
        System.Diagnostics.Debug.WriteLine($"[旗帜波词条] SyncFlagWaveBuffs: FlagWaveBuffEnabled={FlagWaveBuffEnabled}, FlagWaveBuffIds=[{idsStr}]");
        App.DataSync.Value.SendData(new InGameActions
        {
            FlagWaveBuffEnabled = FlagWaveBuffEnabled,
            FlagWaveBuffIds = FlagWaveBuffIds,
            FlagWaveCustomTexts = null // Tab2（常用功能）不使用自定义字幕，设置为null
        });
        System.Diagnostics.Debug.WriteLine($"[旗帜波词条] SyncFlagWaveBuffs: 数据已发送（不使用自定义字幕）");
    }

    public void SyncTravelBuffs()
    {
        List<bool> adv = [];
        List<bool> ulti = [];
        List<bool> invest = [];
        List<bool> deb = [];
        int advLen3 = App.InitData!.Value.AdvBuffs?.Length ?? 0;
        int ultiLen3 = App.InitData.Value.UltiBuffs?.Length ?? 0;

        foreach (var buff in TravelBuffs)
        {
            int idx = buff.TravelBuff.Index;
            if (idx < advLen3)
                adv.Add(buff.TravelBuff.Enabled);
            else if (idx < advLen3 + ultiLen3)
                ulti.Add(buff.TravelBuff.Enabled);
        }

        // 投资词条单独分区
        foreach (var ib in InvestBuffs)
            invest.Add(ib.TravelBuff.Enabled);

        foreach (var d in Debuffs) deb.Add(d.TravelBuff.Enabled);

        App.DataSync.Value.SendData(new SyncTravelBuff
        {
            AdvTravelBuff = adv,
            UltiTravelBuff = ulti,
            Debuffs = deb,
            InvestTravelBuff = invest.Count > 0 ? invest : null
        });
    }

    [RelayCommand]
    public void TravelBuffSelectAll()
    {
        NeedSync = false;
        foreach (var t in TravelBuffs) t.Enabled = true;

        NeedSync = true;
        SyncTravelBuffs();
    }

    [RelayCommand]
    public void TravelBuffUnselectAll()
    {
        NeedSync = false;
        foreach (var t in TravelBuffs) t.Enabled = false;

        NeedSync = true;
        SyncTravelBuffs();
    }

    [RelayCommand]
    public void WriteField()
    {
        App.DataSync.Value.SendData(new InGameActions
            { WriteField = FieldString, ClearOnWritingField = ClearOnWritingField, GaoShuMode = GaoShuMode });
    }

    [RelayCommand]
    public void WriteVases()
    {
        App.DataSync.Value.SendData(new InGameActions
            { WriteVases = VasesFieldString, ClearOnWritingVases = ClearOnWritingVases });
    }

    [RelayCommand]
    public void WriteZombies()
    {
        App.DataSync.Value.SendData(new InGameActions
        {
            WriteZombies = ZombieFieldString, ClearOnWritingZombies = ClearOnWritingZombies, GaoShuMode = GaoShuMode
        });
    }

    [RelayCommand]
    public void WriteMix()
    {
        App.DataSync.Value.SendData(new InGameActions
        {
            WriteMix = MixFieldString, ClearOnWritingMix = ClearOnWritingMix, GaoShuMode = GaoShuMode
        });
    }

    public void ZombieSea()
    {
        if (!App.inited) return;
        List<int> types = [];
        foreach (var type in ZombieSeaTypes) types.Add(type.Key);

        App.DataSync.Value.SendData(new InGameActions
        {
            ZombieSeaEnabled = ZombieSeaEnabled,
            ZombieSeaLowEnabled = ZombieSeaLowEnabled,
            ZombieSeaCD = (int)ZombieSeaCD,
            ZombieSeaTypes = types
        });
    }

    [RelayCommand]
    public void ZombieVase()
    {
        App.DataSync.Value.SendData(new InGameActions
        {
            ZombieVase = true,
            ZombieType = ZombieType,
            Row = (int)Row,
            Column = (int)Col
        });
    }

    
    [RelayCommand]
    public void RandomVase()
    {
        App.DataSync.Value.SendData(new InGameActions
        {
            RandomVase = true,
            Row = (int)Row,
            Column = (int)Col
        });
    }
    
    private void GameModes()
    {
        App.DataSync.Value.SendData(new GameModes
        {
            ScaredyDream = ScaredyDream,
            ColumnPlanting = ColumnPlanting,
            SeedRain = SeedRain,
            RemoveFusionLimit = RemoveFusionLimit
        });
    }

    partial void OnBuffRefreshNoLimitChanged(bool value)
    {
        App.DataSync.Value.SendData(new InGameActions { BuffRefreshNoLimit = value });
    }

    partial void OnUnlimitedRefreshChanged(bool value)
    {
        App.DataSync.Value.SendData(new InGameActions { UnlimitedRefresh = value });
    }

    partial void OnUnlimitedScoreChanged(bool value)
    {
        App.DataSync.Value.SendData(new InGameActions { UnlimitedScore = value });
    }

    partial void OnCardNoInitChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { CardNoInit = value });
    }

    partial void OnChomperNoCDChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ChomperNoCD = value });
    }

    partial void OnSuperStarNoCDChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { SuperStarNoCD = value });
    }
    partial void OnAutoCutFruitChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { AutoCutFruit = value });
    }
    partial void OnRandomCardChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { RandomCard = value });
    }
    partial void OnColumnGloveChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ColumnGlove = value });
    }
    partial void OnRandomBulletChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { RandomBullet = value });
    }
    partial void OnAutoRhythmGameChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { AutoRhythmGame = value });
    }
    partial void OnStarUpBuffChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { StarUpBuff = value });
    }
    partial void OnRandomUpgradeModeChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { RandomUpgradeMode = value });
    }

    // ---- 4.0 新增开关的同步回调（照抄同页既有条目的写法）----
    partial void OnEnableAllCardsChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { EnableAllCards = value });
    }
    partial void OnHardBulletChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { HardBullet = value });
    }
    partial void OnPlantsAllUpgradeChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { PlantsAllUpgrade = value });
    }
    partial void OnPlantsAllStarUpChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { PlantsAllStarUp = value });
    }
    partial void OnLockLightLevelChanged(int value)
    {
        // 注意：这里必须用 InGameActions 下发 —— DataDef 把 LockLightLevel 放在 InGameActions 结构里，
        // 发进 BasicProperties 会因为该结构体没有这个属性而编译不过（也就不会生效）。
        App.DataSync.Value.SendData(new InGameActions { LockLightLevel = value });
    }
    partial void OnCobCannonNoCDChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { CobCannonNoCD = value });
    }

    partial void OnColumnPlantingChanged(bool value)
    {
        GameModes();
    }

    partial void OnConveyBeltModifyChanged(bool value)
    {
        App.DataSync.Value.SendData(new InGameActions
            { ConveyBeltTypes = value ? [.. from type in ConveyBeltTypes select type.Key] : [] });
    }

    partial void OnDeveloperModeChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties
            { DeveloperMode = value, PlantingNoCD = FreeCD });
    }

    partial void OnDevLourChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { DevLour = value });
    }

    partial void OnExchangeChanged(bool value)
    {
        GameModes();
    }

    partial void OnFastShootingChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { FastShooting = value });
    }

    partial void OnFreePlantingChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { FreePlanting = value });
    }

    partial void OnGameSpeedChanged(double value)
    {
        App.DataSync.Value.SendData(new BasicProperties { GameSpeed = value });
    }

    partial void OnGameSpeedEnabledChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { GameSpeedEnabled = value });
    }

    partial void OnGarlicDayChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { GarlicDay = value });
    }

    // ---- 罗盘自定义冷却（协议为单字段 -1=关；照抄手套 GloveFullCD 的 flag+value 发送方式）----
    [ObservableProperty] public partial double WheelFullCD { get; set; } = -1;
    [ObservableProperty] public partial bool WheelFullCDEnabled { get; set; }

    partial void OnWheelFullCDChanged(double value)
    {
        App.DataSync.Value.SendData(new BasicProperties { WheelFullCD = value });
    }

    partial void OnWheelFullCDEnabledChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties
            { WheelFullCD = value ? WheelFullCD : -1 });
    }

    partial void OnGloveFullCDChanged(double value)
    {
        App.DataSync.Value.SendData(new BasicProperties { GloveFullCD = value });
    }

    partial void OnGloveFullCDEnabledChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties
            { GloveFullCD = value ? GloveFullCD : -1 });
    }

    partial void OnGloveNoCDChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { GloveNoCD = value });
    }

    partial void OnHammerFullCDChanged(double value)
    {
        App.DataSync.Value.SendData(new BasicProperties { HammerFullCD = value });
    }

    partial void OnHammerFullCDEnabledChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties
            { HammerFullCD = value ? HammerFullCD : -1 });
    }

    partial void OnHammerNoCDChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { HammerNoCD = value });
    }

    partial void OnWheelNoCDChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { WheelNoCD = value });
    }

    partial void OnHardPlantChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { HardPlant = value });
    }

    partial void OnImmuneForceDeductChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ImmuneForceDeduct = value });
    }

    partial void OnCurseImmunityChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { CurseImmunity = value });
    }

    partial void OnCrushImmunityChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { CrushImmunity = value });
    }

    partial void OnTrampleImmunityChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { TrampleImmunity = value });
    }

    partial void OnPickaxeImmunityChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { PickaxeImmunity = value });
    }

    partial void OnHyponoEmperorNoCDChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { HyponoEmperorNoCD = value });
    }

    partial void OnItemExistForeverChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ItemExistForever = value });
    }

    partial void OnJackboxNotExplodeChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { JackboxNotExplode = value });
    }

    partial void OnLockMoneyChanged(bool value)
    {
        // #27 勾选+数值：仅在【勾选】时随标志携带数值；取消勾选只发标志（CurrentMoney=null），
        // 否则接收端会在锁定已关闭的状态下把数值往棋盘上应用一次（未勾选也应用数值）。
        App.DataSync.Value.SendData(new InGameActions
            { LockMoney = value, CurrentMoney = value ? (int)NewMoney : null });
    }

    partial void OnLockPresentChanged(int value)
    {
        App.DataSync.Value.SendData(new BasicProperties { LockPresent = value });
    }


    partial void OnLockWheatChanged(int value)
    {
        App.DataSync.Value.SendData(new BasicProperties { LockWheat = value });
    }
    
    partial void OnLockPresent1Changed(int value)
    {
        App.DataSync.Value.SendData(new BasicProperties { LockPresent1 = value });
    }
    partial void OnLockPresent2Changed(int value)
    {
        App.DataSync.Value.SendData(new BasicProperties { LockPresent2 = value });
    }
    partial void OnLockPresent3Changed(int value)
    {
        App.DataSync.Value.SendData(new BasicProperties { LockPresent3 = value });
    }
    partial void OnLockPresent4Changed(int value)
    {
        App.DataSync.Value.SendData(new BasicProperties { LockPresent4 = value });
    }
    partial void OnLockPresent5Changed(int value)
    {
        App.DataSync.Value.SendData(new BasicProperties { LockPresent5 = value });
    }

    partial void OnPvEBlindBoxZombie1Changed(int value)
    {
        App.DataSync.Value.SendData(new BasicProperties { PvEBlindBoxZombie1 = value });
    }

    partial void OnPvEBlindBoxZombie2Changed(int value)
    {
        App.DataSync.Value.SendData(new BasicProperties { PvEBlindBoxZombie2 = value });
    }

    partial void OnPvEBlindBoxZombie3Changed(int value)
    {
        App.DataSync.Value.SendData(new BasicProperties { PvEBlindBoxZombie3 = value });
    }

    partial void OnPvEBlindBoxZombie4Changed(int value)
    {
        App.DataSync.Value.SendData(new BasicProperties { PvEBlindBoxZombie4 = value });
    }

    partial void OnPvEBlindBoxZombie5Changed(int value)
    {
        App.DataSync.Value.SendData(new BasicProperties { PvEBlindBoxZombie5 = value });
    }

    partial void OnPvEBlindBoxZombie6Changed(int value)
    {
        App.DataSync.Value.SendData(new BasicProperties { PvEBlindBoxZombie6 = value });
    }

    partial void OnLockSunChanged(bool value)
    {
        // #27 勾选+数值：仅在【勾选】时随标志携带数值；取消勾选只发标志（CurrentSun=null），
        // 否则接收端会在锁定已关闭的状态下把数值往棋盘上应用一次（未勾选也应用数值）。
        App.DataSync.Value.SendData(new InGameActions
            { LockSun = value, CurrentSun = value ? (int)NewSun : null });
    }

    partial void OnMineNoCDChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { MineNoCD = value });
    }

    partial void OnNewZombieUpdateCDChanged(double value)
    {
        App.DataSync.Value.SendData(new BasicProperties { NewZombieUpdateCD = value });
    }

    partial void OnNoFailChanged(bool value)
    {
        App.DataSync.Value.SendData(new InGameActions { NoFail = value });
    }

    partial void OnNoHoleChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { NoHole = value });
    }

    partial void OnNoIceRoadChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { NoIceRoad = value });
    }

    partial void OnDisableIceEffectChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { DisableIceEffect = value });
    }

    partial void OnPotSmashingFixChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { PotSmashingFix = value });
    }

    partial void OnUnlimitedSunlightChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { UnlimitedSunlight = value });
    }

    partial void OnMagnetNutUnlimitedChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { MagnetNutUnlimited = value });
    }

    partial void OnZombieDamageLimit200Changed(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieDamageLimit200 = value });
    }

    partial void OnZombieDamageLimitValueChanged(int value)
    {
        // ★ 勾选+数值的通用纪律（PATTERN，对应「未勾选时改数值也会应用」）：
        // 未勾选时【不下发数值】—— 保持可空字段为 null，接收端以 is not null 判定即可自动跳过。
        // 这样「取消勾选后仍生效」与「未勾选却改了数值被应用」两个问题同时消除。
        App.DataSync.Value.SendData(new BasicProperties
            { ZombieDamageLimitValue = ZombieDamageLimit200 ? value : null });
    }

    partial void OnZombieSpeedModifyEnabledChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieSpeedModifyEnabled = value });
    }

    partial void OnZombieSpeedMultiplierChanged(float value)
    {
        App.DataSync.Value.SendData(new BasicProperties
            { ZombieSpeedMultiplier = ZombieSpeedModifyEnabled ? value : null });
    }

    partial void OnZombieAttackMultiplierEnabledChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieAttackMultiplierEnabled = value });
    }

    partial void OnZombieAttackMultiplierChanged(float value)
    {
        App.DataSync.Value.SendData(new BasicProperties
            { ZombieAttackMultiplier = ZombieAttackMultiplierEnabled ? value : null });
    }

    partial void OnZombieHealthMultiplierEnabledChanged(bool value)
    {
        // 开启时携带当前数值（否则插件侧哨兵停在 -1 不生效）；关闭时数值置 null ——
        // 哨兵纪律：未开启不下发数值（对应「未勾选时改数值也会应用」的根治）。
        App.DataSync.Value.SendData(new BasicProperties
        {
            ZombieHealthMultiplierEnabled = value,
            ZombieHealthMultiplier = value ? ZombieHealthMultiplier : null
        });
    }

    partial void OnZombieHealthMultiplierChanged(float value)
    {
        App.DataSync.Value.SendData(new BasicProperties
            { ZombieHealthMultiplier = ZombieHealthMultiplierEnabled ? value : null });
    }

    /// <summary>「全场血量 ×N」按钮：仅在点击时下发一次性命令（不挂 OnChanged，避免加载即触发）</summary>
    [RelayCommand]
    public void ApplyZombieHealthRatio()
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieHealthRatio = ZombieHealthRatio });
    }

    // ---- 植物速度/攻击/血量三件套（5.3.1 #7+6）：成对下发，照僵尸三倍率模板（VM:2460-2479）----
    partial void OnPlantSpeedMultiplierEnabledChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { PlantSpeedMultiplierEnabled = value });
    }

    partial void OnPlantSpeedMultiplierChanged(float value)
    {
        App.DataSync.Value.SendData(new BasicProperties
            { PlantSpeedMultiplier = PlantSpeedMultiplierEnabled ? value : null });
    }

    partial void OnPlantAttackMultiplierEnabledChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { PlantAttackMultiplierEnabled = value });
    }

    partial void OnPlantAttackMultiplierChanged(float value)
    {
        App.DataSync.Value.SendData(new BasicProperties
            { PlantAttackMultiplier = PlantAttackMultiplierEnabled ? value : null });
    }

    partial void OnPlantHealthMultiplierEnabledChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { PlantHealthMultiplierEnabled = value });
    }

    partial void OnPlantHealthMultiplierChanged(float value)
    {
        App.DataSync.Value.SendData(new BasicProperties
            { PlantHealthMultiplier = PlantHealthMultiplierEnabled ? value : null });
    }

    /// <summary>「全场速度 ×N」按钮：仅在点击时下发一次性命令（不挂 OnChanged，避免加载即触发）</summary>
    [RelayCommand]
    public void ApplyPlantSpeedRatio()
    {
        App.DataSync.Value.SendData(new InGameActions { ApplyPlantSpeedRatio = PlantSpeedMultiplier });
    }

    /// <summary>「全场攻击 ×N」按钮：仅在点击时下发一次性命令</summary>
    [RelayCommand]
    public void ApplyPlantAttackRatio()
    {
        App.DataSync.Value.SendData(new InGameActions { ApplyPlantAttackRatio = PlantAttackMultiplier });
    }

    /// <summary>「全场血量 ×N」按钮：仅在点击时下发一次性命令</summary>
    [RelayCommand]
    public void ApplyPlantHealthRatio()
    {
        App.DataSync.Value.SendData(new InGameActions { ApplyPlantHealthRatio = PlantHealthMultiplier });
    }

    /// <summary>一键获得所有植物皮肤（一次性命令，REF PropertySettingsViewModel:487 同名同语义）</summary>
    [RelayCommand]
    public void ObtainAllPlantSkins()
    {
        App.DataSync.Value.SendData(new InGameActions { ObtainAllPlantSkins = true });
    }

    /// <summary>一键应用全部植物皮肤（一次性命令，REF PropertySettingsViewModel:477 同名同语义）</summary>
    [RelayCommand]
    public void ApplyAllPlantSkins()
    {
        App.DataSync.Value.SendData(new InGameActions { ApplyAllPlantSkins = true });
    }

    partial void OnZombieBulletReflectEnabledChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieBulletReflectEnabled = value });
    }

    partial void OnZombieBulletReflectChanceChanged(float value)
    {
        App.DataSync.Value.SendData(new BasicProperties
            { ZombieBulletReflectChance = ZombieBulletReflectEnabled ? value : null });
    }

    partial void OnZombieReviveDebuffCustomEnabledChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieReviveDebuffCustomEnabled = value });
    }

    partial void OnZombieReviveDebuffChanceChanged(float value)
    {
        // 同上：未勾选自定义复活 Debuff 时不下发概率。
        App.DataSync.Value.SendData(new BasicProperties
            { ZombieReviveDebuffChance = ZombieReviveDebuffCustomEnabled ? value : null });
    }

    partial void OnZombieFreeReviveEnabledChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieFreeReviveEnabled = value });
    }

    partial void OnZombieFreeReviveChanceChanged(float value)
    {
        App.DataSync.Value.SendData(new BasicProperties
            { ZombieFreeReviveChance = ZombieFreeReviveEnabled ? value : null });
    }

    partial void OnUnlimitedCardSlotsChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { UnlimitedCardSlots = value });
    }

    partial void OnZombieStatusCoexistChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieStatusCoexist = value });
    }

    partial void OnMNEntryEnabledChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { MNEntryEnabled = value });
    }

    partial void OnUnlockRedCardPlantsChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { UnlockRedCardPlants = value });
    }

    partial void OnKillUpgradeChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { KillUpgrade = value });
    }

    partial void OnZombieImmuneAllDebuffsChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieImmuneAllDebuffs = value });
    }

    // 僵尸免疫效果 - 分开的9个开关
    partial void OnZombieImmuneFreezeChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieImmuneFreeze = value });
    }

    partial void OnZombieImmuneColdChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieImmuneCold = value });
    }

    partial void OnZombieImmuneButterChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieImmuneButter = value });
    }

    partial void OnZombieImmunePoisonChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieImmunePoison = value });
    }

    partial void OnZombieImmuneJalaedChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieImmuneJalaed = value });
    }

    partial void OnZombieImmuneEmberedChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieImmuneEmbered = value });
    }

    partial void OnZombieImmuneKnockbackChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieImmuneKnockback = value });
    }

    partial void OnZombieImmuneMindControlChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieImmuneMindControl = value });
    }

    partial void OnZombieImmuneDevourChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieImmuneDevour = value });
    }

    partial void OnPlantingNoCDChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { PlantingNoCD = value });
    }

    partial void OnPlantUpgradeChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { PlantUpgrade = value });
    }

    partial void OnPresentFastOpenChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { PresentFastOpen = value });
    }

    partial void OnScaredyDreamChanged(bool value)
    {
        GameModes();
    }
    partial void OnPvPPotRangeChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { PvPPotRange = value });
    }

    /// <summary>旅行关解除融合限制（状态开关，随 GameModes 整包下发）</summary>
    [ObservableProperty] public partial bool RemoveFusionLimit { get; set; }

    partial void OnRemoveFusionLimitChanged(bool value)
    {
        GameModes();
    }

    partial void OnSeedRainChanged(bool value)
    {
        GameModes();
    }

    partial void OnShooting1Changed(bool value)
    {
        GameModes();
    }

    partial void OnShooting2Changed(bool value)
    {
        GameModes();
    }

    partial void OnShooting3Changed(bool value)
    {
        GameModes();
    }

    partial void OnShooting4Changed(bool value)
    {
        GameModes();
    }

    partial void OnStopSummonChanged(bool value)
    {
        App.DataSync.Value.SendData(new InGameActions { StopSummon = value });
    }

    partial void OnSuperPresentChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { SuperPresent = value });
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // ★ 2026-09-24 定稿：换肤只走 App.SwitchTheme。
            //   它现在做两件正确的事：① 用 HandyControl 官方 API（Theme.Skin）真正切换 HC 皮肤；
            //   ② 原地替换本地令牌字典。两者都是"资源字典级"的，{DynamicResource} 会自动重解析 ⇒
            //   浅↔深两个方向天然对称，不需要任何逐控件涂色（涂色会写下本地值，正是此前
            //   "深色切回浅色残留"的元凶；旧实现保留在 MainWindow.ApplyThemeWithAnimation 仅作存档）。
            App.SwitchTheme(value);
        });
    }

    partial void OnTopMostSpriteChanged(bool value)
    {
        if (value)
            MainWindow.Instance!.ModifierSprite.Show();
        else
            MainWindow.Instance!.ModifierSprite.Hide();
    }

    partial void OnUltimateRamdomZombieChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { UltimateRamdomZombie = value });
    }

    partial void OnUltimateSuperGatlingChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { UltimateSuperGatling = value });
    }

    partial void OnUndeadBulletChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { UndeadBullet = value });
    }
    
    partial void OnOldObsidianBulletChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { OldObsidianBullet = value });
    }

    partial void OnUnlockAllFusionsChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { UnlockAllFusions = value });
    }

    partial void OnZombieSeaCDChanged(double value)
    {
        ZombieSea();
    }

    partial void OnZombieSeaEnabledChanged(bool value)
    {
        ZombieSea();
    }

    partial void OnZombieSeaLowEnabledChanged(bool value)
    {
        ZombieSea();
    }

    partial void OnZombieSeaTypesChanged(List<KeyValuePair<int, string>> value)
    {
        ZombieSea();
    }

    #endregion Commands

    #region ItemSources

    public static bool NeedSync { get; set; } = true;

    public static Dictionary<int, string>? Plants { get; set; }

    public Dictionary<int, string> Bullets => App.InitData!.Value.Bullets;

    public Dictionary<int, string> Bullets2 { get; set; }

    public Dictionary<int, string> FirstArmor => App.InitData!.Value.FirstArmors;

    public Dictionary<int, int> Health1sts { get; set; }

    public Dictionary<int, int> Health2nds { get; set; }

    public Dictionary<int, int> HealthPlants { get; set; }

    public Dictionary<int, int> HealthZombies { get; set; }

    public Dictionary<int, string> Items => new()
    {
        { 0, "肥料 Fertilizer" },
        { 1, "铁桶 Bucket" },
        { 2, "橄榄头盔 Helmet" },
        { 3, "小丑礼盒 Jackbox" },
        { 4, "镐子 Pickaxe" },
        { 5, "机甲碎片 Machine" },
        { 6, "超级机甲碎片 SuperMachine" },
        { 7, "花园植物礼盒 GardenPresent" },
        { 8, "超时空碎片 PortalHeart" },
        { 64 + 0, "阳光 Sun" },
        { 64 + 1, "大阳光 BigSun" },
        { 64 + 2, "小阳光 SmallSun" },
        //{64 + 4,"铁桶 Bucket"},
        //{64 + 6,"橄榄头盔 Helmet"},
        //{64 + 7,"小丑礼盒 Jackbox"},
        //{64 + 8,"镐子 Pickaxe"},
        { 64 + 13, "小阳光 LittleSun" },
        { 64 + 34, "银币 SilverCoin" },
        { 64 + 35, "金币 GoldCoin" },
        { 64 + 36, "钻石 DiamondCoin" },
        //{64 + 37," Bean"},
        { 64 + 38, "小银币 SmallSilverCoin" },
        { 64 + 39, "小金币 SmallGoldCoin" },
        //{64 + 41,"机甲碎片 Machine"},
        { 64 + 42, "梯子 Portal" }
    };

    public List<(string, Action)> KeyCommands =>
    [
        ("手套无CD", () => GloveNoCD = !GloveNoCD),
        ("锤子无CD", () => HammerNoCD = !HammerNoCD),
        ("植物卡槽无CD", () => PlantingNoCD = !PlantingNoCD),
        ("自由种植", () => FreePlanting = !FreePlanting),
        ("解锁全部融合配方", () => UnlockAllFusions = !UnlockAllFusions),
        ("游戏加速", () => GameSpeed = GameSpeed < 9 ? ++GameSpeed : GameSpeed),
        ("游戏减速", () => GameSpeed = GameSpeed > 1 ? --GameSpeed : GameSpeed),
        ("胆小菇之梦", () => ScaredyDream = !ScaredyDream),
        ("排山倒海", () => ColumnPlanting = !ColumnPlanting),
        ("植物攻击无间隔", () => FastShooting = !FastShooting),
        ("植物无敌", () => HardPlant = !HardPlant),
        ("生成植物", CreatePlant),
        ("生成僵尸", CreateZombie),
        ("生成物品", CreateItem),
        ("生成究极陨星", CreateUltimateMateorite),
        ("斗蛐蛐快速布阵", SimplePresents),
        ("植物布阵", WriteField),
        ("僵尸布阵", WriteZombies),
        ("读取场上植物代码", CopyFieldScripts),
        ("读取场上僵尸代码", CopyZombieScripts),
        ("极限僵尸海", () => ZombieSeaEnabled = !ZombieSeaEnabled),
        ("修改阳光", Sun),
        ("锁定阳光", () => LockSun = !LockSun),
        ("修改钱数", Money),
        ("锁定钱数", () => LockMoney = !LockMoney),
        ("清空全部植物", ClearAllPlants),
        ("秒杀全部僵尸", KillAllZombies),
        ("清除所有冰道", ClearIceRoads),
        ("魅惑所有僵尸", MindCtrl),
        ("清除所有坑洞", ClearAllHoles),
        ("生成下一波僵尸", NextWave),
        ("暂停出怪", () => StopSummon = !StopSummon),
        ("僵尸进家不死", () => NoFail = !NoFail),
        ("取消游戏失败", CancelGameLose),
        ("启动所有小推车", StartMower),
        ("生成小推车", CreateMower),
        ("修改关卡名称", LevelName),
        ("显示字幕", ShowingText),
        ("显示悬浮窗", () => TopMostSprite = !TopMostSprite),
        ("显示修改窗口", () =>
        {
            MainWindow.Instance!.BringToFront();
        })
    ];

    public Dictionary<int, string> Plants2
    {
        get
        {
            var result = new Dictionary<int, string>();
            // -1 表示礼盒不指定植物 / 使用游戏原始随机
            result.Add(-1, "-1 : 不指定（默认随机）");
            if (App.InitData != null)
            {
                foreach (var plant in App.InitData.Value.Plants)
                {
                    result.Add(plant.Key, $"{plant.Key} : {plant.Value}");
                }
            }
            return result;
        }
    }

    public Dictionary<int, string> SecondArmor => App.InitData!.Value.SecondArmors;

    public Dictionary<int, string> Zombies
    {
        get
        {
            var result = new Dictionary<int, string>();
            // -1 表示不指定 / 恢复游戏原始随机
            result.Add(-1, "-1 : 默认随机（不指定）");
            if (App.InitData != null)
            {
                foreach (var zombie in App.InitData.Value.Zombies)
                {
                    result.Add(zombie.Key, $"{zombie.Key} : {zombie.Value}");
                }
            }
            return result;
        }
    }

    #endregion ItemSources

    #region Properties

    [ObservableProperty] public partial bool BuffRefreshNoLimit { get; set; }

    [ObservableProperty] public partial bool UnlimitedRefresh { get; set; }

    [ObservableProperty] public partial bool UnlimitedScore { get; set; }

    [ObservableProperty] public partial int BulletDamageType { get; set; }

    [ObservableProperty] public partial double BulletDamageValue { get; set; }

    [ObservableProperty] public partial bool CardNoInit { get; set; }

    [ObservableProperty] public partial bool ChomperNoCD { get; set; }
    
    [ObservableProperty] public partial bool SuperStarNoCD { get; set; }
    [ObservableProperty] public partial bool AutoCutFruit { get; set; }
    [ObservableProperty] public partial bool RandomCard { get; set; }
    [ObservableProperty] public partial bool ColumnGlove { get; set; }
    [ObservableProperty] public partial bool RandomBullet { get; set; }
    [ObservableProperty] public partial bool AutoRhythmGame { get; set; }
    [ObservableProperty] public partial bool StarUpBuff { get; set; }
    [ObservableProperty] public partial bool RandomUpgradeMode { get; set; }

    [ObservableProperty] public partial bool ClearOnWritingField { get; set; }

    [ObservableProperty] public partial bool GaoShuMode { get; set; }

    [ObservableProperty] public partial bool ClearOnWritingVases { get; set; }

    [ObservableProperty] public partial bool ClearOnWritingZombies { get; set; }

    [ObservableProperty] public partial bool ClearOnWritingMix { get; set; }

    [ObservableProperty] public partial bool CobCannonNoCD { get; set; }

    [ObservableProperty] public partial double Col { get; set; }

    [ObservableProperty] public partial bool ColumnPlanting { get; set; }

    [ObservableProperty] public partial bool ConveyBeltModify { get; set; }

    [ObservableProperty] public partial List<KeyValuePair<int, string>> ConveyBeltTypes { get; set; }

    [ObservableProperty] public partial BindingList<TravelBuffVM> Debuffs { get; set; }

    [ObservableProperty] public partial bool DeveloperMode { get; set; }

    [ObservableProperty] public partial bool DevLour { get; set; }

    [ObservableProperty] public partial bool Exchange { get; set; }

    [ObservableProperty] public partial bool FastShooting { get; set; }

    [ObservableProperty] public partial string FieldString { get; set; }

    [ObservableProperty] public partial bool FreeCD { get; set; }

    [ObservableProperty] public partial bool FreePlanting { get; set; }

    [ObservableProperty] public partial double GameSpeed { get; set; }

    [ObservableProperty] public partial bool GameSpeedEnabled { get; set; } = false;

    [ObservableProperty] public partial bool GarlicDay { get; set; }

    [ObservableProperty] public partial double GloveFullCD { get; set; }

    [ObservableProperty] public partial bool GloveFullCDEnabled { get; set; }

    [ObservableProperty] public partial bool GloveNoCD { get; set; }

    [ObservableProperty] public partial double HammerFullCD { get; set; }

    [ObservableProperty] public partial bool HammerFullCDEnabled { get; set; }

    [ObservableProperty] public partial bool HammerNoCD { get; set; }

    [ObservableProperty] public partial bool WheelNoCD { get; set; }

    [ObservableProperty] public partial bool HardPlant { get; set; }

    [ObservableProperty] public partial bool ImmuneForceDeduct { get; set; }

    [ObservableProperty] public partial bool CurseImmunity { get; set; }

    [ObservableProperty] public partial bool CrushImmunity { get; set; }

    [ObservableProperty] public partial bool TrampleImmunity { get; set; }

    [ObservableProperty] public partial bool PickaxeImmunity { get; set; }

    [ObservableProperty] public partial int Health1stType { get; set; }

    [ObservableProperty] public partial double Health1stValue { get; set; }

    [ObservableProperty] public partial int Health2ndType { get; set; }

    [ObservableProperty] public partial double Health2ndValue { get; set; }

    [ObservableProperty] public partial int HealthPlantType { get; set; }

    [ObservableProperty] public partial double HealthPlantValue { get; set; }

    [ObservableProperty] public partial int HealthZombieType { get; set; }

    [ObservableProperty] public partial double HealthZombieValue { get; set; }

    [ObservableProperty] public partial List<HotkeyUIVM> Hotkeys { get; set; }

    [ObservableProperty] public partial bool HyponoEmperorNoCD { get; set; }

    [ObservableProperty] public partial BindingList<TravelBuffVM> InGameBuffs { get; set; }

    /// <summary>
    /// 投资词条 UI 列表（初始）
    /// </summary>
    [ObservableProperty] public partial BindingList<TravelBuffVM> InvestBuffs { get; set; }

    /// <summary>
    /// 投资词条 UI 列表（局内）
    /// </summary>
    [ObservableProperty] public partial BindingList<TravelBuffVM> InGameInvestBuffs { get; set; }

    [ObservableProperty] public partial BindingList<TravelBuffVM> InGameDebuffs { get; set; }
    
    /// <summary>
    /// 所有游戏内词条（包含Advanced、Ultimate和Debuff），用于旗帜波词条选择
    /// </summary>
    [ObservableProperty] public partial BindingList<TravelBuffVM> AllInGameBuffs { get; set; }
    
    /// <summary>
    /// 旗帜波词条功能 - 是否启用
    /// </summary>
    [ObservableProperty]
    private bool _flagWaveBuffEnabled = false;
    partial void OnFlagWaveBuffEnabledChanged(bool value)
    {
        SyncFlagWaveBuffs();
    }
    
    /// <summary>
    /// 旗帜波词条功能 - 要应用的词条ID列表（使用 -1 作为分隔符，表示一个旗子的词条结束）
    /// </summary>
    [ObservableProperty]
    private List<int> _flagWaveBuffIds = new List<int>();
    partial void OnFlagWaveBuffIdsChanged(List<int> value)
    {
        SyncFlagWaveBuffs();
    }
    
    // 旗帜波词条高级配置 - 10个旗帜波，每个旗帜波有词条列表和自定义字幕
    [ObservableProperty] public partial List<int> FlagWave1Buffs { get; set; } = new List<int>();
    [ObservableProperty] public partial string FlagWave1CustomText { get; set; } = "";
    
    [ObservableProperty] public partial List<int> FlagWave2Buffs { get; set; } = new List<int>();
    [ObservableProperty] public partial string FlagWave2CustomText { get; set; } = "";
    
    [ObservableProperty] public partial List<int> FlagWave3Buffs { get; set; } = new List<int>();
    [ObservableProperty] public partial string FlagWave3CustomText { get; set; } = "";
    
    [ObservableProperty] public partial List<int> FlagWave4Buffs { get; set; } = new List<int>();
    [ObservableProperty] public partial string FlagWave4CustomText { get; set; } = "";
    
    [ObservableProperty] public partial List<int> FlagWave5Buffs { get; set; } = new List<int>();
    [ObservableProperty] public partial string FlagWave5CustomText { get; set; } = "";
    
    [ObservableProperty] public partial List<int> FlagWave6Buffs { get; set; } = new List<int>();
    [ObservableProperty] public partial string FlagWave6CustomText { get; set; } = "";
    
    [ObservableProperty] public partial List<int> FlagWave7Buffs { get; set; } = new List<int>();
    [ObservableProperty] public partial string FlagWave7CustomText { get; set; } = "";
    
    [ObservableProperty] public partial List<int> FlagWave8Buffs { get; set; } = new List<int>();
    [ObservableProperty] public partial string FlagWave8CustomText { get; set; } = "";
    
    [ObservableProperty] public partial List<int> FlagWave9Buffs { get; set; } = new List<int>();
    [ObservableProperty] public partial string FlagWave9CustomText { get; set; } = "";
    
    [ObservableProperty] public partial List<int> FlagWave10Buffs { get; set; } = new List<int>();
    [ObservableProperty] public partial string FlagWave10CustomText { get; set; } = "";
    
    // 当任何旗帜波配置改变时，同步到游戏
    partial void OnFlagWave1BuffsChanged(List<int> value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave1CustomTextChanged(string value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave2BuffsChanged(List<int> value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave2CustomTextChanged(string value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave3BuffsChanged(List<int> value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave3CustomTextChanged(string value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave4BuffsChanged(List<int> value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave4CustomTextChanged(string value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave5BuffsChanged(List<int> value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave5CustomTextChanged(string value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave6BuffsChanged(List<int> value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave6CustomTextChanged(string value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave7BuffsChanged(List<int> value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave7CustomTextChanged(string value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave8BuffsChanged(List<int> value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave8CustomTextChanged(string value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave9BuffsChanged(List<int> value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave9CustomTextChanged(string value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave10BuffsChanged(List<int> value) { SyncAdvancedFlagWaveBuffs(); }
    partial void OnFlagWave10CustomTextChanged(string value) { SyncAdvancedFlagWaveBuffs(); }
    
    /// <summary>
    /// 同步高级旗帜波词条配置到游戏
    /// </summary>
    private void SyncAdvancedFlagWaveBuffs()
    {
        // 将10个旗帜波的配置合并为FlagWaveBuffIds（使用-1分隔符）
        var allBuffs = new List<int>();
        var allCustomTexts = new List<string>();
        
        var waveBuffs = new[] { FlagWave1Buffs, FlagWave2Buffs, FlagWave3Buffs, FlagWave4Buffs, FlagWave5Buffs,
                                FlagWave6Buffs, FlagWave7Buffs, FlagWave8Buffs, FlagWave9Buffs, FlagWave10Buffs };
        var waveTexts = new[] { FlagWave1CustomText, FlagWave2CustomText, FlagWave3CustomText, FlagWave4CustomText, FlagWave5CustomText,
                                FlagWave6CustomText, FlagWave7CustomText, FlagWave8CustomText, FlagWave9CustomText, FlagWave10CustomText };
        
        for (int i = 0; i < waveBuffs.Length; i++)
        {
            if (waveBuffs[i] != null && waveBuffs[i].Count > 0)
            {
                allBuffs.AddRange(waveBuffs[i]);
            }
            allBuffs.Add(-1); // 分隔符
            
            allCustomTexts.Add(waveTexts[i] ?? "");
        }
        
        // 更新FlagWaveBuffIds（但不触发OnFlagWaveBuffIdsChanged，避免循环）
        // 注意：这里直接设置字段，避免触发属性变更通知
        var field = typeof(ModifierViewModel).GetField("_flagWaveBuffIds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(this, allBuffs);
        }
        
        // 发送到游戏
        App.DataSync.Value.SendData(new InGameActions
        {
            FlagWaveBuffEnabled = FlagWaveBuffEnabled,
            FlagWaveBuffIds = allBuffs,
            FlagWaveCustomTexts = allCustomTexts
        });
        
        System.Diagnostics.Debug.WriteLine($"[旗帜波词条高级] 已同步配置: {allBuffs.Count}个词条ID, {allCustomTexts.Count}个自定义字幕");
    }
    
    /// <summary>
    /// 旗帜波词条功能：不再支持自定义文本，游戏内会自动显示“解锁的词条名”
    /// </summary>

    [ObservableProperty] public partial BindingList<InGameHotkeyUIVM> InGameHotkeys { get; set; }

    [ObservableProperty] public partial bool IsMindCtrl { get; set; }

    [ObservableProperty] public partial bool ItemExistForever { get; set; }

    [ObservableProperty] public partial int ItemType { get; set; }

    [ObservableProperty] public partial bool JackboxNotExplode { get; set; }

    [ObservableProperty] public partial int LockBulletType { get; set; }

    [ObservableProperty] public partial bool LockMoney { get; set; }

    [ObservableProperty] public partial int LockPresent { get; set; }

    [ObservableProperty] public partial int LockWheat { get; set; }
    [ObservableProperty] public partial int LockPresent1 { get; set; }
    [ObservableProperty] public partial int LockPresent2 { get; set; }
    [ObservableProperty] public partial int LockPresent3 { get; set; }
    [ObservableProperty] public partial int LockPresent4 { get; set; }
    [ObservableProperty] public partial int LockPresent5 { get; set; }

    /// <summary>
    /// PvE 斗蛐蛐布阵：盲盒僵尸置顶（-1 表示不置顶，沿用游戏原始随机）
    /// </summary>
    [ObservableProperty] public partial int PvEBlindBoxZombie1 { get; set; }
    [ObservableProperty] public partial int PvEBlindBoxZombie2 { get; set; }
    [ObservableProperty] public partial int PvEBlindBoxZombie3 { get; set; }
    [ObservableProperty] public partial int PvEBlindBoxZombie4 { get; set; }
    [ObservableProperty] public partial int PvEBlindBoxZombie5 { get; set; }
    [ObservableProperty] public partial int PvEBlindBoxZombie6 { get; set; }

    [ObservableProperty] public partial bool LockSun { get; set; }

    [ObservableProperty] public partial bool MineNoCD { get; set; }

    [ObservableProperty] public partial bool NeedSave { get; set; }

    [ObservableProperty] public partial string NewLevelName { get; set; }

    [ObservableProperty] public partial double NewMoney { get; set; }

    [ObservableProperty] public partial double NewSun { get; set; }

    [ObservableProperty] public partial double NewZombieUpdateCD { get; set; }

    [ObservableProperty] public partial bool NoFail { get; set; }

    [ObservableProperty] public partial bool NoHole { get; set; }

    [ObservableProperty] public partial bool NoIceRoad { get; set; }

    [ObservableProperty] public partial bool DisableIceEffect { get; set; }

    [ObservableProperty] public partial bool PotSmashingFix { get; set; }

    [ObservableProperty] public partial bool UnlimitedSunlight { get; set; }

    [ObservableProperty] public partial bool MagnetNutUnlimited { get; set; }

    [ObservableProperty] public partial bool ZombieDamageLimit200 { get; set; }

    [ObservableProperty] public partial int ZombieDamageLimitValue { get; set; } = 100;

    [ObservableProperty] public partial bool ZombieSpeedModifyEnabled { get; set; }

    [ObservableProperty] public partial float ZombieSpeedMultiplier { get; set; } = 1.0f;

    [ObservableProperty] public partial bool ZombieAttackMultiplierEnabled { get; set; }
    [ObservableProperty] public partial float ZombieAttackMultiplier { get; set; } = 1.0f;

    /// <summary>僵尸血量倍率开关（插件侧单哨兵：关=-1，开=Multiplier）</summary>
    [ObservableProperty] public partial bool ZombieHealthMultiplierEnabled { get; set; }
    /// <summary>僵尸血量倍率数值（勾选时才下发）</summary>
    [ObservableProperty] public partial float ZombieHealthMultiplier { get; set; } = 2.0f;
    /// <summary>一次性命令：对场上全体僵尸按此倍率缩放血量（按钮触发，不参与存档恢复下发）</summary>
    [ObservableProperty] public partial float ZombieHealthRatio { get; set; } = 2.0f;

    // ---- 植物速度/攻击/血量三件套（5.3.1 #7 + 缺号6；与僵尸三倍率属性并排）----
    /// <summary>植物速度倍率开关（勾选后新种植物的速度/攻速按右侧倍率缩放）</summary>
    [ObservableProperty] public partial bool PlantSpeedMultiplierEnabled { get; set; }
    [ObservableProperty] public partial float PlantSpeedMultiplier { get; set; } = 1.0f;
    [ObservableProperty] public partial bool PlantAttackMultiplierEnabled { get; set; }
    [ObservableProperty] public partial float PlantAttackMultiplier { get; set; } = 1.0f;
    [ObservableProperty] public partial bool PlantHealthMultiplierEnabled { get; set; }
    [ObservableProperty] public partial float PlantHealthMultiplier { get; set; } = 1.0f;

    [ObservableProperty] public partial bool ZombieBulletReflectEnabled { get; set; }

    [ObservableProperty] public partial float ZombieBulletReflectChance { get; set; } = 10.0f;

    [ObservableProperty] public partial bool ZombieReviveDebuffCustomEnabled { get; set; }

    [ObservableProperty] public partial float ZombieReviveDebuffChance { get; set; } = 33.333f;

    [ObservableProperty] public partial bool ZombieFreeReviveEnabled { get; set; }

    [ObservableProperty] public partial float ZombieFreeReviveChance { get; set; } = 33.333f;

    [ObservableProperty] public partial bool UnlimitedCardSlots { get; set; }

    [ObservableProperty] public partial bool ZombieStatusCoexist { get; set; }

    // 局内存档/回溯
    // 自动快照与失败自动回溯已下线

    [ObservableProperty] public partial bool MNEntryEnabled { get; set; }

    [ObservableProperty] public partial bool UnlockRedCardPlants { get; set; }

    [ObservableProperty] public partial bool KillUpgrade { get; set; }

    [ObservableProperty] public partial bool ZombieImmuneAllDebuffs { get; set; }

    // 僵尸免疫效果 - 分开的9个开关
    [ObservableProperty] public partial bool ZombieImmuneFreeze { get; set; }
    [ObservableProperty] public partial bool ZombieImmuneCold { get; set; }
    [ObservableProperty] public partial bool ZombieImmuneButter { get; set; }
    [ObservableProperty] public partial bool ZombieImmunePoison { get; set; }
    [ObservableProperty] public partial bool ZombieImmuneJalaed { get; set; }
    [ObservableProperty] public partial bool ZombieImmuneEmbered { get; set; }
    [ObservableProperty] public partial bool ZombieImmuneKnockback { get; set; }
    [ObservableProperty] public partial bool ZombieImmuneMindControl { get; set; }
    [ObservableProperty] public partial bool ZombieImmuneDevour { get; set; }

    [ObservableProperty] public partial bool PlantingNoCD { get; set; }

    [ObservableProperty] public partial int PlantType { get; set; }

    [ObservableProperty] public partial bool PlantUpgrade { get; set; }

    [ObservableProperty] public partial bool PresentFastOpen { get; set; }

    [ObservableProperty] public partial double Row { get; set; }

    [ObservableProperty] public partial bool ScaredyDream { get; set; }
    [ObservableProperty] public partial bool PvPPotRange { get; set; }

    [ObservableProperty] public partial bool SeedRain { get; set; }

    [ObservableProperty] public partial bool Shooting1 { get; set; }

    [ObservableProperty] public partial bool Shooting2 { get; set; }

    [ObservableProperty] public partial bool Shooting3 { get; set; }

    [ObservableProperty] public partial bool Shooting4 { get; set; }

    [ObservableProperty] public partial string ShowText { get; set; }

    [ObservableProperty] public partial bool StopSummon { get; set; }

    [ObservableProperty] public partial bool SuperPresent { get; set; }

    [ObservableProperty] public partial double Times { get; set; }

    [ObservableProperty] public partial bool TopMostSprite { get; set; }

    // ★ 2026-09-25 默认改为 true（用户要求「修改器的动画效果默认开启」）。
    //   老存档里存的是 false，仅改默认值对老安装无效 ⇒ 由 AnimationDefaultMigrated 一次性迁移，
    //   迁移后用户仍可自由关掉（关掉后不会再被改回来）。
    [ObservableProperty] public partial bool EnableAnimations { get; set; } = true;

    [ObservableProperty] public partial bool IsDarkMode { get; set; } = false;

    [RelayCommand]
    private void ToggleDarkMode()
    {
        // 切换深色/浅色模式
        IsDarkMode = !IsDarkMode;
    }

    [ObservableProperty] public partial BindingList<TravelBuffVM> TravelBuffs { get; set; }

    [ObservableProperty] public partial bool UltimateRamdomZombie { get; set; }

    [ObservableProperty] public partial bool UltimateSuperGatling { get; set; }

    [ObservableProperty] public partial bool UndeadBullet { get; set; }
    
    [ObservableProperty] public partial bool OldObsidianBullet { get; set; }

    [ObservableProperty] public partial bool UnlockAllFusions { get; set; }

    // ---- 4.0 新增功能（协议字段早已在 DataDef 里，这里补齐用户可见的开关）----
    /// <summary>解锁全部选卡 - 强制 PlantDataManager 把每株植物判为已解锁</summary>
    [ObservableProperty] public partial bool EnableAllCards { get; set; }
    /// <summary>植物子弹秒杀僵尸 - Zombie.ApplyDamage 前缀命中即 Die</summary>
    [ObservableProperty] public partial bool HardBullet { get; set; }
    /// <summary>植物全升级 - 每帧把场上植物升到 3 级</summary>
    [ObservableProperty] public partial bool PlantsAllUpgrade { get; set; }
    /// <summary>植物全星辉 - 每帧给场上植物上星辉</summary>
    [ObservableProperty] public partial bool PlantsAllStarUp { get; set; }
    /// <summary>锁定全场光照等级（-1 = 关闭）</summary>
    [ObservableProperty] public partial int LockLightLevel { get; set; } = -1;
    /// <summary>秒杀指定行僵尸时要杀的行号（UI 1-based，协议侧自行转 0-based）</summary>
    [ObservableProperty] public partial int KillZombiesRow { get; set; } = 1;

    [ObservableProperty] public partial string VasesFieldString { get; set; }

    [ObservableProperty] public partial string ZombieFieldString { get; set; }

    [ObservableProperty] public partial string MixFieldString { get; set; }

    [ObservableProperty] public partial double ZombieSeaCD { get; set; }

    [ObservableProperty] public partial bool ZombieSeaEnabled { get; set; }

    [ObservableProperty] public partial bool ZombieSeaLowEnabled { get; set; }

    [ObservableProperty] public partial List<KeyValuePair<int, string>> ZombieSeaTypes { get; set; }

    [ObservableProperty] public partial int ZombieType { get; set; }
    
    /// <summary>
    /// 特效ID（用于检索分区播放特效）
    /// </summary>
    [ObservableProperty] public partial string ParticleId { get; set; } = "";
    
    /// <summary>
    /// 音效ID（用于检索分区播放音效）
    /// </summary>
    [ObservableProperty] public partial string SoundId { get; set; } = "";

    // 诸神：进化
    [ObservableProperty] public partial bool GodEvolutionUnlimitedRefresh { get; set; }
    [ObservableProperty] public partial bool GodEvolutionFreeUpgradeQuality { get; set; }
    [ObservableProperty] public partial bool GodEvolutionLuckyEnabled { get; set; }
    [ObservableProperty] public partial float GodEvolutionLucky { get; set; } = 1f;
    [ObservableProperty] public partial bool GodEvolutionDifficultyEnabled { get; set; }
    [ObservableProperty] public partial int GodEvolutionDifficulty { get; set; }
    [ObservableProperty] public partial bool GodEvolutionRefreshCountEnabled { get; set; }
    [ObservableProperty] public partial int GodEvolutionRefreshCount { get; set; } = 9999999;
    [ObservableProperty] public partial bool GodEvolutionMaxPlantCountEnabled { get; set; }
    [ObservableProperty] public partial int GodEvolutionMaxPlantCount { get; set; } = 99;
    [ObservableProperty] public partial bool GodEvolutionOptionCountEnabled { get; set; }
    [ObservableProperty] public partial int GodEvolutionOptionCount { get; set; } = 3;
    [ObservableProperty] public partial bool GodEvolutionUpgradeBuffChanceEnabled { get; set; }
    [ObservableProperty] public partial int GodEvolutionUpgradeBuffChance { get; set; } = 100;
    [ObservableProperty] public partial bool GodEvolutionSuperUpgrade { get; set; }
    [ObservableProperty] public partial bool GodEvolutionForceSuperQuality { get; set; }
    [ObservableProperty] public partial bool GodEvolutionUncrashable { get; set; }
    [ObservableProperty] public partial bool GodEvolutionQualityWeightEnabled { get; set; }
    [ObservableProperty] public partial float GodEvolutionQualityDefault { get; set; } = 65f;
    [ObservableProperty] public partial float GodEvolutionQualitySilver { get; set; } = 23f;
    [ObservableProperty] public partial float GodEvolutionQualityGold { get; set; } = 10f;
    [ObservableProperty] public partial float GodEvolutionQualityDiamond { get; set; } = 2f;
    [ObservableProperty] public partial bool GodEvolutionDamageMultiplierEnabled { get; set; }
    [ObservableProperty] public partial float GodEvolutionDamageMultiplier { get; set; } = 1f;

    /// <summary>诸神进化试炼词条概率大幅提升（词条池 AdvBuff 14000~14003）</summary>
    [ObservableProperty] public partial bool GodEvolutionForceMissionBuff { get; set; }

    /// <summary>诸神进化战术词条概率大幅提升</summary>
    [ObservableProperty] public partial bool GodEvolutionForceTacticalBuff { get; set; }

    /// <summary>诸神进化隐藏难度（等价游戏 shoothard 作弊码；进关卡时自动开启）</summary>
    [ObservableProperty] public partial bool GodEvolutionCheatHard { get; set; }

    /// <summary>诸神进化专家邀请词条概率大幅提升</summary>
    [ObservableProperty] public partial bool GodEvolutionForceExpertBuff { get; set; }

    /// <summary>诸神进化超进化（星辉）词条概率大幅提升</summary>
    [ObservableProperty] public partial bool GodEvolutionForceStarUpBuff { get; set; }

    /// <summary>诸神进化质变词条概率大幅提升</summary>
    [ObservableProperty] public partial bool GodEvolutionForceMutationBuff { get; set; }

    /// <summary>诸神进化棱彩词条概率大幅提升</summary>
    [ObservableProperty] public partial bool GodEvolutionForceIridescentBuff { get; set; }

    /// <summary>诸神进化随机词条概率大幅提升</summary>
    [ObservableProperty] public partial bool GodEvolutionForceRandomBuff { get; set; }

    /// <summary>诸神币输入框（点「应用」才下发，避免每敲一个数字就写一次存档）</summary>
    [ObservableProperty] public partial int NewGodCoin { get; set; }

    private GodEvolutionProperties BuildGodEvolutionProperties(bool applyNow = false) => new()
    {
        UnlimitedRefresh = GodEvolutionUnlimitedRefresh,
        FreeUpgradeQuality = GodEvolutionFreeUpgradeQuality,
        LuckyEnabled = GodEvolutionLuckyEnabled,
        Lucky = GodEvolutionLucky,
        DifficultyEnabled = GodEvolutionDifficultyEnabled,
        Difficulty = GodEvolutionDifficulty,
        RefreshCountEnabled = GodEvolutionRefreshCountEnabled,
        RefreshCount = GodEvolutionRefreshCount,
        MaxPlantCountEnabled = GodEvolutionMaxPlantCountEnabled,
        MaxPlantCount = GodEvolutionMaxPlantCount,
        OptionCountEnabled = GodEvolutionOptionCountEnabled,
        OptionCount = GodEvolutionOptionCount,
        UpgradeBuffChanceEnabled = GodEvolutionUpgradeBuffChanceEnabled,
        UpgradeBuffChance = GodEvolutionUpgradeBuffChance,
        SuperUpgrade = GodEvolutionSuperUpgrade,
        ForceSuperQuality = GodEvolutionForceSuperQuality,
        Uncrashable = GodEvolutionUncrashable,
        QualityWeightEnabled = GodEvolutionQualityWeightEnabled,
        QualityDefault = GodEvolutionQualityDefault,
        QualitySilver = GodEvolutionQualitySilver,
        QualityGold = GodEvolutionQualityGold,
        QualityDiamond = GodEvolutionQualityDiamond,
        DamageMultiplierEnabled = GodEvolutionDamageMultiplierEnabled,
        DamageMultiplier = GodEvolutionDamageMultiplier,
        ForceMissionBuff = GodEvolutionForceMissionBuff,
        ForceTacticalBuff = GodEvolutionForceTacticalBuff,
        CheatHard = GodEvolutionCheatHard,
        ForceExpertBuff = GodEvolutionForceExpertBuff,
        ForceStarUpBuff = GodEvolutionForceStarUpBuff,
        ForceMutationBuff = GodEvolutionForceMutationBuff,
        ForceIridescentBuff = GodEvolutionForceIridescentBuff,
        ForceRandomBuff = GodEvolutionForceRandomBuff,
        ApplyNow = applyNow
    };

    private void SyncGodEvolution() =>
        App.DataSync.Value.SendData(BuildGodEvolutionProperties());

    [RelayCommand]
    public void ApplyGodEvolutionNow() =>
        App.DataSync.Value.SendData(BuildGodEvolutionProperties(applyNow: true));

    [RelayCommand]
    public void ResetGodEvolutionDefaults()
    {
        GodEvolutionUnlimitedRefresh = false;
        GodEvolutionFreeUpgradeQuality = false;
        GodEvolutionLuckyEnabled = false;
        GodEvolutionLucky = 1f;
        GodEvolutionDifficultyEnabled = false;
        GodEvolutionDifficulty = 0;
        GodEvolutionRefreshCountEnabled = false;
        GodEvolutionRefreshCount = 9999999;
        GodEvolutionMaxPlantCountEnabled = false;
        GodEvolutionMaxPlantCount = 99;
        GodEvolutionOptionCountEnabled = false;
        GodEvolutionOptionCount = 3;
        GodEvolutionUpgradeBuffChanceEnabled = false;
        GodEvolutionUpgradeBuffChance = 100;
        GodEvolutionSuperUpgrade = false;
        GodEvolutionForceSuperQuality = false;
        GodEvolutionUncrashable = false;
        GodEvolutionQualityWeightEnabled = false;
        // #29 词条权重默认值须与游戏一致（游戏实际权重 65/23/10/2），原 1/1/1/1 会使四档等概率
        GodEvolutionQualityDefault = 65f;
        GodEvolutionQualitySilver = 23f;
        GodEvolutionQualityGold = 10f;
        GodEvolutionQualityDiamond = 2f;
        GodEvolutionDamageMultiplierEnabled = false;
        GodEvolutionDamageMultiplier = 1f;
        SyncGodEvolution();
    }

    partial void OnGodEvolutionUnlimitedRefreshChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionFreeUpgradeQualityChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionLuckyEnabledChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionLuckyChanged(float value) => SyncGodEvolution();
    partial void OnGodEvolutionDifficultyEnabledChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionDifficultyChanged(int value) => SyncGodEvolution();
    partial void OnGodEvolutionRefreshCountEnabledChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionRefreshCountChanged(int value) => SyncGodEvolution();
    partial void OnGodEvolutionMaxPlantCountEnabledChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionMaxPlantCountChanged(int value) => SyncGodEvolution();
    partial void OnGodEvolutionOptionCountEnabledChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionOptionCountChanged(int value) => SyncGodEvolution();
    partial void OnGodEvolutionUpgradeBuffChanceEnabledChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionUpgradeBuffChanceChanged(int value) => SyncGodEvolution();
    partial void OnGodEvolutionSuperUpgradeChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionForceSuperQualityChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionUncrashableChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionQualityWeightEnabledChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionQualityDefaultChanged(float value) => SyncGodEvolution();
    partial void OnGodEvolutionQualitySilverChanged(float value) => SyncGodEvolution();
    partial void OnGodEvolutionQualityGoldChanged(float value) => SyncGodEvolution();
    partial void OnGodEvolutionQualityDiamondChanged(float value) => SyncGodEvolution();
    partial void OnGodEvolutionDamageMultiplierEnabledChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionDamageMultiplierChanged(float value) => SyncGodEvolution();
    // 八个开关随 GodEvolutionProperties 整体下发（试炼/战术/隐藏难度 + 概率族 5 项：专家邀请/超进化/质变/棱彩/随机），
    // 游戏侧各自在对应补丁/进关卡钩子里消费。
    partial void OnGodEvolutionForceMissionBuffChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionForceTacticalBuffChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionCheatHardChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionForceExpertBuffChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionForceStarUpBuffChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionForceMutationBuffChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionForceIridescentBuffChanged(bool value) => SyncGodEvolution();
    partial void OnGodEvolutionForceRandomBuffChanged(bool value) => SyncGodEvolution();

    #endregion Properties

    /// <summary>
    /// 播放特效
    /// </summary>
    public void PlayParticle(int particleId)
    {
        App.DataSync.Value.SendData(new InGameActions { PlayParticleId = particleId });
    }

    /// <summary>
    /// 播放音效
    /// </summary>
    public void PlaySound(int soundId)
    {
        App.DataSync.Value.SendData(new InGameActions { PlaySoundId = soundId });
    }

    // 相关回调移除

    /// <summary>
    /// 从更新后的InitData重新加载词条列表（包括MOD添加的词条）
    /// </summary>
    public void ReloadBuffsFromInitData()
    {
        if (App.InitData == null)
        {
            System.Diagnostics.Debug.WriteLine("ReloadBuffsFromInitData: App.InitData 为 null");
            return;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"ReloadBuffsFromInitData: 开始重新加载 - AdvBuffs={App.InitData.Value.AdvBuffs?.Length ?? 0}, UltiBuffs={App.InitData.Value.UltiBuffs?.Length ?? 0}, InvestBuffs={App.InitData.Value.InvestBuffs?.Length ?? 0}, Debuffs={App.InitData.Value.Debuffs?.Length ?? 0}");
            
            NeedSync = false;

            // 清空现有词条列表
            TravelBuffs.Clear();
            InGameBuffs.Clear();
            InvestBuffs.Clear();
            InGameInvestBuffs.Clear();
            Debuffs.Clear();
            InGameDebuffs.Clear();

            // 重新加载Advanced Buffs
            var bi = 0;
            if (App.InitData.Value.AdvBuffs != null)
            {
            foreach (var b in App.InitData.Value.AdvBuffs)
            {
                    // 确保词条文本不为空，如果为空则使用占位符
                    string buffText = string.IsNullOrEmpty(b) ? $"#{bi} AdvBuff_{bi}" : b;
                    TravelBuffs.Add(new TravelBuffVM(new TravelBuff(bi, buffText, false, false)));
                    InGameBuffs.Add(new TravelBuffVM(new TravelBuff(bi, buffText, true, false)));
                bi++;
                }
            }

            // 重新加载Ultimate Buffs
            if (App.InitData.Value.UltiBuffs != null)
            {
            foreach (var b in App.InitData.Value.UltiBuffs)
            {
                    // 确保词条文本不为空，如果为空则使用占位符
                    string buffText = string.IsNullOrEmpty(b) ? $"#{bi} UltiBuff_{bi}" : b;
                    TravelBuffs.Add(new TravelBuffVM(new TravelBuff(bi, buffText, false, false)));
                    InGameBuffs.Add(new TravelBuffVM(new TravelBuff(bi, buffText, true, false)));
                bi++;
                }
            }

            // 重新加载 Invest Buffs（投资词条） - 单独分区
            if (App.InitData.Value.InvestBuffs is not null)
            {
                foreach (var b in App.InitData.Value.InvestBuffs)
                {
                    // 确保词条文本不为空，如果为空则使用占位符
                    string buffText = string.IsNullOrEmpty(b) ? $"#{bi} InvestBuff_{bi}" : b;
                    InvestBuffs.Add(new TravelBuffVM(new TravelBuff(bi, buffText, false, false)));
                    InGameInvestBuffs.Add(new TravelBuffVM(new TravelBuff(bi, buffText, true, false)));
                    bi++;
                }
            }

            // 重新加载Debuffs
            var di = 0;
            foreach (var d in App.InitData.Value.Debuffs)
            {
                Debuffs.Add(new TravelBuffVM(new TravelBuff(di, d, true, true)));
                InGameDebuffs.Add(new TravelBuffVM(new TravelBuff(di, d, true, true)));
                di++;
            }

            // 更新合并列表 - 先清空再添加，确保UI更新
            if (AllInGameBuffs == null)
            {
                AllInGameBuffs = new BindingList<TravelBuffVM>();
            }
            else
            {
                AllInGameBuffs.Clear();
            }
            foreach (var buff in InGameBuffs)
                AllInGameBuffs.Add(buff);
            foreach (var invest in InGameInvestBuffs)
                AllInGameBuffs.Add(invest);
            foreach (var debuff in InGameDebuffs)
                AllInGameBuffs.Add(debuff);

            NeedSync = true;
            
            // 更新 Plants 字典（静态属性）
            if (Plants == null)
            {
                Plants = new Dictionary<int, string> { { -1, "-1 : 不修改" } };
            }
            else
            {
                Plants.Clear();
                Plants.Add(-1, "-1 : 不修改");
            }
            if (App.InitData != null)
            {
                foreach (var kp in App.InitData.Value.Plants)
                {
                    Plants.Add(kp.Key, kp.Value);
                }
            }
            
            // 更新 Bullets2（包含ID格式）
            if (Bullets2 == null)
            {
                Bullets2 = new Dictionary<int, string>
                {
                    { -2, "-2 : 不修改" },
                    { -1, "-1 : 随机子弹" }
                };
            }
            else
            {
                Bullets2.Clear();
                Bullets2.Add(-2, "-2 : 不修改");
                Bullets2.Add(-1, "-1 : 随机子弹");
            }
            if (App.InitData != null)
            {
                foreach (var b in App.InitData.Value.Bullets)
                {
                    Bullets2.Add(b.Key, $"{b.Key} : {b.Value}");
                }
            }
            
            // 更新 Health1sts, Health2nds, HealthPlants, HealthZombies
            if (App.InitData != null)
            {
                Health1sts.Clear();
                Health2nds.Clear();
                HealthPlants.Clear();
                HealthZombies.Clear();
                foreach (var h1 in App.InitData.Value.FirstArmors) Health1sts.Add(h1.Key, -1);
                foreach (var h2 in App.InitData.Value.SecondArmors) Health2nds.Add(h2.Key, -1);
                foreach (var h3 in App.InitData.Value.Plants) HealthPlants.Add(h3.Key, -1);
                foreach (var h4 in App.InitData.Value.Zombies) HealthZombies.Add(h4.Key, -1);
            }
            
            // 通知所有依赖 InitData 的属性已更改，强制UI刷新ComboBox等控件
            OnPropertyChanged(nameof(Plants2));
            OnPropertyChanged(nameof(Zombies));
            OnPropertyChanged(nameof(Plants));
            OnPropertyChanged(nameof(Bullets));
            OnPropertyChanged(nameof(Bullets2));
            OnPropertyChanged(nameof(FirstArmor));
            OnPropertyChanged(nameof(SecondArmor));
            OnPropertyChanged(nameof(Items));
            OnPropertyChanged(nameof(Health1sts));
            OnPropertyChanged(nameof(Health2nds));
            OnPropertyChanged(nameof(HealthPlants));
            OnPropertyChanged(nameof(HealthZombies));
            
            System.Diagnostics.Debug.WriteLine($"ReloadBuffsFromInitData: 完成 - TravelBuffs={TravelBuffs.Count}, InGameBuffs={InGameBuffs.Count}, Debuffs={Debuffs.Count}, AllInGameBuffs={AllInGameBuffs.Count}");

            if (_loadedSaveModel is ModifierSaveModel loadedSave)
            {
                RestoreSavedTravelBuffStates(loadedSave);
            }

            ApplyPendingSaveIfNeeded();
            IsLoading = false;
        }
        catch (Exception ex)
        {
            // 记录错误但不中断程序
            System.Diagnostics.Debug.WriteLine($"ReloadBuffsFromInitData 错误: {ex.Message}\n{ex.StackTrace}");
        }
    }
}

public partial class TravelBuff : ObservableObject, INotifyPropertyChanged
{
    public TravelBuff(int index, string text, bool inGame, bool debuff)
    {
        Text = text;
        Index = index;
        InGame = inGame;
        Debuff = debuff;
        // 从文本中解析原始ID（如果格式是 "#48 词条名"）
        OriginalId = ParseOriginalIdFromText(text, index);
    }

    public TravelBuff()
    {
    }

    /// <summary>
    /// 从词条文本中解析原始ID（游戏中的字典键）
    /// 格式： "#48 词条名" -> 48
    /// 如果没有 # 前缀，则使用 index 作为原始ID（向后兼容旧数据）
    /// </summary>
    private static int ParseOriginalIdFromText(string text, int index)
    {
        if (string.IsNullOrEmpty(text))
            return index;
        
        // 如果文本以 # 开头，尝试解析ID
        if (text.StartsWith("#"))
        {
            // 提取 # 后面的数字（直到遇到空格或字符串结束）
            int spaceIndex = text.IndexOf(' ');
            if (spaceIndex > 1)
            {
                string idStr = text.Substring(1, spaceIndex - 1);
                if (int.TryParse(idStr, out int originalId))
                {
                    System.Diagnostics.Debug.WriteLine($"ParseOriginalIdFromText: 从文本 '{text}' 解析出 OriginalId={originalId} (index={index})");
                    return originalId;
                }
            }
            else if (text.Length > 1)
            {
                // 如果没有空格，尝试解析整个 # 后面的部分
                string idStr = text.Substring(1);
                if (int.TryParse(idStr, out int originalId))
                {
                    System.Diagnostics.Debug.WriteLine($"ParseOriginalIdFromText: 从文本 '{text}' 解析出 OriginalId={originalId} (index={index})");
                    return originalId;
                }
            }
        }
        
        // 如果没有 # 前缀或解析失败，使用 index 作为原始ID（向后兼容）
        System.Diagnostics.Debug.WriteLine($"ParseOriginalIdFromText: 文本 '{text}' 没有 # 前缀，使用 index={index} 作为 OriginalId");
        return index;
    }

    public bool Debuff { get; set; }

    [ObservableProperty] public partial bool Enabled { get; set; }

    public int Index { get; set; }
    
    /// <summary>
    /// 游戏中的原始ID（字典键），用于旗帜波词条功能
    /// </summary>
    public int OriginalId { get; set; }
    
    public bool InGame { get; set; }

    [JsonIgnore] public string Text { get; set; } = "";
}

public partial class TravelBuffVM(TravelBuff TravelBuff) : ObservableObject
{
    public bool Enabled
    {
        get => TravelBuff.Enabled;
        set
        {
            SetProperty(TravelBuff.Enabled, value, TravelBuff, (t, e) => t.Enabled = e);
            OnPropertyChanged(new PropertyChangedEventArgs("IsChecked"));
        }
    }

    [ObservableProperty] public partial TravelBuff TravelBuff { get; set; } = TravelBuff;
}

//copy from UnityEngine.KeyCode
public enum KeyCode
{
    None = 0,
    Backspace = 8,
    Delete = 127,
    Tab = 9,
    Clear = 12,
    Return = 13,
    Pause = 19,
    Escape = 27,
    Space = 32,
    Keypad0 = 256,
    Keypad1 = 257,
    Keypad2 = 258,
    Keypad3 = 259,
    Keypad4 = 260,
    Keypad5 = 261,
    Keypad6 = 262,
    Keypad7 = 263,
    Keypad8 = 264,
    Keypad9 = 265,
    KeypadPeriod = 266,
    KeypadDivide = 267,
    KeypadMultiply = 268,
    KeypadMinus = 269,
    KeypadPlus = 270,
    KeypadEnter = 271,
    KeypadEquals = 272,
    UpArrow = 273,
    DownArrow = 274,
    RightArrow = 275,
    LeftArrow = 276,
    Insert = 277,
    Home = 278,
    End = 279,
    PageUp = 280,
    PageDown = 281,
    F1 = 282,
    F2 = 283,
    F3 = 284,
    F4 = 285,
    F5 = 286,
    F6 = 287,
    F7 = 288,
    F8 = 289,
    F9 = 290,
    F10 = 291,
    F11 = 292,
    F12 = 293,
    F13 = 294,
    F14 = 295,
    F15 = 296,
    Alpha0 = 48,
    Alpha1 = 49,
    Alpha2 = 50,
    Alpha3 = 51,
    Alpha4 = 52,
    Alpha5 = 53,
    Alpha6 = 54,
    Alpha7 = 55,
    Alpha8 = 56,
    Alpha9 = 57,
    Exclaim = 33,
    DoubleQuote = 34,
    Hash = 35,
    Dollar = 36,
    Percent = 37,
    Ampersand = 38,
    Quote = 39,
    LeftParen = 40,
    RightParen = 41,
    Asterisk = 42,
    Plus = 43,
    Comma = 44,
    Minus = 45,
    Period = 46,
    Slash = 47,
    Colon = 58,
    Semicolon = 59,
    Less = 60,
    Equals = 61,
    Greater = 62,
    Question = 63,
    At = 64,
    LeftBracket = 91,
    Backslash = 92,
    RightBracket = 93,
    Caret = 94,
    Underscore = 95,
    BackQuote = 96,
    A = 97,
    B = 98,
    C = 99,
    D = 100,
    E = 101,
    F = 102,
    G = 103,
    H = 104,
    I = 105,
    J = 106,
    K = 107,
    L = 108,
    M = 109,
    N = 110,
    O = 111,
    P = 112,
    Q = 113,
    R = 114,
    S = 115,
    T = 116,
    U = 117,
    V = 118,
    W = 119,
    X = 120,
    Y = 121,
    Z = 122,
    LeftCurlyBracket = 123,
    Pipe = 124,
    RightCurlyBracket = 125,
    Tilde = 126,
    Numlock = 300,
    CapsLock = 301,
    ScrollLock = 302,
    RightShift = 303,
    LeftShift = 304,
    RightControl = 305,
    LeftControl = 306,
    RightAlt = 307,
    LeftAlt = 308,
    LeftCommand = 310,
    LeftWindows = 311,
    RightCommand = 309,
    RightWindows = 312,
    AltGr = 313,
    Help = 315,
    Print = 316,
    SysReq = 317,
    Break = 318,
    Menu = 319,
    Mouse0 = 323,
    Mouse1 = 324,
    Mouse2 = 325,
    Mouse3 = 326,
    Mouse4 = 327,
    Mouse5 = 328,
    Mouse6 = 329
}