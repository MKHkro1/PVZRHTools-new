using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Collections;
using System.Text.Json;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Utils;
using AlmanacData;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime;
using TMPro;
using ToolModData;
using UnityEngine;
using static ToolModBepInEx.PatchMgr;
using Color = UnityEngine.Color;
using Graphics = UnityEngine.Graphics;
using Logger = BepInEx.Logging.Logger;
using Object = UnityEngine.Object;

namespace ToolModBepInEx
{
    // NoticeMenu 在 3.4.2 版本中已不存在，初始化逻辑已由 GameAPP_Start_Patch 处理
    // [HarmonyPatch(typeof(NoticeMenu), "Start")]
    // public static class Help_Patch
    // {
    //     public static void Postfix()
    //     {
    //         try
    //         {
    //             // 不再在 NoticeMenu.Start 中直接执行 LateInit，避免 TravelDictionary/TravelMgr 尚未完全初始化时
    //             // 提前调用 TravelMgr.GetText 导致 Il2CppStringToManaged 崩溃。
    //             // 初始化逻辑统一交给 GameAPP.Start + LateInitHelper 协程处理。
    //             if (Core.inited)
    //                 return;
    //
    //             Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] NoticeMenu.Start Postfix 被调用，等待 GameAPP.Start 中的 LateInitHelper 负责初始化");
    //         }
    //         catch (System.Exception ex)
    //         {
    //             Core.Instance.Value.LoggerInstance.LogError($"[PVZRHTools] LateInit 执行出错: {ex}");
    //         }
    //     }
    // }

    // 辅助类用于在主线程中延迟执行 LateInit
    public class LateInitHelper : MonoBehaviour
    {
        public void Start()
        {
            this.StartCoroutine(DelayedLateInit());
        }
        
        private System.Collections.IEnumerator DelayedLateInit()
        {
            // 先等待一段时间，确保 GameAPP 与资源管理器完成初始化
            yield return new WaitForSeconds(2.0f);
            
            // 重试多次，直到基础游戏资源可用
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    if (GameAPP.resourcesManager != null &&
                        GameAPP.resourcesManager.allPlants != null &&
                        GameAPP.resourcesManager.allPlants.Count > 0)
                    {
                        Core.Instance.Value.LoggerInstance.LogInfo($"[PVZRHTools] 基础资源已初始化，开始执行 LateInit (尝试 {i + 1}/10)");
                        if (!Core.inited)
                        {
                            Core.Instance.Value.LateInit();
                            Core.inited = true;
                            Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] LateInit 执行完成");
                        }
                        Object.Destroy(gameObject);
                        yield break;
                    }
                }
                catch (System.Exception ex)
                {
                    Core.Instance.Value.LoggerInstance.LogWarning($"[PVZRHTools] 延迟初始化尝试 {i + 1} 失败: {ex.Message}");
                }
                yield return new WaitForSeconds(1.0f);
            }
            
            Core.Instance.Value.LoggerInstance.LogError("[PVZRHTools] 延迟初始化失败：基础资源未能在超时时间内初始化");
            Object.Destroy(gameObject);
        }
    }

    // 添加 GameAPP.Start 作为备用初始化点
    [HarmonyPatch(typeof(GameAPP), "Start")]
    public static class GameAPP_Start_Patch
    {
        public static void Postfix()
        {
            try
            {
                if (Core.inited) return;
                Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] GameAPP.Start Postfix 被调用，延迟执行 LateInit");
                // 在主线程中创建 GameObject 并启动协程
                var gameObject = new GameObject("LateInitHelper");
                Object.DontDestroyOnLoad(gameObject);
                gameObject.AddComponent<LateInitHelper>();
            }
            catch (System.Exception ex)
            {
                Core.Instance.Value.LoggerInstance.LogError($"[PVZRHTools] GameAPP.Start Postfix 执行出错: {ex}");
            }
        }
    }

    [BepInPlugin("tyyh.toolmod", "ToolMod", "3.23")]
    public class Core : BasePlugin
    {
        public static bool inited;

        public static Lazy<ConfigEntry<bool>> AlmanacZombieMindCtrl { get; set; } = new();
        public static Lazy<Core> Instance { get; set; } = new(() =>
            throw new InvalidOperationException("[PVZRHTools] Core.Instance 在 Load() 之前被访问"));
        public static Lazy<ConfigEntry<KeyCode>> KeyAlmanacCreatePlant { get; set; } = new();
        public static Lazy<ConfigEntry<KeyCode>> KeyAlmanacCreatePlantVase { get; set; } = new();
        public static Lazy<ConfigEntry<KeyCode>> KeyAlmanacCreateZombie { get; set; } = new();
        public static Lazy<ConfigEntry<KeyCode>> KeyAlmanacCreateZombieVase { get; set; } = new();
        public static Lazy<ConfigEntry<KeyCode>> KeyRandomCard { get; set; } = new();
        public static Lazy<ConfigEntry<KeyCode>> KeyAlmanacZombieMindCtrl { get; set; } = new();
        public static Lazy<List<ConfigEntry<KeyCode>>> KeyBindings { get; set; } = new();
        public static Lazy<ConfigEntry<KeyCode>> KeyShowGameInfo { get; set; } = new();
        public static Lazy<ConfigEntry<KeyCode>> KeyTimeStop { get; set; } = new();
        public static Lazy<ConfigEntry<KeyCode>> KeyTopMostCardBank { get; set; } = new();
        public static Lazy<ConfigEntry<string>> ModsHash { get; set; } = new();

        public static Lazy<ConfigEntry<int>> Port { get; set; } = new();
        public ManualLogSource LoggerInstance => Logger.CreateLogSource("ToolMod");

        public void LateInit()
        {
            if (inited)
            {
                LoggerInstance.LogWarning("[PVZRHTools] LateInit 已被调用过，跳过");
                return;
            }
            
            try
            {
                LoggerInstance.LogInfo("[PVZRHTools] LateInit 开始执行");

#if GAR
                var needRegen = false;
                var hash = Utils.ComputeFolderHash(Paths.PluginPath);
                if (ModsHash.Value.Value != hash)
                {
                    needRegen = true;
                    ModsHash.Value.Value = hash;

                    if (Directory.Exists("PVZRHTools\\GardenTools\\res"))
                    {
                        foreach (var f in Directory.GetFiles("PVZRHTools\\GardenTools\\res\\0"))
                            if (!(f.EndsWith("-1.png") || f.EndsWith("error.png")))
                                File.Delete(f);

                        foreach (var f in Directory.GetFiles("PVZRHTools\\GardenTools\\res\\1"))
                            if (!(f.EndsWith("-1.png") || f.EndsWith("error.png")))
                                File.Delete(f);

                        foreach (var f in Directory.GetFiles("PVZRHTools\\GardenTools\\res\\2"))
                            if (!(f.EndsWith("-1.png") || f.EndsWith("error.png")))
                                File.Delete(f);
                    }
                }
#if true
                needRegen = false;
#endif
#endif
                MLogger.LogWarning("以下id信息为动态生成，仅适用于当前游戏实例！！！");
                MLogger.LogWarning("以下id信息为动态生成，仅适用于当前游戏实例！！！");
                MLogger.LogWarning("以下id信息为动态生成，仅适用于当前游戏实例！！！");

                // 3.6：优先读取图鉴中文名，失败时回退到枚举名。
                Dictionary<int, string> plants = [];
                Dictionary<int, string> zombies = [];

                for (var i = 0; i < GameAPP.resourcesManager.allPlants.Count; i++)
                {
                    var pt = GameAPP.resourcesManager.allPlants[i];
                    string displayName = null;
                    try
                    {
                        var plantInfo = AlmanacDataLoader.GetPlantData(pt);
                        if (plantInfo != null && !string.IsNullOrWhiteSpace(plantInfo.name))
                            displayName = plantInfo.name.Trim();
                    }
                    catch
                    {
                    }
                    var item = !string.IsNullOrWhiteSpace(displayName)
                        ? $"{displayName} ({(int)pt})"
                        : $"{pt} ({(int)pt})";
                    MLogger.LogInfo($"Dumping Plant String: {item}");
                    plants[(int)pt] = item;
                    HealthPlants[pt] = -1;
                }

                for (var i = 0; i < GameAPP.resourcesManager.allZombieTypes.Count; i++)
                {
                    var zt = GameAPP.resourcesManager.allZombieTypes[i];
                    string displayName = null;
                    try
                    {
                        var zombieInfo = AlmanacDataLoader.GetZombieData(zt);
                        if (zombieInfo != null && !string.IsNullOrWhiteSpace(zombieInfo.name))
                            displayName = zombieInfo.name.Trim();
                    }
                    catch
                    {
                    }
                    var item = !string.IsNullOrWhiteSpace(displayName)
                        ? $"{displayName} ({(int)zt})"
                        : $"{zt} ({(int)zt})";
                    MLogger.LogInfo($"Dumping Zombie String: {item}");
                    zombies[(int)zt] = item;
                    HealthZombies[zt] = -1;
                }
                //zombies.Add(54, "试验假人僵尸 (54)");

                // 3.5 版本中 InvestBuffData 相关泛型在 IL2CPP 侧存在兼容性问题：
                // 访问 TravelMgr.InvestBuffsData 可能触发 BaseBuff<InvestBuff> 约束异常。
                // 因此这里恢复 Advanced/Ultimate/Debuff 读取，Invest 仅做安全兜底读取。
                List<string> advBuffs = [];
                List<string> ultiBuffs = [];
                List<string> debuffs = [];
                List<string> investBuffs = [];

                // Advanced
                try
                {
                    if (TravelDictionary.advancedBuffsText != null && TravelDictionary.advancedBuffsText.Count > 0)
                    {
                        int maxAdvKey = -1;
                        foreach (var kvp in TravelDictionary.advancedBuffsText)
                        {
                            int key = (int)kvp.Key;
                            if (key > maxAdvKey) maxAdvKey = key;
                        }

                        for (int id = 0; id <= maxAdvKey; id++)
                        {
                            if (TravelDictionary.advancedBuffsText.ContainsKey((AdvBuff)id))
                            {
                                var value = TravelDictionary.advancedBuffsText[(AdvBuff)id];
                                if (!string.IsNullOrEmpty(value))
                                    advBuffs.Add($"#{id} {value}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MLogger.LogWarning($"[PVZRHTools] 读取 Advanced 词条失败: {ex.Message}");
                }
                AdvBuffs = new bool[advBuffs.Count];

                // Ultimate
                try
                {
                    if (TravelDictionary.ultimateBuffsText != null && TravelDictionary.ultimateBuffsText.Count > 0)
                    {
                        int maxUltiKey = -1;
                        foreach (var kvp in TravelDictionary.ultimateBuffsText)
                        {
                            int key = (int)kvp.Key;
                            if (key > maxUltiKey) maxUltiKey = key;
                        }

                        for (int id = 0; id <= maxUltiKey; id++)
                        {
                            if (TravelDictionary.ultimateBuffsText.ContainsKey((UltiBuff)id))
                            {
                                var value = TravelDictionary.ultimateBuffsText[(UltiBuff)id];
                                if (!string.IsNullOrEmpty(value))
                                    ultiBuffs.Add($"#{id} {value}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MLogger.LogWarning($"[PVZRHTools] 读取 Ultimate 词条失败: {ex.Message}");
                }
                PatchMgr.UltiBuffs = new bool[ultiBuffs.Count];

                // Debuff
                try
                {
                    if (TravelDictionary.debuffData != null && TravelDictionary.debuffData.Count > 0)
                    {
                        int maxDebuffKey = -1;
                        foreach (var kvp in TravelDictionary.debuffData)
                        {
                            int key = (int)kvp.Key;
                            if (key > maxDebuffKey) maxDebuffKey = key;
                        }

                        for (int id = 0; id <= maxDebuffKey; id++)
                        {
                            if (!TravelDictionary.debuffData.ContainsKey((TravelDebuff)id)) continue;
                            string text = null;
                            try
                            {
                                if (TravelDictionary.debuffData != null &&
                                    TravelDictionary.debuffData.ContainsKey((TravelDebuff)id))
                                {
                                    text = TravelDictionary.debuffData[(TravelDebuff)id].Item1;
                                }
                            }
                            catch
                            {
                            }
                            if (string.IsNullOrWhiteSpace(text))
                                text = ((TravelDebuff)id).ToString();
                            debuffs.Add($"#{id} {text}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    MLogger.LogWarning($"[PVZRHTools] 读取 Debuff 词条失败: {ex.Message}");
                }
                Debuffs = new bool[debuffs.Count];

                // Invest：3.6 下访问 TravelMgr.InvestBuffsData 可能触发泛型约束异常，
                // 这里避免触发该类型加载，优先稳定启动。
                try
                {
                    var values = Enum.GetValues(typeof(InvestBuff));
                    int maxInvestId = -1;
                    foreach (var val in values)
                    {
                        int id = (int)val;
                        if (id > maxInvestId) maxInvestId = id;
                        string text = TryGetTravelTextViaReflection((InvestBuff)id);
                        if (string.IsNullOrWhiteSpace(text) || text.StartsWith("EnumValue", StringComparison.OrdinalIgnoreCase))
                            text = GetInvestBuffChineseName(id);
                        if (string.IsNullOrWhiteSpace(text))
                            text = ((InvestBuff)id).ToString();
                        investBuffs.Add($"#{id} {text}");
                    }
                    PatchMgr.InvestBuffs = new bool[maxInvestId + 1];
                    MLogger.LogInfo("[PVZRHTools] Invest 词条文本读取：优先反射 GetText，失败回退枚举名");
                }
                catch (Exception ex)
                {
                    PatchMgr.InvestBuffs = new bool[0];
                    MLogger.LogWarning($"[PVZRHTools] Invest 词条兜底读取失败: {ex.Message}");
                }

                foreach (var line in advBuffs)
                    MLogger.LogInfo($"Dumping Advanced Buff String: {line}");
                foreach (var line in ultiBuffs)
                    MLogger.LogInfo($"Dumping Ultimate Buff String: {line}");
                foreach (var line in debuffs)
                    MLogger.LogInfo($"Dumping Debuff String: {line}");
                foreach (var line in investBuffs)
                    MLogger.LogInfo($"Dumping Invest Buff String: {line}");

                Dictionary<int, string> bullets = [];

                for (var i = 0; i < GameAPP.resourcesManager.allBullets.Count; i++)
                    if (GameAPP.resourcesManager.bulletPrefabs[GameAPP.resourcesManager.allBullets[i]] is not null)
                    {
                        var text =
                            $"{GameAPP.resourcesManager.bulletPrefabs[GameAPP.resourcesManager.allBullets[i]].name} ({(int)GameAPP.resourcesManager.allBullets[i]})";
                        MLogger.LogInfo($"Dumping Bullet String: {text}");
                        bullets.Add((int)GameAPP.resourcesManager.allBullets[i], text);
                        BulletDamage.Add(GameAPP.resourcesManager.allBullets[i], -1);
                    }

                Dictionary<int, string> firsts = [];
                foreach (var first in Enum.GetValues(typeof(Zombie.FirstArmorType))) firsts.Add((int)first, $"{first}");
                Dictionary<int, string> seconds = [];
                foreach (var second in Enum.GetValues(typeof(Zombie.SecondArmorType)))
                    seconds.Add((int)second, $"{second}");
                MLogger.LogWarning("以上id信息为动态生成，仅适用于当前游戏实例！！！");
                MLogger.LogWarning("以上id信息为动态生成，仅适用于当前游戏实例！！！");
                MLogger.LogWarning("以上id信息为动态生成，仅适用于当前游戏实例！！！");

                InitData initData = new()
                {
                    Plants = plants,
                    Zombies = zombies,
                    AdvBuffs = [.. advBuffs],
                    UltiBuffs = [.. ultiBuffs],
                    Bullets = bullets,
                    FirstArmors = firsts,
                    SecondArmors = seconds,
                    Debuffs = [.. debuffs],
                    InvestBuffs = [.. investBuffs]
                };
                ModifierPaths.EnsureInitDataDirectory();
                string initDataPath = Path.Combine(Directory.GetCurrentDirectory(), ModifierPaths.InitDataPath);
                string legacyInitDataPath = Path.Combine(Directory.GetCurrentDirectory(), ModifierPaths.LegacyInitDataPath);
                string initDataJson = JsonSerializer.Serialize(initData);
                File.WriteAllText(initDataPath, initDataJson);
                File.WriteAllText(legacyInitDataPath, initDataJson);
                
                // 在 LateInit 完成后，初始化 DataSync（这会启动修改器）
                MLogger.LogInfo("[PVZRHTools] LateInit: 数据准备完成，现在启动修改器");
                try
                {
                    DataSync.Initialize();
                    MLogger.LogInfo("[PVZRHTools] LateInit: 修改器启动成功");
                    
                    // 立即发送数据给UI，确保UI使用最新的数据
                    MLogger.LogInfo($"[PVZRHTools] LateInit: 立即发送词条数据给UI - Advanced={advBuffs.Count}, Ultimate={ultiBuffs.Count}, Debuff={debuffs.Count}");
                    DataSync.Instance.SendData(initData);
                    MLogger.LogInfo("[PVZRHTools] LateInit: 已发送词条数据给UI");
                }
                catch (System.Exception ex)
                {
                    // 静默处理错误（不记录错误日志）
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.LogError(ex);
            }

            inited = true;
        }

        private static string? TryGetTravelTextViaReflection(object buff)
        {
            try
            {
                var travelMgr = ResolveTravelMgr();
                if (travelMgr == null)
                    return null;

                var method = typeof(TravelMgr).GetMethod("GetText", new[] { typeof(object) });
                if (method == null)
                    return null;

                var result = method.Invoke(travelMgr, new[] { buff });
                var text = result?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
            catch
            {
            }

            return null;
        }

        private static string? GetInvestBuffChineseName(int id)
        {
            return id switch
            {
                0 => "完美开局",
                1 => "气氛组",
                2 => "无伤通关",
                3 => "植物重组",
                4 => "究极支援",
                5 => "恢复生机",
                6 => "简单模式",
                7 => "难度修改器",
                8 => "当头一棒",
                9 => "榜样的力量",
                10 => "基层贡献",
                11 => "绝对力量奖",
                12 => "存款回报",
                13 => "免费刷新",
                1000 => "现金为王",
                1001 => "降本增效",
                1002 => "精准暴击",
                1003 => "百花齐放",
                1004 => "榜样的力量II",
                1005 => "风暴骑士",
                1006 => "绕口令",
                1007 => "创伤小组",
                1008 => "打通上下游",
                1009 => "人海战术",
                1010 => "固定理财",
                1011 => "星变",
                1012 => "沙里淘金",
                1013 => "延迟收益",
                2000 => "幸运闪避",
                2001 => "攻防一体",
                2002 => "野蛮成长",
                2003 => "鲜血阶梯",
                2004 => "超光速提拔",
                2005 => "幸运之子",
                2006 => "积分大使飘飘",
                2007 => "开源节流",
                2008 => "概率事件",
                2009 => "被动收入",
                2010 => "星辉模仿卡",
                2011 => "淘宝积分",
                2012 => "养精蓄锐",
                2013 => "藏一手",
                _ => null
            };
        }

        private static TravelMgr? ResolveTravelMgr()
        {
            try
            {
                if (TravelMgr.Instance != null)
                    return TravelMgr.Instance;
            }
            catch
            {
            }

            try
            {
                if (GameAPP.Instance != null)
                    return GameAPP.Instance.GetComponent<TravelMgr>();
            }
            catch
            {
            }

            try
            {
                if (GameAPP.board != null)
                    return GameAPP.board.GetComponent<TravelMgr>();
            }
            catch
            {
            }

            return null;
        }

        public override void Load()
        {
            Console.OutputEncoding = Encoding.UTF8;
            // 先设置 Instance 与配置项，避免 Harmony 补丁失败时 LateInit 访问未初始化的 Lazy
            Instance = new Lazy<Core>(() => this);
            Port = new Lazy<ConfigEntry<int>>(Config.Bind("PVZRHTools", "Port", 13531, "修改窗口无法出现时可尝试修改此数值，范围10000~60000"));
            AlmanacZombieMindCtrl =
                new Lazy<ConfigEntry<bool>>(Config.Bind("PVZRHTools", nameof(AlmanacZombieMindCtrl), false));
            KeyTimeStop = new Lazy<ConfigEntry<KeyCode>>(Config.Bind("PVZRHTools", nameof(KeyTimeStop), KeyCode.Alpha5));
            KeyShowGameInfo =
                new Lazy<ConfigEntry<KeyCode>>(Config.Bind("PVZRHTools", nameof(KeyShowGameInfo), KeyCode.BackQuote));
            KeyAlmanacCreatePlant =
                new Lazy<ConfigEntry<KeyCode>>(Config.Bind("PVZRHTools", nameof(KeyAlmanacCreatePlant), KeyCode.B));
            KeyAlmanacCreateZombie =
                new Lazy<ConfigEntry<KeyCode>>(Config.Bind("PVZRHTools", nameof(KeyAlmanacCreateZombie), KeyCode.N));
            KeyAlmanacZombieMindCtrl =
                new Lazy<ConfigEntry<KeyCode>>(Config.Bind("PVZRHTools", nameof(KeyAlmanacZombieMindCtrl),
                    KeyCode.LeftControl));
            KeyTopMostCardBank =
                new Lazy<ConfigEntry<KeyCode>>(Config.Bind("PVZRHTools", nameof(KeyTopMostCardBank), KeyCode.Tab));
            KeyAlmanacCreatePlantVase =
                new Lazy<ConfigEntry<KeyCode>>(Config.Bind("PVZRHTools", nameof(KeyAlmanacCreatePlantVase), KeyCode.J));
            KeyAlmanacCreateZombieVase =
                new Lazy<ConfigEntry<KeyCode>>(Config.Bind("PVZRHTools", nameof(KeyAlmanacCreateZombieVase), KeyCode.K));
            KeyRandomCard =
                new Lazy<ConfigEntry<KeyCode>>(Config.Bind("PVZRHTools", nameof(KeyRandomCard), KeyCode.R));
            if (KeyRandomCard.Value.Value == KeyCode.H)
            {
                KeyRandomCard.Value.Value = KeyCode.R;
            }
            ModsHash = new Lazy<ConfigEntry<string>>(Config.Bind("PVZRHTools", nameof(ModsHash), ""));
            KeyBindings = new Lazy<List<ConfigEntry<KeyCode>>>([
                KeyTimeStop.Value, KeyTopMostCardBank.Value, KeyShowGameInfo.Value,
                KeyAlmanacCreatePlant.Value, KeyAlmanacCreateZombie.Value, KeyAlmanacZombieMindCtrl.Value,
                KeyAlmanacCreatePlantVase.Value, KeyAlmanacCreateZombieVase.Value,KeyRandomCard.Value
            ]);
            Config.Save();

            ClassInjector.RegisterTypeInIl2Cpp<PatchMgr>();
            ClassInjector.RegisterTypeInIl2Cpp<DataProcessor>();
            ClassInjector.RegisterTypeInIl2Cpp<LateInitHelper>();
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
            if (Time.timeScale == 0) Time.timeScale = 1;
        }

        public override bool Unload()
        {
            if (inited)
            {
                if (GameAPP.config != null && GameAPP.config.gameSpeed == 0) GameAPP.config.gameSpeed = 1;
                try
                {
                    DataSync.Instance.SendData(new Exit());
                    Thread.Sleep(100);
                    DataSync.Instance.Stop();
                    DataSync.Instance.Dispose();
                }
                catch (System.Exception ex)
                {
                    LoggerInstance.LogWarning($"[PVZRHTools] Unload: 关闭修改器连接失败: {ex.Message}");
                }
            }

            inited = false;
            return true;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr MessageBox(int hWnd, string text, string caption, uint type);
    }

    public class Utils
    {
        public static string ComputeFolderHash(string folderPath)
        {
            using var sha256 = SHA256.Create();
            sha256.Initialize();
            ProcessDirectory(folderPath, sha256);
            sha256.TransformFinalBlock([], 0, 0);
            return BytesToHex(sha256.Hash!);
        }

        public static Texture2D ConvertViaRenderTexture(Texture2D source)
        {
            var renderTex = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32 // 指定兼容格式
            );

            // 将原纹理复制到RenderTexture
            Graphics.Blit(source, renderTex);

            // 从RenderTexture读取像素
            RenderTexture.active = renderTex;
            Texture2D result = new(source.width, source.height, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
            result.Apply();

            // 清理资源
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(renderTex);
            return result;
        }

        public static Texture2D ExtractSpriteTexture(Sprite sprite)
        {
            // 获取Sprite在图集中的纹理区域
            var rect = sprite.textureRect;

            // 创建新Texture2D
            Texture2D outputTex = new(
                (int)rect.width,
                (int)rect.height,
                TextureFormat.RGBA32,
                false
            );

            // 复制像素
            Color[] pixels = ConvertViaRenderTexture(sprite.texture).GetPixels(
                (int)rect.x,
                (int)rect.y,
                (int)rect.width,
                (int)rect.height
            );

            outputTex.SetPixels(pixels);
            outputTex.Apply();

            return outputTex;
        }

        public static void GenerateGardenData()
        {
            var plantids = GameAPP.resourcesManager.allPlants;
            var currentPage = -1;
            List<Dictionary<string, object>> currentPlants = [];

            for (var i = 0; i < plantids.Count; i++)
            {
                var page = i / 32;
                var row = i % 32 / 8;
                var column = i % 8;
                var plantType = (int)plantids[i];

                var plantDict = new Dictionary<string, object>
                {
                    { "thePlantRow", row },
                    { "thePlantColumn", column },
                    { "thePlantType", plantType },
                    { "growStage", 2 },
                    { "waterLevel", 100 },
                    { "love", 100 },
                    { "nextTime", 11451419198L },
                    { "needTool", 1 },
                    { "page", page },
                    { "GrowStage", 2 }
                };

                if (page != currentPage)
                {
                    if (currentPage != -1) WritePage(currentPlants, currentPage);
                    currentPage = page;
                    currentPlants.Clear();
                }

                currentPlants.Add(plantDict);

                // 处理最后一页
                if (i == plantids.Count - 1) WritePage(currentPlants, currentPage);
            }
        }

        public static bool LoadPlantData()
        {
            try
            {
                // 加载文本资源
                var text = "";
                if (File.Exists("PVZRHTools\\plant_data.csv"))
                {
                    text = new StreamReader(File.Open("PVZRHTools\\plant_data.csv", FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite)).ReadToEnd();
                }
                else
                {
                    var t = Resources.Load<TextAsset>("plant_data");
                    if (t is not null)
                    {
                        using var f = File.Create("PVZRHTools/plant_data.csv");

                        byte[] buffer = t.bytes;
                        f.Write(buffer, 0, buffer.Length);
                        f.Flush();
                    }

                    return true;
                }

                // 分割文本行
                var lines = text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

                for (var i = 1; i < lines.Length; i++) // 跳过标题行
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line))
                        continue;

                    var fields = line.Split(',');
                    if (fields.Length < 7)
                    {
                        MLogger.LogError($"Invalid plant data format at line {i + 1}");
                        continue;
                    }

                    // 3.3.1版本使用PlantDataManager替代PlantDataLoader
                    try
                    {
                        if (PlantDataManager.PlantData_Default != null && PlantDataManager.PlantData_Default.ContainsKey((PlantType)int.Parse(fields[0])))
                        {
                            var plantData = PlantDataManager.PlantData_Default[(PlantType)int.Parse(fields[0])];
                            if (plantData != null)
                            {
                                plantData.attackInterval = float.Parse(fields[1]);     // field_Public_Single_0 -> attackInterval
                                plantData.produceInterval = float.Parse(fields[2]); // produce interval
                                plantData.attackDamage = int.Parse(fields[3]);
                                plantData.maxHealth = int.Parse(fields[4]);            // field_Public_Int32_0 -> maxHealth
                                plantData.cd = float.Parse(fields[5]);                // field_Public_Single_2 -> cd
                                plantData.cost = int.Parse(fields[6]);                // field_Public_Int32_1 -> cost
                                // 3.3.1版本使用字典，不需要数组操作
                            }
                        }
                    }
                    catch (FormatException ex)
                    {
                        MLogger.LogError($"Error parsing data at line {i + 1}: {ex.Message}");
                    }
                }

                return true;
            }
            catch (FileNotFoundException e1)
            {
                MLogger.LogError(e1);
            }
            catch (IOException e1)
            {
                MLogger.LogError(e1);
                MLogger.LogError("plant_data.csv被占用");
            }

            return false;
        }

        [SuppressMessage("Interoperability", "CA1416:验证平台兼容性", Justification = "<挂起>")]
        public static string OutputGardenTexture(int i, string name, string gardenIds)
        {
            var outputBaseDir = @"PVZRHTools\GardenTools\res";
            var filename = ((int)GameAPP.resourcesManager.allPlants[i]) + ".png";
            int[] sizes = [30, 45, 60];
            try
            {
                var sprite = GameAPP.resourcesManager.plantPreviews[GameAPP.resourcesManager.allPlants[i]]
                    .GetComponent<SpriteRenderer>().sprite;

                using var originalImage = Image.FromStream(
                    new MemoryStream(
                        [.. ImageConversion.EncodeToPNG(ConvertViaRenderTexture(ExtractSpriteTexture(sprite)))]));
                for (var ii = 0; ii < sizes.Length; ii++)
                {
                    var size = sizes[ii];
                    var outputDir = Path.Combine(outputBaseDir, $"{ii}");

                    // 确保输出目录存在
                    Directory.CreateDirectory(outputDir);

                    // 调整尺寸
                    using Bitmap resizedImage = new(size, size);
                    using (var g = System.Drawing.Graphics.FromImage(resizedImage))
                    {
                        // 设置高质量缩放参数
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighQuality;

                        g.DrawImage(originalImage, 0, 0, size, size);
                    }

                    // 保存图片
                    var outputPath = Path.Combine(outputDir, filename);
                    resizedImage.Save(outputPath, ImageFormat.Png);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理 {filename} 时出错: {ex}");
            }

            return string.Concat(gardenIds.Concat($"ID: {(int)GameAPP.resourcesManager.allPlants[i]}, {name}\n"));
        }

        private static string BytesToHex(byte[] bytes)
        {
            StringBuilder hex = new(bytes.Length * 2);
            foreach (var b in bytes)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }

        private static string GetRelativePath(string rootDir, string fullPath)
        {
            // 确保根目录以分隔符结尾
            if (!rootDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                rootDir += Path.DirectorySeparatorChar;

            Uri rootUri = new(rootDir);
            Uri fullUri = new(fullPath);
            var relativePath = Uri.UnescapeDataString(rootUri.MakeRelativeUri(fullUri).ToString());

            // 统一替换路径分隔符为当前系统分隔符
            relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            return relativePath;
        }

        private static void ProcessDirectory(string rootDir, SHA256 sha256)
        {
            // 处理当前目录下的所有文件，按相对路径排序
            var files = Directory.GetFiles(rootDir)
                .Select(f => new { FullPath = f, RelativePath = GetRelativePath(rootDir, f) })
                .OrderBy(f => f.RelativePath, StringComparer.Ordinal);

            foreach (var file in files)
            {
                if (file.RelativePath.Contains("ToolMod") || file.RelativePath.Contains("CustomizeLib") ||
                    file.FullPath.Contains("ModifiedPlus") || file.FullPath.Contains("UnityExplorer"))
                    continue;
                // 将相对路径作为元数据添加到哈希
                var pathBytes = Encoding.UTF8.GetBytes(file.RelativePath);
                sha256.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);

                // 将文件内容添加到哈希
                using var stream = File.OpenRead(file.FullPath);
                var buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
            }
        }

        private static void WritePage(List<Dictionary<string, object>> plants, int page)
        {
            var jsonData = new
            {
                plantData = plants
            };

            var jsonString = JsonSerializer.Serialize(jsonData);

            // 自动处理数组末尾逗号问题
            var fileName = page == 0
                ? "PVZRHTools\\GardenTools\\gar_all\\GardenData.json"
                : $"PVZRHTools\\GardenTools\\gar_all\\GardenData{page}.json";
            fileName = Path.Combine(Paths.GameRootPath, fileName);
            // 获取桌面路径示例（可根据需要修改）
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var fullPath = Path.Combine(desktopPath, fileName);

            using var file = File.CreateText(fullPath);
            file.Write(jsonString);
        }
    }
}