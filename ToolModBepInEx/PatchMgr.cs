using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP.Utils;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Newtonsoft.Json;
using TMPro;
using ToolModData;
using Unity.VisualScripting;
using UnityEngine;
using static ToolModBepInEx.PatchMgr;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace ToolModBepInEx;

[HarmonyPatch(typeof(AlmanacCardZombie), "OnMouseDown")]
public static class AlmanacCardZombiePatch
{
    public static void Postfix(AlmanacCardZombie __instance)
    {
        AlmanacZombieType = __instance.theZombieType;
    }
}

/// <summary>
/// 旧版植物图鉴补丁 - AlmanacCard.OnMouseDown
/// </summary>
[HarmonyPatch(typeof(AlmanacCard), "OnMouseDown")]
public static class AlmanacCardPatch
{
    public static void Postfix(AlmanacCard __instance)
    {
        AlmanacSeedType = __instance.theSeedType;
    }
}

[HarmonyPatch(typeof(AlmanacPlantCtrl), "GetSeedType")]
public static class AlmanacPlantCtrlPatch
{
    public static void Postfix(AlmanacPlantCtrl __instance)
    {
        AlmanacSeedType = __instance.plantSelected;
    }
}

/// <summary>
/// 新版图鉴UI补丁 - AlmanacCardUI.OnPointerDown
/// </summary>
[HarmonyPatch(typeof(AlmanacCardUI), "OnPointerDown")]
public static class AlmanacCardUIPatch
{
    public static void Postfix(AlmanacCardUI __instance)
    {
        try
        {
            // 获取菜单名称来判断是植物还是僵尸图鉴
            string menuName = __instance.menu?.name ?? "";

            int plantId = (int)__instance.PlantType;
            int zombieId = (int)__instance.ZombieType;

            if (menuName.Contains("Plant"))
            {
                AlmanacSeedType = plantId;
            }
            else if (menuName.Contains("Zombie"))
            {
                AlmanacZombieType = (ZombieType)zombieId;
            }
            else
            {
                // 备用判断：根据ID值判断
                if (plantId > 0)
                {
                    AlmanacSeedType = plantId;
                }
                else if (zombieId > 0)
                {
                    AlmanacZombieType = (ZombieType)zombieId;
                }
            }
        }
        catch { }
    }
}

[HarmonyPatch(typeof(Board), "Awake")]
public static class BoardPatchA
{
    public static void Postfix()
    {
        var t = Board.Instance.boardTag;
        originalTravel = t.enableTravelPlant;
        t.isScaredyDream |= PatchMgr.GameModes.ScaredyDream;
        t.isColumn |= PatchMgr.GameModes.ColumnPlanting;
        t.isSeedRain |= PatchMgr.GameModes.SeedRain;
        t.enableAllTravelPlant |= UnlockAllFusions;
        Board.Instance.boardTag = t;
    }
}

[HarmonyPatch(typeof(Board), "NewZombieUpdate")]
public static class BoardPatchB
{
    public static void Postfix()
    {
        // 3.3.1版本：设置waveInterval以限制两波间最大刷怪CD
        try
        {
            if (NewZombieUpdateCD > 0f && NewZombieUpdateCD <= 30f && Board.Instance != null)
            {
                // 确保waveInterval不超过设置的最大值
                if (Board.Instance.waveInterval > NewZombieUpdateCD)
                {
                    Board.Instance.waveInterval = NewZombieUpdateCD;
                }
            }
        }
        catch { }
    }
}

/// <summary>
/// 旗帜波词条功能 - 检测旗帜波并应用词条
/// </summary>
[HarmonyPatch(typeof(Board), "Update")]
public static class BoardFlagWaveBuffPatch
{
    public static void Postfix(Board __instance)
    {
        try
        {
            if (!FlagWaveBuffEnabled || FlagWaveBuffIds == null || FlagWaveBuffIds.Count == 0)
                return;

            if (__instance == null || !InGame())
                return;

            // 检测旗帜波状态变化（从非旗帜波变为旗帜波）
            bool currentHugeWave = __instance.isHugeWave;
            bool wasHugeWave = _lastHugeWaveState;
            _lastHugeWaveState = currentHugeWave;

            // 只在进入旗帜波时应用词条（避免重复应用）
            if (currentHugeWave && !wasHugeWave)
            {
                UnlockNextFlagWaveBuff();
            }
        }
        catch (System.Exception ex)
        {
            MLogger?.LogError($"[PVZRHTools] 旗帜波词条检测失败: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// 旗帜波词条：按顺序每次解锁一个旗帜波的所有词条（永久保持到本局结束）
    /// </summary>
    private static void UnlockNextFlagWaveBuff()
    {
        try
        {
            // 防重复解锁：检查当前波数是否已经解锁过
            int currentWave = Board.Instance != null ? Board.Instance.theWave : -1;
            if (currentWave == _lastUnlockWave)
            {
                // 同一波已经解锁过，跳过
                return;
            }
            
            var travelMgr = ResolveTravelMgr(autoCreate: true);
            if (travelMgr == null)
            {
                MLogger?.LogWarning("[PVZRHTools] 无法找到 TravelMgr，无法应用旗帜波词条");
                return;
            }

            if (_flagWaveUnlockIndex < 0) _flagWaveUnlockIndex = 0;
            if (_flagWaveUnlockIndex >= FlagWaveBuffIds.Count)
                return; // 已全部解锁

            // 记录当前波数，防止重复解锁
            _lastUnlockWave = currentWave;

            // 关键日志：记录当前旗帜波的词条列表
            MLogger?.LogInfo($"[PVZRHTools] ========== 旗帜波词条开始处理 ==========");
            MLogger?.LogInfo($"[PVZRHTools] 当前波数: {currentWave}, 索引: {_flagWaveUnlockIndex}/{FlagWaveBuffIds.Count}");
            
            // 收集当前旗帜波的所有词条（直到遇到 -1 分隔符）
            var currentWaveBuffs = new List<int>();
            bool foundSeparator = false;
            
            while (_flagWaveUnlockIndex < FlagWaveBuffIds.Count)
            {
                var encodedBuffId = FlagWaveBuffIds[_flagWaveUnlockIndex];
                _flagWaveUnlockIndex++;
                
                // 如果遇到 -1 分隔符，表示当前旗子的词条结束
                if (encodedBuffId == -1)
                {
                    foundSeparator = true;
                    break;
                }
                
                // 添加到当前旗帜波的词条列表
                currentWaveBuffs.Add(encodedBuffId);
            }
            
            MLogger?.LogInfo($"[PVZRHTools] 当前旗帜波词条数量: {currentWaveBuffs.Count}, 词条列表: [{string.Join(", ", currentWaveBuffs)}]");
            
            // 遍历当前旗帜波的所有词条，依次应用
            foreach (var encodedBuffId in currentWaveBuffs)
            {
                ApplyFlagWaveBuff(encodedBuffId, travelMgr);
            }
            
            // 显示文本：如果FlagWaveCustomTexts不为null且有内容，使用自定义字幕；否则显示词条名字
            // 注意：即使没有词条（空括号），也要显示字幕（如果有自定义字幕的话）
            try
            {
                if (InGameText.Instance != null)
                {
                    string displayText = "";
                    
                    // 检查是否有自定义字幕（FlagWaveCustomTexts不为null说明是从Tab10来的，可以使用自定义字幕）
                    // FlagWaveCustomTexts为null说明是从Tab2来的，不使用自定义字幕
                    if (FlagWaveCustomTexts != null)
                    {
                        // 来自Tab10（词条专区）
                        if (_currentFlagWaveIndex < FlagWaveCustomTexts.Count && 
                            !string.IsNullOrWhiteSpace(FlagWaveCustomTexts[_currentFlagWaveIndex]))
                        {
                            // 使用自定义字幕（来自Tab10）
                            displayText = FlagWaveCustomTexts[_currentFlagWaveIndex];
                            MLogger?.LogInfo($"[PVZRHTools] 使用自定义字幕（来自Tab10）: {displayText}");
                        }
                        else if (currentWaveBuffs.Count > 0)
                        {
                            // Tab10没有自定义字幕，但有词条，显示词条名字和描述（格式：词条名字：（词条功能描述））
                            var buffNames = new List<string>();
                            foreach (var encodedBuffId in currentWaveBuffs)
                            {
                                string? buffName = GetBuffNameWithDescriptionFromEncodedId(encodedBuffId, travelMgr);
                                if (!string.IsNullOrEmpty(buffName))
                                {
                                    buffNames.Add(buffName);
                                }
                            }
                            
                            if (buffNames.Count > 0)
                            {
                                displayText = string.Join("、", buffNames);
                                MLogger?.LogInfo($"[PVZRHTools] Tab10未设置自定义字幕，显示词条名字和描述: {displayText}");
                            }
                        }
                    }
                    else
                    {
                        // 来自Tab2（常用功能），显示词条名字和描述（格式：词条名字：（词条功能描述））
                        var buffNames = new List<string>();
                        foreach (var encodedBuffId in currentWaveBuffs)
                        {
                            string? buffName = GetBuffNameWithDescriptionFromEncodedId(encodedBuffId, travelMgr);
                            if (!string.IsNullOrEmpty(buffName))
                            {
                                buffNames.Add(buffName);
                            }
                        }
                        
                        if (buffNames.Count > 0)
                        {
                            displayText = string.Join("、", buffNames);
                            MLogger?.LogInfo($"[PVZRHTools] 显示词条名字和描述（来自Tab2常用功能）: {displayText}");
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(displayText))
                    {
                        // 显示旗帜波文本
                        InGameText.Instance.ShowText(displayText, 5);
                        MLogger?.LogInfo($"[PVZRHTools] 旗帜波文本已显示: {displayText}");
                    }
                    else if (currentWaveBuffs.Count == 0)
                    {
                        // 如果没有词条也没有自定义字幕，记录日志
                        MLogger?.LogInfo($"[PVZRHTools] 当前旗帜波没有词条（空括号），且无自定义字幕，跳过显示");
                    }
                }
            }
            catch (System.Exception ex)
            {
                MLogger?.LogWarning($"[PVZRHTools] 显示旗帜波解锁文本失败: {ex.Message}");
            }
            
            // 增加旗帜波索引（无论是否有词条都要增加）
            _currentFlagWaveIndex++;
            
            // 如果当前旗帜波没有词条（空括号），在这里返回（但字幕已经处理过了）
            if (currentWaveBuffs.Count == 0)
            {
                MLogger?.LogInfo($"[PVZRHTools] 当前旗帜波没有词条（空括号），处理完成");
                return;
            }
            
            MLogger?.LogInfo($"[PVZRHTools] ========== 旗帜波词条处理完成 ==========");
        }
        catch (System.Exception ex)
        {
            MLogger?.LogError($"[PVZRHTools] 旗帜波词条应用失败: {ex.Message}\n{ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// 从词条文本中提取词条名字（去除ID前缀和描述）
    /// </summary>
    private static string ExtractBuffName(string? fullText)
    {
        if (string.IsNullOrEmpty(fullText))
            return "";
        
        // 如果包含 "#数字 " 前缀，去除它
        if (fullText.StartsWith("#"))
        {
            int spaceIndex = fullText.IndexOf(' ');
            if (spaceIndex > 0 && spaceIndex < fullText.Length - 1)
            {
                fullText = fullText.Substring(spaceIndex + 1);
            }
            else if (spaceIndex < 0)
            {
                // 如果没有空格，尝试找到第一个非数字字符
                int firstNonDigit = 0;
                for (int i = 1; i < fullText.Length; i++)
                {
                    if (!char.IsDigit(fullText[i]))
                    {
                        firstNonDigit = i;
                        break;
                    }
                }
                if (firstNonDigit > 0)
                {
                    fullText = fullText.Substring(firstNonDigit);
                }
            }
        }
        
        // 如果包含 "：" 或 ":" 分隔符，只取前面的部分（词条名字）
        int colonIndex = fullText.IndexOf('：');
        if (colonIndex < 0) colonIndex = fullText.IndexOf(':');
        if (colonIndex > 0)
        {
            fullText = fullText.Substring(0, colonIndex).Trim();
        }
        
        return fullText.Trim();
    }
    
    /// <summary>
    /// 从词条文本中提取词条名字和描述（去除ID前缀，保留名字和描述）
    /// 返回格式：词条名字：（词条功能描述）
    /// </summary>
    private static string ExtractBuffNameWithDescription(string? fullText)
    {
        if (string.IsNullOrEmpty(fullText))
            return "";
        
        // 如果包含 "#数字 " 前缀，去除它
        if (fullText.StartsWith("#"))
        {
            int spaceIndex = fullText.IndexOf(' ');
            if (spaceIndex > 0 && spaceIndex < fullText.Length - 1)
            {
                fullText = fullText.Substring(spaceIndex + 1);
            }
            else if (spaceIndex < 0)
            {
                // 如果没有空格，尝试找到第一个非数字字符
                int firstNonDigit = 0;
                for (int i = 1; i < fullText.Length; i++)
                {
                    if (!char.IsDigit(fullText[i]))
                    {
                        firstNonDigit = i;
                        break;
                    }
                }
                if (firstNonDigit > 0)
                {
                    fullText = fullText.Substring(firstNonDigit);
                }
            }
        }
        
        // 保留完整的文本（包括名字和描述），只去除ID前缀
        return fullText.Trim();
    }
    
    /// <summary>
    /// 应用单个旗帜波词条，返回词条名字（不包含描述）
    /// </summary>
    private static string? ApplyFlagWaveBuff(int encodedBuffId, TravelMgr travelMgr)
    {
        try
        {
            // 关键日志：记录原始编码ID，这是最重要的调试信息
            MLogger?.LogInfo($"[PVZRHTools] 开始处理词条: 编码ID={encodedBuffId}");

            // 解码Buff ID，获取类型和原始ID
            // 特别处理：如果编码ID在1000-1999范围内，强制识别为Ultimate类型
            // 这是最关键的判断：任何 >= 1000 且 < 2000 的编码ID都必须是Ultimate类型
            PatchMgr.BuffType buffType;
            int originalId;
            
            // 严格按照编码规则解码：2000+ = Debuff, 1000-1999 = Ultimate, 0-999 = Advanced
            if (encodedBuffId >= 2000)
            {
                buffType = PatchMgr.BuffType.Debuff;
                originalId = encodedBuffId - 2000;
                MLogger?.LogInfo($"[PVZRHTools] 旗帜波词条解码: 编码ID={encodedBuffId} -> Debuff, 原始ID={originalId}");
            }
            else if (encodedBuffId >= 1000 && encodedBuffId < 2000)
            {
                // 强制识别为Ultimate类型，避免被错误解码为Advanced
                // 这是最关键的判断：任何在 1000-1999 范围内的编码ID都必须是Ultimate类型
                buffType = PatchMgr.BuffType.Ultimate;
                originalId = encodedBuffId - 1000;
                MLogger?.LogInfo($"[PVZRHTools] 旗帜波词条解码: 编码ID={encodedBuffId} -> Ultimate (强制识别), 原始ID={originalId} (数组索引)");
            }
            else if (encodedBuffId >= 0 && encodedBuffId < 1000)
            {
                buffType = PatchMgr.BuffType.Advanced;
                originalId = encodedBuffId;
                MLogger?.LogInfo($"[PVZRHTools] 旗帜波词条解码: 编码ID={encodedBuffId} -> Advanced, 原始ID={originalId}");
            }
            else
            {
                // 无效的编码ID
                MLogger?.LogError($"[PVZRHTools] 旗帜波词条解码失败: 无效的编码ID={encodedBuffId} (应该是 0-2999 范围内的整数)");
                return null; // 直接返回，不处理
            }
            string? buffName = null;
            bool applied = false;

            // 关键验证：如果编码ID在1000-1999范围内，绝对不能进入Advanced分支
            if (encodedBuffId >= 1000 && encodedBuffId < 2000 && buffType == PatchMgr.BuffType.Advanced)
            {
                MLogger?.LogError($"[PVZRHTools] 严重错误: 编码ID={encodedBuffId} 在1000-1999范围内，但buffType被错误识别为Advanced！强制修正为Ultimate！");
                buffType = PatchMgr.BuffType.Ultimate;
                originalId = encodedBuffId - 1000;
            }
            
            MLogger?.LogInfo($"[PVZRHTools] 解码结果: buffType={buffType}, originalId={originalId}, encodedBuffId={encodedBuffId}");
            
            switch (buffType)
            {
                case PatchMgr.BuffType.Advanced:
                    // 高级词条：0..advancedCount-1（对应 advancedUpgrades[ID]）
                    // 再次验证：如果编码ID在1000-1999范围内，绝对不能应用为Advanced
                    if (encodedBuffId >= 1000 && encodedBuffId < 2000)
                    {
                        MLogger?.LogError($"[PVZRHTools] 严重错误: 尝试将编码ID={encodedBuffId} (应该是Ultimate) 应用为Advanced词条！直接返回，不处理！");
                        return null; // 直接返回，防止错误应用
                    }
                    
                    if (travelMgr.advancedUpgrades != null &&
                        originalId >= 0 && originalId < travelMgr.advancedUpgrades.Count)
                    {
                        travelMgr.advancedUpgrades[originalId] = true;
                        TravelMgr.advancedBuffs?.TryGetValue(originalId, out buffName);
                        
                        // 关键修复：同时更新 InGameAdvBuffs 数组，确保一致性
                        if (InGameAdvBuffs != null && originalId < InGameAdvBuffs.Length)
                        {
                            InGameAdvBuffs[originalId] = true;
                            MLogger?.LogInfo($"[PVZRHTools] 已同步更新 InGameAdvBuffs[{originalId}] = true");
                        }
                        
                        MLogger?.LogInfo($"[PVZRHTools] 旗帜波解锁高级词条 ID={originalId} (编码ID={encodedBuffId})");
                        applied = true;
                    }
                    break;
                    
                case PatchMgr.BuffType.Ultimate:
                    // 究极词条：originalId 是数组索引（参考 HeiTa 的实现）
                    // 编码时：U46 -> 1000 + 46 = 1046
                    // 解码后：originalId = 46，应该使用 46 作为 ultimateUpgrades[46] 的索引
                    // 参考 HeiTa: travel.ultimateUpgrades[choice.index] = 1; TravelMgr.ultimateBuffs[choice.index]
                    // 双重验证：确保编码ID在1000-1999范围内，且类型确实是Ultimate
                    if (encodedBuffId < 1000 || encodedBuffId >= 2000)
                    {
                        MLogger?.LogError($"[PVZRHTools] 严重错误: Ultimate词条的编码ID={encodedBuffId} 不在1000-1999范围内！这不应该发生！");
                        break; // 直接退出，不处理
                    }
                    if (buffType != PatchMgr.BuffType.Ultimate)
                    {
                        MLogger?.LogError($"[PVZRHTools] 严重错误: 编码ID={encodedBuffId} 应该对应Ultimate类型，但buffType={buffType}！强制修正为Ultimate类型。");
                        buffType = PatchMgr.BuffType.Ultimate; // 强制修正
                    }
                    
                    MLogger?.LogInfo($"[PVZRHTools] 开始应用Ultimate词条: encodedBuffId={encodedBuffId}, originalId={originalId} (数组索引), ultimateUpgrades.Count={travelMgr.ultimateUpgrades?.Count ?? 0}");
                    
                    if (travelMgr.ultimateUpgrades != null &&
                        originalId >= 0 && originalId < travelMgr.ultimateUpgrades.Count)
                    {
                        // 参考 HeiTa 的实现：直接使用数组索引
                        // 这是最关键的一步：使用 originalId 作为数组索引
                        travelMgr.ultimateUpgrades[originalId] = 1;
                        MLogger?.LogInfo($"[PVZRHTools] 已设置 ultimateUpgrades[{originalId}] = 1");
                        
                        // 关键修复：同时更新 InGameUltiBuffs 数组，确保一致性
                        // 这样即使有其他逻辑从 InGameUltiBuffs 同步回 ultimateUpgrades，也不会覆盖我们的设置
                        if (InGameUltiBuffs != null && originalId < InGameUltiBuffs.Length)
                        {
                            InGameUltiBuffs[originalId] = true;
                            MLogger?.LogInfo($"[PVZRHTools] 已同步更新 InGameUltiBuffs[{originalId}] = true");
                        }
                        else
                        {
                            MLogger?.LogWarning($"[PVZRHTools] 无法同步更新 InGameUltiBuffs: originalId={originalId}, InGameUltiBuffs.Length={InGameUltiBuffs?.Length ?? 0}");
                        }
                        
                        // 参考 HeiTa 的实现：直接使用数组索引作为字典键（假设字典键是连续的）
                        if (TravelMgr.ultimateBuffs != null)
                        {
                            try
                            {
                                // 直接使用 originalId 作为字典键（参考 HeiTa: TravelMgr.ultimateBuffs[choice.index]）
                                if (TravelMgr.ultimateBuffs.ContainsKey(originalId))
                                {
                                    TravelMgr.ultimateBuffs.TryGetValue(originalId, out buffName);
                                    MLogger?.LogInfo($"[PVZRHTools] 从字典获取Ultimate词条名称: 字典键={originalId}, 词条名称={buffName ?? "未知"}");
                                }
                                else
                                {
                                    // 如果字典键不连续，尝试通过排序后的键列表找到对应的键
                                    var keysList = new List<int>();
                                    foreach (var key in TravelMgr.ultimateBuffs.Keys)
                                        keysList.Add(key);
                                    keysList.Sort();
                                    if (originalId < keysList.Count)
                                    {
                                        var dictKey = keysList[originalId];
                                        TravelMgr.ultimateBuffs.TryGetValue(dictKey, out buffName);
                                        MLogger?.LogInfo($"[PVZRHTools] Ultimate词条字典键不连续: 数组索引={originalId}, 字典键={dictKey}, 词条名称={buffName ?? "未知"}");
                                    }
                                    else
                                    {
                                        MLogger?.LogWarning($"[PVZRHTools] Ultimate词条数组索引={originalId} 超出字典键列表范围 (字典键数量={keysList.Count})");
                                    }
                                }
                            }
                            catch (System.Exception ex)
                            {
                                MLogger?.LogWarning($"[PVZRHTools] 获取Ultimate词条名称失败: {ex.Message}\n{ex.StackTrace}");
                            }
                        }
                        else
                        {
                            MLogger?.LogWarning($"[PVZRHTools] TravelMgr.ultimateBuffs 为 null，无法获取词条名称");
                        }
                        
                        MLogger?.LogInfo($"[PVZRHTools] 旗帜波解锁究极词条成功: 数组索引={originalId}, 编码ID={encodedBuffId}, 词条名称={buffName ?? "未知"}");
                        
                        // 最终验证：确保没有错误地应用到Advanced
                        if (travelMgr.advancedUpgrades != null && originalId < travelMgr.advancedUpgrades.Count)
                        {
                            bool wasAdvancedApplied = travelMgr.advancedUpgrades[originalId];
                            if (wasAdvancedApplied)
                            {
                                MLogger?.LogWarning($"[PVZRHTools] 警告: 检测到 advancedUpgrades[{originalId}] 也被设置为true，这可能是之前的操作导致的");
                            }
                        }
                        
                        applied = true;
                    }
                    else
                    {
                        MLogger?.LogWarning($"[PVZRHTools] 旗帜波词条应用失败：Ultimate词条 数组索引={originalId} 超出范围 (数组大小={travelMgr.ultimateUpgrades?.Count ?? 0}, 编码ID={encodedBuffId})");
                        // 即使超出范围，也记录详细信息以便调试
                        if (travelMgr.ultimateUpgrades == null)
                        {
                            MLogger?.LogError($"[PVZRHTools] travelMgr.ultimateUpgrades 为 null！");
                        }
                        else if (originalId < 0)
                        {
                            MLogger?.LogError($"[PVZRHTools] originalId={originalId} 为负数！");
                        }
                        else if (originalId >= travelMgr.ultimateUpgrades.Count)
                        {
                            MLogger?.LogError($"[PVZRHTools] originalId={originalId} >= ultimateUpgrades.Count={travelMgr.ultimateUpgrades.Count}，数组越界！");
                        }
                    }
                    break;
                    
                case PatchMgr.BuffType.Debuff:
                    // 负面词条：使用 debuff[ID] (bool数组)
                    if (travelMgr.debuff != null &&
                        originalId >= 0 && originalId < travelMgr.debuff.Count)
                    {
                        travelMgr.debuff[originalId] = true;
                        TravelMgr.debuffs?.TryGetValue(originalId, out buffName);
                        
                        // 关键修复：同时更新 InGameDebuffs 数组，确保一致性
                        if (InGameDebuffs != null && originalId < InGameDebuffs.Length)
                        {
                            InGameDebuffs[originalId] = true;
                            MLogger?.LogInfo($"[PVZRHTools] 已同步更新 InGameDebuffs[{originalId}] = true");
                        }
                        
                        MLogger?.LogInfo($"[PVZRHTools] 旗帜波解锁负面词条 ID={originalId} (编码ID={encodedBuffId})");
                        applied = true;
                    }
                    break;
            }
            
            if (!applied)
            {
                MLogger?.LogWarning($"[PVZRHTools] 旗帜波词条应用失败：类型={buffType}, 原始ID={originalId}, 编码ID={encodedBuffId}");
            }
            else
            {
                MLogger?.LogInfo($"[PVZRHTools] 词条应用成功: buffType={buffType}, originalId={originalId}, encodedBuffId={encodedBuffId}, buffName={buffName ?? "未知"}");
            }

            // 设置 BoardTag 标志，使游戏识别并应用词条效果
            if (Board.Instance != null && GameAPP.board != null)
            {
                var board = GameAPP.board.GetComponent<Board>();
                if (board != null)
                {
                    var boardTag = board.boardTag;
                    boardTag.isTravel = true;
                    boardTag.enableTravelBuff = true;
                    Board.Instance.boardTag = boardTag;
                }
            }

            // 返回词条名字（不包含描述），不在这里显示
            if (applied && !string.IsNullOrEmpty(buffName))
            {
                return ExtractBuffName(buffName);
            }
            return null;
        }
        catch (System.Exception ex)
        {
            MLogger?.LogError($"[PVZRHTools] 应用单个旗帜波词条失败: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }
    
    /// <summary>
    /// 从编码ID获取词条名字（不应用词条，仅获取名字）
    /// </summary>
    private static string? GetBuffNameFromEncodedId(int encodedBuffId, TravelMgr travelMgr)
    {
        try
        {
            PatchMgr.BuffType buffType;
            int originalId;
            
            if (encodedBuffId >= 2000)
            {
                buffType = PatchMgr.BuffType.Debuff;
                originalId = encodedBuffId - 2000;
            }
            else if (encodedBuffId >= 1000 && encodedBuffId < 2000)
            {
                buffType = PatchMgr.BuffType.Ultimate;
                originalId = encodedBuffId - 1000;
            }
            else if (encodedBuffId >= 0 && encodedBuffId < 1000)
            {
                buffType = PatchMgr.BuffType.Advanced;
                originalId = encodedBuffId;
            }
            else
            {
                return null;
            }
            
            string? buffName = null;
            
            switch (buffType)
            {
                case PatchMgr.BuffType.Advanced:
                    TravelMgr.advancedBuffs?.TryGetValue(originalId, out buffName);
                    break;
                case PatchMgr.BuffType.Ultimate:
                    if (TravelMgr.ultimateBuffs != null)
                    {
                        if (TravelMgr.ultimateBuffs.ContainsKey(originalId))
                        {
                            TravelMgr.ultimateBuffs.TryGetValue(originalId, out buffName);
                        }
                        else
                        {
                            var keysList = new List<int>();
                            foreach (var key in TravelMgr.ultimateBuffs.Keys)
                                keysList.Add(key);
                            keysList.Sort();
                            if (originalId < keysList.Count)
                            {
                                var dictKey = keysList[originalId];
                                TravelMgr.ultimateBuffs.TryGetValue(dictKey, out buffName);
                            }
                        }
                    }
                    break;
                case PatchMgr.BuffType.Debuff:
                    TravelMgr.debuffs?.TryGetValue(originalId, out buffName);
                    break;
            }
            
            if (!string.IsNullOrEmpty(buffName))
            {
                return ExtractBuffName(buffName);
            }
            return null;
        }
        catch (System.Exception ex)
        {
            MLogger?.LogWarning($"[PVZRHTools] 获取词条名字失败: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// 从编码ID获取词条名字和描述（不应用词条，获取包含描述的完整文本）
    /// 返回格式：词条名字：（词条功能描述）
    /// </summary>
    private static string? GetBuffNameWithDescriptionFromEncodedId(int encodedBuffId, TravelMgr travelMgr)
    {
        try
        {
            PatchMgr.BuffType buffType;
            int originalId;
            
            if (encodedBuffId >= 2000)
            {
                buffType = PatchMgr.BuffType.Debuff;
                originalId = encodedBuffId - 2000;
            }
            else if (encodedBuffId >= 1000 && encodedBuffId < 2000)
            {
                buffType = PatchMgr.BuffType.Ultimate;
                originalId = encodedBuffId - 1000;
            }
            else if (encodedBuffId >= 0 && encodedBuffId < 1000)
            {
                buffType = PatchMgr.BuffType.Advanced;
                originalId = encodedBuffId;
            }
            else
            {
                return null;
            }
            
            string? buffName = null;
            
            switch (buffType)
            {
                case PatchMgr.BuffType.Advanced:
                    TravelMgr.advancedBuffs?.TryGetValue(originalId, out buffName);
                    break;
                case PatchMgr.BuffType.Ultimate:
                    if (TravelMgr.ultimateBuffs != null)
                    {
                        if (TravelMgr.ultimateBuffs.ContainsKey(originalId))
                        {
                            TravelMgr.ultimateBuffs.TryGetValue(originalId, out buffName);
                        }
                        else
                        {
                            var keysList = new List<int>();
                            foreach (var key in TravelMgr.ultimateBuffs.Keys)
                                keysList.Add(key);
                            keysList.Sort();
                            if (originalId < keysList.Count)
                            {
                                var dictKey = keysList[originalId];
                                TravelMgr.ultimateBuffs.TryGetValue(dictKey, out buffName);
                            }
                        }
                    }
                    break;
                case PatchMgr.BuffType.Debuff:
                    TravelMgr.debuffs?.TryGetValue(originalId, out buffName);
                    break;
            }
            
            if (!string.IsNullOrEmpty(buffName))
            {
                return ExtractBuffNameWithDescription(buffName);
            }
            return null;
        }
        catch (System.Exception ex)
        {
            MLogger?.LogWarning($"[PVZRHTools] 获取词条名字和描述失败: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// 禁用游戏内置的 WASD 操控植物功能（当随机升级模式开启时）
/// </summary>
[HarmonyPatch(typeof(Board), nameof(Board.ControledPlantUpdate))]
public static class BoardControledPlantUpdatePatch
{
    public static bool Prefix()
    {
        // 当随机升级模式开启时，禁用游戏内置的 WASD 操控
        if (RandomUpgradeMode)
        {
            return false; // 跳过原方法
        }
        return true; // 执行原方法
    }
}

[HarmonyPatch(typeof(Bucket), "Update")]
public static class BucketPatch
{
    public static void Postfix(Bucket __instance)
    {
        if (!ItemExistForever) return;
        try
        {
            if (__instance != null) __instance.existTime = 0.1f;
        }
        catch { }
    }
}

[HarmonyPatch(typeof(Bullet), "Update")]
public static class BulletPatchA
{
    public static void Postfix(Bullet __instance)
    {
        try
        {
            if (__instance == null) return;
            var bulletType = __instance.theBulletType;
            if (!BulletDamage.TryGetValue(bulletType, out var damage)) return;
            if (damage >= 0 && __instance.Damage != damage)
                __instance.Damage = damage;
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(Bullet), "Die")]
public static class BulletPatchB
{
    public static bool Prefix(Bullet __instance)
    {
        if (UndeadBullet && !__instance.fromZombie)
        {
            __instance.hit = false;
            __instance.penetrationTimes = int.MaxValue;
            return false;
        }

        return true;
    }
}

/// <summary>
/// 僵尸概率反弹子弹补丁 - Bullet.OnTriggerEnter2D
/// 当子弹击中僵尸时，有一定概率创建一个铁豆子弹反弹回去攻击植物
/// 如果反弹成功，僵尸不受伤害
/// </summary>
[HarmonyPatch(typeof(Bullet), nameof(Bullet.OnTriggerEnter2D))]
public static class ZombieBulletReflectPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Bullet __instance, Collider2D collision)
    {
        if (!ZombieBulletReflectEnabled || ZombieBulletReflectChance <= 0) return true;
        
        try
        {
            // 只处理植物发射的子弹（非僵尸子弹）
            if (__instance == null || __instance.fromZombie) return true;
            
            // 检查子弹是否已经命中过
            if (__instance.hit) return true;
            
            // 检查碰撞对象是否是僵尸
            if (collision == null) return true;
            var zombie = collision.GetComponent<Zombie>();
            if (zombie == null) return true;
            
            // 跳过魅惑僵尸（友方单位）
            if (zombie.isMindControlled) return true;
            
            // 跳过已死亡的僵尸
            if (zombie.theHealth <= 0) return true;
            
            // 概率判断
            float randomValue = Random.Range(0f, 100f);
            if (randomValue >= ZombieBulletReflectChance) return true;
            
            // 标记子弹已命中，防止后续处理
            __instance.hit = true;
            
            // 创建反弹的铁豆子弹
            CreateReflectedBullet(__instance, zombie);
            
            // 直接销毁子弹对象，不调用Die()方法（Die可能会触发伤害）
            Object.Destroy(__instance.gameObject);
            
            // 阻止原始的碰撞处理，僵尸不受伤
            return false;
        }
        catch
        {
            return true;
        }
    }
    
    /// <summary>
/// 创建反弹的铁豆子弹
/// </summary>
    private static void CreateReflectedBullet(Bullet originalBullet, Zombie zombie)
    {
        try
        {
            if (CreateBullet.Instance == null) return;
            
            // 获取原子弹的位置和行
            Vector3 pos = originalBullet.transform.position;
            int row = originalBullet.theBulletRow;
            
            // 创建一个铁豆子弹，向左飞行
            // fromEnermy/isZombieBullet = true 表示这是僵尸子弹，可以伤害植物
            var newBullet = CreateBullet.Instance.SetBullet(
                pos.x, 
                pos.y, 
                row, 
                BulletType.Bullet_ironPea, 
                BulletMoveWay.Left, // 向左飞行
                true // 这是僵尸子弹
            );
            
            if (newBullet != null)
            {
                // 设置子弹伤害（使用原子弹的伤害）
                newBullet.Damage = originalBullet.Damage;
            }
        }
        catch
        {
            // 忽略错误
        }
    }
}

/// <summary>
/// 卡片无限制补丁 - PresentCard.Start
/// 当启用时，阻止PresentCard.Start()方法执行，取消礼盒卡片的数量限制
/// 参考：AllPresentCard插件
/// </summary>
[HarmonyPatch(typeof(PresentCard), "Start")]
public static class UnlimitedPresentCardPatch
{
    [HarmonyPrefix]
    public static bool Prefix(PresentCard __instance)
    {
        // 当启用卡片无限制时，阻止Start方法执行，取消卡片数量限制
        // 注意：这里直接销毁PresentCard组件，而不是阻止Start方法执行
        // 这样可以确保在任何时候启用"卡片无限制"功能都能生效
        if (UnlimitedCardSlots)
        {
            Object.Destroy(__instance);
            return false;
        }
        return true;
    }
}

/// <summary>
/// 卡片无限制补丁 - TreasureData.GetCardLevel
/// 当启用时，将所有卡片的等级返回为White（最低等级），取消普通卡片"只能带两张"的限制
/// 卡片等级决定了选卡界面中同类型卡片的数量限制：
/// - White(0): 无限制
/// - Green(1) ~ Red(5): 有不同程度的限制
/// </summary>
[HarmonyPatch(typeof(TreasureData), nameof(TreasureData.GetCardLevel))]
public static class UnlimitedCardLevelPatch
{
    [HarmonyPostfix]
    public static void Postfix(ref CardLevel __result)
    {
        // 当启用卡片无限制时，将所有卡片等级设为White（无限制）
        if (UnlimitedCardSlots)
        {
            __result = CardLevel.White;
        }
    }
}

/// <summary>
/// 卡片无限制补丁 - CardUI.LevelLim
/// 当启用时，阻止LevelLim方法执行，取消卡片选取数量限制
/// LevelLim方法是在CardUI.Start中被调用来设置卡片的选取限制
/// </summary>
[HarmonyPatch(typeof(CardUI), "LevelLim")]
public static class UnlimitedCardLevelLimPatch
{
    [HarmonyPrefix]
    public static bool Prefix()
    {
        // 当启用卡片无限制时，阻止LevelLim方法执行
        if (UnlimitedCardSlots)
        {
            return false;
        }
        return true;
    }
}

/// <summary>
/// 卡片无限制补丁 - CardUI.OnMouseDown
/// 当点击选取卡片时，复制一张新卡片
/// </summary>
[HarmonyPatch(typeof(CardUI), nameof(CardUI.OnMouseDown))]
public static class UnlimitedCardOnMouseDownPatch
{
    // 记录复制出来的卡片，用于退出选卡时清除
    public static List<GameObject> CopiedCards = new List<GameObject>();

    [HarmonyPostfix]
    public static void Postfix(CardUI __instance)
    {
        if (!UnlimitedCardSlots) return;

        try
        {
            // 只在选卡界面（卡片被选中时）复制
            if (!__instance.isSelected) return;
            
            // 检查父对象是否存在
            if (__instance.transform.parent == null) return;

            // 复制卡片对象
            GameObject go = GameObject.Instantiate(__instance.gameObject, __instance.transform.parent);
            go.transform.position = __instance.transform.position;
            
            // 设置新卡片的CD
            var newCard = go.GetComponent<CardUI>();
            if (newCard != null)
            {
                newCard.CD = newCard.fullCD;
                newCard.isSelected = false; // 新卡片未被选中
            }

            // 记录复制的卡片
            CopiedCards.Add(go);
        }
        catch { }
    }

    /// <summary>
    /// 清除未被选中的复制卡片（保留已选择的卡片）
    /// </summary>
    public static void ClearUnselectedCopiedCards()
    {
        try
        {
            var toRemove = new List<GameObject>();
            foreach (var card in CopiedCards)
            {
                if (card != null)
                {
                    var cardUI = card.GetComponent<CardUI>();
                    // 只清除未被选中的卡片
                    if (cardUI == null || !cardUI.isSelected)
                    {
                        Object.Destroy(card);
                        toRemove.Add(card!);
                    }
                }
                else
                {
                    toRemove.Add(card!);
                }
            }
            // 从列表中移除已销毁的卡片
            foreach (var card in toRemove)
            {
                CopiedCards.Remove(card);
            }
        }
        catch { }
    }

    /// <summary>
    /// 清除所有复制的卡片（关闭功能时调用）
    /// </summary>
    public static void ClearAllCopiedCards()
    {
        try
        {
            foreach (var card in CopiedCards)
            {
                if (card != null)
                {
                    Object.Destroy(card);
                }
            }
            CopiedCards.Clear();
        }
        catch { }
    }
}

/// <summary>
/// 卡片无限制补丁 - InitBoard.RemoveUI
/// 在退出选卡界面时清除未被选中的复制卡片
/// </summary>
[HarmonyPatch(typeof(InitBoard), nameof(InitBoard.RemoveUI))]
public static class UnlimitedCardRemoveUIPatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        if (UnlimitedCardSlots)
        {
            UnlimitedCardOnMouseDownPatch.ClearUnselectedCopiedCards();
        }
    }
}

/// <summary>
/// 卡片无限制补丁 - Board.Start
/// 在Board.Start时重置状态
/// </summary>
[HarmonyPatch(typeof(Board), nameof(Board.Start))]
public static class UnlimitedCardBoardStartPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        // 清除复制的卡片列表
        UnlimitedCardOnMouseDownPatch.CopiedCards.Clear();
    }
}

/// <summary>
/// 卡片无限制补丁 - CardUI.Awake
/// 当启用时，将maxUsedTimes设置为一个很大的值，取消卡片使用次数限制
/// </summary>
[HarmonyPatch(typeof(CardUI), "Awake")]
public static class UnlimitedCardAwakePatch
{
    [HarmonyPostfix]
    public static void Postfix(CardUI __instance)
    {
        // 卡片无限制：将maxUsedTimes设置为一个很大的值
        if (UnlimitedCardSlots)
        {
            __instance.maxUsedTimes = 9999;
        }
    }
}

[HarmonyPatch(typeof(CardUI))]
public static class CardUIPatch
{
    [HarmonyPostfix]
    [HarmonyPatch("Start")]
    public static void Postfix(CardUI __instance)
    {
        GameObject obj = new("ModifierCardCD");
        var text = obj.AddComponent<TextMeshProUGUI>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts/ContinuumBold SDF");
        text.color = new Color(0.5f, 0.8f, 1f);
        obj.transform.SetParent(__instance.transform);
        obj.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);
        obj.transform.localPosition = new Vector3(39f, 0, 0);

        // 卡片无限制：将maxUsedTimes设置为一个很大的值
        if (UnlimitedCardSlots)
        {
            __instance.maxUsedTimes = 9999;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch("Update")]
    public static void PostUpdate(CardUI __instance)
    {
        try
        {
            if (__instance == null) return;

            // 卡片无限制：动态检查并设置maxUsedTimes
            if (UnlimitedCardSlots && __instance.maxUsedTimes < 9999)
            {
                __instance.maxUsedTimes = 9999;
            }

            var child = __instance.transform.FindChild("ModifierCardCD");
            if (child == null) return;
            if (__instance.isAvailable || !ShowGameInfo)
            {
                child.GameObject().active = false;
            }
            else
            {
                child.GameObject().active = true;
                child.GameObject().GetComponent<TextMeshProUGUI>().text = $"{__instance.CD:N1}/{__instance.fullCD}";
            }
        }
        catch { }
    }
}

// 注释掉 Chomper.Update patch，改用 PatchMgr.Update 中的实现
// 原因：Il2Cpp 对象池在高频 Harmony patch 中会导致栈溢出
/*
[HarmonyPatch(typeof(Chomper), "Update")]
public static class ChomperPatch
{
    public static void Prefix(Chomper __instance)
    {
        if (!ChomperNoCD) return;
        try
        {
            if (__instance != null && __instance.attributeCountdown > 0.05f) 
                __instance.attributeCountdown = 0.05f;
        }
        catch { }
    }
}
*/

/// <summary>
/// 加农炮无CD装填补丁 - CobCannon.AnimShoot
/// 在加农炮发射后立即触发charge动画并重置冷却时间，实现无冷却装填
/// </summary>
[HarmonyPatch(typeof(CobCannon), "AnimShoot")]
public static class CobCannonAnimShootPatch
{
    [HarmonyPostfix]
    public static void Postfix(CobCannon __instance)
    {
        if (!CobCannonNoCD) return;
        try
        {
            if (__instance != null)
            {
                // 重置冷却时间，使加农炮可以立即再次发射
                __instance.attributeCountdown = 0.05f;
                // 触发charge动画
                if (__instance.anim != null)
                    __instance.anim.SetTrigger("charge");
            }
        }
        catch { }
    }
}

/// <summary>
/// 火焰加农炮无CD装填补丁 - FireCannon.AnimShoot
/// </summary>
[HarmonyPatch(typeof(FireCannon), "AnimShoot")]
public static class FireCannonAnimShootPatch
{
    [HarmonyPostfix]
    public static void Postfix(FireCannon __instance)
    {
        if (!CobCannonNoCD) return;
        try
        {
            if (__instance != null)
            {
                __instance.attributeCountdown = 0.05f;
                if (__instance.anim != null)
                    __instance.anim.SetTrigger("charge");
            }
        }
        catch { }
    }
}

/// <summary>
/// 寒冰加农炮无CD装填补丁 - IceCannon.AnimShoot
/// </summary>
[HarmonyPatch(typeof(IceCannon), "AnimShoot")]
public static class IceCannonAnimShootPatch
{
    [HarmonyPostfix]
    public static void Postfix(IceCannon __instance)
    {
        if (!CobCannonNoCD) return;
        try
        {
            if (__instance != null)
            {
                __instance.attributeCountdown = 0.05f;
                if (__instance.anim != null)
                    __instance.anim.SetTrigger("charge");
            }
        }
        catch { }
    }
}

/// <summary>
/// 西瓜加农炮无CD装填补丁 - MelonCannon.AnimShoot
/// </summary>
[HarmonyPatch(typeof(MelonCannon), "AnimShoot")]
public static class MelonCannonAnimShootPatch
{
    [HarmonyPostfix]
    public static void Postfix(MelonCannon __instance)
    {
        if (!CobCannonNoCD) return;
        try
        {
            if (__instance != null)
            {
                __instance.attributeCountdown = 0.05f;
                if (__instance.anim != null)
                    __instance.anim.SetTrigger("charge");
            }
        }
        catch { }
    }
}

/// <summary>
/// 究极加农炮无CD装填补丁 - UltimateCannon.AnimShoot
/// </summary>
[HarmonyPatch(typeof(UltimateCannon), "AnimShoot")]
public static class UltimateCannonAnimShootPatch
{
    [HarmonyPostfix]
    public static void Postfix(UltimateCannon __instance)
    {
        if (!CobCannonNoCD) return;
        try
        {
            if (__instance != null)
            {
                __instance.attributeCountdown = 0.05f;
                if (__instance.anim != null)
                    __instance.anim.SetTrigger("charge");
            }
        }
        catch { }
    }
}

/// <summary>
/// 究极爆破加农炮无CD装填补丁 - UltimateExplodeCannon.AnimShoot
/// </summary>
[HarmonyPatch(typeof(UltimateExplodeCannon), "AnimShoot")]
public static class UltimateExplodeCannonAnimShootPatch
{
    [HarmonyPostfix]
    public static void Postfix(UltimateExplodeCannon __instance)
    {
        if (!CobCannonNoCD) return;
        try
        {
            if (__instance != null)
            {
                __instance.attributeCountdown = 0.05f;
                if (__instance.anim != null)
                    __instance.anim.SetTrigger("charge");
            }
        }
        catch { }
    }
}

/// <summary>
/// 究极冷寂榴弹炮无CD装填补丁 - UltimateMelonCannon.StartShoot
/// UltimateMelonCannon继承自MelonCannon，但有自己的StartShoot方法
/// </summary>
[HarmonyPatch(typeof(UltimateMelonCannon), "StartShoot")]
public static class UltimateMelonCannonStartShootPatch
{
    [HarmonyPostfix]
    public static void Postfix(UltimateMelonCannon __instance)
    {
        if (!CobCannonNoCD) return;
        try
        {
            if (__instance != null)
            {
                __instance.attributeCountdown = 0.05f;
                if (__instance.anim != null)
                    __instance.anim.SetTrigger("charge");
            }
        }
        catch { }
    }
}

[HarmonyPatch(typeof(ConveyManager))]
public static class ConveyManagerPatch
{
    [HarmonyPatch("Awake")]
    [HarmonyPostfix]
    public static void PostAwake(ConveyManager __instance)
    {
        if (ConveyBeltTypes.Count > 0)
        {
            __instance.plants = new Il2CppSystem.Collections.Generic.List<PlantType>();
            foreach (var p in ConveyBeltTypes) __instance.plants.Add((PlantType)p);
        }
    }

    [HarmonyPatch("GetCardPool")]
    [HarmonyPostfix]
    public static void PostGetCardPool(ref Il2CppSystem.Collections.Generic.List<PlantType> __result)
    {
        if (ConveyBeltTypes.Count > 0)
        {
            Il2CppSystem.Collections.Generic.List<PlantType> list = new();
            foreach (var p in ConveyBeltTypes) list.Add((PlantType)p);
            __result = list;
        }
    }
}

[HarmonyPatch(typeof(CreateBullet), "SetBullet", typeof(float), typeof(float), typeof(int), typeof(BulletType),
    typeof(int), typeof(bool))]
[HarmonyPatch(typeof(CreateBullet), "SetBullet", typeof(float), typeof(float), typeof(int), typeof(BulletType),
    typeof(BulletMoveWay), typeof(bool))]
public static class CreateBulletPatch
{
    public static void Prefix(ref BulletType theBulletType)
    {
        // 随机子弹功能（独立开关）
        if (RandomBullet)
            theBulletType = (BulletType)Random.Range(0, 120);
        // 锁定子弹类型功能
        if (LockBulletType == -1)
            theBulletType = Enum.GetValues<BulletType>()[Random.Range(0, Enum.GetValues<BulletType>().Length)];
        if (LockBulletType >= 0) theBulletType = (BulletType)LockBulletType;
    }
}

[HarmonyPatch(typeof(CreatePlant), "SetPlant")]
public static class CreatePlantPatchC
{
    public static void Prefix(ref bool isFreeSet)
    {
        isFreeSet = FreePlanting || isFreeSet;
    }
}

[HarmonyPatch(typeof(DriverZombie), "PositionUpdate")]
public static class DriverZombiePatch
{
    public static void Postfix(DriverZombie __instance)
    {
        if (!NoIceRoad) return;
        try
        {
            if (__instance == null || Board.Instance == null) return;
            for (var i = 0; i < Board.Instance.iceRoads.Count; i++)
                if (Board.Instance.iceRoads[i].theRow == __instance.theZombieRow)
                    Board.Instance.iceRoads[i].fadeTimer = 0;
        }
        catch { }
    }
}

/// <summary>
/// 禁用全屏冰冻特效的 Harmony 补丁
/// 拦截 Board.CreateFreeze 全屏冰冻特效，同时为全场僵尸添加冻结效果并造成伤害，为雪原植物恢复充能
/// </summary>
[HarmonyPatch(typeof(Board), nameof(Board.CreateFreeze))]
public static class BoardCreateFreezePatch
{
    // 雪原植物类型ID列表（从反汇编代码中提取）
    // 38: SnowPea, 913: ?, 925: ?, 947: ?, 1039: ?, 1218-1220: ?, 1227: ?, 1259: ?
    private static readonly HashSet<int> SnowPlantTypes = new HashSet<int>
    {
        38,   // SnowPea
        913,  // 
        925,  // 
        947,  // 
        1039, // 
        1218, 1219, 1220, // 
        1227, // 
        1259  // 
    };

    /// <summary>
    /// 拦截 Board.CreateFreeze 方法，阻止全屏冰冻特效
    /// 同时为全场僵尸添加冻结效果并造成伤害，为雪原植物恢复充能
    /// </summary>
    [HarmonyPrefix]
    public static bool Prefix(Board __instance, Vector2 pos)
    {
        // 功能关闭时，执行原版逻辑
        if (!DisableIceEffect)
            return true;

        // 为全场僵尸添加冻结效果
        ApplyFreezeToAllZombies(__instance);
        
        return false; // 阻止全屏冰冻特效
    }

    /// <summary>
    /// 为全场非魅惑僵尸添加冻结效果并造成伤害，同时为雪原植物恢复充能
    /// 魅惑僵尸（友方单位）将被跳过，既不冻结也不伤害
    /// </summary>
    private static void ApplyFreezeToAllZombies(Board board)
    {
        try
        {
            const int damageAmount = 20; // 伤害值：20点
            const int chargeAmount = 14; // 充能值：14点（与原版一致）
            
            // 遍历所有僵尸
            foreach (var zombie in Board.Instance.zombieArray)
            {
                if (zombie != null && zombie.gameObject.activeInHierarchy)
                {
                    // 跳过魅惑僵尸（友方单位）
                    if (zombie.isMindControlled)
                        continue;
                    
                    // 为非魅惑僵尸添加冻结效果
                    zombie.SetFreeze(4f); // 冻结4秒
                    // 对非魅惑僵尸造成伤害
                    zombie.TakeDamage(DmgType.Normal, damageAmount, PlantType.Nothing, false);
                }
            }
            
            // 为全场雪原植物恢复充能
            var allPlants = Lawnf.GetAllPlants();
            if (allPlants != null)
            {
                foreach (var plant in allPlants)
                {
                    if (plant != null && plant.gameObject.activeInHierarchy)
                    {
                        // 检查是否为雪原植物（使用 TypeMgr.IsSnowPlant 或检查植物类型ID）
                        int plantTypeId = (int)plant.thePlantType;
                        if (TypeMgr.IsSnowPlant(plant.thePlantType) || SnowPlantTypes.Contains(plantTypeId))
                        {
                            try
                            {
                                // 直接增加 attributeCount 属性（与原版 Board.CreateFreeze 一致）
                                plant.attributeCount += chargeAmount;
                                
                                // 调用 UpdateText 方法更新显示
                                plant.UpdateText();
                            }
                            catch
                            {
                                // 忽略充能失败
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // 忽略错误
        }
    }
}

#region PotSmashingFix - 砸罐子修复补丁

/// <summary>
/// 砸罐子修复补丁 - 核心补丁类
/// 功能：
/// 1. 多个罐子重叠时只砸开第一个罐子
/// 2. 小丑类的爆炸和巨人的砸击无法破坏罐子
/// 3. 土豆炸弹和大炸弹等AOE攻击无法破坏罐子
/// 4. 巨人僵尸忽略罐子，直接向前走
/// </summary>
[HarmonyPatch]
public static class PotSmashingPatches
{
    // 跟踪当前锤击事件中已经砸开的罐子
    private static readonly HashSet<ScaryPot> _hitPotsInCurrentSwing = new HashSet<ScaryPot>();
    // 跟踪当前锤击事件中已经处理的罐子（包括被阻止的）
    private static readonly HashSet<ScaryPot> _processedPotsInCurrentSwing = new HashSet<ScaryPot>();
    // 跟踪通过ScaryPot.Hitted调用的罐子
    private static readonly HashSet<ScaryPot> _hittedPots = new HashSet<ScaryPot>();
    // 标记当前是否正在处理僵尸爆炸（Lawnf.ZombieExplode）
    private static bool _isProcessingZombieExplode = false;
    // 标记当前是否正在处理小丑爆炸
    private static bool _isProcessingJackboxExplosion = false;

    public static void SetProcessingZombieExplode(bool value) => _isProcessingZombieExplode = value;
    public static bool IsProcessingZombieExplode() => _isProcessingZombieExplode;
    public static void SetProcessingJackboxExplosion(bool value) => _isProcessingJackboxExplosion = value;
    public static bool IsProcessingJackboxExplosion() => _isProcessingJackboxExplosion;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ScaryPot), nameof(ScaryPot.Hitted))]
    public static bool Prefix_ScaryPotHitted(ScaryPot __instance)
    {
        if (!PotSmashingFix) return true;

        if (IsAnyProjectileZombieRelatedInStack() || IsProjectileZombieAttackInStack() || 
            IsBombingAttack() || IsAnyProjectileZombieRelatedAttack())
            return false;

        if (_processedPotsInCurrentSwing.Contains(__instance))
            return false;

        if (_hitPotsInCurrentSwing.Count > 0)
        {
            _processedPotsInCurrentSwing.Add(__instance);
            return false;
        }

        _hitPotsInCurrentSwing.Add(__instance);
        _processedPotsInCurrentSwing.Add(__instance);
        _hittedPots.Add(__instance);
        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ScaryPot), nameof(ScaryPot.OnHitted))]
    public static bool Prefix_ScaryPotOnHitted(ScaryPot __instance)
    {
        if (!PotSmashingFix) return true;

        try
        {
            if (_isProcessingZombieExplode || _isProcessingJackboxExplosion)
                return false;

            if (_hittedPots.Contains(__instance))
            {
                _hittedPots.Remove(__instance);
                return true;
            }
            return false;
        }
        catch { return true; }
    }

    private static bool IsProjectileZombieAttackInStack()
    {
        try
        {
            var stackTrace = new System.Diagnostics.StackTrace();
            for (int i = 0; i < stackTrace.FrameCount; i++)
            {
                var frame = stackTrace.GetFrame(i);
                var method = frame?.GetMethod();
                var methodName = method?.Name ?? "";
                var className = method?.DeclaringType?.Name ?? "";
                if (className.Contains("PotSmashingPatches")) continue;
                if (className.Contains("ProjectileZombie") || 
                    (className.Contains("Bullet") && methodName.Contains("OnTriggerEnter2D")) ||
                    className.Contains("Submarine_b") || className.Contains("Submarine_c"))
                    return true;
            }
            return false;
        }
        catch { return false; }
    }

    private static bool IsBombingAttack()
    {
        try
        {
            var stackTrace = new System.Diagnostics.StackTrace();
            for (int i = 0; i < stackTrace.FrameCount; i++)
            {
                var frame = stackTrace.GetFrame(i);
                var method = frame?.GetMethod();
                var methodName = method?.Name ?? "";
                var className = method?.DeclaringType?.Name ?? "";
                if (className.Contains("PotSmashingPatches")) continue;
                if ((methodName.Contains("Explode") || methodName.Contains("Bomb") || 
                     methodName.Contains("HitLand") || methodName.Contains("HitZombie")) && 
                    (className.Contains("Bullet") || className.Contains("ProjectileZombie") || 
                     className.Contains("Submarine")))
                    return true;
                if (className.Contains("ProjectileZombie") && 
                    (methodName.Contains("Update") || methodName.Contains("FixedUpdate") || 
                     methodName.Contains("RbUpdate")))
                    return true;
            }
            return false;
        }
        catch { return false; }
    }

    private static bool IsAnyProjectileZombieRelatedAttack()
    {
        try
        {
            var stackTrace = new System.Diagnostics.StackTrace();
            for (int i = 0; i < stackTrace.FrameCount; i++)
            {
                var frame = stackTrace.GetFrame(i);
                var method = frame?.GetMethod();
                var methodName = method?.Name ?? "";
                var className = method?.DeclaringType?.Name ?? "";
                if (className.Contains("PotSmashingPatches")) continue;
                if (className.Contains("ProjectileZombie") || 
                    className.Contains("Submarine_b") || className.Contains("Submarine_c") ||
                    (className.Contains("Bullet") && (methodName.Contains("OnTriggerEnter2D") || 
                     methodName.Contains("HitLand") || methodName.Contains("HitZombie"))))
                    return true;
            }
            return false;
        }
        catch { return false; }
    }

    private static bool IsAnyProjectileZombieRelatedInStack()
    {
        try
        {
            var stackTrace = new System.Diagnostics.StackTrace();
            for (int i = 0; i < stackTrace.FrameCount; i++)
            {
                var frame = stackTrace.GetFrame(i);
                var method = frame?.GetMethod();
                var methodName = method?.Name ?? "";
                var className = method?.DeclaringType?.Name ?? "";
                if (className.Contains("PotSmashingPatches")) continue;
                if (className.Contains("ProjectileZombie") || className.Contains("Submarine") ||
                    methodName.Contains("SetBullet") || methodName.Contains("AnimShoot") ||
                    methodName.Contains("ProjectileZombie"))
                    return true;
            }
            return false;
        }
        catch { return false; }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Board), nameof(Board.Update))]
    public static void Postfix_BoardUpdate()
    {
        if (!PotSmashingFix) return;
        _hitPotsInCurrentSwing.Clear();
        _processedPotsInCurrentSwing.Clear();
    }
}

/// <summary>
/// 巨人僵尸忽略罐子补丁
/// </summary>
[HarmonyPatch]
public static class GargantuarIgnorePotPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(IronGargantuar), nameof(IronGargantuar.OnTriggerEnter2D))]
    public static bool Prefix_IronGargantuarOnTriggerEnter2D(IronGargantuar __instance, Collider2D collision)
    {
        if (!PotSmashingFix) return true;
        try
        {
            if (collision == null) return true;
            var scaryPot = collision.GetComponent<ScaryPot>();
            if (scaryPot != null) return false;
            return true;
        }
        catch { return true; }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Gargantuar), nameof(Gargantuar.AttackUpdate))]
    public static bool Prefix_GargantuarAttackUpdate(Gargantuar __instance)
    {
        if (!PotSmashingFix) return true;
        try
        {
            if (IsGargantuarAttackingPot(__instance)) return false;
            return true;
        }
        catch { return true; }
    }

    private static bool IsGargantuarAttackingPot(Gargantuar gargantuar)
    {
        try
        {
            var zombie = gargantuar.GetComponent<Zombie>();
            if (zombie == null) return false;
            var rigidbody = gargantuar.GetComponent<Rigidbody2D>();
            if (rigidbody != null && rigidbody.velocity.magnitude < 0.1f)
            {
                var colliders = Physics2D.OverlapCircleAll(gargantuar.transform.position, 5.0f);
                foreach (var collider in colliders)
                    if (collider.GetComponent<ScaryPot>() != null) return true;
            }
            return false;
        }
        catch { return false; }
    }
}

/// <summary>
/// 小丑僵尸爆炸保护补丁 - 让小丑可以爆炸，但爆炸不影响罐子
/// </summary>
[HarmonyPatch]
public static class JackboxZombieProtectionPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(JackboxZombie), nameof(JackboxZombie.Explode))]
    public static bool Prefix_JackboxZombieExplode() { if (PotSmashingFix) PotSmashingPatches.SetProcessingJackboxExplosion(true); return true; }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(JackboxZombie), nameof(JackboxZombie.Explode))]
    public static void Postfix_JackboxZombieExplode() { if (PotSmashingFix) PotSmashingPatches.SetProcessingJackboxExplosion(false); }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(JackboxZombie), nameof(JackboxZombie.AnimExplode))]
    public static bool Prefix_JackboxZombieAnimExplode() { if (PotSmashingFix) PotSmashingPatches.SetProcessingJackboxExplosion(true); return true; }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(JackboxZombie), nameof(JackboxZombie.AnimExplode))]
    public static void Postfix_JackboxZombieAnimExplode() { if (PotSmashingFix) PotSmashingPatches.SetProcessingJackboxExplosion(false); }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SuperJackboxZombie), nameof(SuperJackboxZombie.AnimExplode))]
    public static bool Prefix_SuperJackboxZombieAnimExplode() { if (PotSmashingFix) PotSmashingPatches.SetProcessingJackboxExplosion(true); return true; }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SuperJackboxZombie), nameof(SuperJackboxZombie.AnimExplode))]
    public static void Postfix_SuperJackboxZombieAnimExplode() { if (PotSmashingFix) PotSmashingPatches.SetProcessingJackboxExplosion(false); }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UltimateJackboxZombie), nameof(UltimateJackboxZombie.AnimPop))]
    public static bool Prefix_UltimateJackboxZombieAnimPop() { if (PotSmashingFix) PotSmashingPatches.SetProcessingJackboxExplosion(true); return true; }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UltimateJackboxZombie), nameof(UltimateJackboxZombie.AnimPop))]
    public static void Postfix_UltimateJackboxZombieAnimPop() { if (PotSmashingFix) PotSmashingPatches.SetProcessingJackboxExplosion(false); }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(JackboxJumpZombie), nameof(JackboxJumpZombie.DieEvent))]
    public static bool Prefix_JackboxJumpZombieDieEvent() { if (PotSmashingFix) PotSmashingPatches.SetProcessingJackboxExplosion(true); return true; }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(JackboxJumpZombie), nameof(JackboxJumpZombie.DieEvent))]
    public static void Postfix_JackboxJumpZombieDieEvent() { if (PotSmashingFix) PotSmashingPatches.SetProcessingJackboxExplosion(false); }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Jackbox_a), nameof(Jackbox_a.LoseHeadEvent))]
    public static bool Prefix_Jackbox_aLoseHeadEvent() { if (PotSmashingFix) PotSmashingPatches.SetProcessingJackboxExplosion(true); return true; }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Jackbox_a), nameof(Jackbox_a.LoseHeadEvent))]
    public static void Postfix_Jackbox_aLoseHeadEvent() { if (PotSmashingFix) PotSmashingPatches.SetProcessingJackboxExplosion(false); }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Jackbox_c), nameof(Jackbox_c.LoseHeadEvent))]
    public static bool Prefix_Jackbox_cLoseHeadEvent() { if (PotSmashingFix) PotSmashingPatches.SetProcessingJackboxExplosion(true); return true; }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Jackbox_c), nameof(Jackbox_c.LoseHeadEvent))]
    public static void Postfix_Jackbox_cLoseHeadEvent() { if (PotSmashingFix) PotSmashingPatches.SetProcessingJackboxExplosion(false); }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SuperJackboxZombie), nameof(SuperJackboxZombie.DieEvent))]
    public static bool Prefix_SuperJackboxZombieDieEvent() { if (PotSmashingFix) PotSmashingPatches.SetProcessingJackboxExplosion(true); return true; }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SuperJackboxZombie), nameof(SuperJackboxZombie.DieEvent))]
    public static void Postfix_SuperJackboxZombieDieEvent() { if (PotSmashingFix) PotSmashingPatches.SetProcessingJackboxExplosion(false); }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UltimateJackboxZombie), nameof(UltimateJackboxZombie.DieEvent))]
    public static bool Prefix_UltimateJackboxZombieDieEvent() { if (PotSmashingFix) PotSmashingPatches.SetProcessingJackboxExplosion(true); return true; }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UltimateJackboxZombie), nameof(UltimateJackboxZombie.DieEvent))]
    public static void Postfix_UltimateJackboxZombieDieEvent() { if (PotSmashingFix) PotSmashingPatches.SetProcessingJackboxExplosion(false); }
}

/// <summary>
/// Lawnf.ZombieExplode 补丁 - 阻止僵尸爆炸破坏罐子
/// </summary>
[HarmonyPatch]
public static class ZombieExplodeProtectionPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Lawnf), nameof(Lawnf.ZombieExplode))]
    public static bool Prefix_LawnfZombieExplode() { if (PotSmashingFix) PotSmashingPatches.SetProcessingZombieExplode(true); return true; }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Lawnf), nameof(Lawnf.ZombieExplode))]
    public static void Postfix_LawnfZombieExplode() { if (PotSmashingFix) PotSmashingPatches.SetProcessingZombieExplode(false); }
}

#endregion

#region UnlimitedSunlight - 阳光无上限补丁

/// <summary>
/// 阳光无上限补丁 - 取消50000阳光存储上限限制
/// </summary>
[HarmonyPatch(typeof(Board))]
public static class UnlimitedSunlightPatches
{
    /// <summary>
    /// 修改GetSun方法 - 移除50000阳光上限限制
    /// </summary>
    [HarmonyPatch(nameof(Board.GetSun))]
    [HarmonyPrefix]
    public static bool Prefix_GetSun(Board __instance, int count, int r, bool save)
    {
        if (!UnlimitedSunlight) return true;

        try
        {
            if (__instance != null)
            {
                int count_1 = 2 * count;
                int count_2 = 4 * count_1;
                int theSun_1 = r * (count_2 + __instance.theSun);
                int newSun = (theSun_1 - __instance.theSun) / 10 + 5;
                __instance.theSun = __instance.theSun + newSun;

                if (save)
                {
                    int extraSun = __instance.extraSun - theSun_1 + theSun_1;
                    __instance.extraSun = extraSun;
                    __instance.extraSun %= 50;
                }
            }
            return false;
        }
        catch { return true; }
    }

    /// <summary>
    /// 修改UseSun方法 - 确保使用阳光时不受上限限制
    /// </summary>
    [HarmonyPatch(nameof(Board.UseSun))]
    [HarmonyPrefix]
    public static bool Prefix_UseSun(Board __instance, float count)
    {
        if (!UnlimitedSunlight) return true;

        try
        {
            if (__instance != null)
            {
                int countInt = (int)count;  // 3.3.1版本UseSun参数类型为float，需要转换为int
                __instance.theSun -= countInt;
                __instance.theUsedSun += countInt;
            }
            return false;
        }
        catch { return true; }
    }
}

#endregion

#region MagnetNutUnlimited - 磁力坚果无限吸引补丁

/// <summary>
/// 磁力坚果无限吸引补丁 - 取消100个子弹存储限制
/// </summary>
[HarmonyPatch(typeof(MagnetNut))]
public static class MagnetNutUnlimitedPatches
{
    /// <summary>
    /// 补丁 FixedUpdate 方法，取消子弹存储上限（100个限制）
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch("FixedUpdate")]
    public static bool Prefix_FixedUpdate(MagnetNut __instance)
    {
        if (!MagnetNutUnlimited) return true;

        try
        {
            if (__instance == null) return true;
            // 强制调用 SearchBullet，无视100个子弹限制
            __instance.SearchBullet();
            return true;
        }
        catch { return true; }
    }
}

/// <summary>
/// 子弹死亡拦截补丁 - 阻止子弹因时间限制死亡
/// </summary>
[HarmonyPatch(typeof(Bullet))]
public static class BulletMagnetPatches
{
    // 需要排除的子弹类型（这些子弹使用原始逻辑）
    private static readonly HashSet<string> _excludedBulletNames = new HashSet<string>
    {
        "Bullet_star", "Bullet_cactusStar", "Bullet_superStar", "Bullet_ultimateStar",
        "Bullet_lanternStar", "Bullet_seaStar", "Bullet_jackboxStar", "Bullet_pickaxeStar",
        "Bullet_magnetStar", "Bullet_ironStar", "Bullet_threeSpike",
        "Bullet_magicTrack", "Bullet_normalTrack", "Bullet_iceTrack", "Bullet_fireTrack",
        "Bullet_doom", "Bullet_doom_throw", "Bullet_endoSun", "Bullet_extremeSnowPea",
        "Bullet_iceSword", "Bullet_lourCactus", "Bullet_melonCannon",
        "Bullet_shulkLeaf_ultimate", "Bullet_smallGoldCannon", "Bullet_smallSun",
        "Bullet_springMelon", "Bullet_sunCabbage", "Bullet_ultimateSun"
    };

    private static bool ShouldExcludeBullet(Bullet bullet)
    {
        if (bullet == null) return true;
        string className = bullet.GetType().Name;
        if (_excludedBulletNames.Contains(className)) return true;
        // 激进排除：包含特定关键词的子弹
        return className.Contains("Star") || className.Contains("Spike") ||
               className.Contains("Track") || className.Contains("Doom") ||
               className.Contains("Extreme") || className.Contains("Melon") ||
               className.Contains("Sun") || className.Contains("Cactus") ||
               className.Contains("Sword") || className.Contains("Cannon") ||
               className.Contains("Ultimate") || className.Contains("Super");
    }

    /// <summary>
    /// 补丁 Bullet.Die 方法，阻止子弹因时间限制死亡
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Bullet.Die))]
    public static bool Prefix_Die(Bullet __instance)
    {
        if (!MagnetNutUnlimited) return true;

        try
        {
            if (__instance == null || ShouldExcludeBullet(__instance)) return true;

            // 检查是否是因为时间限制要死亡
            if (__instance.theExistTime > 20.0f || (__instance.theMovingWay == 3 && __instance.theExistTime > 0.75f))
            {
                // 重置状态，阻止死亡
                __instance.theMovingWay = 10;
                __instance.theExistTime = 0.0f;
                return false; // 阻止死亡
            }
            return true;
        }
        catch { return true; }
    }
}

#endregion

[HarmonyPatch(typeof(DroppedCard), "Update")]
public static class DroppedCardPatch
{
    public static void Postfix(DroppedCard __instance)
    {
        if (!ItemExistForever) return;
        try
        {
            if (__instance != null) __instance.existTime = 0;
        }
        catch { }
    }
}

[HarmonyPatch(typeof(Fertilize), "Update")]
public static class FertilizePatch
{
    public static void Postfix(Fertilize __instance)
    {
        if (!ItemExistForever) return;
        try
        {
            if (__instance != null) __instance.existTime = 0.1f;
        }
        catch { }
    }
}

[HarmonyPatch(typeof(GameAPP))]  


public static class GameAppPatch
{
    [HarmonyPostfix]
    [HarmonyPatch("Start")]
    public static void PostStart()
    {
        GameObject obj = new("Modifier");
        Object.DontDestroyOnLoad(obj);
        obj.AddComponent<DataProcessor>();
        obj.AddComponent<PatchMgr>();
    }
}

[HarmonyPatch(typeof(Glove), "Update")]
public static class GlovePatchA
{
    public static void Postfix(Glove __instance)
    {
        try
        {
            if (__instance == null) return;
            __instance.gameObject.transform.GetChild(0).gameObject.SetActive(!GloveNoCD);
            if (GloveFullCD > 0) __instance.fullCD = (float)GloveFullCD;
            if (GloveNoCD) __instance.CD = __instance.fullCD;
            var cdChild = __instance.transform.FindChild("ModifierGloveCD");
            if (cdChild == null) return;
            if (__instance.avaliable || !ShowGameInfo)
            {
                cdChild.GameObject().active = false;
            }
            else
            {
                cdChild.GameObject().active = true;
                cdChild.GameObject().GetComponent<TextMeshProUGUI>().text =
                    $"{__instance.CD:N1}/{__instance.fullCD}";
            }
        }
        catch { }
    }
}

[HarmonyPatch(typeof(Glove), "Start")]
public static class GlovePatchB
{
    public static void Postfix(Glove __instance)
    {
        GameObject obj = new("ModifierGloveCD");
        var text = obj.AddComponent<TextMeshProUGUI>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts/ContinuumBold SDF");
        text.color = new Color(0.5f, 0.8f, 1f);
        obj.transform.SetParent(__instance.GameObject().transform);
        obj.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
        obj.transform.localPosition = new Vector3(27.653f, 0, 0);
    }
}

[HarmonyPatch(typeof(GridItem), "SetGridItem")]
public static class GridItemPatch
{
    public static bool Prefix(ref GridItemType theType)
    {
        return (int)theType >= 3 || !NoHole;
    }
}

[HarmonyPatch(typeof(HammerMgr), "Update")]
public static class HammerMgrPatchA
{
    public static float OriginalFullCD { get; set; }

    public static void Postfix(HammerMgr __instance)
    {
        try
        {
            if (__instance == null) return;
            __instance.gameObject.transform.GetChild(0).GetChild(0).gameObject.SetActive(!HammerNoCD);
            if (HammerFullCD > 0)
                __instance.fullCD = (float)HammerFullCD;
            else
                __instance.fullCD = OriginalFullCD;
            if (HammerNoCD) __instance.CD = __instance.fullCD;
            var cdChild = __instance.transform.FindChild("ModifierHammerCD");
            if (cdChild == null) return;
            if (__instance.avaliable || !ShowGameInfo)
            {
                cdChild.GameObject().active = false;
            }
            else
            {
                cdChild.GameObject().active = true;
                cdChild.GameObject().GetComponent<TextMeshProUGUI>().text =
                    $"{__instance.CD:N1}/{__instance.fullCD}";
            }
        }
        catch { }
    }
}

[HarmonyPatch(typeof(HammerMgr), "Start")]
public static class HammerMgrPatchB
{
    public static void Postfix(HammerMgr __instance)
    {
        GameObject obj = new("ModifierHammerCD");
        var text = obj.AddComponent<TextMeshProUGUI>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts/ContinuumBold SDF");
        text.color = new Color(0.5f, 0.8f, 1f);
        obj.transform.SetParent(__instance.GameObject().transform);
        obj.transform.localScale = new Vector3(2f, 2f, 2f);
        obj.transform.localPosition = new Vector3(107, 0, 0);
    }
}

[HarmonyPatch(typeof(HyponoEmperor), "Update")]
public static class HyponoEmperorPatch
{
    public static void Postfix(HyponoEmperor __instance)
    {
        if (!HyponoEmperorNoCD) return;
        try
        {
            if (__instance != null && __instance.summonZombieTime > 2f) 
                __instance.summonZombieTime = 2f;
        }
        catch { }
    }
}

[HarmonyPatch(typeof(InGameBtn), "OnMouseUpAsButton")]
public static class InGameBtnPatch
{
    public static bool BottomEnabled { get; set; }

    public static void Postfix(InGameBtn __instance)
    {
        if (__instance.buttonNumber == 3)
        {
            // 只有在游戏速度功能开启时才允许时停/慢速操作
            if (!GameSpeedEnabled)
            {
                return; // 功能关闭时，不处理时停/慢速，让游戏内部速度调整功能正常工作
            }
            
            TimeSlow = !TimeSlow;
            TimeStop = false;
            if (TimeSlow)
            {
                Time.timeScale = 0.2f;
            }
            else
            {
                // 恢复速度时，如果功能开启且修改器主动设置了速度，使用修改器速度，否则使用游戏内部速度
                if (GameSpeedEnabled && SyncSpeed >= 0 && IsSpeedModifiedByTool)
                {
                    Time.timeScale = SyncSpeed;
                }
                else
                {
                    Time.timeScale = GameAPP.gameSpeed;
                }
            }
        }

        if (__instance.buttonNumber == 13) BottomEnabled = GameObject.Find("Bottom") is not null;
    }
}

[HarmonyPatch(typeof(InGameText), "ShowText")]
public static class InGameTextPatch
{
    public static void Postfix()
    {
        try
        {
            // 使用统一的 TravelMgr 获取方法
            var travelMgr = ResolveTravelMgr(autoCreate: true);
            if (travelMgr == null) return;
            
            if (travelMgr.advancedUpgrades != null && InGameAdvBuffs != null)
            {
                var count = Math.Min(InGameAdvBuffs.Length, travelMgr.advancedUpgrades.Count);
                for (var i = 0; i < count; i++)
                    if (InGameAdvBuffs[i] != travelMgr.advancedUpgrades[i])
                    {
                        SyncInGameBuffs();
                        return;
                    }
            }

            if (travelMgr.ultimateUpgrades != null && InGameUltiBuffs != null)
            {
                var ultiArray = GetBoolArray(travelMgr.ultimateUpgrades);
                if (ultiArray != null)
                {
                    var count = Math.Min(InGameUltiBuffs.Length, ultiArray.Length);
                    for (var i = 0; i < count; i++)
                        if (InGameUltiBuffs[i] != ultiArray[i])
                        {
                            SyncInGameBuffs();
                            return;
                        }
                }
            }
        }
        catch (System.Exception ex)
        {
            MLogger?.LogError($"[PVZRHTools] InGameTextPatch 异常: {ex.Message}\n{ex.StackTrace}");
        }
    }
}

[HarmonyPatch(typeof(InitBoard))]
public static class InitBoardPatch
{
    [HarmonyPrefix]
    [HarmonyPatch("ReadySetPlant")]
    public static void PreReadySetPlant()
    {
        if (CardNoInit)
            if (SeedGroup is not null)
                for (var i = SeedGroup!.transform.childCount - 1; i >= 0; i--)
                {
                    var card = SeedGroup.transform.GetChild(i);
                    if (card is null || card.childCount is 0) continue;
                    card.GetChild(0).gameObject.GetComponent<CardUI>().CD =
                        card.GetChild(0).gameObject.GetComponent<CardUI>().fullCD;
                }

        HammerMgrPatchA.OriginalFullCD =
            Object.FindObjectsOfTypeAll(Il2CppType.Of<HammerMgr>())[0].Cast<HammerMgr>().fullCD;
    }

    [HarmonyPrefix]
    [HarmonyPatch("RightMoveCamera")]
    public static void PreRightMoveCamera(InitBoard __instance)
    {
        __instance.StartCoroutine(PostInitBoard());
    }
}

[HarmonyPatch(typeof(JackboxZombie), "Update")]
public static class JackboxZombiePatch
{
    public static void Postfix(JackboxZombie __instance)
    {
        if (!JackboxNotExplode) return;
        try
        {
            if (__instance != null) 
                __instance.popCountDown = __instance.originalCountDown;
        }
        catch { }
    }
}

[HarmonyPatch(typeof(Plant), "PlantShootUpdate")]
public static class PlantPatch
{
    public static void Prefix(Plant __instance)
    {
        // 提前检查开关，避免不必要的 Il2Cpp 对象访问
        if (!FastShooting) return;
        try
        {
            var s = __instance?.TryCast<Shooter>();
            if (s != null) s.AnimShoot();
        }
        catch { }
    }
}


[HarmonyPatch(typeof(Plant), nameof(Plant.GetDamage))]
public static class PlantGetDamagePatch
{
    [HarmonyPostfix]
    public static void Postfix(Plant __instance, ref int __result)
    {
        if (HardPlant)
        {
            __result = 0;
        }
    }
}

[HarmonyPatch(typeof(Plant), nameof(Plant.Crashed))]
public static class PlantCrashedPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Plant __instance, int level, int soundID, Zombie zombie)
    {
        // 植物无敌或植物免疫碾压时，阻止碾压
        // 注意：踩踏免疫由 TypeMgrUncrashablePlantPatch 和 ZombieOnTriggerStay2DTramplePatch 处理
        if (HardPlant || CrushImmunity)
        {
            return false;
        }
        return true;
    }
}

/// <summary>
/// 免疫强制扣血补丁 - 通过patch Plant.Die方法来阻止异常死亡
/// 针对MorePolevaulterZombie等mod中的吞噬效果（直接修改thePlantHealth绕过TakeDamage）
/// </summary>
[HarmonyPatch(typeof(Plant), nameof(Plant.Die))]
public static class PlantDiePatch
{
    // 记录每个植物上一帧的血量
    private static readonly Dictionary<int, int> LastFrameHealth = new();
    // 记录每个植物是否在本帧通过正常途径受到伤害
    private static readonly HashSet<int> NormalDamageThisFrame = new();
    
    [HarmonyPrefix]
    public static bool Prefix(Plant __instance)
    {
        if (!ImmuneForceDeduct) return true;
        if (__instance == null) return true;
        
        try
        {
            var plantId = __instance.GetInstanceID();
            
            // 如果植物血量还大于0，不应该死亡
            if (__instance.thePlantHealth > 0)
            {
                return true; // 正常死亡流程
            }
            
            // 检查是否有缓存的血量
            if (LastFrameHealth.TryGetValue(plantId, out var lastHealth))
            {
                // 如果上一帧血量很高，但现在突然死亡，可能是强制扣血
                // 恢复血量并阻止死亡
                if (lastHealth > __instance.thePlantMaxHealth * 0.3f)
                {
                    __instance.thePlantHealth = lastHealth;
                    __instance.UpdateText();
                    return false; // 阻止死亡
                }
            }
        }
        catch { }
        
        return true;
    }
    
    /// <summary>
    /// 更新植物血量缓存（在PatchMgr.Update中调用）
    /// </summary>
    public static void UpdateHealthCache()
    {
        if (!ImmuneForceDeduct)
        {
            if (LastFrameHealth.Count > 0)
                LastFrameHealth.Clear();
            return;
        }
        
        try
        {
            var allPlants = Lawnf.GetAllPlants();
            if (allPlants == null) return;
            
            // 收集当前存活植物的ID
            var alivePlantIds = new HashSet<int>();
            foreach (var p in allPlants)
            {
                if (p != null)
                    alivePlantIds.Add(p.GetInstanceID());
            }
            
            // 清理已死亡植物的缓存
            var deadPlantIds = LastFrameHealth.Keys.Where(id => !alivePlantIds.Contains(id)).ToList();
            foreach (var id in deadPlantIds)
                LastFrameHealth.Remove(id);
            
            // 更新缓存
            foreach (var plant in allPlants)
            {
                if (plant == null) continue;
                var plantId = plant.GetInstanceID();
                
                // 只有当植物血量大于0时才更新缓存
                if (plant.thePlantHealth > 0)
                {
                    LastFrameHealth[plantId] = plant.thePlantHealth;
                }
            }
        }
        catch { }
    }
}

#region CurseImmunity - 诅咒免疫补丁

/// <summary>
/// 诅咒免疫补丁 - UltimateHorse.GetDamage
/// 阻止终极马僵尸的诅咒效果
/// </summary>
[HarmonyPatch(typeof(UltimateHorse), nameof(UltimateHorse.GetDamage))]
public static class UltimateHorseGetDamagePatch
{
    [HarmonyPrefix]
    public static bool Prefix(UltimateHorse __instance, ref int theDamage)
    {
        if (!CurseImmunity) return true;
        try
        {
            // 如果诅咒免疫激活，清空诅咒植物列表
            if (__instance != null && __instance.cursedPlants != null && __instance.cursedPlants.Count > 0)
            {
                __instance.cursedPlants.Clear();
            }
        }
        catch { }
        return true;
    }
}

/// <summary>
/// 诅咒免疫补丁 - SuperLadderZombie.GetDamage
/// 阻止超级梯子僵尸的诅咒效果
/// </summary>
[HarmonyPatch(typeof(SuperLadderZombie), nameof(SuperLadderZombie.GetDamage))]
public static class SuperLadderZombieGetDamagePatch
{
    [HarmonyPrefix]
    public static bool Prefix(SuperLadderZombie __instance, ref int theDamage)
    {
        if (!CurseImmunity) return true;
        try
        {
            // 如果诅咒免疫激活且有梯子，阻止诅咒效果
            if (__instance != null && __instance.ladder != null)
            {
                return false; // 阻止原方法执行
            }
        }
        catch { }
        return true;
    }
}

/// <summary>
/// 诅咒免疫补丁 - Zombie.TakeDamage (4参数版本)
/// 通用诅咒免疫，清除僵尸的诅咒植物列表
/// 同时处理僵尸限伤200功能和击杀升级功能
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.TakeDamage), new Type[] { typeof(DmgType), typeof(int), typeof(PlantType), typeof(bool) })]
public static class ZombieTakeDamageCursePatch
{
    private static System.Reflection.FieldInfo? _cachedCursedPlantsField = null;
    
    [HarmonyPrefix]
    public static bool Prefix(Zombie __instance, DmgType theDamageType, ref int theDamage, PlantType reportType, bool fix)
    {
        // 僵尸限伤功能 - 限制每次伤害最多为设定值
        if (ZombieDamageLimit200 && ZombieDamageLimitValue > 0 && theDamage > ZombieDamageLimitValue)
        {
            theDamage = ZombieDamageLimitValue;
        }
        
        // 击杀升级功能 - 记录伤害来源植物
        if (KillUpgrade && reportType != PlantType.Nothing && __instance != null)
        {
            try
            {
                int zombieId = __instance.GetInstanceID();
                ZombieLastDamageSource[zombieId] = reportType;
            }
            catch { }
        }
        
        if (!CurseImmunity) return true;
        try
        {
            // 性能优化：缓存字段信息
            if (_cachedCursedPlantsField == null)
            {
                _cachedCursedPlantsField = typeof(Zombie).GetField("cursedPlants",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            }
            
            if (_cachedCursedPlantsField != null)
            {
                var cursedPlants = _cachedCursedPlantsField.GetValue(__instance) as Il2CppSystem.Collections.Generic.List<Plant>;
                if (cursedPlants != null && cursedPlants.Count > 0)
                {
                    cursedPlants.Clear();
                }
            }
        }
        catch { }
        return true;
    }
}

/// <summary>
/// 僵尸限伤补丁 - Zombie.BodyTakeDamage
/// 限制僵尸身体每次受到的伤害
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.BodyTakeDamage))]
public static class ZombieBodyTakeDamageLimitPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Zombie __instance, ref int theDamage)
    {
        // 僵尸限伤功能 - 限制每次伤害最多为设定值
        if (ZombieDamageLimit200 && ZombieDamageLimitValue > 0 && theDamage > ZombieDamageLimitValue)
        {
            theDamage = ZombieDamageLimitValue;
        }
        return true;
    }
}

/// <summary>
/// 僵尸限伤补丁 - Zombie.FirstArmorTakeDamage
/// 限制僵尸一类护甲每次受到的伤害
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.FirstArmorTakeDamage))]
public static class ZombieFirstArmorTakeDamageLimitPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Zombie __instance, ref int theDamage)
    {
        // 僵尸限伤功能 - 限制每次伤害最多为设定值
        if (ZombieDamageLimit200 && ZombieDamageLimitValue > 0 && theDamage > ZombieDamageLimitValue)
        {
            theDamage = ZombieDamageLimitValue;
        }
        return true;
    }
}

/// <summary>
/// 僵尸限伤补丁 - Zombie.SecondArmorTakeDamage
/// 限制僵尸二类护甲每次受到的伤害
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.SecondArmorTakeDamage))]
public static class ZombieSecondArmorTakeDamageLimitPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Zombie __instance, ref int theDamage)
    {
        // 僵尸限伤功能 - 限制每次伤害最多为设定值
        if (ZombieDamageLimit200 && ZombieDamageLimitValue > 0 && theDamage > ZombieDamageLimitValue)
        {
            theDamage = ZombieDamageLimitValue;
        }
        return true;
    }
}

/// <summary>
/// 僵尸限伤补丁 - Zombie.JalaedExplode (灰烬伤害)
/// 限制僵尸受到的灰烬爆炸伤害
/// 方法签名: void JalaedExplode(bool jala, int damage)
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.JalaedExplode))]
public static class ZombieJalaedExplodeLimitPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Zombie __instance, bool jala, ref int damage)
    {
        // 僵尸限伤功能 - 限制灰烬伤害最多为设定值
        if (ZombieDamageLimit200 && ZombieDamageLimitValue > 0 && damage > ZombieDamageLimitValue)
        {
            damage = ZombieDamageLimitValue;
        }
        return true;
    }
}

/// <summary>
/// 僵尸速度修改补丁 - Zombie.Update
/// 通过在Update的Prefix中修改僵尸的速度属性来实现全局速度倍率调整
/// 需要同时修改theSpeed、theOriginSpeed和动画速度才能生效
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.Update))]
public static class ZombieSpeedModifyPatch
{
    // 用于存储每个僵尸的原始速度，避免重复乘以倍率
    private static readonly Dictionary<int, float> _originalSpeeds = new Dictionary<int, float>();
    
    [HarmonyPrefix]
    public static void Prefix(Zombie __instance)
    {
        if (!ZombieSpeedModifyEnabled || ZombieSpeedMultiplier == 1.0f) return;
        try
        {
            if (__instance == null) return;
            
            int instanceId = __instance.GetInstanceID();
            
            // 如果是第一次处理这个僵尸，记录其原始速度
            if (!_originalSpeeds.ContainsKey(instanceId))
            {
                _originalSpeeds[instanceId] = __instance.theOriginSpeed;
            }
            
            float originalSpeed = _originalSpeeds[instanceId];
            float newSpeed = originalSpeed * ZombieSpeedMultiplier;
            
            // 修改僵尸的速度属性
            __instance.theSpeed = newSpeed;
            __instance.theOriginSpeed = newSpeed;
            
            // 修改动画速度以匹配移动速度
            if (__instance.anim != null)
            {
                __instance.anim.SetFloat("Speed", newSpeed);
            }
        }
        catch { }
    }
    
    // 清理已死亡僵尸的记录，避免内存泄漏
    public static void CleanupDeadZombies()
    {
        try
        {
            var keysToRemove = new List<int>();
            foreach (var kvp in _originalSpeeds)
            {
                // 简单的清理逻辑：当字典过大时清空
                if (_originalSpeeds.Count > 1000)
                {
                    _originalSpeeds.Clear();
                    break;
                }
            }
        }
        catch { }
    }
}

/// <summary>
/// 僵尸攻击力翻倍补丁 - Zombie.AttackEffect
/// 通过在AttackEffect的Prefix中修改僵尸的攻击伤害来实现全局攻击力倍率调整
/// AttackEffect是僵尸实际对植物造成伤害时调用的方法
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.AttackEffect))]
public static class ZombieAttackMultiplierPatch
{
    // 用于存储每个僵尸的原始攻击力，避免重复乘以倍率
    private static readonly Dictionary<int, int> _originalAttackDamages = new Dictionary<int, int>();
    
    [HarmonyPrefix]
    public static void Prefix(Zombie __instance)
    {
        if (!ZombieAttackMultiplierEnabled || ZombieAttackMultiplier == 1.0f) return;
        try
        {
            if (__instance == null) return;
            
            int instanceId = __instance.GetInstanceID();
            
            // 如果是第一次处理这个僵尸，记录其原始攻击力
            if (!_originalAttackDamages.ContainsKey(instanceId))
            {
                _originalAttackDamages[instanceId] = __instance.theAttackDamage;
            }
            
            int originalDamage = _originalAttackDamages[instanceId];
            int newDamage = Mathf.RoundToInt(originalDamage * ZombieAttackMultiplier);
            
            // 修改僵尸的攻击伤害
            __instance.theAttackDamage = newDamage;
        }
        catch { }
    }
    
    // 清理已死亡僵尸的记录，避免内存泄漏
    public static void CleanupDeadZombies()
    {
        try
        {
            if (_originalAttackDamages.Count > 1000)
            {
                _originalAttackDamages.Clear();
            }
        }
        catch { }
    }
}

/// <summary>
/// 矿镐免疫补丁 - Pickaxe_a.ZombieUpdate
/// 阻止第一种矿工挖掘植物
/// </summary>
[HarmonyPatch(typeof(Pickaxe_a), nameof(Pickaxe_a.ZombieUpdate))]
public static class Pickaxe_aImmunityPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Pickaxe_a __instance)
    {
        if (!PickaxeImmunity) return true;
        try
        {
            // 检查矿工是否有攻击目标
            if (__instance?.theAttackTarget != null)
            {
                // 阻止挖掘任何植物
                return false;
            }
        }
        catch { }
        return true;
    }
}

/// <summary>
/// 矿镐免疫补丁 - PickaxeZombie.ZombieUpdate
/// 阻止第二种矿工挖掘植物
/// </summary>
[HarmonyPatch(typeof(PickaxeZombie), nameof(PickaxeZombie.ZombieUpdate))]
public static class PickaxeZombieImmunityPatch
{
    [HarmonyPrefix]
    public static bool Prefix(PickaxeZombie __instance)
    {
        if (!PickaxeImmunity) return true;
        try
        {
            // 检查矿工是否有攻击目标
            if (__instance?.theAttackTarget != null)
            {
                // 阻止挖掘任何植物
                return false;
            }
        }
        catch { }
        return true;
    }
}

/// <summary>
/// 矿镐免疫补丁 - HypnoJalapenoPickaxeZombie.ZombieUpdate
/// 阻止魅惑辣椒矿工挖掘植物
/// </summary>
[HarmonyPatch(typeof(HypnoJalapenoPickaxeZombie), nameof(HypnoJalapenoPickaxeZombie.ZombieUpdate))]
public static class HypnoJalapenoPickaxeZombieImmunityPatch
{
    [HarmonyPrefix]
    public static bool Prefix(HypnoJalapenoPickaxeZombie __instance)
    {
        if (!PickaxeImmunity) return true;
        try
        {
            // 检查矿工是否有攻击目标
            if (__instance?.theAttackTarget != null)
            {
                // 阻止挖掘任何植物
                return false;
            }
        }
        catch { }
        return true;
    }
}

/// <summary>
/// 诅咒免疫补丁 - Board.Update
/// 定期清除植物的诅咒视觉效果，并设置踩踏免疫属性
/// 同时处理无限积分功能
/// </summary>
[HarmonyPatch(typeof(Board), nameof(Board.Update))]
public static class BoardUpdateCursePatch
{
    private static float _curseClearTimer = 0f;
    private const float _curseClearInterval = 1f;
    private static float _trampleImmunityTimer = 0f;
    private const float _trampleImmunityInterval = 0.1f;
    
    [HarmonyPostfix]
    public static void Postfix(Board __instance)
    {
        try
        {
            // 处理无限积分（使用新的独立开关或旧的兼容开关）
            if ((UnlimitedScore || BuffRefreshNoLimit) && __instance != null)
            {
                __instance.thePoints = 999999f;
            }
            
            // 处理诅咒免疫
            if (CurseImmunity)
            {
                _curseClearTimer += Time.deltaTime;
                if (_curseClearTimer >= _curseClearInterval)
                {
                    _curseClearTimer = 0f;
                    ClearAllPlantsCurseVisual();
                }
            }
            
            // 处理踩踏免疫 - 通过设置 canBeCrashed 属性
            if (TrampleImmunity)
            {
                _trampleImmunityTimer += Time.deltaTime;
                if (_trampleImmunityTimer >= _trampleImmunityInterval)
                {
                    _trampleImmunityTimer = 0f;
                    SetAllPlantsCanBeCrashed(false);
                }
            }
            
            // 处理两波间最大刷怪CD - 持续设置waveInterval，防止被游戏重置
            if (NewZombieUpdateCD > 0f && NewZombieUpdateCD <= 30f && __instance != null)
            {
                // 确保waveInterval不超过设置的最大值
                if (__instance.waveInterval > NewZombieUpdateCD)
                {
                    __instance.waveInterval = NewZombieUpdateCD;
                }
            }
        }
        catch { }
    }
    
    private static void ClearAllPlantsCurseVisual()
    {
        try
        {
            if (Board.Instance == null) return;
            
            var allPlants = Lawnf.GetAllPlants();
            if (allPlants == null) return;
            
            foreach (var plant in allPlants)
            {
                if (plant != null && plant.thePlantHealth > 0)
                {
                    ClearPlantCurseVisual(plant);
                }
            }
        }
        catch { }
    }
    
    private static void ClearPlantCurseVisual(Plant plant)
    {
        try
        {
            if (plant == null || plant.gameObject == null) return;
            
            var spriteRenderers = plant.GetComponentsInChildren<SpriteRenderer>();
            if (spriteRenderers != null)
            {
                foreach (var sr in spriteRenderers)
                {
                    if (sr != null)
                    {
                        // 重置颜色到白色（正常状态）
                        sr.color = Color.white;
                    }
                }
            }
        }
        catch { }
    }
    
    /// <summary>
    /// 设置所有植物的 canBeCrashed 属性
    /// 参考 SuperMachinePotComponent.cs 的实现
    /// </summary>
    private static void SetAllPlantsCanBeCrashed(bool value)
    {
        try
        {
            if (Board.Instance == null) return;
            
            var allPlants = Lawnf.GetAllPlants();
            if (allPlants == null) return;
            
            foreach (var plant in allPlants)
            {
                if (plant != null && plant.thePlantHealth > 0)
                {
                    try
                    {
                        var plantType = plant.GetType();
                        var crashedProp = plantType.GetProperty("canBeCrashed");
                        
                        if (crashedProp != null && crashedProp.CanWrite)
                            crashedProp.SetValue(plant, value);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }
}

#endregion

#region TrampleImmunity - 踩踏免疫补丁

/// <summary>
/// 踩踏免疫补丁 - TypeMgr.UncrashablePlant
/// 这是游戏判断植物是否免疫碾压的核心方法
/// Boss类领袖等僵尸会调用此方法来判断是否可以碾压植物
/// 参考 SuperMachinePot 的 TypeMgrUncrashablePlantPatch 实现
/// </summary>
[HarmonyPatch(typeof(TypeMgr), "UncrashablePlant")]
public static class TypeMgrUncrashablePlantPatch
{
    [HarmonyPrefix]
    public static bool Prefix(ref Plant plant, ref bool __result)
    {
        if (!TrampleImmunity) return true;
        
        try
        {
            if (plant == null)
                return true;

            // 当踩踏免疫开启时，所有植物都免疫碾压
            __result = true;
            return false; // 不执行原方法
        }
        catch { }
        
        return true;
    }
}

/// <summary>
/// 踩踏免疫补丁 - Zombie.OnTriggerStay2D
/// 作为备用保护，阻止驾驶类僵尸（如冰车）对植物的踩踏伤害
/// 主要保护逻辑在 TypeMgrUncrashablePlantPatch 中实现
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.OnTriggerStay2D))]
public static class ZombieOnTriggerStay2DTramplePatch
{
    [HarmonyPrefix]
    public static bool Prefix(Collider2D collision, Zombie __instance)
    {
        if (!TrampleImmunity) return true;
        
        try
        {
            if (__instance == null || collision == null)
                return true;
            
            // 获取碰撞的植物
            Plant plant = collision.GetComponent<Plant>();
            if (plant == null)
                return true;
            
            // 检查是否是驾驶类僵尸或巨人僵尸
            if (plant.thePlantRow == __instance.theZombieRow && 
                (TypeMgr.IsDriverZombie(__instance.theZombieType) || TypeMgr.IsGargantuar(__instance.theZombieType)))
            {
                // 阻止踩踏伤害，但让僵尸继续移动
                return false;
            }
        }
        catch { }
        
        return true;
    }
}

#endregion

#region ZombieStatusCoexist - 僵尸状态并存补丁

/// <summary>
/// 僵尸状态并存补丁 - Zombie.Warm
/// 当启用状态并存时，只要僵尸有寒冷/冻结/蒜毒状态就阻止Warm方法
/// 这样可以保护这些状态不被火焰效果清除
/// 
/// 修复说明：
/// 原版游戏中，SetJalaed()内部会调用Warm()来清除寒冷状态
/// 之前的逻辑是"只有同时有红温和寒冷状态时才阻止"，但问题是：
/// 当火爆辣椒爆炸时，SetJalaed()被调用，此时僵尸还没有红温状态，
/// 所以Warm()会被正常执行，清除寒冷状态，然后才设置红温状态。
/// 
/// 修复后的逻辑：只要僵尸有寒冷/冻结/蒜毒状态，就阻止Warm方法执行
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.Warm))]
public static class ZombieWarmPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Zombie __instance)
    {
        if (!ZombieStatusCoexist) return true;
        
        try
        {
            if (__instance == null) return true;
            
            // 只要僵尸有寒冷/冻结/蒜毒状态，就阻止Warm方法执行
            // 这样可以保护这些状态不被火焰效果（如火爆辣椒）清除
            bool hasCold = __instance.coldTimer > 0 || __instance.freezeTimer > 0;
            bool hasPoison = __instance.poisonTimer > 0;
            
            if (hasCold || hasPoison)
            {
                return false; // 阻止原方法执行，保护寒冷/蒜毒状态
            }
        }
        catch { }
        
        return true; // 正常执行
    }
}

/// <summary>
/// 僵尸状态并存补丁 - Zombie.Unfreezing
/// 当启用状态并存时，只要僵尸有冻结状态就阻止Unfreezing方法
/// 这样可以保护冻结状态不被火焰效果清除
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.Unfreezing))]
public static class ZombieUnfreezingPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Zombie __instance)
    {
        if (!ZombieStatusCoexist) return true;
        
        try
        {
            if (__instance == null) return true;
            
            // 只要僵尸有冻结状态，就阻止Unfreezing方法执行
            bool hasFrozen = __instance.freezeTimer > 0;
            
            if (hasFrozen)
            {
                return false; // 阻止原方法执行，保护冻结状态
            }
        }
        catch { }
        
        return true; // 正常执行
    }
}

/// <summary>
/// 僵尸状态并存补丁 - Zombie.SetCold
/// 当启用状态并存时，SetCold不会清除红温状态
/// 原版游戏中SetCold内部会清除红温状态（isJalaed = false）
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.SetCold))]
public static class ZombieSetColdCoexistPatch
{
    // 用于临时存储僵尸的红温状态
    private static readonly Dictionary<int, (bool isJalaed, bool isEmbered)> _savedWarmStates = new Dictionary<int, (bool, bool)>();
    
    [HarmonyPrefix]
    public static void Prefix(Zombie __instance)
    {
        if (!ZombieStatusCoexist) return;
        
        try
        {
            if (__instance == null) return;
            
            int instanceId = __instance.GetInstanceID();
            
            // 保存当前的红温状态
            _savedWarmStates[instanceId] = (__instance.isJalaed, __instance.isEmbered);
        }
        catch { }
    }
    
    [HarmonyPostfix]
    public static void Postfix(Zombie __instance)
    {
        if (!ZombieStatusCoexist) return;
        
        try
        {
            if (__instance == null) return;
            
            int instanceId = __instance.GetInstanceID();
            
            // 恢复红温状态
            if (_savedWarmStates.TryGetValue(instanceId, out var savedState))
            {
                __instance.isJalaed = savedState.isJalaed;
                __instance.isEmbered = savedState.isEmbered;
                _savedWarmStates.Remove(instanceId);
            }
        }
        catch { }
    }
}

/// <summary>
/// 僵尸状态并存补丁 - Zombie.SetFreeze
/// 当启用状态并存时，SetFreeze不会清除红温状态
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.SetFreeze))]
public static class ZombieSetFreezeCoexistPatch
{
    // 用于临时存储僵尸的红温状态
    private static readonly Dictionary<int, (bool isJalaed, bool isEmbered)> _savedWarmStates = new Dictionary<int, (bool, bool)>();
    
    [HarmonyPrefix]
    public static void Prefix(Zombie __instance)
    {
        if (!ZombieStatusCoexist) return;
        
        try
        {
            if (__instance == null) return;
            
            int instanceId = __instance.GetInstanceID();
            
            // 保存当前的红温状态
            _savedWarmStates[instanceId] = (__instance.isJalaed, __instance.isEmbered);
        }
        catch { }
    }
    
    [HarmonyPostfix]
    public static void Postfix(Zombie __instance)
    {
        if (!ZombieStatusCoexist) return;
        
        try
        {
            if (__instance == null) return;
            
            int instanceId = __instance.GetInstanceID();
            
            // 恢复红温状态
            if (_savedWarmStates.TryGetValue(instanceId, out var savedState))
            {
                __instance.isJalaed = savedState.isJalaed;
                __instance.isEmbered = savedState.isEmbered;
                _savedWarmStates.Remove(instanceId);
            }
        }
        catch { }
    }
}

/// <summary>
/// 僵尸状态并存补丁 - Zombie.SetPoison
/// 确保蒜毒状态可以与其他状态并存
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.SetPoison))]
public static class ZombieSetPoisonCoexistPatch
{
    // 用于临时存储僵尸的红温和寒冷状态（包括freezeTimer）
    private static readonly Dictionary<int, (bool isJalaed, bool isEmbered, float coldTimer, float freezeTimer, int freezeLevel)> _savedStates = new Dictionary<int, (bool, bool, float, float, int)>();
    
    [HarmonyPrefix]
    public static void Prefix(Zombie __instance)
    {
        if (!ZombieStatusCoexist) return;
        
        try
        {
            if (__instance == null) return;
            
            int instanceId = __instance.GetInstanceID();
            
            // 保存当前的红温和寒冷状态（包括freezeTimer）
            _savedStates[instanceId] = (__instance.isJalaed, __instance.isEmbered, __instance.coldTimer, __instance.freezeTimer, __instance.freezeLevel);
        }
        catch { }
    }
    
    [HarmonyPostfix]
    public static void Postfix(Zombie __instance)
    {
        if (!ZombieStatusCoexist) return;
        
        try
        {
            if (__instance == null) return;
            
            int instanceId = __instance.GetInstanceID();
            
            // 恢复红温和寒冷状态（包括freezeTimer）
            if (_savedStates.TryGetValue(instanceId, out var savedState))
            {
                __instance.isJalaed = savedState.isJalaed;
                __instance.isEmbered = savedState.isEmbered;
                __instance.coldTimer = savedState.coldTimer;
                __instance.freezeTimer = savedState.freezeTimer;
                __instance.freezeLevel = savedState.freezeLevel;
                _savedStates.Remove(instanceId);
            }
        }
        catch { }
    }
}

/// <summary>
/// 僵尸状态并存补丁 - Zombie.SetJalaed (红温状态)
/// 当启用状态并存时，完全阻止原方法执行，手动设置红温状态以保留寒冷状态
/// 同时手动应用红温视觉效果
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.SetJalaed))]
public static class ZombieSetJalaedCoexistPatch
{
    // 红温颜色 (橙红色)
    private static readonly Color JalaedColor = new Color(1f, 0.5f, 0.2f, 1f);
    
    [HarmonyPrefix]
    public static bool Prefix(Zombie __instance)
    {
        if (!ZombieStatusCoexist) return true; // 不启用时正常执行原方法
        
        try
        {
            if (__instance == null) return true;
            
            // 手动设置红温状态，不调用原方法（原方法会清除寒冷状态）
            __instance.isJalaed = true;
            
            // 手动应用红温视觉效果
            ApplyJalaedVisual(__instance);
            
            return false; // 阻止原方法执行
        }
        catch 
        { 
            return true; // 出错时执行原方法
        }
    }
    
    /// <summary>
    /// 应用红温视觉效果
    /// </summary>
    private static void ApplyJalaedVisual(Zombie zombie)
    {
        try
        {
            // 获取僵尸的所有 SpriteRenderer 并设置红温颜色
            var spriteRenderers = zombie.GetComponentsInChildren<SpriteRenderer>();
            if (spriteRenderers != null)
            {
                foreach (var sr in spriteRenderers)
                {
                    if (sr != null)
                    {
                        sr.color = JalaedColor;
                    }
                }
            }
        }
        catch { }
    }
}

/// <summary>
/// Zombie.SetEmbered 全局安全检查补丁 - 防止内存访问违规
/// 在所有其他 SetEmbered 补丁之前运行，确保对象有效性
/// 关键：完全阻止可能有问题的原方法执行，使用安全的托管实现
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.SetEmbered))]
public static class ZombieSetEmberedSafetyPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)] // 最高优先级，在所有其他补丁之前运行
    public static bool Prefix(Zombie __instance, bool ulti = false)
    {
        try
        {
            // 基本 null 检查
            if (__instance == null) return false; // 阻止执行
            
            // 使用 Il2CppInterop 的安全检查方法验证对象指针
            IntPtr ptr;
            try
            {
                ptr = Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtrNotNull(__instance);
                if (ptr == IntPtr.Zero) return false; // 无效指针，阻止执行
            }
            catch
            {
                // 对象指针验证失败，阻止执行
                return false;
            }
            
            // 安全地检查对象的基本字段是否可访问
            // 如果对象已销毁，访问字段会抛出异常
            int health = 0;
            try
            {
                health = __instance.theHealth;
            }
            catch
            {
                // 对象可能已销毁或字段不可访问，阻止执行
                return false;
            }
            
            // 检查僵尸是否已死亡
            if (health <= 0) return false; // 已死亡对象，阻止执行
            
            // 如果所有安全检查都通过，我们需要决定是否允许原方法执行
            // 由于崩溃发生在 IL2CPP 运行时调用中，我们可以选择：
            // 1. 返回 true 允许执行（但可能仍然崩溃）
            // 2. 返回 false 阻止执行，但需要手动设置状态
            
            // 所有安全检查都通过，对象看起来有效
            // 但为了防止在原生方法调用时对象突然变得无效（竞争条件），
            // 我们完全阻止原生方法执行，改用安全的托管实现
            // 原生实现会访问 this->klass->vtable，如果对象已损坏会导致崩溃
            
            // 安全地手动实现 SetEmbered 的完整功能（对应原生代码）
            // 原生代码：1. 如果 ulti=true，设置 ultiEmbered=1
            //           2. 设置 isEmbered=1
            //           3. 调用 UpdateColor()
            try
            {
                // 1. 设置 isEmbered（对应原生代码的 this->fields.isEmbered = 1）
                __instance.isEmbered = true;
                
                // 2. 如果 ulti=true，设置 ultiEmbered（对应原生代码的 this->fields.ultiEmbered = 1）
                if (ulti)
                {
                    try
                    {
                        // 注意：ultiEmbered 字段可能不存在于所有版本，用 try-catch 保护
                        __instance.ultiEmbered = true;
                    }
                    catch
                    {
                        // 字段可能不存在，忽略（不影响主要功能）
                    }
                }
                
                // 3. 尝试调用 UpdateColor（对应原生代码的 klass->vtable._35_UpdateColor）
                // 使用 try-catch 保护，因为 UpdateColor 可能也需要访问虚函数表
                try
                {
                    __instance.UpdateColor();
                }
                catch
                {
                    // UpdateColor 调用失败，但余烬状态已设置
                    // 不阻止返回，因为主要功能（设置余烬状态）已完成
                }
                
                // 所有操作成功，阻止原方法执行（我们已经手动实现了功能）
                return false; // 阻止原生方法执行，使用安全的托管实现
            }
            catch
            {
                // 如果手动实现失败，对象可能已损坏，阻止执行原方法
                return false;
            }
        }
        catch
        {
            // 任何异常都阻止执行
            return false;
        }
    }
    
    // 添加 Finalizer 来捕获可能的异常（虽然 AccessViolationException 通常无法捕获）
    [HarmonyFinalizer]
    public static Exception? Finalizer(Zombie __instance, bool ulti, Exception? __exception)
    {
        // 记录异常但不重新抛出（因为可能无法捕获 AccessViolationException）
        if (__exception != null)
        {
            // 异常已发生，但已经无法阻止崩溃
        }
        return null; // 不重新抛出异常
    }
}

/// <summary>
/// 僵尸状态并存补丁 - Zombie.SetEmbered (余烬状态)
/// 当启用状态并存时，完全阻止原方法执行，手动设置余烬状态以保留寒冷状态
/// 同时手动应用余烬视觉效果
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.SetEmbered))]
public static class ZombieSetEmberedCoexistPatch
{
    // 余烬颜色 (深红色/暗红色)
    private static readonly Color EmberedColor = new Color(0.8f, 0.3f, 0.1f, 1f);
    
    [HarmonyPrefix]
    public static bool Prefix(Zombie __instance, bool ulti = false)
    {
        if (!ZombieStatusCoexist) return true; // 不启用时正常执行原方法
        
        try
        {
            // 严格的对象有效性检查
            if (__instance == null) return true;
            
            // 安全地检查对象有效性
            try
            {
                var _ = __instance.theHealth;
            }
            catch
            {
                return true; // 对象可能已销毁
            }
            
            try
            {
                if (__instance.theHealth <= 0) return true;
            }
            catch
            {
                return true; // 对象可能已销毁
            }
            
            // 手动设置余烬状态，不调用原方法（原方法会清除寒冷状态）
            try
            {
                __instance.isEmbered = true;
            }
            catch
            {
                return true; // 如果设置失败，执行原方法
            }
            
            // 手动应用余烬视觉效果
            ApplyEmberedVisual(__instance);
            
            return false; // 阻止原方法执行
        }
        catch 
        { 
            return true; // 出错时执行原方法
        }
    }
    
    /// <summary>
    /// 应用余烬视觉效果
    /// </summary>
    private static void ApplyEmberedVisual(Zombie zombie)
    {
        try
        {
            // 获取僵尸的所有 SpriteRenderer 并设置余烬颜色
            var spriteRenderers = zombie.GetComponentsInChildren<SpriteRenderer>();
            if (spriteRenderers != null)
            {
                foreach (var sr in spriteRenderers)
                {
                    if (sr != null)
                    {
                        sr.color = EmberedColor;
                    }
                }
            }
        }
        catch { }
    }
}

#endregion

// 注释掉 PotatoMine.Update patch，改用 PatchMgr.Update 中的实现
// 原因：Il2Cpp 对象池在高频 Harmony patch 中会导致栈溢出
/*
[HarmonyPatch(typeof(PotatoMine), "Update")]
public static class PotatoMinePatch
{
    public static void Prefix(PotatoMine __instance)
    {
        if (!MineNoCD) return;
        try
        {
            if (__instance != null && __instance.attributeCountdown > 0.05f) 
                __instance.attributeCountdown = 0.05f;
        }
        catch { }
    }
}

*/

[HarmonyPatch(typeof(Board), nameof(Board.SetEvePlants))]
public static class BoardPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Board __instance, ref int theColumn, ref int theRow, ref bool fromWheat,ref GameObject __result)
    {
        if (fromWheat && LockWheat >= 0)
        {
            GameObject plantObject = CreatePlant.Instance.SetPlant(
                theColumn, 
                theRow, 
                (PlantType)LockWheat
            );

            plantObject.TryGetComponent<Plant>(out var component);
            if (component is not null)
            {
                component.wheatType = 1;
            }
            
            if (!plantObject)
            {
                float boxX = Mouse.Instance.GetBoxXFromColumn(theColumn);
                float landY = Mouse.Instance.GetLandY(boxX, theRow);
                Lawnf.SetDroppedCard(new Vector2(boxX, landY), (PlantType)LockWheat);
            }
            else
            {
                __result = plantObject;
            }
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(Present), "RandomPlant")]
public static class PresentPatchA
{
    public static bool Prefix(Present __instance)
    {
        if (LockPresent >= 0)
        {
            CreatePlant.Instance.SetPlant(__instance.thePlantColumn, __instance.thePlantRow, (PlantType)LockPresent);
            if (CreatePlant.Instance.IsPuff((PlantType)LockPresent))
            {
                CreatePlant.Instance.SetPlant(__instance.thePlantColumn, __instance.thePlantRow,
                    (PlantType)LockPresent);
                CreatePlant.Instance.SetPlant(__instance.thePlantColumn, __instance.thePlantRow,
                    (PlantType)LockPresent);
            }

            return false;
        }

        if (SuperPresent)
        {
            __instance.SuperRandomPlant();
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(Present), "Start")]
public static class PresentPatchB
{
    public static void Postfix(Present __instance)
    {
        if (PresentFastOpen && (int)__instance.thePlantType != 245) __instance.AnimEvent();
    }
}

[HarmonyPatch(typeof(Present), "AnimEvent")]
public static class PresentPatchC
{
    public static bool Prefix(Present __instance)
    {
        // 检查是否是PvE布阵的礼盒（第3行，第1-5列）
        if (__instance.thePlantRow == 2)
        {
            int lockPlantType = -1;
            switch (__instance.thePlantColumn)
            {
                case 0: lockPlantType = LockPresent1; break;
                case 1: lockPlantType = LockPresent2; break;
                case 2: lockPlantType = LockPresent3; break;
                case 3: lockPlantType = LockPresent4; break;
                case 4: lockPlantType = LockPresent5; break;
            }
            
            if (lockPlantType >= 0)
            {
                var col = __instance.thePlantColumn;
                var row = __instance.thePlantRow;
                var pos = __instance.transform.position;
                
                // 创建粒子效果
                CreateParticle.SetParticle(11, pos, row, true);
                
                // 先销毁礼盒，释放位置
                __instance.Die();
                
                // 再创建指定植物
                CreatePlant.Instance.SetPlant(col, row, (PlantType)lockPlantType);
                if (CreatePlant.Instance.IsPuff((PlantType)lockPlantType))
                {
                    CreatePlant.Instance.SetPlant(col, row, (PlantType)lockPlantType);
                    CreatePlant.Instance.SetPlant(col, row, (PlantType)lockPlantType);
                }
                
                return false; // 阻止原始AnimEvent执行
            }
        }
        
        return true; // 继续执行原始AnimEvent
    }
}

[HarmonyPatch(typeof(ProgressMgr), "Awake")]
public static class ProgressMgrPatchA
{
    public static void Postfix(ProgressMgr __instance)
    {
        GameObject obj = new("ModifierGameInfo");
        var text = obj.AddComponent<TextMeshProUGUI>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts/ContinuumBold SDF");
        text.color = new Color(0, 1, 1);
        obj.transform.SetParent(__instance.GameObject().transform);
        obj.transform.localScale = new Vector3(0.4f, 0.2f, 0.2f);
        obj.transform.localPosition = new Vector3(100f, 2.2f, 0);
        obj.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 50);
    }
}

[HarmonyPatch(typeof(ProgressMgr), "Update")]
public static class ProgressMgrPatchB
{
    public static void Postfix(ProgressMgr __instance)
    {
        try
        {
            if (__instance == null) return;
            var infoChild = __instance.transform.FindChild("ModifierGameInfo");
            if (infoChild == null) return;
            if (ShowGameInfo)
            {
                infoChild.GameObject().active = true;
                // 使用 timeUntilNextWave 显示刷新CD（3.3.1版本中newZombieWaveCountDown字段已被移除）
                float refreshCD = 0f;
                int currentWave = 0;
                int maxWave = 0;
                if (Board.Instance != null)
                {
                    refreshCD = Board.Instance.timeUntilNextWave;
                    currentWave = Board.Instance.theWave;
                    maxWave = Board.Instance.theMaxWave;
                    
                    // 如果刷新CD为0或负数，但游戏还在进行中（不是最后一波），
                    // 可能是刚刚触发了"生成下一波"，此时等待游戏更新 timeUntilNextWave
                    // 如果游戏还没有更新（通常会在 NewZombieUpdate() 中更新），
                    // 则使用 NewZombieUpdateCD 作为临时显示值
                    if (refreshCD <= 0f && currentWave > 0 && currentWave < maxWave)
                    {
                        // 检查 NewZombieUpdateCD 是否有效（通常在 0-30 秒之间）
                        if (NewZombieUpdateCD > 0f && NewZombieUpdateCD <= 30f)
                        {
                            // 使用 NewZombieUpdateCD 作为临时显示值
                            // 游戏会在 NewZombieUpdate() 中更新 timeUntilNextWave
                            refreshCD = NewZombieUpdateCD;
                        }
                        // 如果 NewZombieUpdateCD 无效，保持 refreshCD 为 0，显示 "N/A"
                    }
                }
                string cdText = refreshCD > 0f ? $"{refreshCD:F1}" : "N/A";
                infoChild.GameObject().GetComponent<TextMeshProUGUI>().text =
                    $"波数: {currentWave}/{maxWave} 刷新CD: {cdText}";
            }
            else
            {
                infoChild.GameObject().active = false;
            }
        }
        catch { }
    }
}

[HarmonyPatch(typeof(RandomZombie), "SetRandomZombie")]
public static class RamdomZombiePatch
{
    public static bool Prefix(RandomZombie __instance, ref GameObject __result)
    {
        if (!UltimateRamdomZombie) return true;
        if (Board.Instance is not null && Board.Instance.isEveStarted) return true;
        var id = Random.RandomRangeInt(200, 223);
        if (Random.RandomRangeInt(0, 5) == 1)
        {
            if (!__instance.isMindControlled)
                __result = CreateZombie.Instance.SetZombie(__instance.theZombieRow, (ZombieType)id,
                    __instance.GameObject().transform.position.x);
            else
                __result = CreateZombie.Instance.SetZombieWithMindControl(__instance.theZombieRow, (ZombieType)id,
                    __instance.GameObject().transform.position.x);
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(Squalour), "LourDie")]
public static class SqualourPatch
{
    public static bool OriginalDevMode { get; set; }

    public static void Postfix()
    {
        GameAPP.developerMode = OriginalDevMode;
    }

    public static void Prefix()
    {
        OriginalDevMode = GameAPP.developerMode;
        GameAPP.developerMode |= DevLour;
    }
}

/// <summary>
/// 超级机枪射手无限开大补丁 - SuperSnowGatling.Update
/// 通过设置 keepShooting = true 使植物持续保持射击状态
/// 同时重置 timer 确保大招持续触发
/// </summary>
[HarmonyPatch(typeof(SuperSnowGatling), "Update")]
public static class SuperSnowGatlingPatchA
{
    // 记录哪些植物被修改过（用于关闭时恢复）
    private static HashSet<int> _modifiedPlants = new HashSet<int>();
    // 记录哪些植物已经触发过首次射击
    private static HashSet<int> _initializedPlants = new HashSet<int>();
    
    public static void Prefix(SuperSnowGatling __instance, out bool __state)
    {
        __state = false;
        if (__instance == null) return;
        
        int plantId = __instance.GetInstanceID();
        
        if (UltimateSuperGatling)
        {
            try
            {
                __instance.keepShooting = true;
                _modifiedPlants.Add(plantId);
                
                // 首次触发：植物未初始化且timer为0时，需要手动触发射击
                if (!_initializedPlants.Contains(plantId))
                {
                    if (__instance.timer <= 0f)
                    {
                        __state = true;
                        _initializedPlants.Add(plantId);
                    }
                }
                // 后续触发：timer即将归零时触发
                else if (__instance.timer > 0 && __instance.timer - Time.deltaTime <= 0f)
                {
                    __state = true;
                }
            }
            catch { }
        }
        else
        {
            // 功能关闭：恢复被修改过的植物
            if (_modifiedPlants.Contains(plantId))
            {
                try
                {
                    __instance.keepShooting = false;
                    _modifiedPlants.Remove(plantId);
                    _initializedPlants.Remove(plantId);
                }
                catch { }
            }
        }
    }
    
    public static void Postfix(SuperSnowGatling __instance, bool __state)
    {
        if (!UltimateSuperGatling || __instance == null) return;
        
        try
        {
            __instance.timer = 0.1f;
            if (__state && __instance.anim != null)
            {
                __instance.anim.SetTrigger("shoot");
            }
        }
        catch { }
    }
    
    /// <summary>
    /// 清理记录（切换关卡时调用）
    /// </summary>
    public static void ClearAll()
    {
        _modifiedPlants.Clear();
        _initializedPlants.Clear();
    }
}

/// <summary>
/// 超级机枪射手无限开大补丁 - SuperSnowGatling.Shoot1
/// 在每次射击后立即触发 AttributeEvent 重置大招状态
/// </summary>
[HarmonyPatch(typeof(SuperSnowGatling), "Shoot1")]
public static class SuperSnowGatlingPatchB
{
    public static void Postfix(SuperSnowGatling __instance)
    {
        if (!UltimateSuperGatling) return;
        try
        {
            if (__instance != null) __instance.AttributeEvent();
        }
        catch { }
    }
}

[HarmonyPatch(typeof(TravelRefresh), "OnMouseUpAsButton")]
public static class TravelRefreshPatch
{
    public static void Postfix(TravelRefresh __instance)
    {
        if (UnlimitedRefresh || BuffRefreshNoLimit) __instance.refreshTimes = 2147483647;
    }
}

[HarmonyPatch(typeof(TravelStore), "RefreshBuff")]
public static class TravelStorePatch
{
    public static void Postfix(TravelStore __instance)
    {
        if (UnlimitedRefresh || BuffRefreshNoLimit) __instance.count = 0;
    }
}

[HarmonyPatch(typeof(ShootingMenu), nameof(ShootingMenu.Refresh))]
public static class ShootingMenuPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (UnlimitedRefresh || BuffRefreshNoLimit) ShootingManager.Instance.refreshCount = 2147483647;
    }
}
[HarmonyPatch(typeof(FruitNinjaManager),nameof(FruitNinjaManager.LoseScore))]
public static class FruitNinjaManagerPatch
{
    [HarmonyPrefix]
    public static void Postfix(ref float value)
    {
        if (UnlimitedScore || BuffRefreshNoLimit) value = -1e-10f;
    }
}
[HarmonyPatch(typeof(FruitObject), nameof(FruitObject.FixedUpdate))]
public static class FrFruitObjectPatch
{
    [HarmonyPostfix]
    public static void Postfix(FruitObject __instance)
    {
        if (!AutoCutFruit) return;
        try
        {
            if (__instance == null || __instance.gameObject == null) return;
            __instance.gameObject.TryGetComponent<Rigidbody2D>(out var rb);
            if (rb != null)
            {
                float screenHeight = Camera.main.orthographicSize;
                if (__instance.transform.position.y < -screenHeight && rb.velocity.y < 0f)
                {
                    __instance.Slice();
                }
            }
        }
        catch { }
    }
}
/*
[HarmonyPatch(typeof(CreatePlant), "Lim")]
public static class CreatePlantPatchA
{
    public static void Postfix(ref bool __result) => __result = !UnlockAllFusions && __result;
}

[HarmonyPatch(typeof(CreatePlant), "LimTravel")]
public static class CreatePlantPatchB
{
    public static void Postfix(ref bool __result) => __result = !UnlockAllFusions && __result;
}
*/

[HarmonyPatch(typeof(UIMgr), "EnterMainMenu")]
public static class UIMgrPatch
{
    public static void Postfix()
    {
        GameObject obj1 = new("ModifierInfo");
        var text1 = obj1.AddComponent<TextMeshProUGUI>();
        text1.font = Resources.Load<TMP_FontAsset>("Fonts/ContinuumBold SDF");
        text1.color = new Color(1f, 0.41f, 0.71f, 1);
        text1.text = "原作者@Infinite75已停更，\n这是@梧萱梦汐X从@听雨夜荷的fork接手的适配工作\n若存在任何付费/要求三连+关注/私信发链接的情况\n说明你被盗版骗了，请注意隐私和财产安全！！！\n此信息仅在游戏主菜单和修改窗口显示";
        obj1.transform.SetParent(GameObject.Find("Leaves").transform);
        obj1.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        obj1.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 50);
        obj1.transform.localPosition = new Vector3(-345.5f, -70f, 0);
        
        /*GameObject obj2 = new("UpgradeInfo");
        var text2 = obj2.AddComponent<TextMeshProUGUI>();
        text2.font = Resources.Load<TMP_FontAsset>("Fonts/ContinuumBold SDF");
        text2.color = new Color(0, 1, 0, 1);
        text2.text = "原作者@Infinite75已停更，这是@听雨夜荷的一个fork。\n" +
                     "项目地址: https://github.com/CarefreeSongs712/PVZRHTools\n" +
                     "\n" +
                     "修改器2.8.2-3.29.1更新日志:\n" +
                     "1. 适配2.8.2\n"+
                     "2. 修复旅行商店的bug";
        obj2.transform.SetParent(GameObject.Find("Leaves").transform);
        obj2.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        obj2.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 50);
        obj2.transform.localPosition = new Vector3(-345.5f, 55f, 0);*/
    }
}


public class CustomIZData
{
    public List<ZombieData>? Zombies { get; set; }
    public List<GridItemData>? GridItems { get; set; }
}

public class ZombieData
{
    public int Type { get; set; }
    public int Row { get; set; }
    public float PositionX { get; set; }
    public bool IsMindControlled { get; set; }
}

public class GridItemData
{
    public int Type { get; set; }
    public int Column { get; set; }
    public int Row { get; set; }
    public int PlantType { get; set; }
}

[HarmonyPatch(typeof(Zombie), "Start")]
public static class ZombiePatch
{
    public static void Postfix(Zombie __instance)
    {
        try
        {
            if (HealthZombies[__instance.theZombieType] >= 0)
            {
                __instance.theMaxHealth = HealthZombies[__instance.theZombieType];
                __instance.theHealth = __instance.theMaxHealth;
            }

            if (Health1st[__instance.theFirstArmorType] >= 0 &&
                __instance.theMaxHealth != Health1st[__instance.theFirstArmorType])
            {
                __instance.theFirstArmorMaxHealth = Health1st[__instance.theFirstArmorType];
                __instance.theFirstArmorHealth = __instance.theFirstArmorMaxHealth;
            }

            if (Health2nd[__instance.theSecondArmorType] >= 0 &&
                __instance.theMaxHealth != Health2nd[__instance.theSecondArmorType])
            {
                __instance.theSecondArmorMaxHealth = Health2nd[__instance.theSecondArmorType];
                __instance.theSecondArmorHealth = __instance.theSecondArmorMaxHealth;
            }

            __instance.UpdateHealthText();
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(Mouse), nameof(Mouse.TryToSetPlantByGlove))]
public static class MousePatch
{
    private static Plant? aa = null;
    
    [HarmonyPrefix]
    public static bool Prefix(Mouse __instance)
    {
        if (ColumnGlove)
        {
            aa = __instance.thePlantOnGlove;   
            int vcol = __instance.theMouseColumn - __instance.thePlantOnGlove.thePlantColumn;
            int newCol = __instance.theMouseColumn;
            List<Plant> plants = new List<Plant>();
            var allPlants = Lawnf.GetAllPlants();
            if (allPlants != null)
            {
                foreach (var plant in allPlants)
                {
                    if(plant == null || plant.gameObject == null)continue;
                    if (plant.thePlantColumn == __instance.thePlantOnGlove.thePlantColumn)
                    {
                        if(plant == __instance.thePlantOnGlove){}
                        else
                        {
                            if(plant.thePlantType == __instance.thePlantOnGlove.thePlantType)
                                plants.Add(plant);
                        }
                    }
                }
            }
            foreach (var plant in plants)
            {
                GameObject gameObject =
                    CreatePlant.Instance.SetPlant(newCol, plant.thePlantRow, plant.thePlantType);
                if (Board.Instance.boardTag.isColumn)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        CreatePlant.Instance.SetPlant(__instance.thePlantOnGlove.thePlantColumn, i, plant.thePlantType);
                    }
                }
                else
                {
                    if (gameObject != null && gameObject.TryGetComponent<Plant>(out var component) && component != null)
                    {
                        plant.Die(Plant.DieReason.ByMix);
                    }
                }
            }
        }
        return true;
    }

    [HarmonyPostfix]
    public static void Postfix(Mouse __instance)
    {
        if (ColumnGlove)
        {
            if (Board.Instance.boardTag.isColumn && aa != null)
            {
                CreatePlant.Instance.SetPlant(aa.thePlantColumn, aa.thePlantRow, aa.thePlantType);
            }
        }
    }
}

#region 取消红卡种植限制补丁

/// <summary>
/// 究极剑仙杨桃(AbyssSwordStar)补丁 - 取消红卡种植限制
/// 在Awake方法前临时修改GameStatus，在Start方法前临时修改BoardType为神秘模式(7)
/// </summary>
[HarmonyPatch(typeof(AbyssSwordStar))]
public static class AbyssSwordStarUnlockPatch
{
    [HarmonyPrefix]
    [HarmonyPatch("Awake")]
    public static void PreAwake(ref GameStatus __state)
    {
        __state = GameAPP.theGameStatus;
        if (UnlockRedCardPlants)
        {
            GameAPP.theGameStatus = (GameStatus)(-1);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch("Awake")]
    public static void PostAwake(ref GameStatus __state)
    {
        GameAPP.theGameStatus = __state;
    }

    [HarmonyPrefix]
    [HarmonyPatch("Start")]
    public static void PreStart(ref LevelType __state)
    {
        __state = GameAPP.theBoardType;
        if (UnlockRedCardPlants)
        {
            GameAPP.theBoardType = (LevelType)7; // 神秘模式
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch("Start")]
    public static void PostStart(ref LevelType __state)
    {
        GameAPP.theBoardType = __state;
    }
}

/// <summary>
/// 究极速射樱桃射手(UltimateMinigun)补丁 - 取消红卡种植限制
/// 在Start方法前临时修改BoardTag.isTreasure为true
/// </summary>
[HarmonyPatch(typeof(UltimateMinigun))]
public static class UltimateMinigunUnlockPatch
{
    [HarmonyPrefix]
    [HarmonyPatch("Start")]
    public static void PreStart(ref Board.BoardTag __state)
    {
        __state = Board.Instance.boardTag;
        if (UnlockRedCardPlants)
        {
            Board.BoardTag boardTag = Board.Instance.boardTag;
            boardTag.isTreasure = true;
            Board.Instance.boardTag = boardTag;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch("Start")]
    public static void PostStart(ref Board.BoardTag __state)
    {
        Board.Instance.boardTag = __state;
    }
}

/// <summary>
/// 究极炽阳向日葵(SolarSunflower)补丁 - 取消红卡种植限制
/// 在Start方法前临时修改BoardTag.isTreasure为true
/// </summary>
[HarmonyPatch(typeof(SolarSunflower))]
public static class SolarSunflowerUnlockPatch
{
    [HarmonyPrefix]
    [HarmonyPatch("Start")]
    public static void PreStart(ref Board.BoardTag __state)
    {
        __state = Board.Instance.boardTag;
        if (UnlockRedCardPlants)
        {
            Board.BoardTag boardTag = Board.Instance.boardTag;
            boardTag.isTreasure = true;
            Board.Instance.boardTag = boardTag;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch("Start")]
    public static void PostStart(ref Board.BoardTag __state)
    {
        Board.Instance.boardTag = __state;
    }
}

#endregion

#region 击杀升级补丁

/// <summary>
/// 击杀升级补丁 - Zombie.Die
/// 当僵尸死亡时，找到最后造成伤害的植物并累计击杀数
/// 升级到1级需要击杀20只，升级到2级需要击杀50只，升级到3级需要击杀100只
/// 每次升级完成后重新计数
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.Die))]
public static class ZombieDieKillUpgradePatch
{
    [HarmonyPostfix]
    public static void Postfix(Zombie __instance)
    {
        if (!KillUpgrade || __instance == null) return;

        try
        {
            int zombieId = __instance.GetInstanceID();

            // 检查是否有记录的伤害来源
            if (!ZombieLastDamageSource.TryGetValue(zombieId, out PlantType plantType))
                return;

            // 移除记录
            ZombieLastDamageSource.Remove(zombieId);

            if (plantType == PlantType.Nothing) return;

            // 查找该类型的植物
            var allPlants = Lawnf.GetAllPlants();
            if (allPlants == null) return;

            // 找到同行且距离最近的该类型植物
            Plant? targetPlant = null;
            float minDistance = float.MaxValue;
            int zombieRow = __instance.theZombieRow;
            float zombieX = __instance.transform.position.x;

            foreach (var plant in allPlants)
            {
                if (plant == null || plant.thePlantType != plantType) continue;
                if (plant.thePlantRow != zombieRow) continue;

                float distance = Mathf.Abs(plant.transform.position.x - zombieX);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    targetPlant = plant;
                }
            }

            // 如果同行没找到，找全场最近的
            if (targetPlant == null)
            {
                foreach (var plant in allPlants)
                {
                    if (plant == null || plant.thePlantType != plantType) continue;

                    float distance = Vector3.Distance(plant.transform.position, __instance.transform.position);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        targetPlant = plant;
                    }
                }
            }

            // 累计击杀数并检查是否可以升级
            if (targetPlant != null && targetPlant.theLevel < 3)
            {
                int plantId = targetPlant.GetInstanceID();

                // 获取或初始化击杀计数
                if (!PlantKillCount.TryGetValue(plantId, out int killCount))
                {
                    killCount = 0;
                }

                // 增加击杀计数
                killCount++;
                PlantKillCount[plantId] = killCount;

                // 检查是否达到升级所需击杀数
                int targetLevel = targetPlant.theLevel + 1;
                int requiredKills = GetKillsRequiredForLevel(targetLevel);

                if (killCount >= requiredKills)
                {
                    // 升级植物
                    targetPlant.Upgrade(targetLevel, true, false);
                    // 重置击杀计数
                    PlantKillCount[plantId] = 0;
                }
            }
        }
        catch { }
    }
}

#endregion

#region ZombieImmuneAllDebuffs - 僵尸免疫一切负面效果补丁

/// <summary>
/// 僵尸免疫魅惑补丁 - Zombie.SetMindControl
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.SetMindControl))]
public static class ZombieImmuneSetMindControlPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.High)]
    public static bool Prefix(Zombie __instance)
    {
        if (!ZombieImmuneAllDebuffs && !ZombieImmuneMindControl) return true;
        try
        {
            if (__instance == null) return true;
            // 阻止魅惑效果
            return false;
        }
        catch { return true; }
    }
}

/// <summary>
/// 僵尸免疫冻结补丁 - Zombie.SetFreeze
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.SetFreeze))]
public static class ZombieImmuneSetFreezePatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.High)]
    public static bool Prefix(Zombie __instance)
    {
        if (!ZombieImmuneAllDebuffs && !ZombieImmuneFreeze) return true;
        try
        {
            if (__instance == null) return true;
            // 阻止冻结效果
            return false;
        }
        catch { return true; }
    }
}

/// <summary>
/// 僵尸免疫减速补丁 - Zombie.SetCold
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.SetCold))]
public static class ZombieImmuneSetColdPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.High)]
    public static bool Prefix(Zombie __instance)
    {
        if (!ZombieImmuneAllDebuffs && !ZombieImmuneCold) return true;
        try
        {
            if (__instance == null) return true;
            // 阻止减速效果
            return false;
        }
        catch { return true; }
    }
}

/// <summary>
/// 僵尸免疫黄油定身补丁 - Zombie.Buttered
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.Buttered))]
public static class ZombieImmuneButteredPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.High)]
    public static bool Prefix(Zombie __instance)
    {
        if (!ZombieImmuneAllDebuffs && !ZombieImmuneButter) return true;
        try
        {
            if (__instance == null) return true;
            // 阻止黄油定身效果
            return false;
        }
        catch { return true; }
    }
}

/// <summary>
/// 僵尸免疫中毒补丁 - Zombie.SetPoison
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.SetPoison))]
public static class ZombieImmuneSetPoisonPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.High)]
    public static bool Prefix(Zombie __instance)
    {
        if (!ZombieImmuneAllDebuffs && !ZombieImmunePoison) return true;
        try
        {
            if (__instance == null) return true;
            // 阻止中毒效果
            return false;
        }
        catch { return true; }
    }
}

/// <summary>
/// 僵尸免疫中毒等级增加补丁 - Zombie.AddPoisonLevel
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.AddPoisonLevel))]
public static class ZombieImmuneAddPoisonLevelPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.High)]
    public static bool Prefix(Zombie __instance)
    {
        if (!ZombieImmuneAllDebuffs && !ZombieImmunePoison) return true;
        try
        {
            if (__instance == null) return true;
            // 阻止中毒等级增加
            return false;
        }
        catch { return true; }
    }
}

/// <summary>
/// 僵尸免疫吃大蒜补丁 - Zombie.EatGarlic
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.EatGarlic))]
public static class ZombieImmuneEatGarlicPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.High)]
    public static bool Prefix(Zombie __instance)
    {
        if (!ZombieImmuneAllDebuffs && !ZombieImmunePoison) return true;
        try
        {
            if (__instance == null) return true;
            // 阻止吃大蒜效果（蒜毒）
            return false;
        }
        catch { return true; }
    }
}

/// <summary>
/// 僵尸免疫大蒜影响补丁 - Zombie.Garliced
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.Garliced))]
public static class ZombieImmuneGarlicedPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.High)]
    public static bool Prefix(Zombie __instance)
    {
        if (!ZombieImmuneAllDebuffs && !ZombieImmunePoison) return true;
        try
        {
            if (__instance == null) return true;
            // 阻止大蒜影响（换行）
            return false;
        }
        catch { return true; }
    }
}

/// <summary>
/// 僵尸免疫击退补丁 - Zombie.KnockBack
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.KnockBack))]
public static class ZombieImmuneKnockBackPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.High)]
    public static bool Prefix(Zombie __instance)
    {
        if (!ZombieImmuneAllDebuffs && !ZombieImmuneKnockback) return true;
        try
        {
            if (__instance == null) return true;
            // 阻止击退效果
            return false;
        }
        catch { return true; }
    }
}

/// <summary>
/// 僵尸免疫红温补丁 - Zombie.SetJalaed
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.SetJalaed))]
public static class ZombieImmuneSetJalaedPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.High)]
    public static bool Prefix(Zombie __instance)
    {
        if (!ZombieImmuneAllDebuffs && !ZombieImmuneJalaed) return true;
        try
        {
            if (__instance == null) return true;
            // 阻止红温效果
            return false;
        }
        catch { return true; }
    }
}

/// <summary>
/// 僵尸免疫余烬补丁 - Zombie.SetEmbered
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.SetEmbered))]
public static class ZombieImmuneSetEmberedPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.High)]
    public static bool Prefix(Zombie __instance, bool ulti = false)
    {
        if (!ZombieImmuneAllDebuffs && !ZombieImmuneEmbered) return true;
        try
        {
            // 严格的对象有效性检查
            if (__instance == null) return true;
            
            // 安全地检查对象有效性
            try
            {
                var _ = __instance.theHealth;
            }
            catch
            {
                return true; // 对象可能已销毁
            }
            
            try
            {
                if (__instance.theHealth <= 0) return true;
            }
            catch
            {
                return true; // 对象可能已销毁
            }
            
            // 阻止余烬效果
            return false;
        }
        catch { return true; }
    }
}

/// <summary>
/// 僵尸免疫余烬：拦截新余烬子弹（Bullet_doom_ulti）单体结算
/// 原逻辑：ActionOnZombie 中会 SetEmbered + 按寒冷/红温/中毒追加伤害
/// 这里改为仅造成一次基础伤害，彻底跳过附加效果。
/// </summary>
[HarmonyPatch(typeof(Bullet_doom_ulti), "ActionOnZombie")]
public static class ZombieImmuneEmberedBulletDoomUltiActionPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(Bullet_doom_ulti __instance, Zombie zombie)
    {
        if (!ZombieImmuneAllDebuffs && !ZombieImmuneEmbered) return true;
        try
        {
            if (__instance == null || zombie == null) return true;

            int damage = __instance._damage;
            if (damage <= 0) damage = 1;
            PlantType fromType = __instance.fromType;

            zombie.TakeDamage(DmgType.Normal, damage, fromType, false);
            return false; // 阻止原方法，避免余烬/状态附加
        }
        catch { return true; }
    }
}

/// <summary>
/// 僵尸免疫余烬：拦截新余烬子弹（Bullet_doom_ulti）范围结算（theStatus==6）
/// 改为朴素范围伤害，不触发余烬/寒冷×4/红温爆炸/毒伤追加。
/// </summary>
[HarmonyPatch(typeof(Bullet_doom_ulti), "AttackZombies")]
public static class ZombieImmuneEmberedBulletDoomUltiAttackPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(Bullet_doom_ulti __instance)
    {
        if (!ZombieImmuneAllDebuffs && !ZombieImmuneEmbered) return true;
        try
        {
            if (__instance == null) return true;
            var board = Board.Instance;
            if (board == null) return true;

            int damage = __instance._damage;
            if (damage <= 0) damage = 1;
            PlantType fromType = __instance.fromType;
            var pos = __instance.transform.position;
            const float range = 4f;

            foreach (var z in board.zombieArray)
            {
                if (z == null) continue;
                if (!z.gameObject.activeInHierarchy) continue;

                var zp = z.transform.position;
                if (UnityEngine.Vector2.Distance(new UnityEngine.Vector2(pos.x, pos.y),
                                                 new UnityEngine.Vector2(zp.x, zp.y)) > range)
                    continue;

                z.TakeDamage(DmgType.Normal, damage, fromType, false);
            }

            return false; // 阻止原方法
        }
        catch { return true; }
    }
}

/// <summary>
/// 僵尸免疫吞噬补丁 - Chomper.Chomp
/// 这是实际执行吞噬僵尸的方法
/// </summary>
[HarmonyPatch(typeof(Chomper), nameof(Chomper.Chomp))]
public static class ZombieImmuneChomperChompPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Chomper __instance, Zombie zombie)
    {
        if (!ZombieImmuneAllDebuffs && !ZombieImmuneDevour) return true;
        try
        {
            if (__instance == null || zombie == null) return true;
            // 阻止吞噬效果
            return false;
        }
        catch { return true; }
    }
}

#endregion

public class PatchMgr : MonoBehaviour
{
    public static Board board = new();
    internal static bool originalTravel;
    private static int garlicDayTime;
    private static int seaTime;

    static PatchMgr()
    {
        foreach (var f in Enum.GetValues<Zombie.FirstArmorType>()) Health1st.Add(f, -1);
        foreach (var s in Enum.GetValues<Zombie.SecondArmorType>()) Health2nd.Add(s, -1);
    }

    //public static PlantDataLoader.PlantData_ PlantData => PlantDataLoader.plantDatas;
    public PatchMgr() : base(ClassInjector.DerivedConstructorPointer<PatchMgr>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    public PatchMgr(IntPtr i) : base(i)
    {
    }

    public static bool[] AdvBuffs { get; set; } = [];
    public static bool AlmanacCreate { get; set; } = false;
    public static int AlmanacSeedType { get; set; } = -1;
    public static ZombieType AlmanacZombieType { get; set; } = ZombieType.Nothing;
    public static bool BuffRefreshNoLimit { get; set; } = false;
    /// <summary>无限刷新 - 旅行词条/诸神进化无限刷新</summary>
    public static bool UnlimitedRefresh { get; set; } = false;
    /// <summary>无限积分 - 水果忍者无限积分</summary>
    public static bool UnlimitedScore { get; set; } = false;
    public static Dictionary<BulletType, int> BulletDamage { get; set; } = [];
    public static bool CardNoInit { get; set; } = false;
    public static bool ChomperNoCD { get; set; } = false;
    public static bool SuperStarNoCD { get; set; } = false;
    public static bool AutoCutFruit { get; set; } = false;
    public static bool RandomCard { get; set; } = false;
    public static bool ColumnGlove { get; set; } = false;
    public static bool CobCannonNoCD { get; set; } = false;
    public static List<int> ConveyBeltTypes { get; set; } = [];
    public static bool[] Debuffs { get; set; } = [];
    public static bool DevLour { get; set; } = false;
    public static bool FastShooting { get; set; } = false;
    public static bool FreeCD { get; set; } = false;
    public static bool FreePlanting { get; set; } = false;
    public static GameModes GameModes { get; set; }
    public static bool GarlicDay { get; set; } = false;
    public static double GloveFullCD { get; set; } = 0;
    public static bool GloveNoCD { get; set; } = false;
    public static double HammerFullCD { get; set; } = 0;
    public static bool HammerNoCD { get; set; } = false;
    public static bool HardPlant { get; set; } = false;
    public static bool ImmuneForceDeduct { get; set; } = false;
    public static bool CurseImmunity { get; set; } = false;
    public static bool CrushImmunity { get; set; } = false;
    public static bool TrampleImmunity { get; set; } = false;
    public static Dictionary<int, int> PlantHealthCache { get; set; } = [];
    public static Dictionary<Zombie.FirstArmorType, int> Health1st { get; set; } = [];
    public static Dictionary<Zombie.SecondArmorType, int> Health2nd { get; set; } = [];
    public static Dictionary<PlantType, int> HealthPlants { get; set; } = [];
    public static Dictionary<ZombieType, int> HealthZombies { get; set; } = [];
    public static bool HyponoEmperorNoCD { get; set; } = false;
    public static int ImpToBeThrown { get; set; } = 37;
    public static bool[] InGameAdvBuffs { get; set; } = [];
    public static bool[] InGameDebuffs { get; set; } = [];
    public static bool[] InGameUltiBuffs { get; set; } = [];
    
    /// <summary>
    /// 旗帜波词条功能 - 是否启用
    /// </summary>
    public static bool FlagWaveBuffEnabled { get; set; } = false;
    
    /// <summary>
    /// 旗帜波词条功能 - 要应用的词条ID列表（每个子列表代表一个旗帜波的词条）
    /// </summary>
    public static List<int> FlagWaveBuffIds { get; set; } = new List<int>();
    
    /// <summary>
    /// 旗帜波自定义字幕列表（10个旗帜波的自定义字幕）
    /// </summary>
    public static List<string> FlagWaveCustomTexts { get; set; } = new List<string>();
    
    /// <summary>
    /// 旗帜波词条功能 - 上一次检测到的旗帜波状态（用于检测状态变化）
    /// </summary>
    public static bool _lastHugeWaveState = false;
    
    /// <summary>
    /// 旗帜波词条功能 - 手动设置旗帜波状态时同步更新此标志（防止快速点击时重复触发）
    /// </summary>
    public static void SetHugeWaveState(bool isHugeWave)
    {
        _lastHugeWaveState = isHugeWave;
    }

    /// <summary>
    /// 旗帜波词条功能 - 当前已解锁到第几个（每次旗帜波 +1）
    /// </summary>
    public static int _flagWaveUnlockIndex = 0;
    
    /// <summary>
    /// 旗帜波词条功能 - 上一次解锁时的波数（用于防重复解锁）
    /// </summary>
    public static int _lastUnlockWave = -1;
    
    /// <summary>
    /// 旗帜波词条功能 - 当前已解锁的旗帜波索引（用于获取对应的自定义字幕）
    /// </summary>
    public static int _currentFlagWaveIndex = 0;
    
    /// <summary>
    /// Buff类型枚举
    /// </summary>
    public enum BuffType
    {
        Advanced = 0,  // 高级词条: 0-999
        Ultimate = 1,  // 究极词条: 1000-1999
        Debuff = 2     // 负面词条: 2000-2999
    }
    
    /// <summary>
    /// 编码Buff ID：将类型和原始ID编码为统一ID
    /// Advanced: 0-999, Ultimate: 1000-1999, Debuff: 2000-2999
    /// </summary>
    public static int EncodeBuffId(BuffType type, int originalId)
    {
        return type switch
        {
            BuffType.Advanced => originalId,                    // 0-999
            BuffType.Ultimate => 1000 + originalId,           // 1000-1999
            BuffType.Debuff => 2000 + originalId,             // 2000-2999
            _ => originalId
        };
    }
    
    /// <summary>
    /// 解码Buff ID：从编码ID中提取类型和原始ID
    /// </summary>
    public static (BuffType type, int originalId) DecodeBuffId(int encodedId)
    {
        if (encodedId >= 2000)
            return (BuffType.Debuff, encodedId - 2000);
        if (encodedId >= 1000)
            return (BuffType.Ultimate, encodedId - 1000);
        return (BuffType.Advanced, encodedId);
    }
    public static bool ItemExistForever { get; set; } = false;
    public static int JachsonSummonType { get; set; } = 7;
    public static bool JackboxNotExplode { get; set; } = false;
    public static int LockBulletType { get; set; } = -2;
    public static bool LockMoney { get; set; } = false;
    public static int LockMoneyCount { get; set; } = 3000;
    public static int LockPresent { get; set; } = -1;
    public static int LockWheat { get; set; } = -1;
    public static int LockPresent1 { get; set; } = -1;
    public static int LockPresent2 { get; set; } = -1;
    public static int LockPresent3 { get; set; } = -1;
    public static int LockPresent4 { get; set; } = -1;
    public static int LockPresent5 { get; set; } = -1;
    public static bool LockSun { get; set; } = false;
    public static int LockSunCount { get; set; } = 500;
    public static bool MineNoCD { get; set; } = false;
    public static ManualLogSource MLogger => Core.Instance.Value.LoggerInstance;
    public static float NewZombieUpdateCD { get; set; } = 30;
    public static bool NoHole { get; set; } = false;
    public static bool NoIceRoad { get; set; } = false;
    public static bool PlantUpgrade { get; set; } = false;
    public static bool PvPPotRange { get; set; } = false;
    public static bool PresentFastOpen { get; set; } = false;
    public static List<int> SeaTypes { get; set; } = [];

    public static GameObject? SeedGroup
    {
        get
        {
            try
            {
                return InGame() ? GameObject.Find("SeedGroup") : null;
            }
            catch
            {
                return null;
            }
        }
    }

    public static bool ShowGameInfo { get; set; }
    public static bool StopSummon { get; set; } = false;
    public static bool SuperPresent { get; set; } = false;
    public static float SyncSpeed { get; set; } = -1;
    private static float _lastGameSpeed = -1; // 记录上次游戏内部速度，用于检测变化
    public static bool IsSpeedModifiedByTool { get; set; } = false; // 标记修改器是否主动设置了速度
    public static bool GameSpeedEnabled { get; set; } = false; // 游戏速度功能开关，默认关闭
    public static bool TimeSlow { get; set; }
    public static bool TimeStop { get; set; }
    public static bool[] UltiBuffs { get; set; } = [];
    public static bool UltimateRamdomZombie { get; set; } = false;
    public static bool UltimateSuperGatling { get; set; } = false;
    public static bool UndeadBullet { get; set; } = false;
    public static bool UnlockAllFusions { get; set; } = false;
    public static bool ZombieSea { get; set; } = false;
    public static int ZombieSeaCD { get; set; } = 40;
    public static bool ZombieSeaLow { get; set; } = false;
    public static bool DisableIceEffect { get; set; } = false;
    public static bool PotSmashingFix { get; set; } = false;
    public static bool UnlimitedSunlight { get; set; } = false;
    public static bool MagnetNutUnlimited { get; set; } = false;
    public static bool ZombieDamageLimit200 { get; set; } = false;
    public static int ZombieDamageLimitValue { get; set; } = 100;
    public static bool ZombieSpeedModifyEnabled { get; set; } = false;
    public static float ZombieSpeedMultiplier { get; set; } = 1.0f;
    public static bool ZombieAttackMultiplierEnabled { get; set; } = false;
    public static float ZombieAttackMultiplier { get; set; } = 1.0f;
    public static bool PickaxeImmunity { get; set; } = false;
    public static bool ZombieBulletReflectEnabled { get; set; } = false;
    public static float ZombieBulletReflectChance { get; set; } = 10.0f;
    public static bool UnlimitedCardSlots { get; set; } = false;
    /// <summary>
    /// 僵尸状态并存 - 允许红温与寒冰、蒜毒状态同时存在
    /// </summary>
    public static bool ZombieStatusCoexist { get; set; } = false;
    
    /// <summary>
    /// 僵尸状态并存数据缓存 - 用于在Update中维护状态
    /// </summary>
    public static Dictionary<int, (bool hadCold, float coldTimer, float freezeTimer, int freezeLevel)> ZombieStatusCoexistData = new Dictionary<int, (bool, float, float, int)>();

    /// <summary>
    /// 鱼丸词条 - 坚不可摧(伤害最多200) + 高级后勤(双倍恢复, 阳光磁力菇CD减少)
    /// </summary>
    public static bool MNEntryEnabled { get; set; } = false;
    
    /// <summary>
    /// 取消红卡种植限制 - 允许在非神秘模式种植红卡植物(AbyssSwordStar, UltimateMinigun, SolarSunflower)
    /// </summary>
    public static bool UnlockRedCardPlants { get; set; } = false;

    /// <summary>
    /// 击杀升级 - 植物击杀僵尸时自动升级
    /// </summary>
    public static bool KillUpgrade { get; set; } = false;

    /// <summary>
    /// 僵尸免疫一切负面效果 - 免疫负面buff、击退、吞噬、魅惑等（已弃用，保留兼容）
    /// </summary>
    public static bool ZombieImmuneAllDebuffs { get; set; } = false;

    // 僵尸免疫效果 - 分开的9个开关
    /// <summary>僵尸免疫冻结</summary>
    public static bool ZombieImmuneFreeze { get; set; } = false;
    /// <summary>僵尸免疫减速</summary>
    public static bool ZombieImmuneCold { get; set; } = false;
    /// <summary>僵尸免疫黄油定身</summary>
    public static bool ZombieImmuneButter { get; set; } = false;
    /// <summary>僵尸免疫蒜毒</summary>
    public static bool ZombieImmunePoison { get; set; } = false;
    /// <summary>僵尸免疫红温</summary>
    public static bool ZombieImmuneJalaed { get; set; } = false;
    /// <summary>僵尸免疫余烬</summary>
    public static bool ZombieImmuneEmbered { get; set; } = false;
    /// <summary>僵尸免疫击退</summary>
    public static bool ZombieImmuneKnockback { get; set; } = false;
    /// <summary>僵尸免疫魅惑</summary>
    public static bool ZombieImmuneMindControl { get; set; } = false;
    /// <summary>僵尸免疫吞噬</summary>
    public static bool ZombieImmuneDevour { get; set; } = false;

    /// <summary>
    /// 随机子弹 - 植物发射的子弹类型随机
    /// </summary>
    public static bool RandomBullet { get; set; } = false;

    /// <summary>
    /// 星辉buff - 点击植物解锁星辉buff模式（如果该植物有星辉buff功能）
    /// </summary>
    public static bool StarUpBuff { get; set; } = false;

    /// <summary>
    /// 给植物上星辉buff的辅助方法
    /// </summary>
    /// <param name="plant">目标植物</param>
    /// <returns>是否成功上星辉</returns>
    private static bool ApplyStarUpBuff(Plant plant)
    {
        if (plant == null)
        {
            MLogger?.LogWarning("[PVZRHTools] 植物为 null，无法上星辉buff");
            return false;
        }

        try
        {
            // 先调用 StarUp()，然后设置属性，最后更新图标
            var starUpMethod = typeof(Plant).GetMethod("StarUp", 
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            
            // 尝试作为属性访问
            var starUpProperty = typeof(Plant).GetProperty("starUp", 
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            
            // 如果属性不存在，尝试作为字段访问
            var starUpField = typeof(Plant).GetField("starUp", 
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            
            var updateStarIconMethod = typeof(Plant).GetMethod("UpdateStarIcon", 
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            // 步骤1：调用 StarUp 方法（如果存在）
            if (starUpMethod != null)
            {
                try
                {
                    starUpMethod.Invoke(plant, null);
                }
                catch (Exception ex)
                {
                    MLogger?.LogWarning($"[PVZRHTools] 调用 StarUp() 方法时出错: {ex.Message}");
                }
            }

            // 步骤2：设置 starUp 属性或字段为 true
            bool setSuccess = false;
            if (starUpProperty != null)
            {
                try
                {
                    starUpProperty.SetValue(plant, true);
                    setSuccess = true;
                    MLogger?.LogInfo("[PVZRHTools] 通过属性设置 starUp = true");
                }
                catch (Exception ex)
                {
                    MLogger?.LogWarning($"[PVZRHTools] 通过属性设置 starUp 时出错: {ex.Message}");
                }
            }
            else if (starUpField != null)
            {
                try
                {
                    starUpField.SetValue(plant, true);
                    setSuccess = true;
                    MLogger?.LogInfo("[PVZRHTools] 通过字段设置 starUp = true");
                }
                catch (Exception ex)
                {
                    MLogger?.LogWarning($"[PVZRHTools] 通过字段设置 starUp 时出错: {ex.Message}");
                }
            }
            else
            {
                MLogger?.LogError("[PVZRHTools] 无法找到 Plant.starUp 属性或字段");
                return false;
            }

            if (!setSuccess)
            {
                MLogger?.LogError("[PVZRHTools] 设置 starUp 失败");
                return false;
            }
            
            // 步骤3：调用 UpdateStarIcon 更新UI显示
            if (updateStarIconMethod != null)
            {
                try
                {
                    updateStarIconMethod.Invoke(plant, null);
                }
                catch (Exception ex)
                {
                    MLogger?.LogWarning($"[PVZRHTools] 调用 UpdateStarIcon() 方法时出错: {ex.Message}");
                }
            }
            
            // 验证是否设置成功
            bool starUpValue = false;
            if (starUpProperty != null)
            {
                starUpValue = (bool)(starUpProperty.GetValue(plant) ?? false);
            }
            else if (starUpField != null)
            {
                starUpValue = (bool)(starUpField.GetValue(plant) ?? false);
            }
            
            if (starUpValue)
            {
                MLogger?.LogInfo($"[PVZRHTools] 成功给植物 {plant.thePlantType} 上星辉buff（starUp={starUpValue}）");
                return true;
            }
            else
            {
                MLogger?.LogWarning($"[PVZRHTools] 设置 starUp 后验证失败，值仍为 false");
                return false;
            }
        }
        catch (Exception ex)
        {
            MLogger?.LogError($"[PVZRHTools] 给植物上星辉buff时发生错误: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// 随机升级模式 - 点击植物操控(WASD移动)
    /// </summary>
    public static bool RandomUpgradeMode { get; set; } = false;

    /// <summary>
    /// 记录僵尸最后受到伤害的植物类型，用于击杀升级功能
    /// </summary>
    public static Dictionary<int, PlantType> ZombieLastDamageSource { get; set; } = new Dictionary<int, PlantType>();

    /// <summary>
    /// 记录每个植物的击杀计数，用于击杀升级功能
    /// Key: 植物实例ID, Value: 击杀数
    /// </summary>
    public static Dictionary<int, int> PlantKillCount { get; set; } = new Dictionary<int, int>();

    /// <summary>
    /// 获取升级到指定等级所需的击杀数
    /// </summary>
    public static int GetKillsRequiredForLevel(int targetLevel)
    {
        return targetLevel switch
        {
            1 => 20,   // 升级到1级需要击杀20只
            2 => 50,   // 升级到2级需要击杀50只
            3 => 100,  // 升级到3级需要击杀100只
            _ => int.MaxValue
        };
    }

    public void Update()
    {
        try
        {
            board = GameAPP.board.GetComponent<Board>();
        }
        catch (Exception)
        {
        }
        if (GameAPP.theGameStatus is GameStatus.InGame or GameStatus.InInterlude or GameStatus.Selecting)
        {
            // 只有在游戏速度功能开启时才允许时停/慢速操作
            if (GameSpeedEnabled)
        {
            if (Input.GetKeyDown(Core.KeyTimeStop.Value.Value))
            {
                TimeStop = !TimeStop;
                TimeSlow = false;
            }

            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                TimeStop = false;
                TimeSlow = !TimeSlow;
                }
            }
            else
            {
                // 功能关闭时，清除时停/慢速状态，让游戏内部速度调整功能正常工作
                if (TimeStop || TimeSlow)
                {
                    TimeStop = false;
                    TimeSlow = false;
                }
            }

            if (Input.GetKeyDown(Core.KeyShowGameInfo.Value.Value)) ShowGameInfo = !ShowGameInfo;
            
            // 检测游戏内部速度变化（GameAPP.gameSpeed）
            // 只有在功能关闭时才检测，避免干扰游戏内部速度调整
            if (!GameSpeedEnabled)
            {
                try
                {
                    float currentGameSpeed = GameAPP.gameSpeed;
                    if (_lastGameSpeed >= 0 && Mathf.Abs(currentGameSpeed - _lastGameSpeed) > 0.01f)
                    {
                        // 游戏内部速度改变了，且功能关闭，让游戏内部的速度生效
                        SyncSpeed = -1; // 重置为未设置状态
                        IsSpeedModifiedByTool = false; // 清除修改标记
                    }
                    _lastGameSpeed = currentGameSpeed;
                }
                catch { }
            }
            else
            {
                // 功能开启时，更新记录的游戏内部速度，但不自动应用
                try
                {
                    _lastGameSpeed = GameAPP.gameSpeed;
                }
                catch { }
            }
            
            // 应用速度设置：只有在功能开启时才修改 Time.timeScale
            if (GameSpeedEnabled)
            {
                // 功能开启时，应用速度设置
                if (!TimeStop && !TimeSlow)
                {
                    if (SyncSpeed >= 0 && IsSpeedModifiedByTool)
                    {
                        // 修改器主动设置了速度，应用修改器的速度
                        Time.timeScale = SyncSpeed;
                    }
                    else
                    {
                        // 如果修改器没有设置速度，恢复为游戏内部速度
                        Time.timeScale = GameAPP.gameSpeed;
                    }
                }
                else if (!TimeStop && TimeSlow)
                {
                    Time.timeScale = 0.2f;
                }
                else if (InGameBtnPatch.BottomEnabled || (TimeStop && !TimeSlow))
                {
                    Time.timeScale = 0;
                }
            }
            // 功能关闭时，不修改 Time.timeScale，让游戏内部的速度调整功能正常工作

            // SlowTrigger UI更新 - 独立try块，不影响其他功能
            try
            {
                var slow = GameObject.Find("SlowTrigger")?.transform;
                if (slow != null)
                {
                    slow.GetChild(0).gameObject.GetComponent<TextMeshProUGUI>().text = $"时停(x{Time.timeScale})";
                    slow.GetChild(1).gameObject.GetComponent<TextMeshProUGUI>().text = $"时停(x{Time.timeScale})";
                }
            }
            catch { }

            // 卡组置顶切换
            try
            {
                if (Input.GetKeyDown(Core.KeyTopMostCardBank.Value.Value))
                {
                    if (GameAPP.canvas.GetComponent<Canvas>().sortingLayerName == "Default")
                        GameAPP.canvas.GetComponent<Canvas>().sortingLayerName = "UI";
                    else
                        GameAPP.canvas.GetComponent<Canvas>().sortingLayerName = "Default";
                }
            }
            catch { }

            // 植物升级功能 - 右键点击场上植物升级
            try
            {
                if (PlantUpgrade && Board.Instance != null && Mouse.Instance != null)
                {
                    // 检测鼠标右键点击
                    if (Input.GetMouseButtonDown(1))
                    {
                        // 获取鼠标所在格子的植物
                        int column = Mouse.Instance.theMouseColumn;
                        int row = Mouse.Instance.theMouseRow;
                        
                        // 使用 Lawnf.Get1x1Plants 获取该格子的所有植物
                        var plants = Lawnf.Get1x1Plants(column, row);
                        if (plants != null && plants.Count > 0)
                        {
                            // 遍历该格子的植物，找到可以升级的植物
                            foreach (var plant in plants)
                            {
                                if (plant != null && plant.theLevel < 3)
                                {
                                    // 升级植物
                                    plant.Upgrade(plant.theLevel + 1, true, false);
                                    break; // 只升级一个植物
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // 随机升级模式 - 点击植物操控，R键切换僵尸显血
            try
            {
                if (RandomUpgradeMode && Board.Instance != null && Mouse.Instance != null)
                {
                    // 左键点击植物来操控，再次点击同一植物则停止操控
                    if (Input.GetMouseButtonDown(0))
                    {
                        int column = Mouse.Instance.theMouseColumn;
                        int row = Mouse.Instance.theMouseRow;
                        
                        // 先检查是否点击了当前操控的植物（根据植物当前位置）
                        var controled = Board.Instance.controledPlant;
                        if (controled != null && controled.thePlantColumn == column && controled.thePlantRow == row)
                        {
                            // 点击当前操控的植物，停止操控
                            Board.Instance.controledPlant = null;
                        }
                        else
                        {
                            // 检查点击位置是否有其他植物
                            var plants = Lawnf.Get1x1Plants(column, row);
                            if (plants != null && plants.Count > 0)
                            {
                                var plant = plants[0];
                                if (plant != null)
                                {
                                    // 设置为操控植物
                                    Board.Instance.controledPlant = plant;
                                }
                            }
                        }
                    }
                    
                    // 方向键移动操控的植物（使用游戏内置方法）
                    if (Board.Instance.controledPlant != null)
                    {
                        // 使用游戏内置的 MoveControlPlant 方法
                        // index: 0=上, 1=左, 2=下, 3=右
                        if (Input.GetKeyDown(KeyCode.UpArrow))
                        {
                            Board.Instance.MoveControlPlant(0);
                        }
                        if (Input.GetKeyDown(KeyCode.DownArrow))
                        {
                            Board.Instance.MoveControlPlant(2);
                        }
                        if (Input.GetKeyDown(KeyCode.LeftArrow))
                        {
                            Board.Instance.MoveControlPlant(1);
                        }
                        if (Input.GetKeyDown(KeyCode.RightArrow))
                        {
                            Board.Instance.MoveControlPlant(3);
                        }
                    }
                }
            }
            catch { }

            // 星辉buff功能 - 点击植物解锁星辉buff模式（如果该植物有星辉buff功能）
            try
            {
                if (StarUpBuff && Board.Instance != null && Mouse.Instance != null)
                {
                    // 左键点击植物来应用星辉buff
                    if (Input.GetMouseButtonDown(0))
                    {
                        int column = Mouse.Instance.theMouseColumn;
                        int row = Mouse.Instance.theMouseRow;
                        
                        // 检查点击位置是否有植物
                        var plants = Lawnf.Get1x1Plants(column, row);
                        if (plants != null && plants.Count > 0)
                        {
                            var plant = plants[0];
                            if (plant != null && !plant.isCrashed && plant.thePlantHealth > 0)
                            {
                                // 使用辅助方法给植物上星辉buff
                                ApplyStarUpBuff(plant);
                            }
                        }
                    }
                }
            }
            catch { }

            // 图鉴放置功能 - 独立try块，确保在任何关卡都能正常工作
            try
            {
                if (Board.Instance != null && Mouse.Instance != null)
                {
                    // 放置植物
                    if (Input.GetKeyDown(Core.KeyAlmanacCreatePlant.Value.Value) && AlmanacSeedType != -1)
                    {
                        if (CreatePlant.Instance != null)
                            CreatePlant.Instance.SetPlant(Mouse.Instance.theMouseColumn, Mouse.Instance.theMouseRow,
                                (PlantType)AlmanacSeedType);
                    }

                    // 切换魅惑僵尸模式
                    if (Input.GetKeyDown(Core.KeyAlmanacZombieMindCtrl.Value.Value))
                        Core.AlmanacZombieMindCtrl.Value.Value = !Core.AlmanacZombieMindCtrl.Value.Value;

                    // 放置僵尸
                    if (Input.GetKeyDown(Core.KeyAlmanacCreateZombie.Value.Value) &&
                        AlmanacZombieType is not ZombieType.Nothing)
                    {
                        if (CreateZombie.Instance != null)
                        {
                            if (Core.AlmanacZombieMindCtrl.Value.Value)
                                CreateZombie.Instance.SetZombieWithMindControl(Mouse.Instance.theMouseRow, AlmanacZombieType,
                                    Mouse.Instance.mouseX);
                            else
                                CreateZombie.Instance.SetZombie(Mouse.Instance.theMouseRow, AlmanacZombieType,
                                    Mouse.Instance.mouseX);
                        }
                    }

                    // 植物罐子 - 使用 ScaryPot_plant 类型
                    if (Input.GetKeyDown(Core.KeyAlmanacCreatePlantVase.Value.Value) && AlmanacSeedType != -1)
                    {
                        var gridItem = GridItem.SetGridItem(Mouse.Instance.theMouseColumn, Mouse.Instance.theMouseRow,
                            GridItemType.ScaryPot_plant);
                        if (gridItem != null)
                        {
                            var scaryPot = gridItem.GetComponent<ScaryPot>();
                            if (scaryPot != null)
                            {
                                scaryPot.thePlantType = (PlantType)AlmanacSeedType;
                            }
                        }
                    }

                    // 僵尸罐子 - 使用 ScaryPot_zombie 类型
                    if (Input.GetKeyDown(Core.KeyAlmanacCreateZombieVase.Value.Value) &&
                        AlmanacZombieType is not ZombieType.Nothing)
                    {
                        var gridItem = GridItem.SetGridItem(Mouse.Instance.theMouseColumn, Mouse.Instance.theMouseRow,
                            GridItemType.ScaryPot_zombie);
                        if (gridItem != null)
                        {
                            var scaryPot = gridItem.GetComponent<ScaryPot>();
                            if (scaryPot != null)
                            {
                                scaryPot.theZombieType = AlmanacZombieType;
                            }
                        }
                    }
                }
            }
            catch { }

            // 随机卡片切换
            try
            {
                if (Input.GetKeyDown(Core.KeyRandomCard.Value.Value))
                    RandomCard = !RandomCard;
            }
            catch { }

            // 解锁融合植物
            try
            {
                if (Board.Instance != null)
                {
                    var t = Board.Instance.boardTag;
                    t.enableTravelPlant = t.enableTravelPlant || UnlockAllFusions;
                    Board.Instance.boardTag = t;
                }
            }
            catch { }
        }

        if (!InGame()) return;
        if (LockSun) Board.Instance!.theSun = LockSunCount;
        if (LockMoney) Board.Instance!.theMoney = LockMoneyCount;
        if (StopSummon) Board.Instance!.iceDoomFreezeTime = 1;
        if (ZombieSea)
            if (++seaTime >= ZombieSeaCD &&
                Board.Instance!.theWave is not 0 && Board.Instance!.theWave < Board.Instance!.theMaxWave &&
                GameAPP.theGameStatus == (int)GameStatus.InGame)
            {
                foreach (var j in SeaTypes)
                {
                    if (j < 0) continue;
                    for (var i = 0; i < Board.Instance!.rowNum; i++) CreateZombie.Instance!.SetZombie(i, (ZombieType)j, 11f);
                }

                seaTime = 0;
            }

        if (GarlicDay && ++garlicDayTime >= 500 && GameAPP.theGameStatus == (int)GameStatus.InGame)
        {
            garlicDayTime = 0;
            _ = FindObjectsOfTypeAll(Il2CppType.Of<Zombie>()).All(b =>
            {
                var zombie = b?.TryCast<Zombie>();
                if (zombie != null)
                {
                    var coroutine = zombie.DeLayGarliced(0.1f, false, false);
                    if (coroutine != null) zombie.StartCoroutine_Auto(coroutine);
                }
                return true;
            });
        }
        
        if (SuperStarNoCD)
        {
            if (board.bigStarActiveCountDown > 0.5f)
            {
                board.bigStarActiveCountDown = 0.5f;
            }
        }
        
        // 土豆雷无CD - 使用 FindObjectsOfType 替代 Harmony patch 避免栈溢出
        if (MineNoCD)
        {
            try
            {
                var mines = FindObjectsOfType<PotatoMine>();
                foreach (var mine in mines)
                {
                    if (mine != null && mine.attributeCountdown > 0.05f)
                        mine.attributeCountdown = 0.05f;
                }
            }
            catch { }
        }
        
        // 大嘴花无CD - 使用 FindObjectsOfType 替代 Harmony patch 避免栈溢出
        if (ChomperNoCD)
        {
            try
            {
                var chompers = FindObjectsOfType<Chomper>();
                foreach (var chomper in chompers)
                {
                    if (chomper != null && chomper.attributeCountdown > 0.05f)
                        chomper.attributeCountdown = 0.05f;
                }
            }
            catch { }
        }

        // 免疫强制扣血 - 通过缓存植物血量并在异常扣血时恢复来实现
        if (ImmuneForceDeduct)
        {
            try
            {
                var allPlants = Lawnf.GetAllPlants();
                if (allPlants != null)
                {
                    // 收集当前存活植物的ID
                    var alivePlantIds = new HashSet<int>();
                    foreach (var p in allPlants)
                    {
                        if (p != null)
                            alivePlantIds.Add(p.GetInstanceID());
                    }

                    // 清理已死亡植物的缓存
                    var deadPlantIds = PlantHealthCache.Keys.Where(id => !alivePlantIds.Contains(id)).ToList();
                    foreach (var id in deadPlantIds)
                        PlantHealthCache.Remove(id);

                    foreach (var plant in allPlants)
                    {
                        if (plant == null) continue;
                        var plantId = plant.GetInstanceID();

                        if (PlantHealthCache.TryGetValue(plantId, out var cachedHealth))
                        {
                            // 检测异常扣血：血量突然大幅下降
                            // 如果血量从正常值突然变成0或负数，或者扣血量超过5000（正常伤害很少这么高）
                            var healthDrop = cachedHealth - plant.thePlantHealth;
                            if (healthDrop > 0 && (plant.thePlantHealth <= 0 || healthDrop > 5000))
                            {
                                // 恢复血量（可能是强制扣血）
                                plant.thePlantHealth = cachedHealth;
                                plant.UpdateText();
                            }
                        }

                        // 只有当植物血量大于0时才更新缓存
                        if (plant.thePlantHealth > 0)
                        {
                            PlantHealthCache[plantId] = plant.thePlantHealth;
                        }
                    }
                }
                
                // 同时更新Die补丁的缓存
                PlantDiePatch.UpdateHealthCache();
            }
            catch { }
        }
        else
        {
            // 功能关闭时清空缓存
            if (PlantHealthCache.Count > 0)
                PlantHealthCache.Clear();
        }

        if (RandomCard)
        {
            Il2CppSystem.Collections.Generic.List<PlantType> randomPlant = GameAPP.resourcesManager.allPlants;
            if (InGameUI.Instance && randomPlant != null && randomPlant.Count != 0)
            {
                for (int i = 0; i < InGameUI.Instance.cardOnBank.Length; i++)
                {
                    try
                    {
                        var index = Random.RandomRangeInt(0, randomPlant.Count);
                        var card = InGameUI.Instance.cardOnBank[i];
                        card.thePlantType = randomPlant[index];
                        card.ChangeCardSprite();
                        card.theSeedCost = 0;
                        card.fullCD = 0;
                    }
                    catch { }
                }
            }
        }
        
        // 僵尸状态并存 - 在每帧维护红温与寒冷状态的并存
        if (ZombieStatusCoexist)
        {
            try
            {
                foreach (var zombie in Board.Instance!.zombieArray)
                {
                    if (zombie == null) continue;
                    
                    // 如果僵尸同时有红温状态和之前保存的寒冷状态，恢复寒冷状态
                    int zombieId = zombie.GetInstanceID();
                    if (zombie.isJalaed && ZombieStatusCoexistData.TryGetValue(zombieId, out var savedState))
                    {
                        // 如果寒冷/冻结状态被清除了，恢复它
                        if (savedState.hadCold && zombie.coldTimer <= 0 && zombie.freezeTimer <= 0 && zombie.freezeLevel <= 0)
                        {
                            zombie.coldTimer = savedState.coldTimer;
                            zombie.freezeTimer = savedState.freezeTimer;
                            zombie.freezeLevel = savedState.freezeLevel;
                        }
                    }
                    
                    // 保存当前状态用于下一帧检查
                    bool hasCold = zombie.coldTimer > 0 || zombie.freezeTimer > 0 || zombie.freezeLevel > 0;
                    if (hasCold)
                    {
                        ZombieStatusCoexistData[zombieId] = (true, zombie.coldTimer, zombie.freezeTimer, zombie.freezeLevel);
                    }
                    else if (!zombie.isJalaed)
                    {
                        // 如果僵尸既没有红温也没有寒冷，清除缓存
                        ZombieStatusCoexistData.Remove(zombieId);
                    }
                }
                
                // 清理已死亡僵尸的缓存
                var deadZombieIds = ZombieStatusCoexistData.Keys.ToList();
                foreach (var id in deadZombieIds)
                {
                    bool found = false;
                    foreach (var zombie in Board.Instance.zombieArray)
                    {
                        if (zombie != null && zombie.GetInstanceID() == id)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        ZombieStatusCoexistData.Remove(id);
                    }
                }
            }
            catch { }
        }
        else
        {
            // 功能关闭时清空缓存
            if (ZombieStatusCoexistData.Count > 0)
                ZombieStatusCoexistData.Clear();
        }
    }

    //from Gaoshu
    public static string CompressString(string text)
    {
        var buffer = Encoding.UTF8.GetBytes(text);
        using var memoryStream = new MemoryStream();
        using (var gZipStream = new GZipStream(memoryStream, CompressionMode.Compress, true))
        {
            gZipStream.Write(buffer, 0, buffer.Length);
        }

        return Convert.ToBase64String(memoryStream.ToArray());
    }

    //from Gaoshu
    public static string DecompressString(string compressedText)
    {
        var gZipBuffer = Convert.FromBase64String(compressedText);
        using var memoryStream = new MemoryStream(gZipBuffer);
        using var gZipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
        using var resultStream = new MemoryStream();
        gZipStream.CopyTo(resultStream);
        var buffer = resultStream.ToArray();
        return Encoding.UTF8.GetString(buffer);
    }

    public static bool[] GetBoolArray(Il2CppStructArray<int> list)
    {
        return [.. from i in list select i > 0];
    }

    public static Il2CppStructArray<int> GetIntArray(bool[] array)
    {
        return new Il2CppStructArray<int>([.. from i in array select i ? 1 : 0]);
    }

    public static bool InGame()
    {
        return Board.Instance is not null &&
               GameAPP.theGameStatus is not GameStatus.OpenOptions or GameStatus.OutGame or GameStatus.Almanac;
    }

    public static IEnumerator PostInitBoard()
    {
        MLogger?.LogInfo("[PVZRHTools] PostInitBoard: 开始执行");
        // 使用统一的 TravelMgr 获取方法
        var travelMgr = ResolveTravelMgr(autoCreate: true);
        if (travelMgr == null)
        {
            MLogger?.LogWarning("[PVZRHTools] PostInitBoard: 无法找到 TravelMgr 组件");
            yield break;
        }
        
        Board.Instance.freeCD = FreeCD;
        // 已移除：不再在游戏开局自动生成小推车
        yield return null;
        if (!(GameAPP.theBoardType == (LevelType)3 && Board.Instance.theCurrentSurvivalRound != 1))
        {
            yield return null;

            var advs = travelMgr.advancedUpgrades;
            if (advs != null && AdvBuffs != null)
            {
                // 修复数组越界：确保访问本地数组时不超过其长度
                var count = Math.Min(advs.Count, AdvBuffs.Length);
                for (var i = 0; i < count; i++)
                {
                    advs[i] = AdvBuffs[i] || advs[i];
                    yield return null;
                }
            }

            var ultis = travelMgr.ultimateUpgrades;
            if (ultis != null && UltiBuffs != null)
            {
                // 修复数组越界：确保访问本地数组时不超过其长度
                var count = Math.Min(ultis.Count, UltiBuffs.Length);
                for (var i = 0; i < count; i++)
                {
                    ultis[i] = UltiBuffs[i] || ultis[i] is 1 ? 1 : 0;
                    yield return null;
                }
            }

            var deb = travelMgr.debuff;
            if (deb != null && Debuffs != null)
            {
                // 修复数组越界：确保访问本地数组时不超过其长度
                var count = Math.Min(deb.Count, Debuffs.Length);
                for (var i = 0; i < count; i++)
                {
                    deb[i] = Debuffs[i] || deb[i];
                    yield return null;
                }
            }
            
            // 设置 BoardTag 标志，使游戏识别并应用词条效果
            // 注意：这里只在关卡本身就是旅行关（isTravel 为 true）时，才开启 enableTravelBuff，
            // 避免把所有普通关卡都强行当成旅行关，从而影响小推车等原版关卡行为
            try
            {
                if (Board.Instance != null && GameAPP.board != null)
                {
                    var board = GameAPP.board.GetComponent<Board>();
                    if (board != null)
                    {
                        var boardTag = board.boardTag;
                        if (boardTag.isTravel)
                        {
                        boardTag.enableTravelBuff = true;
                        Board.Instance.boardTag = boardTag;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                MLogger?.LogError($"[PVZRHTools] PostInitBoard 设置 BoardTag 失败: {ex.Message}\n{ex.StackTrace}");
            }
        }

        InGameAdvBuffs = new bool[TravelMgr.advancedBuffs.Count];
        InGameUltiBuffs = new bool[TravelMgr.ultimateBuffs.Count];
        InGameDebuffs = new bool[TravelMgr.debuffs.Count];
        
        // 重置旗帜波状态检测
        // 注意：同步当前旗帜波状态，避免在 PostInitBoard 创建 TravelMgr 后导致旗帜波检测失效
        if (Board.Instance != null)
        {
            _lastHugeWaveState = Board.Instance.isHugeWave;
        }
        else
        {
            _lastHugeWaveState = false;
        }
        _flagWaveUnlockIndex = 0;
        _lastUnlockWave = -1;
        _currentFlagWaveIndex = 0;
        
        yield return null;

        // 安全地复制数组，防止越界
        if (travelMgr.advancedUpgrades != null)
        {
            var count = Math.Min(travelMgr.advancedUpgrades.Count, InGameAdvBuffs.Length);
            for (var i = 0; i < count; i++)
                InGameAdvBuffs[i] = travelMgr.advancedUpgrades[i];
        }
        
        if (travelMgr.ultimateUpgrades != null)
        {
            var ultiArray = GetBoolArray(travelMgr.ultimateUpgrades);
            if (ultiArray != null)
            {
                var count = Math.Min(ultiArray.Length, InGameUltiBuffs.Length);
                for (var i = 0; i < count; i++)
                    InGameUltiBuffs[i] = ultiArray[i];
            }
        }
        
        if (travelMgr.debuff != null)
        {
            var count = Math.Min(travelMgr.debuff.Count, InGameDebuffs.Length);
            for (var i = 0; i < count; i++)
                InGameDebuffs[i] = travelMgr.debuff[i];
        }
        yield return null;
        new Thread(SyncInGameBuffs).Start();

        // 进入游戏后重新读取所有词条（包括MOD添加的），并发送给UI
        // MOD词条通常在TravelMgr.Awake中注册，需要等待更长时间确保所有MOD都完成注册
        // 已禁用：用户不需要在游戏启动时读取词条数据
        /*
        MLogger?.LogInfo("[PVZRHTools] PostInitBoard: 准备重新读取词条数据（第1次）");
        yield return new WaitForSeconds(1.5f); // 等待MOD词条注册完成（增加到1.5秒）
        MLogger?.LogInfo("[PVZRHTools] PostInitBoard: 开始重新读取词条数据（第1次）");
        ReloadAndSendBuffsData();
        
        // 再次延迟后重试一次，确保捕获所有MOD词条（包括延迟注册的MOD）
        MLogger?.LogInfo("[PVZRHTools] PostInitBoard: 准备重新读取词条数据（第2次）");
        yield return new WaitForSeconds(1.5f);
        MLogger?.LogInfo("[PVZRHTools] PostInitBoard: 开始重新读取词条数据（第2次）");
        ReloadAndSendBuffsData();
        
        // 第三次重试，确保万无一失
        MLogger?.LogInfo("[PVZRHTools] PostInitBoard: 准备重新读取词条数据（第3次）");
        yield return new WaitForSeconds(1.0f);
        MLogger?.LogInfo("[PVZRHTools] PostInitBoard: 开始重新读取词条数据（第3次）");
        ReloadAndSendBuffsData();
        */

        yield return null;
        if (ZombieSeaLow && SeaTypes.Count > 0)
        {
            var i = 0;
            for (var wave = 0; wave < Board.Instance.theMaxWave; wave++)
            for (var index = 0; index < 100; index++)
            {
                SetZombieList(index, wave, (ZombieType)SeaTypes[i]);
                if (++i >= SeaTypes.Count) i = 0;
            }
        }
    }

    //感谢@高数带我飞(Github:https://github.com/LibraHp/)的在出怪表修改上的技术支持
    public static void SetZombieList(int zombieIndex, int waveIndex, ZombieType value)
    {
        try
        {
            // 直接访问 InitZombieList.zombieList，与 Modified-Plus 的实现一致
            Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<ZombieType>>? zombieList = null;
            
            try
            {
                // 尝试直接访问
                zombieList = InitZombieList.zombieList;
            }
            catch
            {
                // 如果直接访问失败，使用反射
                var zombieListField = typeof(InitZombieList).GetField("zombieList",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                
                if (zombieListField != null)
                {
                    zombieList = zombieListField.GetValue(null) as Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<ZombieType>>;
                }
            }

            if (zombieList == null)
            {
                MLogger?.LogWarning("[PVZRHTools] SetZombieList: InitZombieList.zombieList 为 null");
                return;
            }

            // 检查波次索引是否有效
            if (waveIndex < 0 || waveIndex >= zombieList.Count)
            {
                MLogger?.LogWarning($"[PVZRHTools] SetZombieList: 波次索引 {waveIndex} 超出范围 (0-{zombieList.Count - 1})");
                return;
            }

            var wave = zombieList[waveIndex];
            if (wave == null)
            {
                MLogger?.LogWarning($"[PVZRHTools] SetZombieList: 第 {waveIndex} 波为 null");
                return;
            }

            // 检查僵尸索引是否有效
            if (zombieIndex < 0 || zombieIndex >= wave.Count)
            {
                MLogger?.LogWarning($"[PVZRHTools] SetZombieList: 僵尸索引 {zombieIndex} 超出范围 (0-{wave.Count - 1})");
                return;
            }

            // 直接修改列表，与 Modified-Plus 的实现一致
            wave[zombieIndex] = value;
            
            MLogger?.LogInfo($"[PVZRHTools] SetZombieList: 已修改第 {waveIndex} 波第 {zombieIndex} 个出怪为 {value}");
        }
        catch (Exception ex)
        {
            MLogger?.LogError($"[PVZRHTools] SetZombieList 异常: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// 获取出怪列表数据
    /// </summary>
    public static Dictionary<int, List<int>>? GetZombieListData()
    {
        try
        {
            // 直接访问InitZombieList.zombieList（如果是public属性）
            // 如果无法直接访问，则使用反射
            Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<ZombieType>>? zombieList = null;
            
            try
            {
                // 尝试直接访问
                zombieList = InitZombieList.zombieList;
            }
            catch
            {
                // 如果直接访问失败，使用反射
                var zombieListField = typeof(InitZombieList).GetField("zombieList",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                
                if (zombieListField != null)
                {
                    zombieList = zombieListField.GetValue(null) as Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<ZombieType>>;
                }
            }

            if (zombieList == null)
            {
                MLogger?.LogWarning("[PVZRHTools] GetZombieListData: InitZombieList.zombieList 为 null");
                return null;
            }

            var result = new Dictionary<int, List<int>>();
            
            // 遍历所有波次（从1开始，跳过索引0）
            for (int waveIndex = 1; waveIndex < zombieList.Count; waveIndex++)
            {
                var wave = zombieList[waveIndex];
                if (wave == null) continue;

                var zombieTypes = new List<int>();
                for (int i = 0; i < wave.Count; i++)
                {
                    zombieTypes.Add((int)wave[i]);
                }

                if (zombieTypes.Count > 0)
                {
                    result[waveIndex] = zombieTypes;
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            MLogger?.LogError($"[PVZRHTools] GetZombieListData 异常: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    public static void SyncInGameBuffs()
    {
        if (!InGame()) return;
        try
        {
            // 使用统一的 TravelMgr 获取方法
            var travelMgr = ResolveTravelMgr(autoCreate: true);
            if (travelMgr == null)
            {
                MLogger?.LogWarning("[PVZRHTools] SyncInGameBuffs: 无法找到 TravelMgr 组件");
                return;
            }

            if (travelMgr.advancedUpgrades == null || travelMgr.ultimateUpgrades == null || travelMgr.debuff == null)
            {
                MLogger?.LogWarning("[PVZRHTools] SyncInGameBuffs: TravelMgr 的词条数据未初始化");
                return;
            }
            
            DataSync.Instance.SendData(new SyncTravelBuff
            {
                AdvInGame = [.. travelMgr.advancedUpgrades],
                UltiInGame = [.. GetBoolArray(travelMgr.ultimateUpgrades)],
                DebuffsInGame = [.. travelMgr.debuff]
            });
        }
        catch (System.Exception ex)
        {
            MLogger?.LogError($"[PVZRHTools] SyncInGameBuffs 异常: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// 重新读取所有词条数据（包括MOD添加的）并发送给UI
    /// 在进入游戏后调用，确保MOD词条已注册
    /// </summary>
    public static void ReloadAndSendBuffsData()
    {
        try
        {
            MLogger?.LogInfo("[PVZRHTools] ReloadAndSendBuffsData: 开始执行");
            var travelMgr = ResolveTravelMgr(autoCreate: true);
            if (travelMgr == null)
            {
                MLogger?.LogWarning("[PVZRHTools] ReloadAndSendBuffsData: 无法找到 TravelMgr 组件");
                return;
            }
            if (TravelMgr.advancedBuffs == null || TravelMgr.ultimateBuffs == null || TravelMgr.debuffs == null)
            {
                MLogger?.LogWarning("[PVZRHTools] ReloadAndSendBuffsData: 词条数据未初始化");
                MLogger?.LogInfo($"[PVZRHTools] ReloadAndSendBuffsData: advancedBuffs={TravelMgr.advancedBuffs?.GetType().Name}, ultimateBuffs={TravelMgr.ultimateBuffs?.GetType().Name}, debuffs={TravelMgr.debuffs?.GetType().Name}");
                return;
            }
            MLogger?.LogInfo($"[PVZRHTools] ReloadAndSendBuffsData: TravelMgr 已找到，词条数量 - Advanced={TravelMgr.advancedBuffs.Count}, Ultimate={TravelMgr.ultimateBuffs.Count}, Debuff={TravelMgr.debuffs.Count}");

            // 遍历所有键值对以确保捕获所有词条（包括MOD添加的不连续ID词条）
            List<string> advBuffs = [];
            int maxAdvKey = -1;
            // 先找出最大键值
            foreach (var kvp in TravelMgr.advancedBuffs)
            {
                if (kvp.Key > maxAdvKey) maxAdvKey = kvp.Key;
            }
            // 然后从0到最大键值遍历，使用TryGetValue检查
            for (int i = 0; i <= maxAdvKey; i++)
            {
                string buffText = null;
                if (TravelMgr.advancedBuffs.TryGetValue(i, out buffText) && !string.IsNullOrEmpty(buffText))
                {
                    advBuffs.Add($"#{i} {buffText}");
                    MLogger?.LogInfo($"[PVZRHTools] 重新读取高级词条: #{i} {buffText}");
                }
            }

            List<string> ultiBuffs = [];
            int maxUltiKey = -1;
            // 先找出最大键值
            foreach (var kvp in TravelMgr.ultimateBuffs)
            {
                if (kvp.Key > maxUltiKey) maxUltiKey = kvp.Key;
            }
            // 然后从0到最大键值遍历，使用TryGetValue检查
            for (int i = 0; i <= maxUltiKey; i++)
            {
                string buffText = null;
                if (TravelMgr.ultimateBuffs.TryGetValue(i, out buffText) && !string.IsNullOrEmpty(buffText))
                {
                    ultiBuffs.Add($"#{i} {buffText}");
                    MLogger?.LogInfo($"[PVZRHTools] 重新读取究极词条: #{i} {buffText}");
                }
            }

            List<string> debuffs = [];
            int maxDebuffKey = -1;
            // 先找出最大键值
            foreach (var kvp in TravelMgr.debuffs)
            {
                if (kvp.Key > maxDebuffKey) maxDebuffKey = kvp.Key;
            }
            // 然后从0到最大键值遍历，使用TryGetValue检查
            for (int i = 0; i <= maxDebuffKey; i++)
            {
                string buffText = null;
                if (TravelMgr.debuffs.TryGetValue(i, out buffText) && !string.IsNullOrEmpty(buffText))
                {
                    debuffs.Add($"#{i} {buffText}");  // 添加 # 前缀，与 Advanced 和 Ultimate 保持一致
                    MLogger?.LogInfo($"[PVZRHTools] 重新读取负面词条: #{i} {buffText}");
                }
            }

            // 更新本地数组大小（如果MOD添加了新词条）
            // 使用最大键值+1作为数组大小
            int newAdvSize = maxAdvKey + 1;
            if (AdvBuffs == null || AdvBuffs.Length < newAdvSize)
            {
                var oldLength = AdvBuffs?.Length ?? 0;
                var newArray = new bool[newAdvSize];
                if (AdvBuffs != null)
                    Array.Copy(AdvBuffs, newArray, Math.Min(oldLength, newArray.Length));
                AdvBuffs = newArray;
            }

            int newUltiSize = maxUltiKey + 1;
            if (UltiBuffs == null || UltiBuffs.Length < newUltiSize)
            {
                var oldLength = UltiBuffs?.Length ?? 0;
                var newArray = new bool[newUltiSize];
                if (UltiBuffs != null)
                    Array.Copy(UltiBuffs, newArray, Math.Min(oldLength, newArray.Length));
                UltiBuffs = newArray;
            }

            int newDebuffSize = maxDebuffKey + 1;
            if (Debuffs == null || Debuffs.Length < newDebuffSize)
            {
                var oldLength = Debuffs?.Length ?? 0;
                var newArray = new bool[newDebuffSize];
                if (Debuffs != null)
                    Array.Copy(Debuffs, newArray, Math.Min(oldLength, newArray.Length));
                Debuffs = newArray;
            }

            // 更新并保存InitData
            // 先读取现有的InitData（保留Plants、Zombies等数据，但不使用旧的词条数据）
            InitData data = new()
            {
                AdvBuffs = [.. advBuffs],  // 使用最新读取的词条数据
                UltiBuffs = [.. ultiBuffs],  // 使用最新读取的词条数据
                Debuffs = [.. debuffs]  // 使用最新读取的词条数据
            };

            // 读取现有的InitData（仅保留Plants、Zombies、Bullets等非词条数据）
            try
            {
                if (File.Exists("./PVZRHTools/InitData.json"))
                {
                    try
                    {
                        var existingJson = File.ReadAllText("./PVZRHTools/InitData.json");
                        var existingData = System.Text.Json.JsonSerializer.Deserialize<InitData>(existingJson);
                        // 只保留非词条数据，词条数据使用上面最新读取的
                        if (existingData.Plants != null && existingData.Plants.Count > 0)
                        {
                            data.Plants = existingData.Plants;
                        }
                        if (existingData.Zombies != null && existingData.Zombies.Count > 0)
                        {
                            data.Zombies = existingData.Zombies;
                        }
                        if (existingData.Bullets != null && existingData.Bullets.Count > 0)
                        {
                            data.Bullets = existingData.Bullets;
                        }
                        if (existingData.FirstArmors != null && existingData.FirstArmors.Count > 0)
                        {
                            data.FirstArmors = existingData.FirstArmors;
                        }
                        if (existingData.SecondArmors != null && existingData.SecondArmors.Count > 0)
                        {
                            data.SecondArmors = existingData.SecondArmors;
                        }
                        MLogger?.LogInfo($"[PVZRHTools] ReloadAndSendBuffsData: 从旧文件保留了 Plants={data.Plants?.Count ?? 0}, Zombies={data.Zombies?.Count ?? 0}, Bullets={data.Bullets?.Count ?? 0}");
                    }
                    catch (System.Exception ex2)
                    {
                        MLogger?.LogWarning($"[PVZRHTools] 读取现有InitData失败: {ex2.Message}");
                    }
                }
                else
                {
                    MLogger?.LogInfo("[PVZRHTools] ReloadAndSendBuffsData: InitData.json 不存在，将创建新文件");
                }
            }
            catch (System.Exception ex)
            {
                MLogger?.LogWarning($"[PVZRHTools] 读取现有InitData失败: {ex.Message}");
            }

            // 保存更新后的InitData
            Directory.CreateDirectory("./PVZRHTools");
            File.WriteAllText("./PVZRHTools/InitData.json", System.Text.Json.JsonSerializer.Serialize(data));

            // 发送更新后的词条数据给UI
            try
            {
                MLogger?.LogInfo($"[PVZRHTools] 准备发送词条数据: Advanced={advBuffs.Count}, Ultimate={ultiBuffs.Count}, Debuff={debuffs.Count}");
                DataSync.Instance.SendData(data);
                MLogger?.LogInfo($"[PVZRHTools] 已重新读取并发送词条数据: Advanced={advBuffs.Count}, Ultimate={ultiBuffs.Count}, Debuff={debuffs.Count}");
            }
            catch (System.Exception ex)
            {
                MLogger?.LogWarning($"[PVZRHTools] 发送词条数据失败: {ex.Message}");
            }
        }
        catch (System.Exception ex)
        {
            MLogger?.LogError($"[PVZRHTools] ReloadAndSendBuffsData 异常: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// 统一获取 TravelMgr（兼容多种场景）
    /// </summary>
    /// <param name="autoCreate">是否在找不到时自动创建 TravelMgr（仅在需要修改词条时使用）</param>
    internal static TravelMgr? ResolveTravelMgr(bool autoCreate = false)
    {
        TravelMgr? travelMgr = null;
        try { travelMgr = TravelMgr.Instance; } catch { }
        if (travelMgr == null && GameAPP.gameAPP != null)
        {
            travelMgr = GameAPP.gameAPP.GetComponent<TravelMgr>();
        }
        if (travelMgr == null)
        {
            travelMgr = UnityEngine.Object.FindObjectOfType<TravelMgr>();
        }
        if (travelMgr == null && GameAPP.board != null)
        {
            travelMgr = GameAPP.board.GetComponent<TravelMgr>();
        }
        
        // 仅在需要修改词条时才自动创建 TravelMgr
        // GetOrAdd TravelMgr + 设置 boardTag.isTravel/enableTravelBuff
        if (travelMgr == null && autoCreate && InGame() && GameAPP.gameAPP != null)
        {
            try
            {
                travelMgr = GameAPP.gameAPP.GetComponent<TravelMgr>();
                if (travelMgr == null)
                {
                    travelMgr = GameAPP.gameAPP.AddComponent<TravelMgr>();
                    MLogger?.LogInfo("[PVZRHTools] ResolveTravelMgr: 已自动创建 TravelMgr（需要修改词条时实时开启）");
                    
                    // 关键修复：自动创建 TravelMgr 时，同步旗帜波状态，避免旗帜波检测失效
                    // 确保 _lastHugeWaveState 与当前游戏状态一致
                    if (Board.Instance != null)
                    {
                        _lastHugeWaveState = Board.Instance.isHugeWave;
                        MLogger?.LogInfo($"[PVZRHTools] ResolveTravelMgr: 已同步旗帜波状态 _lastHugeWaveState = {_lastHugeWaveState}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                MLogger?.LogWarning($"[PVZRHTools] ResolveTravelMgr: 自动创建 TravelMgr 失败: {ex.Message}");
            }
        }
        return travelMgr;
    }

    public static void UpdateInGameBuffs()
    {
        try
        {
            // 使用统一的 TravelMgr 获取方法
            var travelMgr = ResolveTravelMgr(autoCreate: true);
            if (travelMgr == null)
            {
                MLogger?.LogWarning("[PVZRHTools] 无法找到 TravelMgr 组件，可能是游戏未初始化");
                return;
            }
            
            // 修复数组越界问题：确保访问本地数组时不超过其长度
            if (travelMgr.advancedUpgrades != null && InGameAdvBuffs != null)
            {
                var count = Math.Min(travelMgr.advancedUpgrades.Count, InGameAdvBuffs.Length);
                for (var i = 0; i < count; i++)
                    travelMgr.advancedUpgrades[i] = InGameAdvBuffs[i];
            }
            
            if (travelMgr.ultimateUpgrades != null && InGameUltiBuffs != null)
            {
                var ultiArray = GetIntArray(InGameUltiBuffs);
                if (ultiArray != null)
                {
                    var count = Math.Min(travelMgr.ultimateUpgrades.Count, ultiArray.Length);
                    for (var i = 0; i < count; i++)
                        travelMgr.ultimateUpgrades[i] = ultiArray[i];
                }
            }
            
            if (travelMgr.debuff != null && InGameDebuffs != null)
            {
                var count = Math.Min(travelMgr.debuff.Count, InGameDebuffs.Length);
                for (var i = 0; i < count; i++)
                    travelMgr.debuff[i] = InGameDebuffs[i];
            }
            
            // 关键修复：设置 BoardTag 标志，使游戏识别并应用词条效果
            // 参考 HeiTa 和 SuperGoldPresent 的处理方式
            try
            {
                if (Board.Instance != null && GameAPP.board != null)
                {
                    var board = GameAPP.board.GetComponent<Board>();
                    if (board != null)
                    {
                        var boardTag = board.boardTag;
                        boardTag.isTravel = true;
                        boardTag.enableTravelBuff = true;
                        Board.Instance.boardTag = boardTag;
                    }
                }
            }
            catch (System.Exception ex)
            {
                MLogger?.LogError($"[PVZRHTools] 设置 BoardTag 失败: {ex.Message}\n{ex.StackTrace}");
            }
        }
        catch (System.Exception ex)
        {
            MLogger?.LogError($"UpdateInGameBuffs 异常: {ex.Message}\n{ex.StackTrace}");
        }
    }
}

/// <summary>
/// 鱼丸坚不可摧 - 鱼丸受到的伤害最多为200
/// 注意：此Patch已被SuperMachineNutTakeDamageGameBuffPatch替代，保留此类仅作为占位
/// </summary>
// SuperMachineNutTakeDamagePatch 已移除，功能合并到 SuperMachineNutTakeDamageGameBuffPatch

// PlantRecoverMNEntryPatch 已移除，功能合并到 PlantRecoverGameBuffPatch

// SunMagnetShroomMNEntryPatch 已移除，功能合并到 SunMagnetShroomGameBuffPatch

/// <summary>
/// MNEntry词条注册 - 将词条注册到游戏的旅行词条系统中
/// 只有当修改器中开关开启时，才会注册词条到游戏中
/// </summary>
[HarmonyPatch(typeof(TravelMgr))]
public static class MNEntryTravelMgrPatch
{
    /// <summary>
    /// 词条1(坚不可摧)在TravelMgr.advancedBuffs中的ID，-1表示未注册
    /// </summary>
    public static int TravelId1 = -1;

    /// <summary>
    /// 词条2(高级后勤)在TravelMgr.advancedBuffs中的ID，-1表示未注册
    /// </summary>
    public static int TravelId2 = -1;

    /// <summary>
    /// 词条文本
    /// </summary>
    private const string BuffText1 = "坚不可摧: 鱼丸受到的伤害最多为200";
    private const string BuffText2 = "高级后勤: 鱼丸恢复血量时恢复双倍血量, 阳光磁力菇冷却时间大幅减少";

    /// <summary>
    /// TravelMgr.Awake 后置补丁
    /// 在TravelMgr初始化时根据修改器开关状态注册自定义buff词条
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch("Awake")]
    public static void PostAwake(TravelMgr __instance)
    {
        try
        {
            // 重置词条ID
            TravelId1 = -1;
            TravelId2 = -1;

            // 只有开启时才注册两个词条
            if (!PatchMgr.MNEntryEnabled) return;

            // 检查 TravelMgr.advancedBuffs 是否已初始化
            if (TravelMgr.advancedBuffs == null)
            {
                MLogger.LogError("MNEntry词条注册失败: TravelMgr.advancedBuffs 为 null");
                return;
            }

            if (__instance.advancedUpgrades == null)
            {
                MLogger.LogError("MNEntry词条注册失败: __instance.advancedUpgrades 为 null");
                return;
            }

            int baseId = TravelMgr.advancedBuffs.Count;

            // 注册两个词条
            TravelId1 = baseId;
            TravelId2 = baseId + 1;

            // 扩展数组
            bool[] newUpgrades = new bool[__instance.advancedUpgrades.Count + 2];
            Array.Copy(__instance.advancedUpgrades.ToArray(), newUpgrades, __instance.advancedUpgrades.Count);
            __instance.advancedUpgrades = newUpgrades;

            // 注册词条文本（Dictionary 会自动处理键值对）
            TravelMgr.advancedBuffs[TravelId1] = BuffText1;
            TravelMgr.advancedBuffs[TravelId2] = BuffText2;
            MLogger.LogInfo($"MNEntry词条注册成功，ID1: {TravelId1}, ID2: {TravelId2}");
        }
        catch (Exception ex)
        {
            MLogger.LogError($"MNEntry词条注册失败: {ex.Message}");
        }
    }

    /// <summary>
    /// GetPlantTypeByAdvBuff 后置补丁
    /// 返回词条对应的植物类型，用于在选词条时展示植物图标
    /// </summary>
    [HarmonyPatch("GetPlantTypeByAdvBuff")]
    [HarmonyPostfix]
    public static void PostGetPlantTypeByAdvBuff(ref int index, ref PlantType __result)
    {
        // 如果是我们注册的词条，返回鱼丸的植物类型
        if ((TravelId1 >= 0 && index == TravelId1) || (TravelId2 >= 0 && index == TravelId2))
        {
            __result = (PlantType)1151; // SuperMachineNut = 1151
        }
    }
}

/// <summary>
/// MNEntry词条效果 - 坚不可摧：鱼丸受到的伤害最多为200
/// </summary>
[HarmonyPatch(typeof(SuperMachineNut), nameof(SuperMachineNut.TakeDamage))]
public static class SuperMachineNutTakeDamageGameBuffPatch
{
    [HarmonyPrefix]
    public static bool Prefix(ref int damage)
    {
        // 检查修改器开关（开启时两个效果都生效）
        if (PatchMgr.MNEntryEnabled)
        {
            if (damage > 200) damage = 200;
            return true;
        }

        // 检查游戏内词条是否激活
        if (MNEntryTravelMgrPatch.TravelId1 >= 0 && Lawnf.TravelAdvanced(MNEntryTravelMgrPatch.TravelId1))
        {
            if (damage > 200) damage = 200;
        }
        return true;
    }
}

/// <summary>
/// MNEntry词条效果 - 鱼丸双倍恢复（游戏内词条版本）
/// </summary>
[HarmonyPatch(typeof(Plant), nameof(Plant.Recover))]
public static class PlantRecoverGameBuffPatch
{
    [HarmonyPrefix]
    public static bool Prefix(ref float health, Plant __instance)
    {
        if (__instance.thePlantType != (PlantType)1151) return true;

        // 检查修改器开关（MNEntryEnabled 同时控制坚不可摧和高级后勤两个效果）
        if (PatchMgr.MNEntryEnabled)
        {
            health *= 2f;
            return true;
        }

        // 检查游戏内词条是否激活
        if (MNEntryTravelMgrPatch.TravelId2 >= 0 && Lawnf.TravelAdvanced(MNEntryTravelMgrPatch.TravelId2))
        {
            health *= 2f;
        }
        return true;
    }
}

/// <summary>
/// MNEntry词条效果 - 阳光磁力菇CD减少（游戏内词条版本）
/// </summary>
[HarmonyPatch(typeof(SunMagnetShroom), nameof(SunMagnetShroom.AttributeEvent))]
public static class SunMagnetShroomGameBuffPatch
{
    [HarmonyPostfix]
    public static void Postfix(SunMagnetShroom __instance)
    {
        // 检查修改器开关（MNEntryEnabled 同时控制坚不可摧和高级后勤两个效果）
        if (PatchMgr.MNEntryEnabled)
        {
            if (__instance.attributeCountdown > 5f)
                __instance.attributeCountdown = 4.5f;
            return;
        }

        // 检查游戏内词条是否激活
        if (MNEntryTravelMgrPatch.TravelId2 >= 0 && Lawnf.TravelAdvanced(MNEntryTravelMgrPatch.TravelId2))
        {
            if (__instance.attributeCountdown > 5f)
                __instance.attributeCountdown = 4.5f;
        }
    }
}