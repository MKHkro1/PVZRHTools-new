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
    public bool? GameSpeedEnabled { get; set; }
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
    public bool? ZombieDamageLimit200 { get; set; }
    public int? ZombieDamageLimitValue { get; set; }
    public bool? ZombieSpeedModifyEnabled { get; set; }
    public float? ZombieSpeedMultiplier { get; set; }
    public bool? ZombieAttackMultiplierEnabled { get; set; }
    public float? ZombieAttackMultiplier { get; set; }
    public bool? PickaxeImmunity { get; set; }
    public bool? ZombieBulletReflectEnabled { get; set; }
    public float? ZombieBulletReflectChance { get; set; }
    public bool? UnlimitedCardSlots { get; set; }
    /// <summary>
    /// 僵尸状态并存 - 允许红温与寒冰、蒜毒状态同时存在
    /// </summary>
    public bool? ZombieStatusCoexist { get; set; }
    
    /// <summary>
    /// 鱼丸词条 - 坚不可摧(伤害最多200) + 高级后勤(双倍恢复, 阳光磁力菇CD减少)
    /// </summary>
    public bool? MNEntryEnabled { get; set; }
    
    /// <summary>
    /// 取消红卡种植限制 - 允许在非神秘模式种植红卡植物(AbyssSwordStar, UltimateMinigun, SolarSunflower)
    /// </summary>
    public bool? UnlockRedCardPlants { get; set; }

    /// <summary>
    /// 击杀升级 - 植物击杀僵尸时自动升级
    /// </summary>
    public bool? KillUpgrade { get; set; }

    /// <summary>
    /// 僵尸免疫一切负面效果 - 免疫负面buff、击退、吞噬、魅惑等（已弃用，保留兼容）
    /// </summary>
    public bool? ZombieImmuneAllDebuffs { get; set; }

    // 僵尸免疫效果 - 分开的9个开关
    /// <summary>僵尸免疫冻结</summary>
    public bool? ZombieImmuneFreeze { get; set; }
    /// <summary>僵尸免疫减速</summary>
    public bool? ZombieImmuneCold { get; set; }
    /// <summary>僵尸免疫黄油定身</summary>
    public bool? ZombieImmuneButter { get; set; }
    /// <summary>僵尸免疫蒜毒</summary>
    public bool? ZombieImmunePoison { get; set; }
    /// <summary>僵尸免疫红温</summary>
    public bool? ZombieImmuneJalaed { get; set; }
    /// <summary>僵尸免疫余烬</summary>
    public bool? ZombieImmuneEmbered { get; set; }
    /// <summary>僵尸免疫击退</summary>
    public bool? ZombieImmuneKnockback { get; set; }
    /// <summary>僵尸免疫魅惑</summary>
    public bool? ZombieImmuneMindControl { get; set; }
    /// <summary>僵尸免疫吞噬</summary>
    public bool? ZombieImmuneDevour { get; set; }

    /// <summary>
    /// 随机子弹 - 植物发射的子弹类型随机
    /// </summary>
    public bool? RandomBullet { get; set; }

    /// <summary>
    /// 星辉buff - 点击植物解锁星辉buff模式（如果该植物有星辉buff功能）
    /// </summary>
    public bool? StarUpBuff { get; set; }

    /// <summary>
    /// 随机升级模式 - 点击植物操控，手套点击自己升级，融合重置等级返还阳光
    /// </summary>
    public bool? RandomUpgradeMode { get; set; }

    /// <summary>
    /// PvE 斗蛐蛐布阵：盲盒僵尸置顶列表（-1 表示不置顶，沿用游戏原始随机）
    /// 依次对应 6 个盲盒僵尸位；不足 6 个时，前面的为有效配置，后面的忽略。
    /// </summary>
    public int? PvEBlindBoxZombie1 { get; set; }
    public int? PvEBlindBoxZombie2 { get; set; }
    public int? PvEBlindBoxZombie3 { get; set; }
    public int? PvEBlindBoxZombie4 { get; set; }
    public int? PvEBlindBoxZombie5 { get; set; }
    public int? PvEBlindBoxZombie6 { get; set; }
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
    // --- 基本保留字段 ---
    public bool? PvE { get; set; }
    public bool? AbyssCheat { get; set; }
    public bool? BuffRefreshNoLimit { get; set; }
    /// <summary>无限刷新 - 旅行词条/诸神进化无限刷新</summary>
    public bool? UnlimitedRefresh { get; set; }
    /// <summary>无限积分 - 水果忍者无限积分</summary>
    public bool? UnlimitedScore { get; set; }
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
    
    // --- 快照/回溯：局内存档 ---
    /// <summary>启用自动快照（按固定间隔将场上“植物+僵尸+阳光+金币”写入环形缓冲）</summary>
    // 自动快照已下线
    /// <summary>手动立即保存一个快照（不改变自动快照的开关）</summary>
    public bool? ManualSnapshot { get; set; }
    /// <summary>手动恢复最近一次快照</summary>
    public bool? RestoreLastSnapshot { get; set; }
    // 失败自动回溯已下线
    // 跨会话恢复已下线
    
    /// <summary>
    /// 召唤迷你黑曜石巨人
    /// </summary>
    public bool? SpawnPetGargantuar { get; set; }
    
    /// <summary>
    /// 召唤迷你黑橄榄将军
    /// </summary>
    public bool? SpawnPetFootball { get; set; }
    
    /// <summary>
    /// 召唤迷你雪皇
    /// </summary>
    public bool? PetSnowBoss { get; set; }
    
    /// <summary>
    /// 旗帜波词条功能 - 是否启用
    /// </summary>
    public bool? FlagWaveBuffEnabled { get; set; }
    
    /// <summary>
    /// 旗帜波词条功能 - 要应用的词条ID列表（使用 -1 作为分隔符，表示一个旗子的词条结束）
    /// </summary>
    public List<int>? FlagWaveBuffIds { get; set; }
    
    /// <summary>
    /// 旗帜波自定义字幕列表（10个旗帜波的自定义字幕）
    /// </summary>
    public List<string>? FlagWaveCustomTexts { get; set; }
    
    /// <summary>
    /// 播放特效ID（用于检索分区播放特效）
    /// </summary>
    public int? PlayParticleId { get; set; }
    
    /// <summary>
    /// 播放音效ID（用于检索分区播放音效）
    /// </summary>
    public int? PlaySoundId { get; set; }
    
    /// <summary>
    /// 请求获取出怪列表
    /// </summary>
    public bool? GetZombieList { get; set; }
    
    /// <summary>
    /// 修改出怪列表 - 格式：waveIndex,zombieIndex,zombieType
    /// </summary>
    public string? ModifyZombieList { get; set; }
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
    /// <summary>
    /// 投资词条（InvestBuff）名称列表
    /// </summary>
    public string[] InvestBuffs { get; set; }
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
    /// <summary>
    /// 投资词条在修改器中的开关状态
    /// </summary>
    public List<bool>? InvestTravelBuff { get; set; }
    public List<bool>? Debuffs { get; set; }
    public List<bool>? DebuffsInGame { get; set; }
    public readonly int ID => 4;
    /// <summary>
    /// 投资词条在游戏内的实际生效状态
    /// </summary>
    public List<bool>? InvestInGame { get; set; }
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

/// <summary>
///     game->modifier
///     17 - 出怪列表数据
/// </summary>
[Serializable]
public struct ZombieListData : ISyncData
{
    public readonly int ID => 17;
    
    /// <summary>
    /// 当前波数
    /// </summary>
    public int CurrentWave { get; set; }
    
    /// <summary>
    /// 出怪列表 - Key为波次索引，Value为该波的僵尸类型列表
    /// </summary>
    public Dictionary<int, List<int>> ZombieListByWave { get; set; }
}

public static class Modifier
{
    public static string CommandLineToken => "PVZRHTools";
    public static bool Dev { get; set; } = false;
}