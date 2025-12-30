using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastHotKeyForWPF;
using HandyControl.Tools.Extension;
using ToolModData;

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
}

public partial class ModifierViewModel : ObservableObject
{
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
        foreach (var kp in App.InitData!.Value.Plants) Plants.Add(kp.Key, kp.Value);

        foreach (var h1 in App.InitData.Value.FirstArmors) Health1sts.Add(h1.Key, -1);

        foreach (var h2 in App.InitData.Value.SecondArmors) Health2nds.Add(h2.Key, -1);

        foreach (var h3 in App.InitData.Value.Plants) HealthPlants.Add(h3.Key, -1);

        foreach (var h4 in App.InitData.Value.Zombies) HealthZombies.Add(h4.Key, -1);

        foreach (var b in Bullets) Bullets2.Add(b.Key, b.Key + " : " + b.Value);

        GameSpeed = 1;
        ZombieSeaCD = 40;
        ZombieSeaTypes = [];
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
        Debuffs = [];
        InGameDebuffs = [];
        Times = 1;
        NewZombieUpdateCD = 30;

        var bi = 0;
        foreach (var b in App.InitData.Value.AdvBuffs)
        {
            TravelBuffs.Add(new TravelBuffVM(new TravelBuff(bi, b, false, false)));
            InGameBuffs.Add(new TravelBuffVM(new TravelBuff(bi, b, true, false)));
            bi++;
        }

        foreach (var b in App.InitData.Value.UltiBuffs)
        {
            TravelBuffs.Add(new TravelBuffVM(new TravelBuff(bi, b, false, false)));
            InGameBuffs.Add(new TravelBuffVM(new TravelBuff(bi, b, true, false)));
            bi++;
        }

        var di = 0;
        foreach (var d in App.InitData.Value.Debuffs)
        {
            Debuffs.Add(new TravelBuffVM(new TravelBuff(di, d, true, true)));
            InGameDebuffs.Add(new TravelBuffVM(new TravelBuff(di, d, true, true)));
            di++;
        }

        TravelBuffs.ListChanged += (sender, e) => SyncTravelBuffs();
        InGameBuffs.ListChanged += (sender, e) => SyncInGameBuffs();
        Debuffs.ListChanged += (_, _) => SyncTravelBuffs();
        InGameDebuffs.ListChanged += (_, _) => SyncInGameBuffs();
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
        InGameDebuffs = [];
        BuffRefreshNoLimit = s.BuffRefreshNoLimit;
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
        DeveloperMode = s.DeveloperMode;
        DevLour = s.DevLour;
        Exchange = s.Exchange;
        FastShooting = s.FastShooting;
        FieldString = s.FieldString;
        FreeCD = s.FreeCD;
        FreePlanting = s.FreePlanting;
        GameSpeed = s.GameSpeed;
        GarlicDay = s.GarlicDay;
        GloveNoCD = s.GloveNoCD;
        HammerNoCD = s.HammerNoCD;
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
        TravelBuffs = [.. s.TravelBuffs];
        UltimateRamdomZombie = s.UltimateRamdomZombie;
        UltimateSuperGatling = s.UltimateSuperGatling;
        UndeadBullet = s.UndeadBullet;
        UnlockAllFusions = s.UnlockAllFusions;
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

        var di = 0;
        foreach (var d in App.InitData.Value.Debuffs)
            InGameDebuffs.Add(new TravelBuffVM(new TravelBuff(di, d, true, true)));

        TravelBuffs.ListChanged += (sender, e) => SyncTravelBuffs();
        InGameBuffs.ListChanged += (sender, e) => SyncInGameBuffs();
        Debuffs.ListChanged += (_, _) => SyncTravelBuffs();
        InGameDebuffs.ListChanged += (_, _) => SyncInGameBuffs();
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

        InGameHotkeys = [];
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
    public void GardenTools()
    {
        try
        {
            Process modifier = new();
            ProcessStartInfo info = new()
            {
                FileName = "PVZRHTools\\GardenTools\\start.bat",
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            modifier.StartInfo = info;
            modifier.Start();
        }
        catch
        {
        }
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

    public void InitInGameHotkeys(List<int> keycodes)
    {
        InGameHotkeys =
        [
            new InGameHotkeyUIVM(new InGameHotkeyUI("高级时停 TimeStop", (KeyCode)keycodes[0])),
            new InGameHotkeyUIVM(new InGameHotkeyUI("卡槽栏置顶 TopMostCardBank", (KeyCode)keycodes[1])),
            new InGameHotkeyUIVM(new InGameHotkeyUI("显示CD信息 ShowCDInfo", (KeyCode)keycodes[2])),
            new InGameHotkeyUIVM(new InGameHotkeyUI("图鉴种植：植物 AlmanacCreatePlant", (KeyCode)keycodes[3])),
            new InGameHotkeyUIVM(new InGameHotkeyUI("图鉴种植：僵尸 AlmanacCreateZombie", (KeyCode)keycodes[4])),
            new InGameHotkeyUIVM(new InGameHotkeyUI("图鉴种植：僵尸是否魅惑 AlmanacZombieMindCtrl", (KeyCode)keycodes[5])),
            new InGameHotkeyUIVM(new InGameHotkeyUI("图鉴种植：植物罐子 AlmanacCreatePlantVase", (KeyCode)keycodes[6])),
            new InGameHotkeyUIVM(new InGameHotkeyUI("图鉴种植：僵尸罐子 AlmanacCreateZombieVase", (KeyCode)keycodes[7])),
            new InGameHotkeyUIVM(new InGameHotkeyUI("随机卡槽 RandomCard", (KeyCode)keycodes[8])),
        ];
        InGameHotkeys.ListChanged += (_, _) => SyncInGameHotkeys();
    }

    [RelayCommand]
    public void KillAllZombies()
    {
        App.DataSync.Value.SendData(new InGameActions { ClearAllZombies = true });
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
        App.DataSync.Value.SendData(new InGameActions { CurrentMoney = (int)NewMoney });
    }

    [RelayCommand]
    public void NextWave()
    {
        App.DataSync.Value.SendData(new InGameActions { NextWave = true });
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
            GarlicDay = GarlicDay,
            GloveNoCD = GloveNoCD,
            HammerNoCD = HammerNoCD,
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
            TravelBuffs = [.. TravelBuffs],
            UltimateRamdomZombie = UltimateRamdomZombie,
            UltimateSuperGatling = UltimateSuperGatling,
            UndeadBullet = UndeadBullet,
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
            GloveFullCDEnabled = GloveFullCDEnabled,
            HammerFullCD = HammerFullCD,
            HammerFullCDEnabled = HammerFullCDEnabled,
            Hotkeys = Hotkeys,
            NewZombieUpdateCD = NewZombieUpdateCD,
            PlantUpgrade = PlantUpgrade,
        };
        if (ZombieSeaTypes.Count > 0) s.ZombieSeaTypes.AddRange(from zst in ZombieSeaTypes select zst.Key);

        if (ConveyBeltTypes.Count > 0) s.ConveyBeltTypes.AddRange(from cbt in ConveyBeltTypes select cbt.Key);

        File.WriteAllText(App.IsBepInEx ? "BepInEx/config/ModifierSettings.json" : "UserData/ModifierSettings.json",
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
        App.DataSync.Value.SendData(new InGameActions { CurrentSun = (int)NewSun });
    }

    public void SyncAll()
    {
        if (!NeedSave) return;
        List<bool> adv = [];
        List<bool> ulti = [];
        List<bool> deb = [];
        foreach (var buff in TravelBuffs)
            if (buff.TravelBuff.Index < App.InitData!.Value.AdvBuffs.Length)
                adv.Add(buff.TravelBuff.Enabled);
            else
                ulti.Add(buff.TravelBuff.Enabled);

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
                GarlicDay = GarlicDay,
                GloveNoCD = GloveNoCD,
                HammerNoCD = HammerNoCD,
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
                UndeadBullet = UndeadBullet,
                UnlockAllFusions = UnlockAllFusions,
                GloveFullCD = GloveFullCDEnabled ? (int)GloveFullCD : -1,
                HammerFullCD = HammerFullCDEnabled ? (int)HammerFullCD : -1,
                NewZombieUpdateCD = NewZombieUpdateCD,
                PlantUpgrade = PlantUpgrade
            },
            InGameActions = iga,
            TravelBuffs = new SyncTravelBuff
            {
                AdvTravelBuff = adv,
                UltiTravelBuff = ulti,
                Debuffs = deb
            },
            ValueProperties = new ValueProperties { LockBulletType = LockBulletType },
            GameModes = new GameModes
            {
                ScaredyDream = ScaredyDream,
                ColumnPlanting = ColumnPlanting,
                SeedRain = SeedRain
            }
        };

        App.DataSync.Value.SendData(syncAll);
    }

    public void SyncInGameBuffs()
    {
        List<bool> adv = [];
        List<bool> ulti = [];
        List<bool> deb = [];
        foreach (var buff in InGameBuffs)
            if (buff.TravelBuff.Index < App.InitData!.Value.AdvBuffs.Length)
                adv.Add(buff.TravelBuff.Enabled);
            else
                ulti.Add(buff.TravelBuff.Enabled);

        foreach (var d in InGameDebuffs) deb.Add(d.TravelBuff.Enabled);

        App.DataSync.Value.SendData(new SyncTravelBuff
        {
            AdvInGame = adv,
            UltiInGame = ulti,
            DebuffsInGame = deb
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

    public void SyncTravelBuffs()
    {
        List<bool> adv = [];
        List<bool> ulti = [];
        List<bool> deb = [];
        foreach (var buff in TravelBuffs)
            if (buff.TravelBuff.Index < App.InitData!.Value.AdvBuffs.Length)
                adv.Add(buff.TravelBuff.Enabled);
            else
                ulti.Add(buff.TravelBuff.Enabled);

        foreach (var d in Debuffs) deb.Add(d.TravelBuff.Enabled);

        App.DataSync.Value.SendData(new SyncTravelBuff
        {
            AdvTravelBuff = adv,
            UltiTravelBuff = ulti,
            Debuffs = deb
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
            SeedRain = SeedRain
        });
    }

    partial void OnBuffRefreshNoLimitChanged(bool value)
    {
        App.DataSync.Value.SendData(new InGameActions { BuffRefreshNoLimit = value });
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

    partial void OnGarlicDayChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { GarlicDay = value });
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
        App.DataSync.Value.SendData(new InGameActions
            { LockMoney = value, CurrentMoney = (int)NewMoney });
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

    partial void OnLockSunChanged(bool value)
    {
        App.DataSync.Value.SendData(new InGameActions
            { LockSun = value, CurrentSun = (int)NewSun });
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
        App.DataSync.Value.SendData(new BasicProperties { ZombieDamageLimitValue = value });
    }

    partial void OnZombieSpeedModifyEnabledChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieSpeedModifyEnabled = value });
    }

    partial void OnZombieSpeedMultiplierChanged(float value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieSpeedMultiplier = value });
    }

    partial void OnZombieAttackMultiplierEnabledChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieAttackMultiplierEnabled = value });
    }

    partial void OnZombieAttackMultiplierChanged(float value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieAttackMultiplier = value });
    }

    partial void OnZombieBulletReflectEnabledChanged(bool value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieBulletReflectEnabled = value });
    }

    partial void OnZombieBulletReflectChanceChanged(float value)
    {
        App.DataSync.Value.SendData(new BasicProperties { ZombieBulletReflectChance = value });
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
        ("启动所有小推车", StartMower),
        ("生成小推车", CreateMower),
        ("修改关卡名称", LevelName),
        ("显示字幕", ShowingText),
        ("显示悬浮窗", () => TopMostSprite = !TopMostSprite),
        ("显示修改窗口", () =>
        {
            MainWindow.Instance!.Topmost = true;
            MainWindow.Instance!.Topmost = false;
        })
    ];

    public Dictionary<int, string> Plants2 => App.InitData!.Value.Plants;

    public Dictionary<int, string> SecondArmor => App.InitData!.Value.SecondArmors;

    public Dictionary<int, string> Zombies => App.InitData!.Value.Zombies;

    #endregion ItemSources

    #region Properties

    [ObservableProperty] public partial bool BuffRefreshNoLimit { get; set; }

    [ObservableProperty] public partial int BulletDamageType { get; set; }

    [ObservableProperty] public partial double BulletDamageValue { get; set; }

    [ObservableProperty] public partial bool CardNoInit { get; set; }

    [ObservableProperty] public partial bool ChomperNoCD { get; set; }
    
    [ObservableProperty] public partial bool SuperStarNoCD { get; set; }
    [ObservableProperty] public partial bool AutoCutFruit { get; set; }
    [ObservableProperty] public partial bool RandomCard { get; set; }
    [ObservableProperty] public partial bool ColumnGlove { get; set; }

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

    [ObservableProperty] public partial bool GarlicDay { get; set; }

    [ObservableProperty] public partial double GloveFullCD { get; set; }

    [ObservableProperty] public partial bool GloveFullCDEnabled { get; set; }

    [ObservableProperty] public partial bool GloveNoCD { get; set; }

    [ObservableProperty] public partial double HammerFullCD { get; set; }

    [ObservableProperty] public partial bool HammerFullCDEnabled { get; set; }

    [ObservableProperty] public partial bool HammerNoCD { get; set; }

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

    [ObservableProperty] public partial BindingList<TravelBuffVM> InGameDebuffs { get; set; }

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

    [ObservableProperty] public partial bool ZombieBulletReflectEnabled { get; set; }

    [ObservableProperty] public partial float ZombieBulletReflectChance { get; set; } = 10.0f;

    [ObservableProperty] public partial bool UnlimitedCardSlots { get; set; }

    [ObservableProperty] public partial bool ZombieStatusCoexist { get; set; }

    [ObservableProperty] public partial bool MNEntryEnabled { get; set; }

    [ObservableProperty] public partial bool UnlockRedCardPlants { get; set; }

    [ObservableProperty] public partial bool KillUpgrade { get; set; }

    [ObservableProperty] public partial bool ZombieImmuneAllDebuffs { get; set; }

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

    [ObservableProperty] public partial BindingList<TravelBuffVM> TravelBuffs { get; set; }

    [ObservableProperty] public partial bool UltimateRamdomZombie { get; set; }

    [ObservableProperty] public partial bool UltimateSuperGatling { get; set; }

    [ObservableProperty] public partial bool UndeadBullet { get; set; }

    [ObservableProperty] public partial bool UnlockAllFusions { get; set; }

    [ObservableProperty] public partial string VasesFieldString { get; set; }

    [ObservableProperty] public partial string ZombieFieldString { get; set; }

    [ObservableProperty] public partial string MixFieldString { get; set; }

    [ObservableProperty] public partial double ZombieSeaCD { get; set; }

    [ObservableProperty] public partial bool ZombieSeaEnabled { get; set; }

    [ObservableProperty] public partial bool ZombieSeaLowEnabled { get; set; }

    [ObservableProperty] public partial List<KeyValuePair<int, string>> ZombieSeaTypes { get; set; }

    [ObservableProperty] public partial int ZombieType { get; set; }

    #endregion Properties
}

public partial class TravelBuff : ObservableObject, INotifyPropertyChanged
{
    public TravelBuff(int index, string text, bool inGame, bool debuff)
    {
        Text = text;
        Index = index;
        InGame = inGame;
        Debuff = debuff;
    }

    public TravelBuff()
    {
    }

    public bool Debuff { get; set; }

    [ObservableProperty] public partial bool Enabled { get; set; }

    public int Index { get; set; }
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