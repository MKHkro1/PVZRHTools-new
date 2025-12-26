
//#define F1

using System.Text.Json;
using System.Text.Json.Nodes;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using ToolModData;
using Unity.VisualScripting;
using UnityEngine;
using static ToolModBepInEx.PatchMgr;
using static ToolModData.Modifier;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;
using System.Threading.Tasks;
namespace ToolModBepInEx;

public class DataProcessor : MonoBehaviour
{
    public DataProcessor() : base(ClassInjector.DerivedConstructorPointer<DataProcessor>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    public DataProcessor(IntPtr i) : base(i)
    {
    }

    public static string Data { get; set; } = "";

    public static List<GameObject> Items =>
    [
        Resources.Load<GameObject>("Items/Fertilize/Ferilize"),
        Resources.Load<GameObject>("Items/Bucket"),
        Resources.Load<GameObject>("Items/Helmet"),
        Resources.Load<GameObject>("Items/Jackbox"),
        Resources.Load<GameObject>("Items/Pickaxe"),
        Resources.Load<GameObject>("Items/Machine"),
        Resources.Load<GameObject>("Items/SuperMachine"),
        Resources.Load<GameObject>("Items/SproutPotPrize/SproutPotPrize"),
        Resources.Load<GameObject>("Items/PortalHeart")
    ];

    public void Awake()
    {
        new Thread(() =>
        {
            Thread.Sleep(3000);
            DataSync.Instance.Value.SendData(new SyncAll());
            DataSync.Instance.Value.SendData(new InGameHotkeys
                { KeyCodes = [.. from i in Core.KeyBindings.Value select (int)i.Value] });
        })
        {
            IsBackground = true
        }.Start();
    }

    public void Update()
    {
        lock (Data)
        {
            if (!string.IsNullOrEmpty(Data)) ProcessData(Data);
            Data = "";
        }
    }

    public static void AddData(string d)
    {
        Data = d;
    }

    public static void ProcessData<T>(T data) where T : ISyncData
    {
        if (data is ValueProperties v)
        {
            if (v.PlantsHealth is not null)
            {
                HealthPlants[(PlantType)v.PlantsHealth.Value.Key] = v.PlantsHealth.Value.Value;
                if (InGame())
                {
                    var allPlants = Lawnf.GetAllPlants();
                    if (allPlants != null)
                        foreach (var pl in allPlants)
                            if (HealthPlants[pl.thePlantType] >= 0)
                            {
                                pl.thePlantMaxHealth = HealthPlants[pl.thePlantType];
                                if (pl.thePlantHealth > pl.thePlantMaxHealth) pl.thePlantHealth = pl.thePlantHealth;
                                pl.UpdateText();
                            }
                }
            }

            if (v.ZombiesHealth is not null)
            {
                HealthZombies[(ZombieType)v.ZombiesHealth.Value.Key] = v.ZombiesHealth.Value.Value;
                if (InGame())
                    foreach (var z in Board.Instance.zombieArray)
                    {
                        if (z is null)continue;
                        if (HealthZombies[z.theZombieType] >= 0)
                        {
                            z.theMaxHealth = HealthZombies[z.theZombieType];
                            if (z.theHealth > z.theMaxHealth) z.theHealth = z.theMaxHealth;
                        }

                        z.UpdateHealthText();
                    }
            }

            if (v.FirstArmorsHealth is not null)
            {
                Health1st[(Zombie.FirstArmorType)v.FirstArmorsHealth.Value.Key] = v.FirstArmorsHealth.Value.Value;
                if (InGame())
                    foreach (var z in Board.Instance.zombieArray)
                    {
                        if (z is null)continue;
                        if (Health1st[z.theFirstArmorType] >= 0)
                        {
                            z.theFirstArmorMaxHealth = Health1st[z.theFirstArmorType];
                            if (z.theFirstArmorHealth > z.theFirstArmorMaxHealth)
                                z.theFirstArmorHealth = z.theFirstArmorMaxHealth;
                        }

                        z.UpdateHealthText();
                    }
            }

            if (v.SecondArmorsHealth is not null)
            {
                Health2nd[(Zombie.SecondArmorType)v.SecondArmorsHealth.Value.Key] = v.SecondArmorsHealth.Value.Value;
                if (InGame())
                    foreach (var z in Board.Instance.zombieArray)
                    {
                        if (z is null)continue;
                        if (Health2nd[z.theSecondArmorType] >= 0)
                        {
                            z.theSecondArmorMaxHealth = Health2nd[z.theSecondArmorType];
                            if (z.theSecondArmorHealth > z.theSecondArmorMaxHealth)
                                z.theSecondArmorHealth = z.theSecondArmorMaxHealth;
                        }

                        z.UpdateHealthText();
                    }
            }

            if (v.BulletsDamage is not null)
                BulletDamage[(BulletType)v.BulletsDamage.Value.Key] = v.BulletsDamage.Value.Value;
            if (v.LockBulletType is not null) LockBulletType = (int)v.LockBulletType;
            return;
        }

        if (data is BasicProperties p1)
        {            
            if (p1.DeveloperMode is not null)
            {
#if F1
                GloveNoCD = true;
                HammerNoCD = true;
                FreeCD = true;
                UnlockAllFusions = true;
                LockMoney = true;
                LockMoneyCount = 10_0000;
                LockSun = true;
                LockSunCount = 6666;
#else
                GameAPP.developerMode = (bool)p1.DeveloperMode;
#endif
            }
            if (p1.GameSpeed is not null) SyncSpeed = (float)p1.GameSpeed;
            if (p1.GloveNoCD is not null) GloveNoCD = (bool)p1.GloveNoCD;
            if (p1.HammerNoCD is not null) HammerNoCD = (bool)p1.HammerNoCD;
            if (p1.PlantingNoCD is not null && Board.Instance is not null)
            {
                FreeCD = (bool)p1.PlantingNoCD;
                Board.Instance.freeCD = FreeCD;
            }

            if (p1.FreePlanting is not null) FreePlanting = (bool)p1.FreePlanting;
            if (p1.UnlockAllFusions is not null)
            {
                UnlockAllFusions = (bool)p1.UnlockAllFusions;
                if (InGame())
                {
                    var t = Board.Instance!.boardTag;
                    t.enableAllTravelPlant = UnlockAllFusions || originalTravel;
                    Board.Instance.boardTag = t;
                }
            }

            if (p1.SuperPresent is not null) SuperPresent = (bool)p1.SuperPresent;
            if (p1.UltimateRamdomZombie is not null) UltimateRamdomZombie = (bool)p1.UltimateRamdomZombie;
            if (p1.PresentFastOpen is not null) PresentFastOpen = (bool)p1.PresentFastOpen;
            if (p1.LockPresent is not null) LockPresent = (int)p1.LockPresent;
            if (p1.LockWheat is not null) LockWheat = (int)p1.LockWheat;
            if (p1.LockPresent1 is not null) LockPresent1 = (int)p1.LockPresent1;
            if (p1.LockPresent2 is not null) LockPresent2 = (int)p1.LockPresent2;
            if (p1.LockPresent3 is not null) LockPresent3 = (int)p1.LockPresent3;
            if (p1.LockPresent4 is not null) LockPresent4 = (int)p1.LockPresent4;
            if (p1.LockPresent5 is not null) LockPresent5 = (int)p1.LockPresent5;
            if (p1.FastShooting is not null) FastShooting = (bool)p1.FastShooting;
            if (p1.HardPlant is not null) HardPlant = (bool)p1.HardPlant;
            if (p1.ImmuneForceDeduct is not null) ImmuneForceDeduct = (bool)p1.ImmuneForceDeduct;
            if (p1.CurseImmunity is not null) CurseImmunity = (bool)p1.CurseImmunity;
            if (p1.CrushImmunity is not null) CrushImmunity = (bool)p1.CrushImmunity;
            if (p1.TrampleImmunity is not null) TrampleImmunity = (bool)p1.TrampleImmunity;
            if (p1.NoHole is not null) NoHole = (bool)p1.NoHole;
            if (p1.HyponoEmperorNoCD is not null) HyponoEmperorNoCD = (bool)p1.HyponoEmperorNoCD;
            if (p1.MineNoCD is not null) MineNoCD = (bool)p1.MineNoCD;
            if (p1.ChomperNoCD is not null) ChomperNoCD = (bool)p1.ChomperNoCD;
            if (p1.SuperStarNoCD is not null) SuperStarNoCD = (bool)p1.SuperStarNoCD;
            if (p1.AutoCutFruit is not null) AutoCutFruit = (bool)p1.AutoCutFruit;
            if (p1.RandomCard is not null) RandomCard = (bool)p1.RandomCard;
            if (p1.ColumnGlove is not null) ColumnGlove = (bool)p1.ColumnGlove;
            if (p1.CobCannonNoCD is not null) CobCannonNoCD = (bool)p1.CobCannonNoCD;
            if (p1.NoIceRoad is not null) NoIceRoad = (bool)p1.NoIceRoad;
            if (p1.ItemExistForever is not null) ItemExistForever = (bool)p1.ItemExistForever;
            if (p1.CardNoInit is not null) CardNoInit = (bool)p1.CardNoInit;
            if (p1.JackboxNotExplode is not null) JackboxNotExplode = (bool)p1.JackboxNotExplode;
            if (p1.UndeadBullet is not null) UndeadBullet = (bool)p1.UndeadBullet;
            if (p1.GarlicDay is not null) GarlicDay = (bool)p1.GarlicDay;
            if (p1.DevLour is not null) DevLour = (bool)p1.DevLour;
            if (p1.HammerFullCD is not null) HammerFullCD = (double)p1.HammerFullCD;
            if (p1.GloveFullCD is not null) GloveFullCD = (double)p1.GloveFullCD;
            if (p1.NewZombieUpdateCD is not null) NewZombieUpdateCD = (float)p1.NewZombieUpdateCD;
            if (p1.UltimateSuperGatling is not null) UltimateSuperGatling = (bool)p1.UltimateSuperGatling;
            if (p1.PlantUpgrade is not null) PlantUpgrade = (bool)p1.PlantUpgrade;
            if (p1.PvPPotRange is not null) PvPPotRange = (bool)p1.PvPPotRange;
            if (p1.DisableIceEffect is not null) DisableIceEffect = (bool)p1.DisableIceEffect;
            if (p1.PotSmashingFix is not null) PotSmashingFix = (bool)p1.PotSmashingFix;
            if (p1.UnlimitedSunlight is not null) UnlimitedSunlight = (bool)p1.UnlimitedSunlight;
            if (p1.MagnetNutUnlimited is not null) MagnetNutUnlimited = (bool)p1.MagnetNutUnlimited;
            return;
        }

        if (data is InGameHotkeys h)
        {
            for (var index = 0; index < h.KeyCodes.Count; index++)
                Core.KeyBindings.Value[index].Value = (KeyCode)h.KeyCodes[index];
            return;
        }

        if (data is SyncTravelBuff s)
        {
            if (s.AdvTravelBuff is not null) AdvBuffs = [.. s.AdvTravelBuff];
            if (s.UltiTravelBuff is not null) PatchMgr.UltiBuffs = [.. s.UltiTravelBuff];
            if (s.Debuffs is not null) Debuffs = [.. s.Debuffs];
            if (InGame())
            {
                if (s.AdvInGame is not null) InGameAdvBuffs = [.. s.AdvInGame];
                if (s.UltiInGame is not null) InGameUltiBuffs = [.. s.UltiInGame];
                if (s.DebuffsInGame is not null) InGameDebuffs = [.. s.DebuffsInGame];
                UpdateInGameBuffs();
            }

            return;
        }

        if (data is InGameActions iga)
        {
            if (iga.ZombieSeaEnabled is not null
                && iga.ZombieSeaCD is not null
                && iga.ZombieSeaTypes is not null
                && iga.ZombieSeaLowEnabled is not null)
            {
                ZombieSea = (bool)iga.ZombieSeaEnabled;
                ZombieSeaCD = (int)iga.ZombieSeaCD;
                SeaTypes = iga.ZombieSeaTypes;
                ZombieSeaLow = (bool)iga.ZombieSeaLowEnabled;
            }

            if (iga.LockSun is not null
                && iga.CurrentSun is not null)
            {
                LockSun = (bool)iga.LockSun;
                LockSunCount = (int)iga.CurrentSun;
            }

            if (iga.LockMoney is not null
                && iga.CurrentMoney is not null)
            {
                LockMoney = (bool)iga.LockMoney;
                LockMoneyCount = (int)iga.CurrentMoney;
            }

            if (iga.NoFail is not null) EnableAll<GameLose>(!(bool)iga.NoFail);
            if (iga.BuffRefreshNoLimit is not null) BuffRefreshNoLimit = (bool)iga.BuffRefreshNoLimit;
            if (iga.BuffRefreshNoLimit is true)
            {
                if (FruitNinjaManager.Instance != null)
                    FruitNinjaManager.Instance.AddScore(114514);
            }
            if (iga.StopSummon is not null) StopSummon = (bool)iga.StopSummon;
            if (iga.ConveyBeltTypes is not null) ConveyBeltTypes = iga.ConveyBeltTypes;
            if (iga.AbyssCheat is not null)
            {
                var abyssManager = GameAPP.gameAPP?.GetComponent<AbyssManager>();
                if (abyssManager != null)
                {
                    abyssManager.Money = 99999999;
                    abyssManager.maxPlantCount = 99999999;
                }
                var buffMenu = GameObject.Find("AbyssSelectBuffMenu(Clone)")?.GetComponent<AbyssSelectBuffMenu>();
                if (buffMenu != null)
                {
                    buffMenu.refreshCount = 99999999;
                }
            }

            if (iga.LoadCustomPlantData is not null)
                if (!Utils.LoadPlantData())
                    MLogger.LogError("Failed to reload custom plant data.");

            if (!InGame()) return;

            if (iga.Row is not null && iga.Column is not null && iga.PlantType is not null && iga.Times is not null)
            {
                var id = (int)iga.PlantType;
                var r = (int)iga.Row;
                var c = (int)iga.Column;
                //if (iga.Times > 50) iga.Times = 50;
                try
                {
                    for (var n = 0; n < iga.Times; n++)
                    {
                        if (PvPPotRange)
                        {
                            for (var i = 0; i < Board.Instance!.rowNum; i++)
                            for (var j = 3; j < Board.Instance.columnNum; j++)
                                CreatePlant.Instance.SetPlant(j, i, (PlantType)id);

                            continue;
                        }
                        
                        if (r * r + c * c == 0)
                        {
                            for (var i = 0; i < Board.Instance!.rowNum; i++)
                            for (var j = 0; j < Board.Instance.columnNum; j++)
                                CreatePlant.Instance.SetPlant(j, i, (PlantType)id);

                            continue;
                        }

                        if (r == 0 && c != 0)
                        {
                            for (var j = 0; j < Board.Instance!.columnNum; j++)
                                CreatePlant.Instance.SetPlant(c - 1, j, (PlantType)id);

                            continue;
                        }

                        if (c == 0 && r != 0)
                        {
                            for (var j = 0; j < Board.Instance!.columnNum; j++)
                                CreatePlant.Instance.SetPlant(j, r - 1, (PlantType)id);

                            continue;
                        }

                        if (c > 0 && r > 0 && c <= Board.Instance!.columnNum && r <= Board.Instance.rowNum)
                            CreatePlant.Instance.SetPlant(c - 1, r - 1, (PlantType)id);
                    }
                }
                catch
                {
                    if (id is 300)
                        InGameText.Instance.ShowText("不支持此操作，用豌豆代替", 5);
                    else
                        throw;
                }
            }

            if (iga.Row is not null
                && iga.Column is not null
                && iga.ZombieType is not null
                && iga.SummonMindControlledZombies is not null
                && iga.Times is not null)
            {
                if (iga.Times > 50) iga.Times = 50;
                for (var n = 0; n < iga.Times; n++)
                {
                    var id = (int)iga.ZombieType;
                    var r = (int)iga.Row;
                    var c = (int)iga.Column;
                    if (PvPPotRange)
                    {
                        for (var i = 0; i < Board.Instance.rowNum; i++)
                        for (var j = 3; j < Board.Instance.columnNum; j++)
                            if (!(bool)iga.SummonMindControlledZombies)
                                CreateZombie.Instance.SetZombie(i, (ZombieType)id, -5f + j * 1.37f);
                            else
                                CreateZombie.Instance.SetZombieWithMindControl(i, (ZombieType)id,
                                    -5f + j * 1.37f);
                        continue;
                    }
                    if (r * r + c * c == 0)
                    {
                        for (var i = 0; i < Board.Instance.rowNum; i++)
                        for (var j = 0; j < Board.Instance.columnNum; j++)
                            if (!(bool)iga.SummonMindControlledZombies)
                                CreateZombie.Instance.SetZombie(i, (ZombieType)id, -5f + j * 1.37f);
                            else
                                CreateZombie.Instance.SetZombieWithMindControl(i, (ZombieType)id,
                                    -5f + j * 1.37f);

                        continue;
                    }

                    if (r == 0 && c != 0)
                    {
                        for (var j = 0; j < Board.Instance.rowNum; j++)
                            if (!(bool)iga.SummonMindControlledZombies)
                                CreateZombie.Instance.SetZombie(j, (ZombieType)id, -5f + (c - 1) * 1.37f);
                            else
                                CreateZombie.Instance.SetZombieWithMindControl(j, (ZombieType)id,
                                    -5f + (c - 1) * 1.37f);

                        continue;
                    }

                    if (c == 0 && r != 0)
                    {
                        for (var j = 0; j < Board.Instance.columnNum; j++)
                            if (!(bool)iga.SummonMindControlledZombies)
                                CreateZombie.Instance.SetZombie(r - 1, (ZombieType)id, -5f + j * 1.37f);
                            else
                                CreateZombie.Instance.SetZombieWithMindControl(r - 1, (ZombieType)id,
                                    -5f + j * 1.37f);

                        continue;
                    }

                    if (c > 0 && r > 0 && c <= Board.Instance.columnNum + 1 && r <= Board.Instance.rowNum)
                    {
                        if (!(bool)iga.SummonMindControlledZombies)
                            CreateZombie.Instance.SetZombie(r - 1, (ZombieType)id, -5f + (c - 1) * 1.37f);
                        else
                            CreateZombie.Instance.SetZombieWithMindControl(r - 1, (ZombieType)id,
                                -5f + (c - 1) * 1.37f);
                    }
                }
            }

            if (iga.Row is not null && iga.Column is not null && iga.PlantType is not null &&
                iga.PlantVase is not null)
            {
                var id = (int)iga.PlantType;
                var r = (int)iga.Row;
                var c = (int)iga.Column;
                if (PvPPotRange)
                {
                    
                    for (var i = 0; i < Board.Instance!.rowNum; i++)
                    for (var j = 3; j < Board.Instance.columnNum; j++)
                        GridItem.SetGridItem(j, i, GridItemType.ScaryPot).Cast<ScaryPot>().thePlantType =
                            (PlantType)id;
                    
                }
                else
                {
                    if (r * r + c * c == 0)
                        for (var i = 0; i < Board.Instance!.rowNum; i++)
                        for (var j = 0; j < Board.Instance.columnNum; j++)
                            GridItem.SetGridItem(j, i, GridItemType.ScaryPot).Cast<ScaryPot>().thePlantType =
                                (PlantType)id;

                    if (r == 0 && c != 0)
                        for (var j = 0; j < Board.Instance!.columnNum; j++)
                            GridItem.SetGridItem(c - 1, j, GridItemType.ScaryPot).Cast<ScaryPot>().thePlantType =
                                (PlantType)id;

                    if (c == 0 && r != 0)
                        for (var j = 0; j < Board.Instance!.columnNum; j++)
                            GridItem.SetGridItem(j, r - 1, GridItemType.ScaryPot).Cast<ScaryPot>().thePlantType =
                                (PlantType)id;

                    if (c > 0 && r > 0 && c <= Board.Instance!.columnNum && r <= Board.Instance.rowNum)
                        GridItem.SetGridItem(c - 1, r - 1, GridItemType.ScaryPot).Cast<ScaryPot>().thePlantType =
                            (PlantType)id;
                }
            }

            if (iga.Row is not null && iga.Column is not null && iga.ZombieType is not null &&
                iga.ZombieVase is not null)
            {
                var id = (int)iga.ZombieType;
                var r = (int)iga.Row;
                var c = (int)iga.Column;
                if (PvPPotRange)
                {
                    for (var i = 0; i < Board.Instance!.rowNum; i++)
                    for (var j = 3; j < Board.Instance.columnNum; j++)
                        GridItem.SetGridItem(j, i, GridItemType.ScaryPot).Cast<ScaryPot>().theZombieType =
                            (ZombieType)id;
                }
                else
                {
                    if (r * r + c * c == 0)
                        for (var i = 0; i < Board.Instance!.rowNum; i++)
                        for (var j = 0; j < Board.Instance.columnNum; j++)
                            GridItem.SetGridItem(j, i, GridItemType.ScaryPot).Cast<ScaryPot>().theZombieType =
                                (ZombieType)id;

                    if (r == 0 && c != 0)
                        for (var j = 0; j < Board.Instance!.columnNum; j++)
                            GridItem.SetGridItem(c - 1, j, GridItemType.ScaryPot).Cast<ScaryPot>().theZombieType =
                                (ZombieType)id;

                    if (c == 0 && r != 0)
                        for (var j = 0; j < Board.Instance!.columnNum; j++)
                            GridItem.SetGridItem(j, r - 1, GridItemType.ScaryPot).Cast<ScaryPot>().theZombieType =
                                (ZombieType)id;

                    if (c > 0 && r > 0 && c <= Board.Instance!.columnNum && r <= Board.Instance.rowNum)
                        GridItem.SetGridItem(c - 1, r - 1, GridItemType.ScaryPot).Cast<ScaryPot>().theZombieType =
                            (ZombieType)id;
                }
            }

            if (iga.Row is not null && iga.Column is not null&& iga.RandomVase is not null)
            {
                var r = (int)iga.Row;
                var c = (int)iga.Column;
                if (PvPPotRange)
                {
                    
                    for (var i = 0; i < Board.Instance!.rowNum; i++)
                    for (var j = 3; j < Board.Instance.columnNum; j++)
                        PutRandomPot(j, i);
                }
                else
                {
                    
                    if (r * r + c * c == 0)
                        for (var i = 0; i < Board.Instance!.rowNum; i++)
                        for (var j = 0; j < Board.Instance.columnNum; j++)
                            PutRandomPot(j, i);

                    if (r == 0 && c != 0)
                        for (var j = 0; j < Board.Instance!.columnNum; j++)
                            PutRandomPot(c - 1, j);

                    if (c == 0 && r != 0)
                        for (var j = 0; j < Board.Instance!.columnNum; j++)
                            PutRandomPot(j, r-1);

                    if (c > 0 && r > 0 && c <= Board.Instance!.columnNum && r <= Board.Instance.rowNum)
                        PutRandomPot(c - 1, r-1);
                }
            }

            void PutRandomPot(int i1, int i2)
            {
                var g = GridItem.SetGridItem(i1, i2, GridItemType.ScaryPot);
                const string filename = "randompot.txt";
                if (!File.Exists(filename))
                {
                    File.WriteAllText(filename,@"# 这个文件用来设置右键放置罐子里面的类型
# 下面这个表示罐子出现僵尸的概率：
0.5
# 下面这个表示植物的种类
# 例如：0,1,2,3,4,5,6
# 你也可以用all表示所有
all
# 下面这个表示僵尸的种类
# 例如：0,1,2,3,4,5,6
# 你也可以用all表示所有
all");
                }
                
                var lines = File.ReadLines(filename);
                int index = 0;
                float zombiechance = 0.5f;

                List<int> plantlist = new List<int>();
                List<int> zombielist = new List<int>();
                foreach (var line in lines)
                {
                    if (line.StartsWith("#")) continue;
                    if (line=="") continue;
                    if (index == 0)
                    {
                        float.TryParse(line.Trim(), out zombiechance);
                    }

                    if (index == 1)
                    {
                        try
                        {
                            plantlist = line.Replace("，", ",")
                                .Split(",")
                                .Where(x => int.TryParse(x.Trim(), out _))
                                .Select(x => int.Parse(x.Trim()))
                                .ToList();
                            var a = plantlist[0];
                        }
                        catch
                        {
                            plantlist.Clear();
                            foreach (var t in GameAPP.resourcesManager.allPlants)
                            {
                                plantlist.Add((int)t);
                            }
                        }
                    }

                    if (index == 2)
                    {
                        try
                        {
                            zombielist = line.Replace("，", ",")
                                .Split(",")
                                .Where(x => int.TryParse(x.Trim(), out _))
                                .Select(x => int.Parse(x.Trim()))
                                .ToList();
                            var a = zombielist[0];
                        }
                        catch
                        {
                            zombielist.Clear();
                            //zombielist = loadedZombies.Keys.ToList();
                             foreach (var t in GameAPP.resourcesManager.allZombieTypes)
                             {
                                 if(t is ZombieType.ZombieBoss or ZombieType.ZombieBoss2 or ZombieType.TrainingDummy) continue;
                                 zombielist.Add((int)t);
                             }
                        }
                    }

                    index++;
                }
                var nextDouble = System.Random.Shared.NextDouble();
                if (nextDouble < zombiechance)
                {
                    int ty = zombielist[Random.RandomRangeInt(0, zombielist.Count - 1)];
                    g.Cast<ScaryPot>().theZombieType = (ZombieType)ty;
                }
                else
                {
                    int ty = plantlist[Random.RandomRangeInt(0, plantlist.Count - 1)];
                    g.Cast<ScaryPot>().thePlantType = (PlantType)ty;
                }

                //GridItem.SetGridItem(i1, i2, GridItemType.ScaryPot).Cast<ScaryPot>().theZombieType =
                //    (ZombieType)id1;
            }

            
            if (iga.ItemType is not null)
            {
                if (iga.ItemType <= 8 && iga.ItemType >= 0)
                {
                    Instantiate(Items[(int)iga.ItemType]).transform.SetParent(GameAPP.board.transform);
                }
                else if (iga.ItemType >= 64)
                {
                    //处理 coin 类的物品
                    var newItemType = (int)(iga.ItemType - 64);
                    CreateItem.Instance.SetCoin(0, 0, newItemType, 0, Vector3.zero);
                }
            }

            if (iga.CreatePassiveMateorite is not null) Board.Instance.CreatePassiveMateorite();
            if (iga.CreateActiveMateorite is not null) Board.Instance.CreateActiveMateorite();
            if (iga.CreateUltimateMateorite is not null) Board.Instance.CreateUltimateMateorite();
            if (iga.CurrentSun is not null) Board.Instance.theSun = (int)iga.CurrentSun;
            if (iga.CurrentMoney is not null) Board.Instance.theMoney = (int)iga.CurrentMoney;

            if (iga.ClearAllPlants is not null)
            {
                var allPlants = Lawnf.GetAllPlants();
                if (allPlants != null)
                {
                    for (var i = allPlants.Count - 1; i >= 0; i--) allPlants[i]?.Die();
                }
            }

            if (iga.ClearAllZombies is not null)
            {
                Il2CppReferenceArray<Object> zombies = FindObjectsOfTypeAll(Il2CppType.Of<Zombie>());
                for (var i = zombies.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        var zombie = (Zombie)zombies[i];
                        if (zombie == null || !zombie) continue;
                        
                        // 对于会生成新僵尸的特殊僵尸，直接销毁而不是调用Die
                        var zt = zombie.theZombieType;
                        if (zt == ZombieType.DiamondRandomZombie ||
                            zt == ZombieType.DollDiamond ||
                            zt == ZombieType.DollGold ||
                            zt == ZombieType.DollSilver)
                        {
                            Destroy(zombie.gameObject);
                            continue;
                        }
                        
                        zombie.TakeDamage(DmgType.MaxDamage, 2147483647);
                        zombie.BodyTakeDamage(2147483647);
                        zombie.Die();
                    }
                    catch 
                    {
                    }
                }

                for (var j = Board.Instance.zombieArray.Count - 1; j >= 0; j--)
                    try
                    {
                        var zombie = Board.Instance.zombieArray[j];
                        if (zombie == null || !zombie) continue;
                        
                        // 对于会生成新僵尸的特殊僵尸，直接销毁
                        var zt = zombie.theZombieType;
                        if (zt == ZombieType.DiamondRandomZombie ||
                            zt == ZombieType.DollDiamond ||
                            zt == ZombieType.DollGold ||
                            zt == ZombieType.DollSilver)
                        {
                            Destroy(zombie.gameObject);
                            continue;
                        }
                        
                        zombie.TakeDamage(DmgType.MaxDamage, 2147483647);
                        zombie.BodyTakeDamage(2147483647);
                        zombie.Die();
                    }
                    catch
                    {
                    }

                Board.Instance.zombieArray.Clear();
            }

            if (iga.ClearAllHoles is not null)
            {
                for (var i = Board.Instance.griditemArray.Count - 1; i >= 0; i--)
                    Destroy(Board.Instance.griditemArray[i].GameObject());
                Board.Instance.griditemArray.Clear();
            }

            if (iga.MindControlAllZombies is not null)
            {
                Il2CppReferenceArray<Object> zombies = FindObjectsOfTypeAll(Il2CppType.Of<Zombie>());
                for (var i = zombies.Count - 1; i >= 0; i--)
                    try
                    {
                        ((Zombie)zombies[i])?.SetMindControl();
                    }
                    catch
                    {
                    }

                for (var j = Board.Instance.zombieArray.Count; j >= 0; j--)
                    try
                    {
                        Board.Instance.zombieArray[j]?.SetMindControl();
                    }
                    catch
                    {
                    }
            }

            if (iga.ChangeLevelName is not null)
            {
                var uimgr = InGameUI.Instance;
                if (uimgr is not null)
                    uimgr.ChangeString(new Il2CppReferenceArray<TextMeshProUGUI>([
                        uimgr.LevelName1.GetComponent<TextMeshProUGUI>(),
                        uimgr.LevelName2.GetComponent<TextMeshProUGUI>(),
                        uimgr.LevelName3.GetComponent<TextMeshProUGUI>(),
                        uimgr.LevelName1.transform.GetChild(0).GameObject().GetComponent<TextMeshProUGUI>(),
                        uimgr.LevelName2.transform.GetChild(0).GameObject().GetComponent<TextMeshProUGUI>(),
                        uimgr.LevelName3.transform.GetChild(0).GameObject().GetComponent<TextMeshProUGUI>()
                    ]), iga.ChangeLevelName);
            }

            if (iga.ShowText is not null)
                try
                {
                    InGameText.Instance.ShowText(iga.ShowText, 5);
                }
                catch
                {
                }

            if (iga.SetZombieIdle is not null)
                foreach (var z in Board.Instance.zombieArray)
                {
                    if (z is null)continue;
                    z?.anim.Play("idle");
                }

            if (iga.ClearAllIceRoads is not null)
            {
                try
                {
                    // 直接设置冰道位置，无视觉效果无伤害
                    // IceRoad 字段: offset 0x30 = theX (当前位置), offset 0x34 = originalX (原始位置)
                    for (var i = 0; i < Board.Instance.iceRoads.Count; i++)
                    {
                        try
                        {
                            var iceRoad = Board.Instance.iceRoads[i];
                            if (iceRoad != null)
                            {
                                // 使用反射或直接字段访问将 theX 设为 originalX
                                // IceRoad 的 theX 和 originalX 是 float 类型
                                var ptr = iceRoad.Pointer;
                                unsafe
                                {
                                    float* theXPtr = (float*)(ptr + 0x30);
                                    float* originalXPtr = (float*)(ptr + 0x34);
                                    *theXPtr = *originalXPtr;
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }


            if (iga.NextWave is not null) Board.Instance.newZombieWaveCountDown = 0;

            //感谢@高数带我飞(Github:https://github.com/LibraHp/)的植物阵容码导出和解码代码
            //现在此修改器和高数带我飞的修改器植物阵容码可以互通了
            if (iga.WriteField is not null
                && iga.ClearOnWritingField is not null)
            {
                if (iga.GaoShuMode == false)
                    try
                    {
                        var plants = JsonSerializer.Deserialize<List<PlantInfo>>(iga.WriteField);
                        if (plants is not null)
                        {
                            if ((bool)iga.ClearOnWritingField)
                            {
                                var allPlants = Lawnf.GetAllPlants();
                                if (allPlants != null)
                                {
                                    for (var i = allPlants.Count - 1; i >= 0; i--)
                                        allPlants[i]?.Die();
                                }
                            }

                            foreach (var plant in plants)
                            {
                                var pl = CreatePlant.Instance.SetPlant(plant.Column, plant.Row,
                                    (PlantType)plant.ID);
                                if (pl is null) continue;
                                if (pl.GetComponent<Plant>().isLily)
                                    pl.GetComponent<Plant>().theLilyType = (PlantType)plant.LilyType;
                            }
                        }
                    }
                    catch (JsonException)
                    {
                    }
                    catch (NotSupportedException)
                    {
                    }

                if (iga.GaoShuMode == true)
                {
                    if ((bool)iga.ClearOnWritingField)
                    {
                        var allPlants = Lawnf.GetAllPlants();
                        if (allPlants != null)
                        {
                            for (var i = allPlants.Count - 1; i >= 0; i--)
                                try
                                {
                                    allPlants[i]?.Die();
                                }
                                catch
                                {
                                }
                        }
                    }

                    //from Gaoshu
                    var lineupCode = DecompressString(iga.WriteField);
                    var plantEntries = lineupCode.Split(';');
                    foreach (var entry in plantEntries)
                    {
                        var plantData = entry.Split(',');
                        if (plantData.Length == 3)
                            if (int.TryParse(plantData[0], out var column) &&
                                int.TryParse(plantData[1], out var row) &&
                                int.TryParse(plantData[2], out var plantType))
                                CreatePlant.Instance.SetPlant(column, row, (PlantType)plantType);
                    }
                }
            }

            if (iga.ReadField is not null)
            {
                if (iga.GaoShuMode == false)
                {
                    List<PlantInfo> bases = [];
                    List<PlantInfo> plants = [];
                    var allPlants = Lawnf.GetAllPlants();
                    if (allPlants != null)
                    {
                        foreach (var plant in allPlants)
                        {
                            if (plant is null) continue;
                            if (plant.plantTag.potPlant || plant.isLily)
                            {
                                bases.Add(new PlantInfo
                                {
                                    ID = (int)plant.thePlantType,
                                    Row = plant.thePlantRow,
                                    Column = plant.thePlantColumn,
                                    LilyType = (int)plant.theLilyType
                                });
                                continue;
                            }

                            plants.Add(new PlantInfo
                            {
                                ID = (int)plant.thePlantType,
                                Row = plant.thePlantRow,
                                Column = plant.thePlantColumn,
                                LilyType = (int)plant.theLilyType
                            });
                        }
                    }

                    bases.AddRange(plants);
                    DataSync.Instance.Value.SendData(new InGameActions
                    {
                        WriteField = JsonSerializer.Serialize(bases)
                    });
                }

                if (iga.GaoShuMode == true)
                {
                    //from Gaoshu
                    List<string> lineupData = [];
                    var allPlants = Lawnf.GetAllPlants();
                    if (allPlants != null)
                    {
                        foreach (var plant in allPlants)
                        {
                            // 格式为 "行,列,类型"
                            if (plant == null) continue;
                            var plantData = $"{plant.thePlantColumn},{plant.thePlantRow},{(int)plant.thePlantType}";
                            lineupData.Add(plantData);
                        }
                    }

                    var lineupCode = string.Join(";", lineupData);
                    DataSync.Instance.Value.SendData(new InGameActions
                    {
                        WriteField = CompressString(lineupCode)
                    });
                }
            }

            if (iga.ReadVases is not null)
            {
                List<VaseInfo> vases = [];
                foreach (var vase in Board.Instance.griditemArray)
                {
                    if (vase is null ||
                        vase.theItemType is not (GridItemType)4 or (GridItemType)5 or (GridItemType)6) continue;
                    vases.Add(new VaseInfo
                    {
                        Row = vase.theItemRow,
                        Col = vase.theItemColumn,
                        PlantType = (int)vase.Cast<ScaryPot>().thePlantType,
                        ZombieType = (int)vase.Cast<ScaryPot>().theZombieType
                    });
                }

                DataSync.Instance.Value.SendData(new InGameActions
                {
                    WriteVases = JsonSerializer.Serialize(vases)
                });
            }

            if (iga.WriteVases is not null && iga.ClearOnWritingVases is not null)
                try
                {
                    var fieldVases = JsonSerializer.Deserialize<List<VaseInfo>>(iga.WriteVases);
                    if (fieldVases is not null)
                    {
                        if ((bool)iga.ClearOnWritingVases)
                            for (var i = Board.Instance.griditemArray.Count - 1; i >= 0; i--)
                                if (Board.Instance.griditemArray[i] is not null &&
                                    Board.Instance.griditemArray[i].theItemType is (GridItemType)4
                                        or (GridItemType)5 or (GridItemType)6)
                                {
                                    Board.Instance.griditemArray[i].gameObject.active = false;
                                    Destroy(Board.Instance.griditemArray[i]);
                                }

                        foreach (var vase in fieldVases)
                        {
                            var g = GridItem.SetGridItem(vase.Col, vase.Row, GridItemType.ScaryPot);
                            g.Cast<ScaryPot>().thePlantType = (PlantType)vase.PlantType;
                            g.Cast<ScaryPot>().theZombieType = (ZombieType)vase.ZombieType;
                        }
                    }
                }
                catch (JsonException)
                {
                    MLogger.LogError("布阵代码存在错误！");
                }
                catch (NotSupportedException)
                {
                    MLogger.LogError("布阵代码存在错误！");
                }

            if (iga.Card is not null
                && iga.PlantType is not null)
            {
                var droppedCard = Lawnf.SetDroppedCard(new Vector2(0f, 0f), (PlantType)iga.PlantType);
                if (droppedCard != null && InGameUI.Instance != null)
                    droppedCard.GameObject().transform.SetParent(InGameUI.Instance.transform);
            }

            if (iga.ReadZombies is not null)
            {
                if (iga.GaoShuMode == false)
                {
                    // JSON 模式（保留完整对象结构）
                    List<ToolModData.ZombieInfo> zombies = [];
                    foreach (var zombie in Board.Instance.zombieArray!)
                        if (zombie is not null && zombie.gameObject is not null && !zombie.isMindControlled)
                            zombies.Add(new ToolModData.ZombieInfo
                            {
                                ID = (int)zombie.theZombieType,
                                X = zombie.gameObject.transform.position.x,
                                Row = zombie.theZombieRow
                            });

                    DataSync.Instance.Value.SendData(new InGameActions
                    {
                        WriteZombies = JsonSerializer.Serialize(zombies)
                    });
                }

                if (iga.GaoShuMode == true)
                {
                    // 高数模式（原生字符串格式 + 压缩）
                    List<string> zombieDataList = [];
                    foreach (var zombie in Board.Instance.zombieArray!)
                        if (zombie is not null && zombie.gameObject is not null && !zombie.isMindControlled)
                        {
                            // 使用原始坐标精度（保持 7.8724456 的完整精度）
                            var zombieData =
                                $"{zombie.theZombieRow},{zombie.gameObject.transform.position.x},{(int)zombie.theZombieType}";
                            zombieDataList.Add(zombieData);
                        }

                    // 先拼接后压缩（添加GZIP+Base64处理）
                    var zombieCode = string.Join(";", zombieDataList);
                    var compressedCode = CompressString(zombieCode); // GZIP压缩 + Base64编码

                    DataSync.Instance.Value.SendData(new InGameActions
                    {
                        WriteZombies = compressedCode // 发送压缩后的Base64字符串
                    });
                }
            }

            if (iga.WriteZombies is not null && iga.ClearOnWritingZombies is not null)
            {
                if (iga.GaoShuMode == false)
                    try
                    {
                        var fieldZombies = JsonSerializer.Deserialize<List<ToolModData.ZombieInfo>>(iga.WriteZombies);
                        if (fieldZombies is not null)
                        {
                            if ((bool)iga.ClearOnWritingZombies)
                            {
                                Il2CppReferenceArray<Object> zombies =
                                    FindObjectsOfTypeAll(Il2CppType.Of<Zombie>());
                                for (var i = zombies.Count - 1; i >= 0; i--)
                                    try
                                    {
                                        ((Zombie)zombies[i])?.Die();
                                    }
                                    catch
                                    {
                                    }

                                Board.Instance.zombieArray!.Clear();
                            }

                            foreach (var z in fieldZombies)
                                CreateZombie.Instance.SetZombie(z.Row, (ZombieType)z.ID, z.X);
                        }
                    }
                    catch (JsonException)
                    {
                    }
                    catch (NotSupportedException)
                    {
                    }

                if (iga.GaoShuMode == true)
                {
                    if ((bool)iga.ClearOnWritingZombies)
                    {
                        Il2CppReferenceArray<Object> zombies =
                            FindObjectsOfTypeAll(Il2CppType.Of<Zombie>());
                        for (var i = zombies.Count - 1; i >= 0; i--)
                            try
                            {
                                ((Zombie)zombies[i])?.Die();
                            }
                            catch
                            {
                            }

                        Board.Instance.zombieArray!.Clear();
                    }

                    // 先解压后解析（添加GZIP+Base64处理）
                    var zombieCode = DecompressString(iga.WriteZombies); // Base64解码 + GZIP解压
                    var zombieEntries = zombieCode.Split(';');

                    foreach (var entry in zombieEntries)
                    {
                        var zombieData = entry.Split(',');
                        if (zombieData.Length == 3)
                            if (int.TryParse(zombieData[0], out var row) &&
                                float.TryParse(zombieData[1], out var x) &&
                                int.TryParse(zombieData[2], out var zombieType))
                                CreateZombie.Instance.SetZombie(row, (ZombieType)zombieType, x);
                    }
                }
            }

            if (iga.ReadMix is not null)
            {
                // 高数模式（原生字符串格式 + 压缩）
                List<string> zombieDataList = [];
                foreach (var zombie in Board.Instance.zombieArray!)
                    if (zombie is not null && zombie.gameObject is not null && !zombie.isMindControlled)
                    {
                        // 使用原始坐标精度（保持 7.8724456 的完整精度）
                        var zombieData =
                            $"{zombie.theZombieRow},{zombie.gameObject.transform.position.x},{(int)zombie.theZombieType}";
                        zombieDataList.Add(zombieData);
                    }

                // 先拼接后压缩（添加GZIP+Base64处理）
                var zombieCode = string.Join(";", zombieDataList);
                var zombieString = CompressString(zombieCode); // GZIP压缩 + Base64编码

                //from Gaoshu
                List<string> lineupData = [];
                var allPlants = Lawnf.GetAllPlants();
                if (allPlants != null)
                {
                    foreach (var plant in allPlants)
                    {
                        // 格式为 "行,列,类型"
                        if (plant == null) continue;
                        var plantData = $"{plant.thePlantColumn},{plant.thePlantRow},{(int)plant.thePlantType}";
                        lineupData.Add(plantData);
                    }
                }

                var plantCode = string.Join(";", lineupData);
                var PlantString = CompressString(plantCode); // GZIP压缩 + Base64编码

                var result = PlantString + "|" + zombieString;

                DataSync.Instance.Value.SendData(new InGameActions
                {
                    WriteMix = result
                });
            }

            if (iga.WriteMix is not null && iga.ClearOnWritingMix is not null)
            {
                if (iga.ClearOnWritingMix == true)
                {
                    var allPlants = Lawnf.GetAllPlants();
                    if (allPlants != null)
                    {
                        for (var i = allPlants.Count - 1; i >= 0; i--)
                            try
                            {
                                allPlants[i]?.Die();
                            }
                            catch
                            {
                            }
                    }

                    Il2CppReferenceArray<Object> zombies =
                        FindObjectsOfTypeAll(Il2CppType.Of<Zombie>());
                    for (var i = zombies.Count - 1; i >= 0; i--)
                        try
                        {
                            ((Zombie)zombies[i])?.Die();
                        }
                        catch
                        {
                        }

                    Board.Instance.zombieArray!.Clear();
                }

                //from Gaoshu
                var codes = iga.WriteMix.Split('|');

                var plantCode = codes[0];
                plantCode = DecompressString(plantCode);
                var plantEntries = plantCode.Split(';');
                foreach (var entry in plantEntries)
                {
                    var plantData = entry.Split(',');
                    if (plantData.Length == 3)
                        if (int.TryParse(plantData[0], out var column) &&
                            int.TryParse(plantData[1], out var row) &&
                            int.TryParse(plantData[2], out var plantType))
                            CreatePlant.Instance.SetPlant(column, row, (PlantType)plantType);
                }

                var zombieCode = codes[1];
                zombieCode = DecompressString(zombieCode);
                var zombieEntries = zombieCode.Split(';');

                foreach (var entry in zombieEntries)
                {
                    var zombieData = entry.Split(',');
                    if (zombieData.Length == 3)
                        if (int.TryParse(zombieData[0], out var row) &&
                            float.TryParse(zombieData[1], out var x) &&
                            int.TryParse(zombieData[2], out var zombieType))
                            CreateZombie.Instance.SetZombie(row, (ZombieType)zombieType, x);
                }
            }

            if (iga.StartMower is not null && Board.Instance != null && Board.Instance.mowerArray != null)
            {
                // 只启动 Board.Instance.mowerArray 中的小推车，避免启动预制体
                foreach (var mower in Board.Instance.mowerArray)
                {
                    try
                    {
                        if (mower != null) mower.StartMove();
                    }
                    catch { }
                }
            }

            if (iga.CreateMower is not null && GameAPP.board != null)
            {
                // 先清除现有的小推车，避免重复生成导致的问题
                if (Board.Instance != null && Board.Instance.mowerArray != null)
                {
                    for (var i = Board.Instance.mowerArray.Count - 1; i >= 0; i--)
                    {
                        try
                        {
                            var mower = Board.Instance.mowerArray[i];
                            if (mower != null)
                            {
                                Destroy(mower.gameObject);
                            }
                        }
                        catch { }
                    }
                    Board.Instance.mowerArray.Clear();
                }
                
                // 重新生成小推车
                var initBoard = GameAPP.board.GetComponent<InitBoard>();
                if (initBoard != null) initBoard.InitMower();
            }
            if (iga.SetAward is not null && Board.Instance != null) Lawnf.SetAward(Board.Instance, Vector2.zero);
            if (iga.DestroyAward is not null && Board.Instance != null)
            {
                var prizes = FindObjectsOfType<PrizeMgr>();
                foreach (var prize in prizes)
                {
                    try { Destroy(prize.gameObject); } catch { }
                }
            }

            // PvE 斗蛐蛐快速布阵功能
            if (iga.PvE == true && Board.Instance != null)
            {
                // 放置5个礼盒在第3行（row=2），第1-5列（column=0-4）
                // 礼盒打开时会通过 PresentPatchA 检查 LockPresent1-5 来决定生成什么植物
                CreatePlant.Instance.SetPlant(0, 2, PlantType.Present);
                CreatePlant.Instance.SetPlant(1, 2, PlantType.Present);
                CreatePlant.Instance.SetPlant(2, 2, PlantType.Present);
                CreatePlant.Instance.SetPlant(3, 2, PlantType.Present);
                CreatePlant.Instance.SetPlant(4, 2, PlantType.Present);
                
                // 放置僵尸：1个巨人盲盒 + 1个钻石盲盒 + 4个黄金盲盒
                CreateZombie.Instance.SetZombie(2, ZombieType.RandomGargantuar, 8);
                CreateZombie.Instance.SetZombie(2, ZombieType.DiamondRandomZombie, 8);
                CreateZombie.Instance.SetZombie(2, ZombieType.RandomPlusZombie, 8);
                CreateZombie.Instance.SetZombie(2, ZombieType.RandomPlusZombie, 8);
                CreateZombie.Instance.SetZombie(2, ZombieType.RandomPlusZombie, 8);
                CreateZombie.Instance.SetZombie(2, ZombieType.RandomPlusZombie, 8);
            }
        }

        if (data is GameModes ga)
        {
            PatchMgr.GameModes = ga;

            if (InGame())
            {
                var t = Board.Instance.boardTag;
                t.isScaredyDream = PatchMgr.GameModes.ScaredyDream;
                t.isColumn = PatchMgr.GameModes.ColumnPlanting;
                t.isSeedRain = PatchMgr.GameModes.SeedRain;
                Board.Instance.boardTag = t;
            }
        }
    }

    public static void ProcessData(string data)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(data) || string.IsNullOrEmpty(data)) return;
            if (Dev) MLogger.LogError(data);
            var json = JsonNode.Parse(data)!.AsObject();
            switch ((int)json["ID"]!)
            {
                case 1:
                {
                    ProcessData(json.Deserialize<ValueProperties>());
                    break;
                }
                case 2:
                {
                    ProcessData(json.Deserialize<BasicProperties>());
                    break;
                }
                case 3:
                {
                    ProcessData(json.Deserialize<InGameHotkeys>());
                    break;
                }
                case 4:
                {
                    ProcessData(json.Deserialize<SyncTravelBuff>());
                    break;
                }
                case 6:
                {
                    ProcessData(json.Deserialize<InGameActions>());
                    break;
                }
                case 7:
                {
                    ProcessData(json.Deserialize<GameModes>());
                    break;
                }
                case 15:
                {
                    var all = json.Deserialize<SyncAll>();
                    if (all.TravelBuffs is not null) ProcessData((SyncTravelBuff)all.TravelBuffs);
                    if (all.InGameActions is not null) ProcessData((InGameActions)all.InGameActions);
                    if (all.BasicProperties is not null) ProcessData((BasicProperties)all.BasicProperties);
                    if (all.ValueProperties is not null) ProcessData((ValueProperties)all.ValueProperties);
                    if (all.GameModes is not null) ProcessData((GameModes)all.GameModes);
                    break;
                }
                case 16:
                {
                    Application.Quit();
                    break;
                }
            }
        }
        catch (JsonException)
        {
            Core.Instance.Value.LoggerInstance.LogError("操作失败，可能是点太快或设置词条时出错，取消后重设置一下吧。");
        }
        catch (Exception ex)
        {
            Core.Instance.Value.LoggerInstance.LogError(ex + ex.StackTrace);
        }
    }

    protected static void EnableAll<T>(bool enabled) where T : Component
    {
        _ = FindObjectsOfTypeAll(Il2CppType.Of<T>()).All(c =>
        {
            var v = c?.Cast<T>()?.gameObject.GetComponent<BoxCollider2D>();
            var v2 = c?.Cast<T>()?.gameObject.GetComponent<PolygonCollider2D>();
            if (v is not null) v.enabled = enabled;
            if (v2 is not null) v2.enabled = enabled;
            return true;
        });
    }
}