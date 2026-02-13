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
    [HarmonyPatch(typeof(NoticeMenu), "Start")]
    public static class Help_Patch
    {
        public static void Postfix()
        {
            try
            {
                // 不再在 NoticeMenu.Start 中直接执行 LateInit，避免 TravelDictionary/TravelMgr 尚未完全初始化时
                // 提前调用 TravelMgr.GetText 导致 Il2CppStringToManaged 崩溃。
                // 初始化逻辑统一交给 GameAPP.Start + LateInitHelper 协程处理。
                if (Core.inited)
                    return;

                Core.Instance.Value.LoggerInstance.LogInfo("[PVZRHTools] NoticeMenu.Start Postfix 被调用，等待 GameAPP.Start 中的 LateInitHelper 负责初始化");
            }
            catch (System.Exception ex)
            {
                Core.Instance.Value.LoggerInstance.LogError($"[PVZRHTools] LateInit 执行出错: {ex}");
            }
        }
    }

    // 辅助类用于在主线程中延迟执行 LateInit
    public class LateInitHelper : MonoBehaviour
    {
        public void Start()
        {
            this.StartCoroutine(DelayedLateInit());
        }
        
        private System.Collections.IEnumerator DelayedLateInit()
        {
            // 等待 TravelDictionary 初始化（3.4.1 起词条文本存放在 TravelDictionary 中）
            yield return new WaitForSeconds(2.0f);
            
            // 重试多次，直到 TravelDictionary.advancedBuffsText 可用
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    if (TravelDictionary.advancedBuffsText != null && TravelDictionary.advancedBuffsText.Count > 0)
                    {
                        Core.Instance.Value.LoggerInstance.LogInfo($"[PVZRHTools] TravelDictionary 已初始化，开始执行 LateInit (尝试 {i + 1}/10)");
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
            
            Core.Instance.Value.LoggerInstance.LogError("[PVZRHTools] 延迟初始化失败：TravelDictionary 未能在超时时间内初始化");
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
        public static Lazy<Core> Instance { get; set; } = new();
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
                if (Port.Value.Value < 10000 || Port.Value.Value > 60000)
                {
                    MessageBox(0, "Port值无效，已使用默认值13531", "修改器警告", 0);
                    Port.Value.Value = 13531;
                }

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

                Dictionary<int, string> plants = [];
                Dictionary<int, string> zombies = [];
                GameObject gameObject = new();
                GameObject back1 = new();
                back1.transform.SetParent(gameObject.transform);
                GameObject name1 = new("Name");
                GameObject shadow1 = new("Shadow");
                shadow1.transform.SetParent(name1.transform);
                var nameText1 = name1.AddComponent<TextMeshPro>();
                name1.transform.SetParent(gameObject.transform);
                GameObject info1 = new("Info");
                info1.transform.SetParent(gameObject.transform);
                GameObject cost1 = new("Cost");
                cost1.transform.SetParent(gameObject.transform);
                var alm = gameObject.AddComponent<AlmanacPlantBank>();
                alm.cost = cost1.AddComponent<TextMeshPro>();
                alm.plantName_shadow = shadow1.AddComponent<TextMeshPro>();
                alm.plantName = name1.GetComponent<TextMeshPro>();
                alm.introduce = info1.AddComponent<TextMeshPro>();
                gameObject.AddComponent<TravelMgr>();
#if GAR
                var gardenIds = "";
#endif
                for (var i = 0; i < GameAPP.resourcesManager.allPlants.Count; i++)
                {
                    alm.theSeedType = (int)GameAPP.resourcesManager.allPlants[i];
                    alm.InitNameAndInfoFromJson();
                    var item =
                        $"{alm.plantName.GetComponent<TextMeshPro>().text} ({(int)GameAPP.resourcesManager.allPlants[i]})";
                    MLogger.LogInfo($"Dumping Plant String: {item}");
                    plants.Add((int)GameAPP.resourcesManager.allPlants[i], item);
                    HealthPlants.Add(GameAPP.resourcesManager.allPlants[i], -1);
#if GAR
                    if (needRegen)
                        gardenIds = Utils.OutputGardenTexture(i, alm.plantName.GetComponent<TextMeshPro>().text, gardenIds);
#endif
                    alm.plantName.GetComponent<TextMeshPro>().text = "";
                }

                Object.Destroy(gameObject);
#if GAR
                if (needRegen)
                {
                    if (File.Exists("PVZRHTools/GardenTools/plant_id.txt"))
                        File.Delete("PVZRHTools/GardenTools/plant_id.txt");
                    using FileStream gid = new("PVZRHTools/GardenTools/plant_id.txt", FileMode.Create);
                    var buffer = Encoding.UTF8.GetBytes(gardenIds);
                    gid.Write(buffer, 0, buffer.Length);
                    gid.Flush();
                    Utils.GenerateGardenData();
                }
#endif
                GameObject gameObject2 = new();
                GameObject back2 = new();
                back2.transform.SetParent(gameObject2.transform);
                back2.AddComponent<SpriteRenderer>();
                GameObject name2 = new("Name");
                GameObject shadow2 = new("Name_1");
                shadow2.transform.SetParent(name2.transform);
                shadow2.AddComponent<TextMeshPro>();
                name2.AddComponent<TextMeshPro>();
                name2.transform.SetParent(gameObject2.transform);
                name2.AddComponent<TextMeshPro>();
                GameObject info2 = new("Info");
                info2.transform.SetParent(gameObject2.transform);
                var almz = gameObject2.AddComponent<AlmanacMgrZombie>();
                almz.info = info2;
                almz.zombieName = name2;
                almz.introduce = info2.AddComponent<TextMeshPro>();
                ;

                for (var i = 0; i < GameAPP.resourcesManager.allZombieTypes.Count; i++)
                {
                    almz.theZombieType = GameAPP.resourcesManager.allZombieTypes[i];
                    almz.InitNameAndInfoFromJson();
                    HealthZombies.Add(GameAPP.resourcesManager.allZombieTypes[i], -1);

                    if (!string.IsNullOrEmpty(almz.zombieName.GetComponent<TextMeshPro>().text))
                    {
                        var item =
                            $"{almz.zombieName.GetComponent<TextMeshPro>().text} ({(int)GameAPP.resourcesManager.allZombieTypes[i]})";
                        MLogger.LogInfo($"Dumping Zombie String: {item}");
                        zombies.Add((int)GameAPP.resourcesManager.allZombieTypes[i], item);
                        almz.zombieName.GetComponent<TextMeshPro>().text = "";
                    }
                }

                Object.Destroy(gameObject2);
                //zombies.Add(54, "试验假人僵尸 (54)");

                // 遍历所有键值对以确保捕获所有词条（包括 MOD 添加的不连续 ID 词条）
                // 3.4.1：词条文本数据改为存放在 TravelDictionary 中
                MLogger.LogInfo("[PVZRHTools] 开始读取词条数据...");

                List<string> advBuffs = [];
                List<string> ultiBuffs = [];
                List<string> debuffs = [];
                List<string> investBuffs = [];

                AdvBuffs = [];
                PatchMgr.UltiBuffs = [];
                Debuffs = [];
                PatchMgr.InvestBuffs = [];

                if (TravelDictionary.advancedBuffsText == null ||
                    TravelDictionary.ultimateBuffsText == null ||
                    TravelDictionary.debuffData == null)
                {
                    MLogger.LogWarning("[PVZRHTools] TravelDictionary 中的词条数据尚未初始化，跳过导出");
                }
                else
                {
                    MLogger.LogInfo($"[PVZRHTools] TravelDictionary.advancedBuffsText.Count = {TravelDictionary.advancedBuffsText.Count}");
                    MLogger.LogInfo($"[PVZRHTools] TravelDictionary.ultimateBuffsText.Count = {TravelDictionary.ultimateBuffsText.Count}");
                    MLogger.LogInfo($"[PVZRHTools] TravelDictionary.debuffData.Count = {TravelDictionary.debuffData.Count}");

                    // 高级词条
                    int advCount = TravelDictionary.advancedBuffsText?.Count ?? 0;
                    if (advCount > 0)
                    {
                        foreach (var kvp in TravelDictionary.advancedBuffsText)
                        {
                            int key = (int)kvp.Key;
                            if (!string.IsNullOrEmpty(kvp.Value))
                            {
                                MLogger.LogInfo($"Dumping Advanced Buff String:#{key} {kvp.Value}");
                                advBuffs.Add($"#{key} {kvp.Value}");
                            }
                        }
                        AdvBuffs = new bool[advCount];
                        MLogger.LogInfo($"[PVZRHTools] AdvBuffs 数组大小: {advCount}");
                    }
                    else
                    {
                        AdvBuffs = new bool[0];
                        MLogger.LogWarning("[PVZRHTools] 未找到高级词条，AdvBuffs 数组为空");
                    }

                    // 究极词条
                    int ultiCount = TravelDictionary.ultimateBuffsText?.Count ?? 0;
                    if (ultiCount > 0)
                    {
                        foreach (var kvp in TravelDictionary.ultimateBuffsText)
                        {
                            int key = (int)kvp.Key;
                            if (!string.IsNullOrEmpty(kvp.Value))
                            {
                                MLogger.LogInfo($"Dumping Ultimate Buff String:#{key} {kvp.Value}");
                                ultiBuffs.Add($"#{key} {kvp.Value}");
                            }
                        }
                        PatchMgr.UltiBuffs = new bool[ultiCount];
                        MLogger.LogInfo($"[PVZRHTools] UltiBuffs 数组大小: {ultiCount}");
                    }
                    else
                    {
                        PatchMgr.UltiBuffs = new bool[0];
                        MLogger.LogWarning("[PVZRHTools] 未找到究极词条，UltiBuffs 数组为空");
                    }

                    // 负面词条（Debuff）
                    // 参考游戏内代码实现：直接通过 TravelDictionary.debuffData[debuff].Item1 访问文本
                    int debuffCount = TravelDictionary.debuffData?.Count ?? 0;
                    if (debuffCount > 0)
                    {
                        int successCount = 0;
                        int failCount = 0;
                        foreach (var kvp in TravelDictionary.debuffData)
                        {
                            int key = (int)kvp.Key;
                            string desc = $"Debuff_{key}";
                            
                            try
                            {
                                // 直接访问 Item1 属性（与游戏内代码一致）
                                var item1Value = kvp.Value.Item1;
                                if (!string.IsNullOrEmpty(item1Value))
                                {
                                    desc = item1Value;
                                    successCount++;
                                }
                                else
                                {
                                    failCount++;
                                }
                            }
                            catch (System.Exception ex)
                            {
                                // 访问失败，使用默认名称
                                MLogger.LogWarning($"[PVZRHTools] 访问 debuff {key} 的 Item1 属性失败: {ex.GetType().Name}");
                                failCount++;
                                desc = $"Debuff_{key}";
                            }

                            var line = $"#{key} {desc}";
                            MLogger.LogInfo($"Dumping Debuff String: {line}");
                            debuffs.Add(line);
                        }
                        Debuffs = new bool[debuffCount];
                        MLogger.LogInfo($"[PVZRHTools] Debuffs 数组大小: {debuffCount}, 成功读取: {successCount}, 失败: {failCount}");
                    }
                    else
                    {
                        Debuffs = new bool[0];
                        MLogger.LogWarning("[PVZRHTools] 未找到负面词条，Debuffs 数组为空");
                    }

                    // 投资词条（InvestBuff）：从 TravelMgr.InvestBuffsData 里读取描述
                    try
                    {
                        if (TravelMgr.InvestBuffsData != null)
                        {
                            int maxInvestKey = -1;
                            foreach (var kvp in TravelMgr.InvestBuffsData)
                            {
                                int key = (int)kvp.Key;
                                if (key > maxInvestKey) maxInvestKey = key;
                            }

                            if (maxInvestKey >= 0)
                            {
                                for (int i = 0; i <= maxInvestKey; i++)
                                {
                                    if (TravelMgr.InvestBuffsData.TryGetValue((InvestBuff)i, out var buff))
                                    {
                                        string desc = null;
                                        try
                                        {
                                            desc = buff.GetDescription();
                                        }
                                        catch (System.Exception ex)
                                        {
                                            MLogger.LogWarning($"[PVZRHTools] 读取 InvestBuff 描述时出错: id={i}, err={ex.Message}");
                                        }

                                        if (!string.IsNullOrEmpty(desc))
                                        {
                                            MLogger.LogInfo($"Dumping Invest Buff String:#{i} {desc}");
                                            investBuffs.Add($"#{i} {desc}");
                                        }
                                    }
                                }

                                PatchMgr.InvestBuffs = new bool[maxInvestKey + 1];
                                MLogger.LogInfo($"[PVZRHTools] InvestBuffs 数组大小: {maxInvestKey + 1}");
                            }
                            else
                            {
                                PatchMgr.InvestBuffs = new bool[0];
                                MLogger.LogWarning("[PVZRHTools] 未找到投资词条，InvestBuffs 数组为空");
                            }
                        }
                        else
                        {
                            PatchMgr.InvestBuffs = new bool[0];
                            MLogger.LogWarning("[PVZRHTools] TravelMgr.InvestBuffsData 为空，InvestBuffs 数组为空");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        PatchMgr.InvestBuffs = new bool[0];
                        MLogger.LogWarning($"[PVZRHTools] 读取投资词条数据时出错: {ex.Message}");
                    }
                }

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
                Directory.CreateDirectory("./PVZRHTools");
                File.WriteAllText("./PVZRHTools/InitData.json", JsonSerializer.Serialize(initData));
                
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

        public override void Load()
        {
            Console.OutputEncoding = Encoding.UTF8;
            ClassInjector.RegisterTypeInIl2Cpp<PatchMgr>();
            ClassInjector.RegisterTypeInIl2Cpp<DataProcessor>();
            ClassInjector.RegisterTypeInIl2Cpp<LateInitHelper>();
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
            Instance = new Lazy<Core>(this);
            if (Time.timeScale == 0) Time.timeScale = 1;
            // 初始化时不设置 SyncSpeed，让游戏内部的速度调整功能正常工作
            // SyncSpeed = Time.timeScale; // 注释掉，避免干扰游戏内部速度调整
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
                new Lazy<ConfigEntry<KeyCode>>(Config.Bind("PVZRHTools", nameof(KeyRandomCard), KeyCode.H));
            ModsHash = new Lazy<ConfigEntry<string>>(Config.Bind("PVZRHTools", nameof(ModsHash), ""));

            KeyBindings = new Lazy<List<ConfigEntry<KeyCode>>>([
                KeyTimeStop.Value, KeyTopMostCardBank.Value, KeyShowGameInfo.Value,
                KeyAlmanacCreatePlant.Value, KeyAlmanacCreateZombie.Value, KeyAlmanacZombieMindCtrl.Value,
                KeyAlmanacCreatePlantVase.Value, KeyAlmanacCreateZombieVase.Value,KeyRandomCard.Value
            ]);
            Config.Save();
        }

        public override bool Unload()
        {
            if (inited)
            {
                if (GameAPP.gameSpeed == 0) GameAPP.gameSpeed = 1;
                try
                {
                    DataSync.Instance.SendData(new Exit());
                Thread.Sleep(100);
                    DataSync.Instance.modifierSocket.Shutdown(SocketShutdown.Both);
                    DataSync.Instance.modifierSocket.Close();
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
                                plantData.field_Public_Single_0 = float.Parse(fields[2]); // produce interval (保留)
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