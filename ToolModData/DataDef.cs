namespace ToolModData;

public interface ISyncData
{
    public int ID { get; }
}

/// <summary>
///     modifier<->game
///     2
/// </summary>
[Serializable]
public struct BasicProperties : ISyncData
{
    public bool? CardNoInit { get; set; }
    public bool? ChomperNoCD { get; set; }
    public bool? SuperStarNoCD { get; set; }
    public bool? AutoCutFruit { get; set; }
    public bool? RandomCard { get; set; }
    public bool? ColumnGlove { get; set; }
    public bool? CobCannonNoCD { get; set; }
    public bool? DeveloperMode { get; set; }
    public bool? DevLour { get; set; }
    public bool? FastShooting { get; set; }
    public bool? FreePlanting { get; set; }
    public double? GameSpeed { get; set; }
    public bool? GarlicDay { get; set; }
    public double? GloveFullCD { get; set; }
    public bool? GloveNoCD { get; set; }
    public double? HammerFullCD { get; set; }
    public bool? HammerNoCD { get; set; }
    public bool? HardPlant { get; set; }
    public bool? ImmuneForceDeduct { get; set; }
    public bool? CurseImmunity { get; set; }
    public bool? CrushImmunity { get; set; }
    public bool? TrampleImmunity { get; set; }
    public bool? HyponoEmperorNoCD { get; set; }
    public readonly int ID => 2;
    public bool? ItemExistForever { get; set; }
    public bool? JackboxNotExplode { get; set; }
    public int? LockPresent { get; set; }
    public int? LockWheat { get; set; }
    public int? LockPresent1 { get; set; }
    public int? LockPresent2 { get; set; }
    public int? LockPresent3 { get; set; }
    public int? LockPresent4 { get; set; }
    public int? LockPresent5 { get; set; }
    public bool? MineNoCD { get; set; }
    public double? NewZombieUpdateCD { get; set; }
    public bool? NoHole { get; set; }
    public bool? NoIceRoad { get; set; }
    public bool? PlantingNoCD { get; set; }
    public bool? PlantUpgrade { get; set; }
    public bool? PresentFastOpen { get; set; }
    public bool? PvPPotRange { get; set; }
    public bool? SuperPresent { get; set; }
    public bool? UltimateRamdomZombie { get; set; }
    public bool? UltimateSuperGatling { get; set; }
    public bool? UndeadBullet { get; set; }
    public bool? UnlockAllFusions { get; set; }
    public bool? DisableIceEffect { get; set; }
    public bool? PotSmashingFix { get; set; }
    public bool? UnlimitedSunlight { get; set; }
    public bool? MagnetNutUnlimited { get; set; }
}

[Serializable]
public struct Exit : ISyncData
{
    public readonly int ID => 16;
}

/// <summary>
///     7
/// </summary>
[Serializable]
public struct GameModes : ISyncData
{
    public GameModes()
    {
    }

    public bool ColumnPlanting { get; set; } = false;
    public readonly int ID => 7;
    public bool ScaredyDream { get; set; } = false;
    public bool SeedRain { get; set; } = false;
}

/// <summary>
///     modifier->game
///     6
/// </summary>
[Serializable]
public struct InGameActions : ISyncData
{
    public bool? PvE { get; set; }
    public bool? AbyssCheat { get; set; }
    public bool? BuffRefreshNoLimit { get; set; }
    public bool? Card { get; set; }
    public string? ChangeLevelName { get; set; }
    public bool? ClearAllHoles { get; set; }
    public bool? ClearAllIceRoads { get; set; }
    public bool? ClearAllPlants { get; set; }
    public bool? ClearAllZombies { get; set; }
    public bool? ClearOnWritingField { get; set; }
    public bool? GaoShuMode { get; set; }
    public bool? ClearOnWritingVases { get; set; }
    public bool? ClearOnWritingZombies { get; set; }
    public bool? ClearOnWritingMix { get; set; }
    public int? Column { get; set; }
    public List<int>? ConveyBeltTypes { get; set; }
    public bool? CreateActiveMateorite { get; set; }
    public bool? CreateMower { get; set; }
    public bool? CreatePassiveMateorite { get; set; }
    public bool? CreateUltimateMateorite { get; set; }
    public int? CurrentMoney { get; set; }
    public int? CurrentSun { get; set; }
    public readonly int ID => 6;
    public int? ItemType { get; set; }
    public bool? LoadCustomPlantData { get; set; }
    public bool? LockMoney { get; set; }
    public bool? LockSun { get; set; }
    public bool? MindControlAllZombies { get; set; }
    public bool? NextWave { get; set; }
    public bool? NoFail { get; set; }
    public int? PlantType { get; set; }
    public bool? PlantVase { get; set; }
    public bool? ReadField { get; set; }
    public bool? ReadVases { get; set; }
    public bool? ReadZombies { get; set; }
    public bool? ReadMix { get; set; }
    public int? Row { get; set; }
    public bool? SetAward { get; set; }
    public bool? DestroyAward { get; set; }
    public bool? SetZombieIdle { get; set; }
    public string? ShowText { get; set; }
    public bool? StartMower { get; set; }
    public bool? StopSummon { get; set; }
    public bool? SummonMindControlledZombies { get; set; }
    public int? Times { get; set; }
    public string? WriteField { get; set; }
    public string? WriteVases { get; set; }
    public string? WriteZombies { get; set; }
    public string? WriteMix { get; set; }
    public int? ZombieSeaCD { get; set; }
    public bool? ZombieSeaEnabled { get; set; }
    public bool? ZombieSeaLowEnabled { get; set; }
    public List<int>? ZombieSeaTypes { get; set; }
    public int? ZombieType { get; set; }
    public bool? ZombieVase { get; set; }
    public bool? RandomVase { get; set; }
}

public struct InGameHotkeys : ISyncData
{
    public readonly int ID => 3;
    public List<int> KeyCodes { get; set; }
}

/// <summary>
///     modifier<-game
///     0
/// </summary>
[Serializable]
public struct InitData
{
    public string[] AdvBuffs { get; set; }
    public Dictionary<int, string> Bullets { get; set; }
    public string[] Debuffs { get; set; }
    public Dictionary<int, string> FirstArmors { get; set; }
    public Dictionary<int, string> Plants { get; set; }
    public Dictionary<int, string> SecondArmors { get; set; }
    public string[] UltiBuffs { get; set; }
    public Dictionary<int, string> Zombies { get; set; }
}

[Serializable]
public struct PlantInfo
{
    public int Column { get; set; }
    public int ID { get; set; }
    public int LilyType { get; set; }
    public int Row { get; set; }
}

[Serializable]
public struct SyncAll : ISyncData
{
    public BasicProperties? BasicProperties { get; set; }
    public GameModes? GameModes { get; set; }
    public readonly int ID => 15;
    public InGameActions? InGameActions { get; set; }
    public SyncTravelBuff? TravelBuffs { get; set; }
    public ValueProperties? ValueProperties { get; set; }
}

/// <summary>
///     modifier<->game
///     4
/// </summary>
[Serializable]
public struct SyncTravelBuff : ISyncData
{
    public List<bool>? AdvInGame { get; set; }
    public List<bool>? AdvTravelBuff { get; set; }
    public List<bool>? Debuffs { get; set; }
    public List<bool>? DebuffsInGame { get; set; }
    public readonly int ID => 4;
    public List<bool>? UltiInGame { get; set; }
    public List<bool>? UltiTravelBuff { get; set; }
}

/// <summary>
///     modifier->game
///     1
/// </summary>
[Serializable]
public struct ValueProperties : ISyncData
{
    public KeyValuePair<int, int>? BulletsDamage { get; set; }
    public KeyValuePair<int, int>? FirstArmorsHealth { get; set; }
    public readonly int ID => 1;
    public int? LockBulletType { get; set; }
    public KeyValuePair<int, int>? PlantsHealth { get; set; }
    public KeyValuePair<int, int>? SecondArmorsHealth { get; set; }
    public KeyValuePair<int, int>? ZombiesHealth { get; set; }
}

[Serializable]
public struct VaseInfo
{
    public int Col { get; set; }
    public int PlantType { get; set; }
    public int Row { get; set; }
    public int ZombieType { get; set; }
}

[Serializable]
public struct ZombieInfo
{
    public int ID { get; set; }
    public int Row { get; set; }
    public float X { get; set; }
}

public static class Modifier
{
    public static string CommandLineToken => "PVZRHTools";
    public static bool Dev { get; set; } = false;
}