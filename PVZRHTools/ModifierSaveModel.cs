namespace PVZRHTools;

[Serializable]
public struct ModifierSaveModel
{
    // CS8983：带字段初始值设定项的结构必须显式声明构造函数（为空即可）。
    // 初始值本身有用：System.Text.Json 先构造结构再覆盖出现的字段，
    // 旧存档缺 GodEvolutionQuality* 字段时会保留 65/23/10/2 而不是反序列化成 0（权重 0 = 该档永不出现）。
    // CS8618：结构的默认值保证所有自动属性都有初值，空构造函数里无需逐个赋值 —— 这里整体关闭该检查。
#pragma warning disable CS8618
    public ModifierSaveModel()
    {
    }
#pragma warning restore CS8618

    public bool BuffRefreshNoLimit { get; set; }
    public bool UnlimitedRefresh { get; set; }
    public bool UnlimitedScore { get; set; }
    public bool CardNoInit { get; set; }
    public bool ChomperNoCD { get; set; }
    public bool SuperStarNoCD { get; set; }
    public bool AutoCutFruit { get; set; }
    public bool RandomCard { get; set; }
    public bool ClearOnWritingField { get; set; }
    public bool GaoShuMode { get; set; }
    public bool ClearOnWritingVases { get; set; }
    public bool ClearOnWritingZombies { get; set; }
    public bool ClearOnWritingMix { get; set; }
    public bool CobCannonNoCD { get; set; }
    public double Col { get; set; }
    public bool ColumnPlanting { get; set; }
    public bool ConveyBeltModify { get; set; }
    public List<int> ConveyBeltTypes { get; set; }
    public List<TravelBuffVM> Debuffs { get; set; }
    public List<TravelBuffVM> InvestBuffs { get; set; }
    public bool DeveloperMode { get; set; }
    public bool DevLour { get; set; }
    public bool Exchange { get; set; }
    public bool FastShooting { get; set; }
    public string FieldString { get; set; }
    public bool FreeCD { get; set; }
    public bool FreePlanting { get; set; }
    public double GameSpeed { get; set; }
    public bool GameSpeedEnabled { get; set; }
    public bool GarlicDay { get; set; }
    public double GloveFullCD { get; set; }
    public bool GloveFullCDEnabled { get; set; }
    public bool GloveNoCD { get; set; }
    public double HammerFullCD { get; set; }
    public bool HammerFullCDEnabled { get; set; }
    public bool HammerNoCD { get; set; }
    public bool WheelNoCD { get; set; }
    public bool HardPlant { get; set; }
    public bool ImmuneForceDeduct { get; set; }
    public bool CurseImmunity { get; set; }
    public bool CrushImmunity { get; set; }
    public bool TrampleImmunity { get; set; }
    public List<HotkeyUIVM> Hotkeys { get; set; }
    public List<int> InGameHotkeyCodes { get; set; }
    public bool HyponoEmperorNoCD { get; set; }
    public bool IsMindCtrl { get; set; }
    public bool ItemExistForever { get; set; }
    public int ItemType { get; set; }
    public bool JackboxNotExplode { get; set; }
    public int LockBulletType { get; set; }
    public bool LockMoney { get; set; }
    public int LockPresent { get; set; }
    public int LockWheat { get; set; }
    public bool LockSun { get; set; }
    public bool MineNoCD { get; set; }
    public bool NeedSave { get; set; }
    public string NewLevelName { get; set; }
    public double NewMoney { get; set; }
    public double NewSun { get; set; }
    public double NewZombieUpdateCD { get; set; }
    public bool NoFail { get; set; }
    public bool NoHole { get; set; }
    public bool NoIceRoad { get; set; }
    public bool DisableIceEffect { get; set; }
    public bool UnlockRedCardPlants { get; set; }
    public bool PlantingNoCD { get; set; }
    public int PlantType { get; set; }
    public bool PlantUpgrade { get; set; }
    public bool PresentFastOpen { get; set; }
    public double Row { get; set; }
    public bool ScaredyDream { get; set; }
    public bool SeedRain { get; set; }
    public bool Shooting1 { get; set; }
    public bool Shooting2 { get; set; }
    public bool Shooting3 { get; set; }
    public bool Shooting4 { get; set; }
    public string ShowText { get; set; }
    public bool StopSummon { get; set; }
    public bool SuperPresent { get; set; }
    public double Times { get; set; }
    public bool TopMostSprite { get; set; }
    public bool EnableAnimations { get; set; }
    /// <summary>「动画默认开启」的一次性迁移标记（2026-09-25）：迁移过的存档不再被强制打开，用户可自由关闭。</summary>
    public bool AnimationDefaultMigrated { get; set; }
    public bool IsDarkMode { get; set; }
    public List<TravelBuffVM> TravelBuffs { get; set; }
    public bool UltimateRamdomZombie { get; set; }
    public bool UltimateSuperGatling { get; set; }
    // 僵尸血量倍率（5.1.6）：开关+数值+一次性比例，与 VM 属性同名同语义
    public bool ZombieHealthMultiplierEnabled { get; set; }
    public double ZombieHealthMultiplier { get; set; } = 2.0;
    public double ZombieHealthRatio { get; set; } = 2.0;
    public bool AutoRhythmGame { get; set; }
    public bool UndeadBullet { get; set; }
    public bool OldObsidianBullet { get; set; }
    public bool UnlockAllFusions { get; set; }
    public string VasesFieldString { get; set; }
    public string ZombieFieldString { get; set; }
    public string MixFieldString { get; set; }
    public double ZombieSeaCD { get; set; }
    public bool ZombieSeaEnabled { get; set; }
    public bool ZombieSeaLowEnabled { get; set; }
    public List<int> ZombieSeaTypes { get; set; }
    public int ZombieType { get; set; }
    // PvE 斗蛐蛐布阵：盲盒僵尸置顶（-1 表示不置顶，沿用游戏原始随机）
    public int PvEBlindBoxZombie1 { get; set; }
    public int PvEBlindBoxZombie2 { get; set; }
    public int PvEBlindBoxZombie3 { get; set; }
    public int PvEBlindBoxZombie4 { get; set; }
    public int PvEBlindBoxZombie5 { get; set; }
    public int PvEBlindBoxZombie6 { get; set; }

    // 诸神：进化
    public bool GodEvolutionUnlimitedRefresh { get; set; }
    public bool GodEvolutionFreeUpgradeQuality { get; set; }
    public bool GodEvolutionLuckyEnabled { get; set; }
    public float GodEvolutionLucky { get; set; }
    public bool GodEvolutionDifficultyEnabled { get; set; }
    public int GodEvolutionDifficulty { get; set; }
    public bool GodEvolutionRefreshCountEnabled { get; set; }
    public int GodEvolutionRefreshCount { get; set; }
    public bool GodEvolutionMaxPlantCountEnabled { get; set; }
    public int GodEvolutionMaxPlantCount { get; set; }
    public bool GodEvolutionOptionCountEnabled { get; set; }
    public int GodEvolutionOptionCount { get; set; }
    public bool GodEvolutionUpgradeBuffChanceEnabled { get; set; }
    public int GodEvolutionUpgradeBuffChance { get; set; }
    public bool GodEvolutionSuperUpgrade { get; set; }
    public bool GodEvolutionForceSuperQuality { get; set; }
    public bool GodEvolutionUncrashable { get; set; }
    public bool GodEvolutionQualityWeightEnabled { get; set; }
    // #29 与游戏真实权重一致的默认值（65/23/10/2），避免旧存档缺字段时反序列化成 0（权重 0 = 该档永不出现）
    public float GodEvolutionQualityDefault { get; set; } = 65f;
    public float GodEvolutionQualitySilver { get; set; } = 23f;
    public float GodEvolutionQualityGold { get; set; } = 10f;
    public float GodEvolutionQualityDiamond { get; set; } = 2f;
    public bool GodEvolutionDamageMultiplierEnabled { get; set; }
    public float GodEvolutionDamageMultiplier { get; set; }

    // 诸神概率族开关：本批新增 5 项（专家邀请/超进化/质变/棱彩/随机），
    // 顺带补齐此前未落盘的 3 项（试炼/战术/隐藏难度），与 VM 属性同名同语义
    public bool GodEvolutionForceMissionBuff { get; set; }
    public bool GodEvolutionForceTacticalBuff { get; set; }
    public bool GodEvolutionCheatHard { get; set; }
    public bool GodEvolutionForceExpertBuff { get; set; }
    public bool GodEvolutionForceStarUpBuff { get; set; }
    public bool GodEvolutionForceMutationBuff { get; set; }
    public bool GodEvolutionForceIridescentBuff { get; set; }
    public bool GodEvolutionForceRandomBuff { get; set; }

    // 星辉冒险（REF「星辉冒险修改」组：普通/困难星星数 + 天赋免费点亮）
    public int StarAdvStar { get; set; }
    public int StarAdvStarHard { get; set; }
    public bool StarAdvFreeBuff { get; set; }

    // 小命令批次（E 组）：罗盘自定义冷却对（单字段 -1=关协议 + UI 开关）+ 旅行解除融合限制
    public double WheelFullCD { get; set; } = -1;
    public bool WheelFullCDEnabled { get; set; }
    public bool RemoveFusionLimit { get; set; }

    // 植物速度/攻击/血量三件套（5.3.1 #7+6）：开关+数值，与 VM 属性同名同语义（重启持久化）
    public bool PlantSpeedMultiplierEnabled { get; set; }
    public double PlantSpeedMultiplier { get; set; } = 1.0;
    public bool PlantAttackMultiplierEnabled { get; set; }
    public double PlantAttackMultiplier { get; set; } = 1.0;
    public bool PlantHealthMultiplierEnabled { get; set; }
    public double PlantHealthMultiplier { get; set; } = 1.0;

    // 自定义面板（2026-09-25「自由度排版」功能区）：收藏的功能 id 顺序 + 列数（1/2/3）。
    // 老存档没有这两个字段 ⇒ null / 0，由 CustomPanelViewModel.LoadFrom 兜底成"空面板 + 2 列"。
    public List<string>? CustomPanelItems { get; set; }
    public int CustomPanelColumns { get; set; }

    // 多配置存档（2026-09-25「多配置存档系统」）：命名槽位，每套 = 一份面板布局。
    // 老存档没有 ⇒ null，LoadFrom 兜底成空列表。
    public List<CustomPanelPreset>? CustomPanelPresets { get; set; }
}

/// <summary>一套命名的面板配置（多槽位存档用）。</summary>
public sealed class CustomPanelPreset
{
    public string Name { get; set; } = string.Empty;
    public List<string> Items { get; set; } = new();
    public int Columns { get; set; } = 2;
}