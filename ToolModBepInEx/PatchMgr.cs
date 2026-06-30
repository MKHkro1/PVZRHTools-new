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
using GameLevel.RogueShooting;
using ToolModData;
using UI;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using static ToolModBepInEx.PatchMgr;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace ToolModBepInEx;

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
                if (Board.Instance.config != null && Board.Instance.config.waveInterval > NewZombieUpdateCD)
                {
                    Board.Instance.config.waveInterval = NewZombieUpdateCD;
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
            
            
            // 遍历当前旗帜波的所有词条，依次应用
            foreach (var encodedBuffId in currentWaveBuffs)
            {
                ApplyFlagWaveBuff(encodedBuffId, travelMgr);
            }
            
            // 显示文本：如果FlagWaveCustomTexts不为null且有内容，使用自定义字幕；否则显示词条名字
            // 注意：即使没有词条（空括号），也要显示字幕（如果有自定义字幕的话）
            try
            {
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
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(displayText))
                    {
                        GameApiCompat.ShowInGameText(displayText, 5);
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
                return;
            }
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
            }
            else if (encodedBuffId >= 1000 && encodedBuffId < 2000)
            {
                // 强制识别为Ultimate类型，避免被错误解码为Advanced
                // 这是最关键的判断：任何在 1000-1999 范围内的编码ID都必须是Ultimate类型
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
            
            switch (buffType)
            {
                case PatchMgr.BuffType.Advanced:
                    // 高级词条：0..advancedCount-1，对应 AdvBuff 枚举
                    // 再次验证：如果编码ID在1000-1999范围内，绝对不能应用为 Advanced
                    if (encodedBuffId >= 1000 && encodedBuffId < 2000)
                    {
                        MLogger?.LogError($"[PVZRHTools] 严重错误: 尝试将编码ID={encodedBuffId} (应该是Ultimate) 应用为Advanced词条！直接返回，不处理！");
                        return null; // 直接返回，防止错误应用
                    }

                    if (originalId >= 0)
                    {
                        // 3.4.1：通过 TravelMgr.GetNormalBuff / Lawnf.TravelAdvanced 应用高级词条，而不是直接操作 advancedUpgrades 数组
                        if (CanApplyRuntimeBuff())
                        {
                            try
                            {
                                travelMgr.GetNormalBuff((AdvBuff)originalId);
                            }
                            catch (System.Exception ex)
                            {
                                MLogger?.LogWarning($"[PVZRHTools] 调用 GetNormalBuff 失败: id={originalId}, ex={ex.Message}");
                            }
                        }
                        else
                        {
                            MLogger?.LogWarning($"[PVZRHTools] 跳过 GetNormalBuff: 关卡对象未就绪, id={originalId}");
                        }

                        if (TravelDictionary.advancedBuffsText != null)
                        {
                            TravelDictionary.advancedBuffsText.TryGetValue((AdvBuff)originalId, out buffName);
                        }

                        // 同步本地 InGameAdvBuffs 状态
                        if (InGameAdvBuffs != null && originalId < InGameAdvBuffs.Length)
                        {
                            InGameAdvBuffs[originalId] = true;
                        }

                        applied = true;
                    }
                    break;
                    
                case PatchMgr.BuffType.Ultimate:
                    // 究极词条：originalId 是 UltiBuff 枚举值
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
                    
                    if (originalId >= 0)
                    {
                        // 3.4.1：通过 TravelMgr.GetUltiBuff 应用究极词条，而不是直接操作 ultimateUpgrades 数组
                        try
                        {
                            travelMgr.GetUltiBuff((UltiBuff)originalId, true);
                        }
                        catch (System.Exception ex)
                        {
                            MLogger?.LogWarning($"[PVZRHTools] 调用 GetUltiBuff 失败: id={originalId}, ex={ex.Message}");
                        }
                        
                        // 同步 InGameUltiBuffs 数组，确保一致性
                        if (InGameUltiBuffs != null && originalId < InGameUltiBuffs.Length)
                        {
                            InGameUltiBuffs[originalId] = true;
                        }
                        else
                        {
                            MLogger?.LogWarning($"[PVZRHTools] 无法同步更新 InGameUltiBuffs: originalId={originalId}, InGameUltiBuffs.Length={InGameUltiBuffs?.Length ?? 0}");
                        }
                        
                        // 通过 TravelDictionary.ultimateBuffsText 获取词条名称
                        if (TravelDictionary.ultimateBuffsText != null)
                        {
                            try
                            {
                                TravelDictionary.ultimateBuffsText.TryGetValue((UltiBuff)originalId, out buffName);
                            }
                            catch (System.Exception ex)
                            {
                                MLogger?.LogWarning($"[PVZRHTools] 获取Ultimate词条名称失败: {ex.Message}\n{ex.StackTrace}");
                            }
                        }
                        else
                        {
                            MLogger?.LogWarning("[PVZRHTools] TravelDictionary.ultimateBuffsText 为 null，无法获取词条名称");
                        }
                        
                        applied = true;
                    }
                    break;
                    
                case PatchMgr.BuffType.Debuff:
                    // 负面词条：通过 TravelMgr.GetDebuff / Lawnf.TravelDebuff 应用
                    // 直接使用 TravelDictionary.debuffData[travelDebuff].Item1
                    if (originalId >= 0)
                    {
                        try
                        {
                            travelMgr.GetDebuff((TravelDebuff)originalId);
                        }
                        catch (System.Exception ex)
                        {
                            MLogger?.LogWarning($"[PVZRHTools] 调用 GetDebuff 失败: id={originalId}, ex={ex.Message}");
                        }

                        // 直接读取 debuffData.Item1
                        try
                        {
                            if (TravelDictionary.debuffData != null && 
                                TravelDictionary.debuffData.ContainsKey((TravelDebuff)originalId))
                            {
                                var debuffData = TravelDictionary.debuffData[(TravelDebuff)originalId];
                                var item1Value = debuffData.Item1;
                                if (!string.IsNullOrEmpty(item1Value))
                                {
                                    buffName = item1Value;
                                }
                                else
                                {
                        buffName = $"Debuff_{originalId}";
                                }
                            }
                            else
                            {
                                buffName = $"Debuff_{originalId}";
                            }
                        }
                        catch (System.Exception ex)
                        {
                            MLogger?.LogWarning($"[PVZRHTools] 读取 debuff {originalId} 的 Item1 属性失败: {ex.GetType().Name}");
                            buffName = $"Debuff_{originalId}";
                        }
                        
                        if (InGameDebuffs != null && originalId < InGameDebuffs.Length)
                        {
                            InGameDebuffs[originalId] = true;
                        }
                        
                        applied = true;
                    }
                    break;
            }
            
            if (!applied)
            {
                MLogger?.LogWarning($"[PVZRHTools] 旗帜波词条应用失败：类型={buffType}, 原始ID={originalId}, 编码ID={encodedBuffId}");
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
                    // 3.4.1：高级词条文本改为从 TravelDictionary.advancedBuffsText 读取
                    if (TravelDictionary.advancedBuffsText != null)
                        TravelDictionary.advancedBuffsText.TryGetValue((AdvBuff)originalId, out buffName);
                    break;
                case PatchMgr.BuffType.Ultimate:
                    if (TravelDictionary.ultimateBuffsText != null)
                        TravelDictionary.ultimateBuffsText.TryGetValue((UltiBuff)originalId, out buffName);
                    break;
                case PatchMgr.BuffType.Debuff:
                    // 直接使用 TravelDictionary.debuffData[travelDebuff].Item1
                    try
                    {
                        if (TravelDictionary.debuffData != null && 
                            TravelDictionary.debuffData.ContainsKey((TravelDebuff)originalId))
                        {
                            var debuffData = TravelDictionary.debuffData[(TravelDebuff)originalId];
                            var item1Value = debuffData.Item1;
                            if (!string.IsNullOrEmpty(item1Value))
                            {
                                buffName = item1Value;
                            }
                            else
                            {
                    buffName = $"Debuff_{originalId}";
                            }
                        }
                        else
                        {
                            buffName = $"Debuff_{originalId}";
                        }
                    }
                    catch (System.Exception ex)
                    {
                        MLogger?.LogWarning($"[PVZRHTools] 读取 debuff {originalId} 的 Item1 属性失败: {ex.GetType().Name}");
                        buffName = $"Debuff_{originalId}";
                    }
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
                    if (TravelDictionary.advancedBuffsText != null)
                        TravelDictionary.advancedBuffsText.TryGetValue((AdvBuff)originalId, out buffName);
                    break;
                case PatchMgr.BuffType.Ultimate:
                    if (TravelDictionary.ultimateBuffsText != null)
                        TravelDictionary.ultimateBuffsText.TryGetValue((UltiBuff)originalId, out buffName);
                    break;
                case PatchMgr.BuffType.Debuff:
                    if (TravelDictionary.debuffData != null &&
                        TravelDictionary.debuffData.TryGetValue((TravelDebuff)originalId, out var debData))
                        buffName = debData.Item1;
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
    public static bool IsFromZombie(Bullet bullet)
    {
        if (bullet == null) return false;
        try
        {
            return bullet.shootByZombie || bullet.from_zombie != null;
        }
        catch
        {
            return false;
        }
    }

    public static bool Prefix(Bullet __instance)
    {
        if (__instance == null) return true;

        // 老版黑曜石子弹：前两次命中不销毁，实现“穿透两次”。
        // 性能优化：先做最廉价判定，再做来源判断，避免在高频 Die 上产生额外开销。
        if (OldObsidianBullet &&
            __instance.theBulletType == BulletType.Bullet_steelPea &&
            __instance.hitTimes < 2 &&
            !__instance.shootByZombie &&
            __instance.from_zombie == null)
        {
            __instance.hit = false;
            return false;
        }

        if (UndeadBullet && !__instance.shootByZombie && __instance.from_zombie == null)
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
            if (__instance == null || BulletPatchB.IsFromZombie(__instance)) return true;
            
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
/// 卡片无限制补丁 - InitBoard.HideSeedBank（3.7 替代原 RemoveUI）
/// 在退出选卡界面时清除未被选中的复制卡片
/// </summary>
[HarmonyPatch(typeof(InitBoard), "HideSeedBank")]
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
        
        // 应用初始词条（仅在游戏开始时应用一次）
        ApplyInitialTravelBuffs();
    }
    
    /// <summary>
    /// 应用初始词条（从 AdvTravelBuff、UltiTravelBuff、InvestTravelBuff、Debuffs 应用）
    /// 这些词条应该在游戏开始时自动添加，而不是只在局内实时修改时生效
    /// </summary>
    private static void ApplyInitialTravelBuffs()
    {
        try
        {
            // 先检查是否真正“配置了”任何初始词条：
            // - 若所有数组都为 null / 长度为 0 / 全是 false，则认为没有设置初始词条，直接跳过，
            //   避免在场景切换时用一堆 false 覆盖当前已有的词条状态。
            bool hasAnyInitialAdv = false;
            if (AdvBuffs != null && AdvBuffs.Length > 0)
            {
                for (int i = 0; i < AdvBuffs.Length; i++)
                {
                    if (AdvBuffs[i])
                    {
                        hasAnyInitialAdv = true;
                        break;
                    }
                }
            }

            bool hasAnyInitialUlti = false;
            if (UltiBuffs != null && UltiBuffs.Length > 0)
            {
                for (int i = 0; i < UltiBuffs.Length; i++)
                {
                    if (UltiBuffs[i])
                    {
                        hasAnyInitialUlti = true;
                        break;
                    }
                }
            }

            bool hasAnyInitialInvest = false;
            if (InvestBuffs != null && InvestBuffs.Length > 0)
            {
                for (int i = 0; i < InvestBuffs.Length; i++)
                {
                    if (InvestBuffs[i])
                    {
                        hasAnyInitialInvest = true;
                        break;
                    }
                }
            }

            bool hasAnyInitialDebuff = false;
            if (Debuffs != null && Debuffs.Length > 0)
            {
                for (int i = 0; i < Debuffs.Length; i++)
                {
                    if (Debuffs[i])
                    {
                        hasAnyInitialDebuff = true;
                        break;
                    }
                }
            }

            // 如果四类初始词条都“完全为空”，则认为玩家没有配置初始词条，什么都不做。
            if (!hasAnyInitialAdv && !hasAnyInitialUlti && !hasAnyInitialInvest && !hasAnyInitialDebuff)
            {
                return;
            }
            
            
            // 应用初始词条到当前游戏状态
            if (hasAnyInitialAdv)
            {
                // 将初始词条同步到局内词条数组
                // 确保 InGameAdvBuffs 数组大小足够：直接使用 Count
                int requiredSize = TravelDictionary.advancedBuffsText?.Count ?? AdvBuffs.Length;
                
                if (InGameAdvBuffs == null || InGameAdvBuffs.Length < requiredSize)
                {
                    var newArray = new bool[requiredSize];
                    if (InGameAdvBuffs != null)
                    {
                        Array.Copy(InGameAdvBuffs, newArray, Math.Min(InGameAdvBuffs.Length, requiredSize));
                    }
                    InGameAdvBuffs = newArray;
                }
                
                for (int i = 0; i < AdvBuffs.Length && i < InGameAdvBuffs.Length; i++)
                {
                    // 只“叠加”初始词条：true 优先，不会把已有的 true 覆盖成 false，
                    // 避免在切换场景时把上一关/当前局内已经解锁的词条清空。
                    InGameAdvBuffs[i] = InGameAdvBuffs[i] || AdvBuffs[i];
                }
            }
            
            if (UltiBuffs != null && UltiBuffs.Length > 0)
            {
                if (InGameUltiBuffs == null || InGameUltiBuffs.Length < UltiBuffs.Length)
                {
                    InGameUltiBuffs = new bool[UltiBuffs.Length];
                }
                for (int i = 0; i < UltiBuffs.Length; i++)
                {
                    // 同样采用“叠加”逻辑，保留已有的 true 状态
                    InGameUltiBuffs[i] = InGameUltiBuffs[i] || UltiBuffs[i];
                }
            }
            
            if (InvestBuffs != null && InvestBuffs.Length > 0)
            {
                if (InGameInvestBuffs == null || InGameInvestBuffs.Length < InvestBuffs.Length)
                {
                    InGameInvestBuffs = new bool[InvestBuffs.Length];
                }
                for (int i = 0; i < InvestBuffs.Length; i++)
                {
                    InGameInvestBuffs[i] = InGameInvestBuffs[i] || InvestBuffs[i];
                }
            }
            
            if (Debuffs != null && Debuffs.Length > 0)
            {
                if (InGameDebuffs == null || InGameDebuffs.Length < Debuffs.Length)
                {
                    InGameDebuffs = new bool[Debuffs.Length];
                }
                for (int i = 0; i < Debuffs.Length; i++)
                {
                    InGameDebuffs[i] = InGameDebuffs[i] || Debuffs[i];
                }
            }
            
            // 应用词条到游戏（初始词条不允许移除已有词条）
            bool oldAllow = AllowBuffRemoval;
            try
            {
                AllowBuffRemoval = false;
            UpdateInGameBuffs();
            }
            finally
            {
                AllowBuffRemoval = oldAllow;
            }
            
        }
        catch (System.Exception ex)
        {
            MLogger?.LogError($"[PVZRHTools] Board.Start: 应用初始词条失败: {ex.Message}\n{ex.StackTrace}");
        }
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

    public static void Postfix(Bullet __result)
    {
        try
        {
            if (!OldObsidianBullet || __result == null) return;
            if (__result.theBulletType != BulletType.Bullet_steelPea) return;
            if (__result.shootByZombie || __result.from_zombie != null) return;

            // 老版黑曜石子弹：至少穿透两次
            if (__result.penetrationTimes < 2)
                __result.penetrationTimes = 2;
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(Bullet_steelPea), "HitZombie")]
public static class OldObsidianBulletHitPatch
{
    public static void Postfix(Bullet_steelPea __instance, Zombie zombie)
    {
        try
        {
            if (!OldObsidianBullet || __instance == null || zombie == null) return;
            if (__instance.shootByZombie || __instance.from_zombie != null) return;
            if (zombie.theHealth <= 0) return;
            
            // 命中计数在不同流程里的更新时机不完全一致，这里用 <= 1 兼容首段命中。
            if (__instance.hitTimes <= 1)
            {
                zombie.KnockBack(0.1f);
            }
        }
        catch
        {
        }
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
[HarmonyPatch(typeof(BoardAction), nameof(BoardAction.CreateFreeze))]
public static class BoardActionCreateFreezePatch
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
    public static bool Prefix(BoardAction __instance, Vector2 pos)
    {
        // 功能关闭时，执行原版逻辑
        if (!DisableIceEffect)
            return true;

        // 为全场僵尸添加冻结效果
        ApplyFreezeToAllZombies(__instance?.board);
        
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
                    zombie.ApplyDamage(DamageType.Normal, damageAmount);
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
    [HarmonyPatch(typeof(Gargantuar), "GargantuarAttackUpdate")]
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
    /// 修改 GetSun 方法 - 移除阳光上限限制
    /// 3.6：Board.GetSun 签名为 (float count, bool save = true)
    /// </summary>
    [HarmonyPatch(nameof(Board.GetSun))]
    [HarmonyPrefix]
    public static bool Prefix_GetSun(Board __instance, float count, bool save)
    {
        if (!UnlimitedSunlight) return true;

        try
        {
            if (__instance != null)
            {
                // 3.6：不再依赖旧版 GetSun 的中间参数，直接按 count 累加即可避免上限裁剪。
                __instance.theSun += (int)count;
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
    /// 3.4.1 版本中 Bullet 不再公开 theMovingWay 字段，这里仅根据存在时间进行判断。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Bullet.Die))]
    public static bool Prefix_Die(Bullet __instance)
    {
        if (!MagnetNutUnlimited) return true;

        try
        {
            if (__instance == null || ShouldExcludeBullet(__instance)) return true;

            // 检查是否是因为存在时间过长要死亡
            if (__instance.theExistTime > 20.0f)
            {
                // 重置存在时间，阻止死亡
                __instance.theExistTime = 0.0f;
                return false; // 阻止死亡
            }
            return true;
        }
        catch { return true; }
    }
}

#endregion

[HarmonyPatch(typeof(RhythmGame.RhythmGameManager), "Update")]
public static class RhythmGameAutoPlayPatch
{
    private const float AutoRhythmLateWindow = 0.12f;

    public static void Postfix(RhythmGame.RhythmGameManager __instance)
    {
        if (!AutoRhythmGame || __instance == null) return;
        if (!__instance.isPlaying || __instance.isPaused) return;

        try
        {
            float now = __instance.CurrentTime;
            var tracks = __instance.tracks;
            if (tracks == null) return;

            foreach (var track in tracks)
            {
                if (track == null) continue;
                var notes = track.GetActiveNotes();
                if (notes == null || notes.Count == 0) continue;

                // 仅在到达判定点（targetTime）后触发，不提前按。
                RhythmGame.FallingNote? targetNote = null;
                float bestDelay = float.MaxValue;

                foreach (var note in notes)
                {
                    if (note == null || note.hasAutoPlayed) continue;
                    if (!note.IsClickable()) continue;

                    float delay = now - note.targetTime;
                    // 到判定线后才按，且限制在可接受晚判范围内。
                    if (delay < 0f || delay > AutoRhythmLateWindow) continue;

                    if (delay < bestDelay)
                    {
                        bestDelay = delay;
                        targetNote = note;
                    }
                }

                if (targetNote == null) continue;

                if (targetNote.noteType == RhythmGame.NoteType.Hold)
                {
                    targetNote.OnHoldStart();
                }
                else
                {
                    targetNote.OnClick();
                }
            }
        }
        catch
        {
            // 静默失败，避免影响正常局内流程。
        }
    }
}

[HarmonyPatch(typeof(RhythmGame.RhythmGameManager), nameof(RhythmGame.RhythmGameManager.IsHoldKeyPressed))]
public static class RhythmGameAutoHoldKeyPatch
{
    public static void Postfix(RhythmGame.RhythmGameManager __instance, int trackIndex, ref bool __result)
    {
        if (!AutoRhythmGame || __instance == null) return;
        if (!__instance.isPlaying || __instance.isPaused) return;
        if (trackIndex < 0 || trackIndex >= 4) return;

        // 自动音游开启时，视为对应轨道按键（S/D/J/K）持续按下，保证长按音符稳定结算。
        __result = true;
    }
}

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

[HarmonyPatch(typeof(Glove), "OnUpdate")]
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

[HarmonyPatch(typeof(Hammer), "OnUpdate")]
public static class HammerPatchA
{
    public static float OriginalFullCD { get; set; }

    public static void Postfix(Hammer __instance)
    {
        try
        {
            if (__instance == null) return;
            if (OriginalFullCD <= 0 && __instance.fullCD > 0)
                OriginalFullCD = __instance.fullCD;
            __instance.gameObject.transform.GetChild(0).GetChild(0).gameObject.SetActive(!HammerNoCD);
            if (HammerFullCD > 0)
                __instance.fullCD = (float)HammerFullCD;
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

[HarmonyPatch(typeof(Hammer), "Start")]
public static class HammerPatchB
{
    public static void Postfix(Hammer __instance)
    {
        try
        {
            if (__instance != null && __instance.fullCD > 0 && HammerPatchA.OriginalFullCD <= 0)
                HammerPatchA.OriginalFullCD = __instance.fullCD;
        }
        catch { }

        GameObject obj = new("ModifierHammerCD");
        var text = obj.AddComponent<TextMeshProUGUI>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts/ContinuumBold SDF");
        text.color = new Color(0.5f, 0.8f, 1f);
        obj.transform.SetParent(__instance.GameObject().transform);
        obj.transform.localScale = new Vector3(2f, 2f, 2f);
        obj.transform.localPosition = new Vector3(107, 0, 0);
    }
}

[HarmonyPatch(typeof(Wheel), "OnUpdate")]
public static class WheelPatchA
{
    public static void Postfix(Wheel __instance)
    {
        try
        {
            if (__instance == null) return;
            __instance.gameObject.transform.GetChild(0).gameObject.SetActive(!WheelNoCD);
            if (WheelNoCD)
            {
                __instance.CD = __instance.fullCD;
                if (__instance.cdMask != null)
                    __instance.cdMask.gameObject.SetActive(false);
            }

            var cdChild = __instance.transform.FindChild("ModifierWheelCD");
            if (cdChild == null)
            {
                GameObject obj = new("ModifierWheelCD");
                var text = obj.AddComponent<TextMeshProUGUI>();
                text.font = Resources.Load<TMP_FontAsset>("Fonts/ContinuumBold SDF");
                text.color = new Color(0.5f, 0.8f, 1f);
                obj.transform.SetParent(__instance.GameObject().transform);
                obj.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
                obj.transform.localPosition = new Vector3(27.653f, 0, 0);
                cdChild = __instance.transform.FindChild("ModifierWheelCD");
            }

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
                    Time.timeScale = GameAPP.config != null ? GameAPP.config.gameSpeed : 1f;
                }
            }
        }

        if (__instance.buttonNumber == 13) BottomEnabled = GameObject.Find("Bottom") is not null;
    }
}

[HarmonyPatch(typeof(PlayerShootingMenu), nameof(PlayerShootingMenu.Refresh))]
public static class PlayerShootingMenuRefreshPatch
{
    public static void Postfix()
    {
        try
        {
            SyncInGameBuffs();
        }
        catch (System.Exception ex)
        {
            MLogger?.LogError($"[PVZRHTools] PlayerShootingMenuRefreshPatch 异常: {ex.Message}\n{ex.StackTrace}");
        }
    }
}

[HarmonyPatch(typeof(InitBoard))]
public static class InitBoardPatch
{
    [HarmonyPrefix]
    [HarmonyPatch("ShowBottom")]
    public static void PreShowBottom()
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

        if (Hammer.Instance != null)
            HammerPatchA.OriginalFullCD = Hammer.Instance.fullCD;
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
/// 3.7 诅咒免疫 - EffectManager.SetEffect(Plant, EffectType.Curse, ...)
/// 拦截所有通过 Effect 体系施加的 PlantCurseEffect。
/// </summary>
[HarmonyPatch(typeof(EffectManager), nameof(EffectManager.SetEffect), new Type[] { typeof(Plant), typeof(EffectType), typeof(float), typeof(float) })]
public static class EffectManagerSetCurseImmunityPatch
{
    [HarmonyPrefix]
    public static bool Prefix(EffectType effectType, ref bool __result)
    {
        if (!CurseImmunity)
            return true;

        if (effectType != EffectType.Curse)
            return true;

        __result = false;
        return false;
    }
}

/// <summary>
/// 3.7 诅咒免疫 - Plant.TakeDamage
/// 受伤时立即清除已存在的诅咒 Effect。
/// </summary>
[HarmonyPatch(typeof(Plant), nameof(Plant.TakeDamage), new Type[] { typeof(int), typeof(IDamageMaker), typeof(DamageType), typeof(PlantType), typeof(bool) })]
public static class PlantTakeDamageCurseImmunityPatch
{
    [HarmonyPrefix]
    public static void Prefix(Plant __instance)
    {
        if (!CurseImmunity)
            return;

        GameApiCompat.RemovePlantCurseEffect(__instance);
    }
}

/// <summary>
/// 诅咒免疫补丁 - UltimateHorse.GetDamage（3.6 及以前遗留，3.7 诅咒已改走 SetEffect）
/// </summary>
[HarmonyPatch(typeof(UltimateHorse), nameof(UltimateHorse.GetDamage), new Type[] { typeof(int), typeof(DamageType), typeof(bool), typeof(PlantType) })]
public static class UltimateHorseGetDamagePatch
{
    [HarmonyPrefix]
    public static bool Prefix(UltimateHorse __instance)
    {
        if (!CurseImmunity) return true;
        try
        {
            if (__instance != null)
                GameApiCompat.ClearCursedPlants(__instance);
        }
        catch { }
        return true;
    }
}

/// <summary>
/// 诅咒免疫补丁 - SuperLadderZombie（3.7 已移除 GetDamage 重写，改挂 Zombie.GetDamage）
/// 有梯子时跳过诅咒相关伤害计算
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.GetDamage), new Type[] { typeof(int), typeof(DamageType), typeof(bool), typeof(PlantType) })]
public static class SuperLadderZombieGetDamagePatch
{
    private static System.Reflection.FieldInfo? _ladderField;

    [HarmonyPrefix]
    public static bool Prefix(Zombie __instance, int theDamage, ref int __result)
    {
        if (!CurseImmunity) return true;
        try
        {
            if (__instance is not SuperLadderZombie) return true;

            _ladderField ??= typeof(SuperLadderZombie).GetField("ladder",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (_ladderField?.GetValue(__instance) != null)
            {
                __result = theDamage;
                return false;
            }
        }
        catch { }
        return true;
    }
}

/// <summary>
/// 诅咒免疫补丁 - Zombie.TakeDamage (3.7 五参数版本)
/// 通用诅咒免疫，清除僵尸的诅咒植物列表
/// 同时处理僵尸限伤200功能和击杀升级功能
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.TakeDamage), new Type[] { typeof(int), typeof(IDamageMaker), typeof(DamageType), typeof(PlantType), typeof(bool) })]
public static class ZombieTakeDamageCursePatch
{
    private static System.Reflection.FieldInfo? _cachedCursedPlantsField = null;
    
    [HarmonyPrefix]
    public static bool Prefix(Zombie __instance, ref int theDamage, IDamageMaker damageFrom, DamageType theDamageType, PlantType reportType, bool fix)
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
            
            // 处理诅咒免疫（3.7：移除 PlantCurseEffect，不再仅重置贴图颜色）
            if (CurseImmunity)
            {
                _curseClearTimer += Time.deltaTime;
                if (_curseClearTimer >= _curseClearInterval)
                {
                    _curseClearTimer = 0f;
                    RemoveCurseFromAllPlants();
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
                if (__instance.config != null && __instance.config.waveInterval > NewZombieUpdateCD)
                {
                    __instance.config.waveInterval = NewZombieUpdateCD;
                }
            }
        }
        catch { }
    }
    
    private static void RemoveCurseFromAllPlants()
    {
        try
        {
            if (Board.Instance == null) return;

            var allPlants = Lawnf.GetAllPlants();
            if (allPlants == null) return;

            foreach (var plant in allPlants)
            {
                if (plant != null && plant.thePlantHealth > 0)
                    GameApiCompat.RemovePlantCurseEffect(plant);
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

// 3.7：Zombie 基类不再有 OnTriggerStay2D，踩踏免疫由 TypeMgrUncrashablePlantPatch 处理。
#if false
[HarmonyPatch(typeof(Zombie), nameof(Zombie.OnTriggerStay2D))]
public static class ZombieOnTriggerStay2DTramplePatch
{
    [HarmonyPrefix]
    public static bool Prefix(Collider2D collision, Zombie __instance) => true;
}
#endif

#endregion

// 3.6：僵尸红温/寒冷等状态从字段改为 Effect 体系，旧实现依赖已移除字段。
// 先禁用该整段补丁，避免编译失败；后续若需要可按 EffectType 重新实现。
#if false

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

#endif

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
    public static bool Prefix(Board __instance, ref int theColumn, ref int theRow, ref bool fromWheat, ref Plant __result)
    {
        if (fromWheat && LockWheat >= 0)
        {
            Plant plantObject = CreatePlant.Instance.SetPlant(
                theColumn, 
                theRow, 
                (PlantType)LockWheat
            );

            if (plantObject is not null)
            {
                plantObject.wheatType = 1;
            }
            
            if (plantObject == null)
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

[HarmonyPatch(typeof(LevelProgress), "Awake")]
public static class ProgressMgrPatchA
{
    public static void Postfix(LevelProgress __instance)
    {
        GameObject obj = new("ModifierGameInfo");
        var text = obj.AddComponent<TextMeshProUGUI>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts/ContinuumBold SDF");
        text.color = new Color(0, 1, 1);
        obj.transform.SetParent(__instance.GameObject().transform);
        obj.transform.localScale = new Vector3(0.4f, 0.2f, 0.2f);
        obj.transform.localPosition = new Vector3(9f, 15f, 0);
        obj.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 50);
    }
}

[HarmonyPatch(typeof(LevelProgress), "Update")]
public static class ProgressMgrPatchB
{
    public static void Postfix(LevelProgress __instance)
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

/// <summary>
/// 旅行刷新补丁
/// 确保无限刷新在旅行投资的新游戏模式中也能生效
/// </summary>
[HarmonyPatch(typeof(TravelRefresh))]
public static class TravelRefreshPatch
{
    /// <summary>
    /// 在 TravelRefresh.Awake 时设置 refreshTimes，确保旅行投资模式中也能生效
    ///  TravelRefreshOnMouseUpAsButtonPatch.PrefixStart
    /// </summary>
    [HarmonyPatch("Awake")]
    [HarmonyPrefix]
    public static void PrefixStart(TravelRefresh __instance)
    {
        try
        {
            if (UnlimitedRefresh || BuffRefreshNoLimit)
            {
                __instance.refreshTimes = 9999999;
                if (__instance.text != null)
                {
                    __instance.text.text = "∞";
                }
            }
        }
        catch (System.Exception ex)
        {
            MLogger?.LogWarning($"[PVZRHTools] TravelRefresh.Awake 补丁异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 在点击刷新按钮前设置 refreshTimes
    ///  TravelRefreshOnMouseUpAsButtonPatch.Prefix
    /// </summary>
    [HarmonyPatch("OnMouseUpAsButton")]
    [HarmonyPrefix]
    public static void Prefix(TravelRefresh __instance)
    {
        try
        {
            if (UnlimitedRefresh || BuffRefreshNoLimit)
            {
                __instance.refreshTimes = 9999999;
            }
        }
        catch (System.Exception ex)
        {
            MLogger?.LogWarning($"[PVZRHTools] TravelRefresh.OnMouseUpAsButton Prefix 补丁异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 在点击刷新按钮后设置 refreshTimes 和文本
    ///  TravelRefreshOnMouseUpAsButtonPatch.Postfix
    /// </summary>
    [HarmonyPatch("OnMouseUpAsButton")]
    [HarmonyPostfix]
    public static void Postfix(TravelRefresh __instance)
    {
        try
        {
            if (UnlimitedRefresh || BuffRefreshNoLimit)
            {
                __instance.refreshTimes = 9999999;
                if (__instance.text != null)
                {
                    __instance.text.text = "∞";
    }
}
        }
        catch (System.Exception ex)
        {
            MLogger?.LogWarning($"[PVZRHTools] TravelRefresh.OnMouseUpAsButton Postfix 补丁异常: {ex.Message}");
        }
    }
}

/// <summary>
/// 旅行商店补丁
/// 确保无限刷新在旅行投资的新游戏模式中也能生效
/// </summary>
[HarmonyPatch(typeof(TravelStore))]
public static class TravelStoreUpdatePatch
{
    /// <summary>
    /// 在 TravelStore.Update 时设置 refreshCount = 0，确保旅行投资模式中也能生效
    ///TravelStoreUpdatePatch.Postfix
    /// </summary>
    [HarmonyPatch("Update")]
    [HarmonyPostfix]
    public static void PostfixUpdate(TravelStore __instance)
    {
        try
        {
            if (UnlimitedRefresh || BuffRefreshNoLimit)
            {
                // 直接设置 refreshCount = 0
                if (__instance != null)
                {
                    __instance.refreshCount = 0;
                }
            }
        }
        catch (System.Exception ex)
        {
            MLogger?.LogWarning($"[PVZRHTools] TravelStore.Update 补丁异常: {ex.Message}");
        }
    }

}

[HarmonyPatch(typeof(PlayerShootingMenu), nameof(PlayerShootingMenu.Refresh))]
public static class ShootingMenuPatch
{
    private static System.Reflection.FieldInfo? _playerField;

    [HarmonyPrefix]
    public static void Prefix(PlayerShootingMenu __instance)
    {
        if (!UnlimitedRefresh && !BuffRefreshNoLimit) return;
        try
        {
            _playerField ??= typeof(PlayerShootingMenu).GetField("player",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (_playerField?.GetValue(__instance) is Player player)
                player.refreshCount = 9999999;
            if (ShootingManager.Instance != null)
                ShootingManager.Instance.refreshCount = int.MaxValue;
        }
        catch { }
    }

    [HarmonyPostfix]
    public static void Postfix(PlayerShootingMenu __instance)
    {
        if (!UnlimitedRefresh && !BuffRefreshNoLimit) return;
        try
        {
            _playerField ??= typeof(PlayerShootingMenu).GetField("player",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (_playerField?.GetValue(__instance) is Player player)
                player.refreshCount = 9999999;
            if (ShootingManager.Instance != null)
                ShootingManager.Instance.refreshCount = int.MaxValue;
        }
        catch { }
    }
}

/// <summary>
/// 诸神：进化等模式使用 MultipleChoiceMenu 刷新，3.7 需单独补丁。
/// 无尽模式 ShowBuff 在 OptionCount 不超过 maxPlantCount 时会传 interactable=false，
/// SetRefreshable 会直接禁用按钮；Refresh 只更新次数文本，不会重新启用按钮。
/// </summary>
[HarmonyPatch(typeof(MultipleChoiceMenu))]
public static class MultipleChoiceMenuRefreshPatch
{
    private static System.Reflection.FieldInfo? _refreshCountField;

    [HarmonyPrefix]
    [HarmonyPatch("SetRefreshable", new Type[] { typeof(bool), typeof(int), typeof(bool), typeof(bool) })]
    public static void PrefixSetRefreshable(ref int refreshCount, ref bool interactable)
    {
        if (!ShouldFixGodEvolutionRefreshButton) return;
        refreshCount = GetGodEvolutionMenuRefreshCount();
        interactable = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch("SetRefreshable", new Type[] { typeof(bool), typeof(int), typeof(bool), typeof(bool) })]
    public static void PostfixSetRefreshable(MultipleChoiceMenu __instance)
    {
        if (!ShouldFixGodEvolutionRefreshButton) return;
        try
        {
            if (__instance?.refreshButton != null)
                __instance.refreshButton.Interactable = true;
        }
        catch { }
    }

    [HarmonyPrefix]
    [HarmonyPatch("Refresh")]
    public static void PrefixRefresh(MultipleChoiceMenu __instance)
    {
        if (!ShouldFixGodEvolutionRefreshButton) return;
        try
        {
            _refreshCountField ??= typeof(MultipleChoiceMenu).GetField("refreshCount",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            _refreshCountField?.SetValue(__instance, GetGodEvolutionMenuRefreshCount());
        }
        catch { }
    }

    [HarmonyPostfix]
    [HarmonyPatch("Refresh")]
    public static void PostfixRefresh(MultipleChoiceMenu __instance)
    {
        if (!ShouldFixGodEvolutionRefreshButton) return;
        try
        {
            _refreshCountField ??= typeof(MultipleChoiceMenu).GetField("refreshCount",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var count = GetGodEvolutionMenuRefreshCount();
            _refreshCountField?.SetValue(__instance, count);
            if (__instance?.refreshButton != null)
                __instance.refreshButton.Interactable = true;
        }
        catch { }
    }
}

[HarmonyPatch(typeof(ShootingManager), "ShowBuff")]
public static class ShootingManagerShowBuffRefreshPatch
{
    [HarmonyPrefix]
    public static void Prefix(ShootingManager __instance)
    {
        if (__instance == null) return;
        GodEvolutionHelper.ApplySettings(__instance);
        if (!ShouldFixGodEvolutionRefreshButton) return;
        __instance.refreshCount = GetGodEvolutionMenuRefreshCount();
    }
}

#region GodEvolution - 诸神：进化

public static class GodEvolutionHelper
{
    private static FieldInfo? _appearSuperQualitativeField;
    private static FieldInfo? _uncrashableField;
    private static FieldInfo? _qualityWeightsField;
    private static MethodInfo? _advQualitativeCallbackMethod;

    [ThreadStatic] private static bool _inRegisterCoreBuff;
    [ThreadStatic] private static float _savedLucky;
    [ThreadStatic] private static ShootingManager? _coreBuffMgr;

    private static readonly int[] ShootingQualitativeBuffIds = { 12000, 12001, 12002, 12003, 12004, 12005 };

    public static bool InRegisterCoreBuff => _inRegisterCoreBuff;

    public static float GetLuckyMultiplier()
    {
        if (!GodEvolutionLuckyEnabled) return 1f;
        return Mathf.Max(0.01f, GodEvolutionLucky);
    }

    public static bool IsQualitativeBuff(BaseBuff buff)
    {
        if (buff == null) return false;
        string typeName = buff.GetType().Name;
        return typeName.Contains("UniqueUpgrade", StringComparison.Ordinal)
               || typeName.Contains("SuperUpgrade", StringComparison.Ordinal)
               || typeName.Contains("SuperBuff", StringComparison.Ordinal)
               || typeName.Contains("SuperForce", StringComparison.Ordinal);
    }

    public static bool IsSuperQualitativeBuff(BaseBuff buff) => IsQualitativeBuff(buff);

    public static bool GetAppearSuperQualitative(ShootingManager mgr)
    {
        try
        {
            return (_appearSuperQualitativeField ??= typeof(ShootingManager).GetField("appearSuperQualitative",
                BindingFlags.Instance | BindingFlags.NonPublic))?.GetValue(mgr) is true;
        }
        catch
        {
            return false;
        }
    }

    public static void SetAppearSuperQualitative(ShootingManager mgr, bool value)
    {
        try
        {
            (_appearSuperQualitativeField ??= typeof(ShootingManager).GetField("appearSuperQualitative",
                BindingFlags.Instance | BindingFlags.NonPublic))?.SetValue(mgr, value);
        }
        catch { }
    }

    public static void BeginCoreBuffForce(ShootingManager mgr)
    {
        if (!GodEvolutionForceSuperQuality || mgr == null) return;
        _inRegisterCoreBuff = true;
        _coreBuffMgr = mgr;
        _savedLucky = mgr.Lucky;
        mgr.Lucky = 99999f;
    }

    public static void EndCoreBuffForce()
    {
        if (!_inRegisterCoreBuff) return;
        try
        {
            if (_coreBuffMgr != null)
                _coreBuffMgr.Lucky = _savedLucky;
        }
        catch { }
        _inRegisterCoreBuff = false;
        _coreBuffMgr = null;
    }

    public static void TryForceAdvQualitativeOption(ShootingManager mgr, MultipleChoiceMenu menu)
    {
        if (!GodEvolutionForceSuperQuality || mgr.endless || menu == null) return;
        if (GetAppearSuperQualitative(mgr)) return;

        SetAppearSuperQualitative(mgr, true);

        if (TryRegisterAdvQualitativeViaGameCallback(mgr, menu))
            return;

        RegisterAdvQualitativeViaTravelMgr(menu);
    }

    private static bool TryRegisterAdvQualitativeViaGameCallback(ShootingManager mgr, MultipleChoiceMenu menu)
    {
        try
        {
            _advQualitativeCallbackMethod ??= FindAdvQualitativeCallbackMethod();
            if (_advQualitativeCallbackMethod == null) return false;

            var callback = Delegate.CreateDelegate(typeof(UnityAction), mgr, _advQualitativeCallbackMethod);
            menu.RegisterOption("超质变", "随机获得一个诸神质变词条", (UnityAction)callback,
                (PlantType)254, ZombieType.Nothing, Quality.diamond, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static MethodInfo? FindAdvQualitativeCallbackMethod()
    {
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(ShootingManager)))
        {
            string name = method.Name;
            if (!name.Contains("RegisterOtherBuff", StringComparison.Ordinal)) continue;
            if (name.Contains("55_14", StringComparison.Ordinal)
                || name.Contains("55_13", StringComparison.Ordinal)
                || name.Contains("55_15", StringComparison.Ordinal))
                return method;
        }
        return null;
    }

    private static void RegisterAdvQualitativeViaTravelMgr(MultipleChoiceMenu menu)
    {
        try
        {
            int buffId = ShootingQualitativeBuffIds[Random.Range(0, ShootingQualitativeBuffIds.Length)];
            var buff = (AdvBuff)buffId;
            string title = "超质变";
            string desc = "获得诸神质变词条";
            if (TravelDictionary.advancedBuffsText != null
                && TravelDictionary.advancedBuffsText.TryGetValue(buff, out var buffText)
                && !string.IsNullOrEmpty(buffText))
            {
                title = buffText;
                desc = buffText;
            }

            AdvBuff captured = buff;
            UnityAction callback = (UnityAction)(Action)(() =>
            {
                try
                {
                    TravelMgr.Instance?.GetNormalBuff(captured);
                }
                catch { }
            });

            menu.RegisterOption(title, desc, callback, (PlantType)254, ZombieType.Nothing, Quality.diamond, true);
        }
        catch { }
    }

    public static void ApplySettings(ShootingManager mgr)
    {
        if (mgr == null) return;
        try
        {
            if (GodEvolutionLuckyEnabled)
                mgr.Lucky = GodEvolutionLucky;
            if (GodEvolutionDifficultyEnabled)
                mgr.difficulty = GodEvolutionDifficulty;
            if (ShouldFixGodEvolutionRefreshButton)
                mgr.refreshCount = GetGodEvolutionMenuRefreshCount();
            if (GodEvolutionMaxPlantCountEnabled)
                mgr.maxPlantCount = GodEvolutionMaxPlantCount;
            if (GodEvolutionOptionCountEnabled)
                mgr.optionCount = GodEvolutionOptionCount;
            if (GodEvolutionUpgradeBuffChanceEnabled || GodEvolutionFreeUpgradeQuality)
                mgr.upgradeBuffChance = GodEvolutionFreeUpgradeQuality ? 999999 : GodEvolutionUpgradeBuffChance;
            if (GodEvolutionSuperUpgrade)
                mgr.superUpgrade = true;
            if (GodEvolutionUncrashable)
                (_uncrashableField ??= typeof(ShootingManager).GetField("uncrashable",
                    BindingFlags.Instance | BindingFlags.NonPublic))?.SetValue(mgr, true);
            SyncQualityWeights(mgr);
        }
        catch { }
    }

    private static void SyncQualityWeights(ShootingManager mgr)
    {
        if (!GodEvolutionQualityWeightEnabled) return;
        try
        {
            _qualityWeightsField ??= typeof(ShootingManager).GetField("qualityWeights",
                BindingFlags.Instance | BindingFlags.NonPublic);
            object? weightsObj = _qualityWeightsField?.GetValue(mgr);
            if (weightsObj == null) return;
            float luckyMult = GetLuckyMultiplier();
            SetQualityWeight(weightsObj, Quality.Default, GodEvolutionQualityDefault);
            SetQualityWeight(weightsObj, Quality.silver, GodEvolutionQualitySilver * luckyMult);
            SetQualityWeight(weightsObj, Quality.gold, GodEvolutionQualityGold * luckyMult);
            SetQualityWeight(weightsObj, Quality.diamond, GodEvolutionQualityDiamond * luckyMult);
        }
        catch { }
    }

    private static void SetQualityWeight(object weights, Quality quality, float value)
    {
        var setItem = weights.GetType().GetMethod("set_Item");
        setItem?.Invoke(weights, new object[] { quality, value });
    }

    public static Quality RollQuality()
    {
        float luckyMult = GetLuckyMultiplier();
        float defaultWeight = GodEvolutionQualityDefault;
        float silverWeight = GodEvolutionQualitySilver * luckyMult;
        float goldWeight = GodEvolutionQualityGold * luckyMult;
        float diamondWeight = GodEvolutionQualityDiamond * luckyMult;
        float total = defaultWeight + silverWeight + goldWeight + diamondWeight;
        if (total <= 0f) return Quality.Default;
        var r = Random.Range(0f, total);
        if (r < defaultWeight) return Quality.Default;
        r -= defaultWeight;
        if (r < silverWeight) return Quality.silver;
        r -= silverWeight;
        if (r < goldWeight) return Quality.gold;
        return Quality.diamond;
    }
}

[HarmonyPatch(typeof(ShootingManager), "Update")]
public static class GodEvolutionUpdatePatch
{
    public static void Postfix(ShootingManager __instance)
    {
        GodEvolutionHelper.ApplySettings(__instance);
    }
}

[HarmonyPatch(typeof(ShootingManager), "GetRandomQuality")]
public static class GodEvolutionGetRandomQualityPatch
{
    public static bool Prefix(ref Quality __result)
    {
        if (!GodEvolutionQualityWeightEnabled) return true;
        __result = GodEvolutionHelper.RollQuality();
        return false;
    }
}

[HarmonyPatch(typeof(ShootingManager), "RegisterCoreBuff")]
public static class GodEvolutionRegisterCoreBuffPatch
{
    [HarmonyPrefix]
    public static void Prefix(ShootingManager __instance)
    {
        GodEvolutionHelper.ApplySettings(__instance);
        GodEvolutionHelper.BeginCoreBuffForce(__instance);
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(Exception? __exception)
    {
        GodEvolutionHelper.EndCoreBuffForce();
        return __exception;
    }
}

[HarmonyPatch(typeof(ShootingManager), "RegisterOtherBuff")]
public static class GodEvolutionRegisterOtherBuffPatch
{
    [HarmonyPrefix]
    public static void Prefix(ShootingManager __instance)
    {
        if (__instance == null) return;
        GodEvolutionHelper.ApplySettings(__instance);
        if (!GodEvolutionForceSuperQuality || __instance.endless) return;
        GodEvolutionHelper.SetAppearSuperQualitative(__instance, false);
    }

    [HarmonyPostfix]
    public static void Postfix(ShootingManager __instance, MultipleChoiceMenu menu)
    {
        GodEvolutionHelper.TryForceAdvQualitativeOption(__instance, menu);
    }
}

[HarmonyPatch(typeof(Lawnf), nameof(Lawnf.TravelUltimate))]
public static class GodEvolutionTravelUltimatePatch
{
    public static bool Prefix(ref bool __result)
    {
        if (!GodEvolutionForceSuperQuality || !GodEvolutionHelper.InRegisterCoreBuff) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Random), "Range", typeof(float), typeof(float))]
public static class GodEvolutionRandomRangePatch
{
    public static bool Prefix(float minInclusive, float maxInclusive, ref float __result)
    {
        if (!GodEvolutionForceSuperQuality || !GodEvolutionHelper.InRegisterCoreBuff) return true;
        if (minInclusive != 0f || maxInclusive != 1f) return true;
        __result = 0f;
        return false;
    }
}

[HarmonyPatch(typeof(ShootingManager), "get_LuckyMultiplier")]
public static class GodEvolutionLuckyMultiplierPatch
{
    public static bool Prefix(ref float __result)
    {
        if (!GodEvolutionLuckyEnabled) return true;
        __result = GodEvolutionHelper.GetLuckyMultiplier();
        return false;
    }
}

[HarmonyPatch(typeof(BaseBuff), "get_AppearWeight")]
public static class GodEvolutionAppearWeightPatch
{
    public static void Postfix(BaseBuff __instance, ref float __result)
    {
        if (__instance == null || __result <= 0f) return;
        if (GodEvolutionLuckyEnabled)
            __result *= GodEvolutionHelper.GetLuckyMultiplier();
        if (GodEvolutionQualityWeightEnabled && GodEvolutionHelper.IsQualitativeBuff(__instance))
        {
            float tierBoost = GodEvolutionQualityDiamond + GodEvolutionQualityGold;
            if (tierBoost > 0f)
                __result *= tierBoost;
        }
        if (GodEvolutionForceSuperQuality && GodEvolutionHelper.IsQualitativeBuff(__instance))
            __result = Mathf.Max(__result, 1f);
    }
}

[HarmonyPatch(typeof(BaseBuff), "get_CanAppear")]
public static class GodEvolutionCanAppearPatch
{
    public static void Postfix(BaseBuff __instance, ref bool __result)
    {
        if (!GodEvolutionForceSuperQuality || __instance == null || __result) return;
        if (GodEvolutionHelper.IsQualitativeBuff(__instance))
            __result = true;
    }
}

[HarmonyPatch(typeof(ShootingManager), "GetQualityValue", new[] { typeof(float), typeof(Quality) })]
public static class GodEvolutionGetQualityValueFloatPatch
{
    public static void Postfix(ref float __result)
    {
        if (GodEvolutionDamageMultiplierEnabled)
            __result *= GodEvolutionDamageMultiplier;
    }
}

[HarmonyPatch(typeof(ShootingManager), "GetQualityValue", new[] { typeof(int), typeof(Quality) })]
public static class GodEvolutionGetQualityValueIntPatch
{
    public static void Postfix(ref int __result)
    {
        if (GodEvolutionDamageMultiplierEnabled)
            __result = Mathf.RoundToInt(__result * GodEvolutionDamageMultiplier);
    }
}

#endregion

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
[HarmonyPatch(typeof(Lawnf), nameof(Lawnf.CheckIfPlantUnlock))]
public static class LawnfCheckIfPlantUnlockPatch
{
    public static void Postfix(ref UnlockType __result)
    {
        if (UnlockAllPlants)
        {
            __result = UnlockType.Unlocked;
        }
    }
}

[HarmonyPatch(typeof(CreatePlant), nameof(CreatePlant.LimTravel))]
public static class CreatePlantLimTravelUnlockAllPlantsPatch
{
    public static void Postfix(ref bool __result)
    {
        if (UnlockAllPlants)
        {
            __result = false;
        }
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

/// <summary>
/// 取消失败处理期间屏蔽进家判负，避免 timeScale 被反复置 0。
/// </summary>
[HarmonyPatch(typeof(GameLose), "OnTriggerEnter2D")]
public static class GameLoseSuppressDuringCancelPatch
{
    public static bool Prefix()
    {
        return !DataProcessor.SuppressEnterLoseMenu;
    }
}

[HarmonyPatch(typeof(GameLose), "HandleGameLose")]
public static class GameLoseHandleSuppressDuringCancelPatch
{
    public static bool Prefix()
    {
        return !DataProcessor.SuppressEnterLoseMenu;
    }
}

/// <summary>
/// 原版 EnterLoseMenu 会清空并销毁整个 UI 栈（含 InGameUI），导致取消失败后无法保留卡槽等状态。
/// 改为仅叠加 LoseMenu，并在进入失败前抓取局内快照供兜底恢复。
/// </summary>
[HarmonyPatch(typeof(UIMgr), "EnterLoseMenu")]
public static class EnterLoseMenuPreservePatch
{
    public static bool Prefix(string reason)
    {
        if (DataProcessor.SuppressEnterLoseMenu)
            return false;

        try
        {
            DataProcessor.CapturePreLoseSnapshot();
            DataProcessor.EnterLoseMenuPreserveStack(reason);
            return false;
        }
        catch (System.Exception ex)
        {
            MLogger?.LogError($"[PVZRHTools] EnterLoseMenuPreservePatch 异常，尝试兜底保留 UI 栈: {ex.Message}");
            try
            {
                DataProcessor.EnterLoseMenuPreserveStack(reason);
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}

[HarmonyPatch(typeof(UIMgr), "EnterMainMenu")]
public static class UIMgrPatch
{
    public static void Postfix()
    {
        GameObject obj1 = new("ModifierInfo");
        var text1 = obj1.AddComponent<TextMeshProUGUI>();
        text1.font = Resources.Load<TMP_FontAsset>("Fonts/ContinuumBold SDF");
        text1.color = new Color(1f, 0.41f, 0.71f, 1);
        text1.text = "修改器原创@Infinite75，\n这是@梧萱梦汐X从@听雨夜荷的fork接手的分支\n若存在任何付费/要求三连+关注/私信发链接的情况\n说明你被盗版骗了，请注意隐私和财产安全！！！\n此信息仅在游戏主菜单和修改窗口显示";
        obj1.transform.SetParent(GameObject.Find("Leaves").transform);
        obj1.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        obj1.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 50);
        obj1.transform.localPosition = new Vector3(-345.5f, 20f, 0);
        
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
                Plant gameObject =
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
                    if (gameObject != null)
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
/// 在Awake方法前临时修改GameStatus，并临时修改BoardType为神秘模式(7)
/// </summary>
[HarmonyPatch(typeof(AbyssSwordStar))]
public static class AbyssSwordStarUnlockPatch
{
    public struct AwakeState
    {
        public GameStatus GameStatus;
        public LevelType BoardType;
    }

    [HarmonyPrefix]
    [HarmonyPatch("Awake")]
    public static void PreAwake(AbyssSwordStar __instance, ref AwakeState __state)
    {
        __state = new AwakeState
        {
            GameStatus = GameAPP.theGameStatus,
            BoardType = GameAPP.theBoardType
        };
        if (!UnlockRedCardPlants) return;

        try
        {
            var existing = AbyssSwordStar.Instance;
            if (existing != null && existing != __instance && existing.gameObject != null)
                existing.Die(Plant.DieReason.Default);
        }
        catch { }

        GameAPP.theGameStatus = (GameStatus)(-1);
        GameAPP.theBoardType = (LevelType)7; // 神秘模式
    }

    [HarmonyPostfix]
    [HarmonyPatch("Awake")]
    public static void PostAwake(ref AwakeState __state)
    {
        GameAPP.theGameStatus = __state.GameStatus;
        GameAPP.theBoardType = __state.BoardType;
    }

    [HarmonyFinalizer]
    [HarmonyPatch("Awake")]
    public static Exception FinalizerAwake(Exception __exception)
    {
        if (__exception != null && UnlockRedCardPlants)
        {
            try
            {
                MLogger?.LogWarning(
                    $"[PVZRHTools] AbyssSwordStar.Awake 异常已忽略: {__exception.GetType().Name} - {__exception.Message}");
            }
            catch { }

            return null;
        }

        return __exception;
    }
}

/// <summary>
/// 究极速射樱桃射手(UltimateMinigun)补丁 - 取消红卡种植限制
/// 在构造函数前临时修改BoardTag.isTreasure为true
/// </summary>
[HarmonyPatch(typeof(UltimateMinigun), MethodType.Constructor)]
public static class UltimateMinigunUnlockPatch
{
    [HarmonyPrefix]
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

            zombie.ApplyDamage(DamageType.Normal, damage);
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

                z.ApplyDamage(DamageType.Normal, damage);
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
    /// <summary>诸神：进化专用无限刷新</summary>
    public static bool GodEvolutionUnlimitedRefresh { get; set; } = false;
    public static bool GodEvolutionFreeUpgradeQuality { get; set; } = false;
    public static bool GodEvolutionLuckyEnabled { get; set; } = false;
    public static float GodEvolutionLucky { get; set; } = 1f;
    public static bool GodEvolutionDifficultyEnabled { get; set; } = false;
    public static int GodEvolutionDifficulty { get; set; } = 0;
    public static bool GodEvolutionRefreshCountEnabled { get; set; } = false;
    public static int GodEvolutionRefreshCount { get; set; } = 9999999;
    public static bool GodEvolutionMaxPlantCountEnabled { get; set; } = false;
    public static int GodEvolutionMaxPlantCount { get; set; } = 99;
    public static bool GodEvolutionOptionCountEnabled { get; set; } = false;
    public static int GodEvolutionOptionCount { get; set; } = 3;
    public static bool GodEvolutionUpgradeBuffChanceEnabled { get; set; } = false;
    public static int GodEvolutionUpgradeBuffChance { get; set; } = 100;
    public static bool GodEvolutionSuperUpgrade { get; set; } = false;
    public static bool GodEvolutionForceSuperQuality { get; set; } = false;
    public static bool GodEvolutionUncrashable { get; set; } = false;
    public static bool GodEvolutionQualityWeightEnabled { get; set; } = false;
    public static float GodEvolutionQualityDefault { get; set; } = 1f;
    public static float GodEvolutionQualitySilver { get; set; } = 1f;
    public static float GodEvolutionQualityGold { get; set; } = 1f;
    public static float GodEvolutionQualityDiamond { get; set; } = 1f;
    public static bool GodEvolutionDamageMultiplierEnabled { get; set; } = false;
    public static float GodEvolutionDamageMultiplier { get; set; } = 1f;
    public static bool IsRefreshUnlimited =>
        UnlimitedRefresh || BuffRefreshNoLimit || GodEvolutionUnlimitedRefresh;

    /// <summary>诸神进化「锁定刷新次数」生效且次数大于 0</summary>
    public static bool GodEvolutionRefreshOverrideActive =>
        GodEvolutionRefreshCountEnabled && GodEvolutionRefreshCount > 0;

    /// <summary>需要修复诸神进化刷新按钮可点击性（无限刷新或锁定刷新次数）</summary>
    public static bool ShouldFixGodEvolutionRefreshButton =>
        IsRefreshUnlimited || GodEvolutionRefreshOverrideActive;

    public static int GetGodEvolutionMenuRefreshCount()
    {
        if (IsRefreshUnlimited) return 9999999;
        if (GodEvolutionRefreshOverrideActive) return GodEvolutionRefreshCount;
        return 0;
    }
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
    public static bool WheelNoCD { get; set; } = false;
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
    /// <summary>
    /// 高级词条当前状态字典（用于支持非连续的大ID词条）
    /// </summary>
    public static Dictionary<int, bool> InGameAdvBuffsDict { get; set; } = new Dictionary<int, bool>();
    public static bool[] InGameDebuffs { get; set; } = [];
    public static bool[] InGameUltiBuffs { get; set; } = [];
    
    /// <summary>
    /// 修改器“期望”的局内负面词条开关（用于锁定/强制应用，以避免被游戏状态同步覆盖）
    /// </summary>
    public static bool[] DesiredInGameDebuffs { get; set; } = [];
    
    /// <summary>
    /// 修改器“期望”的局内究极词条开关（用于锁定/强制应用，以避免被游戏状态同步覆盖）
    /// </summary>
    public static bool[] DesiredInGameUltiBuffs { get; set; } = [];
    
    /// <summary>
    /// 上一次应用时的“期望”究极词条状态，用于检测 true -> false 这种“用户取消勾选”的变化
    /// </summary>
    public static bool[] LastDesiredInGameUltiBuffs { get; set; } = [];
    
    /// <summary>
    /// 上一次应用时的高级词条期望状态（用于检测高级词条 true->false 的变化）
    /// </summary>
    public static Dictionary<int, bool> LastInGameAdvBuffsDict { get; set; } = new Dictionary<int, bool>();
    /// <summary>
    /// 投资词条（InvestBuff）当前配置状态
    /// </summary>
    public static bool[] InvestBuffs { get; set; } = [];
    /// <summary>
    /// 投资词条在游戏中的实际生效状态
    /// </summary>
    public static bool[] InGameInvestBuffs { get; set; } = [];
    
    /// <summary>
    /// 修改器“期望”的局内投资词条开关（用于锁定/强制应用，以避免被游戏状态同步覆盖）
    /// </summary>
    public static bool[] DesiredInGameInvestBuffs { get; set; } = [];
    
    /// <summary>
    /// 上一次应用时的“期望”负面词条状态，用于检测 true -> false 这种“用户取消勾选”的变化
    /// </summary>
    public static bool[] LastDesiredInGameDebuffs { get; set; } = [];
    
    /// <summary>
    /// 上一次应用时的“期望”投资词条状态，用于检测 true -> false 这种“用户取消勾选”的变化
    /// </summary>
    public static bool[] LastDesiredInGameInvestBuffs { get; set; } = [];
    
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
    // PvE 斗蛐蛐布阵：盲盒僵尸置顶（-1 表示不置顶，沿用游戏原始随机）
    public static int PvEBlindBoxZombie1 { get; set; } = -1;
    public static int PvEBlindBoxZombie2 { get; set; } = -1;
    public static int PvEBlindBoxZombie3 { get; set; } = -1;
    public static int PvEBlindBoxZombie4 { get; set; } = -1;
    public static int PvEBlindBoxZombie5 { get; set; } = -1;
    public static int PvEBlindBoxZombie6 { get; set; } = -1;

    /// <summary>
    /// PvE 斗蛐蛐布阵：记录 6 个盲盒僵尸实例ID与槽位号的映射
    /// 仅斗蛐蛐布阵时填充，死亡后会移除对应项。
    /// </summary>
    public static readonly Dictionary<int, int> PveBlindBoxSlotByInstance = new();

    /// <summary>
    /// 黄金盲盒僵尸：覆盖 FirstArmorFall，在开盒时按 PvE 槽2~5 指定僵尸生成并让自身死亡
    /// </summary>
    [HarmonyPatch(typeof(RandomZombie), "FirstArmorFall")]
    public static class RandomZombieFirstArmorFallPatch
    {
        public static bool Prefix(RandomZombie __instance)
        {
            if (!InGame() || Board.Instance == null || CreateZombie.Instance == null)
                return true;

            if (__instance == null)
                return true;

            int instId = __instance.GetInstanceID();
            if (!PveBlindBoxSlotByInstance.TryGetValue(instId, out int slot))
                return true;

            // 只处理 PvE 布阵中记录的 4 个黄金盲盒 (槽2~5)
            if (slot < 2 || slot > 5)
                return true;

            int targetId = slot switch
            {
                2 => PvEBlindBoxZombie2,
                3 => PvEBlindBoxZombie3,
                4 => PvEBlindBoxZombie4,
                5 => PvEBlindBoxZombie5,
                _ => -1
            };

            // 未配置则走原逻辑
            if (targetId < 0)
                return true;

            // 用完就移除，避免后续其它逻辑再次误用
            PveBlindBoxSlotByInstance.Remove(instId);

            var axis = __instance.axis;
            if (axis == null)
                return true;

            var pos = axis.position;
            int row = __instance.theZombieRow;
            var targetType = (ZombieType)targetId;

            if (__instance.isMindControlled)
                CreateZombie.Instance.SetZombieWithMindControl(row, targetType, pos.x);
            else
                CreateZombie.Instance.SetZombie(row, targetType, pos.x);

            // 模拟原逻辑的结尾：播放粒子并让自身死亡
            try
            {
                // 原逻辑中在 FirstArmorFall 里会调用 CreateParticle.SetParticle(11, pos+偏移, row, true)
                // 这里简单复用 Lawnf/Board 的通用粒子接口（如果可用），否则忽略粒子表现
                var go = __instance.GameObject();
                if (go != null)
                {
                    var p = go.transform.position;
                    CreateParticle.SetParticle(11, new Vector2(p.x, p.y + 1f), row, true);
                }
            }
            catch
            {
                // 粒子失败不影响主要逻辑
            }

            __instance.Die(2);
            return false;
        }
    }

    /// <summary>
    /// 钻石盲盒僵尸：覆盖 SetRandomZombie，实现按 PvE 槽1 指定僵尸生成
    /// </summary>
    [HarmonyPatch(typeof(DiamondRandomZombie), nameof(DiamondRandomZombie.SetRandomZombie))]
    public static class DiamondRandomZombiePatch
    {
        public static bool Prefix(DiamondRandomZombie __instance, ref Zombie __result, Vector3 pos)
        {
            if (!InGame() || Board.Instance == null || CreateZombie.Instance == null)
                return true;

            int instId = __instance.GetInstanceID();
            if (!PveBlindBoxSlotByInstance.TryGetValue(instId, out int slot) || slot != 1)
                return true; // 只处理 PvE 布阵中的那一个钻石盲盒

            if (PvEBlindBoxZombie1 < 0)
                return true; // 未配置则保持原逻辑

            // 用完就移除，避免后续其它逻辑再次误用
            PveBlindBoxSlotByInstance.Remove(instId);

            float x = pos.x;
            var targetType = (ZombieType)PvEBlindBoxZombie1;

            if (!__instance.isMindControlled)
                __result = CreateZombie.Instance.SetZombie(__instance.theZombieRow, targetType, x);
            else
                __result = CreateZombie.Instance.SetZombieWithMindControl(__instance.theZombieRow, targetType, x);

            // 不再走原始随机逻辑
            return false;
        }
    }

    /// <summary>
    /// 巨人盲盒僵尸：覆盖 FirstArmorFall，在盔甲掉落时按 PvE 槽6 指定僵尸生成并让自身死亡
    /// </summary>
    [HarmonyPatch(typeof(RandomGargantuar), nameof(RandomGargantuar.FirstArmorFall))]
    public static class RandomGargantuarFirstArmorFallPatch
    {
        public static bool Prefix(RandomGargantuar __instance)
        {
            if (!InGame() || Board.Instance == null || CreateZombie.Instance == null)
                return true;

            int instId = __instance.GetInstanceID();
            if (!PveBlindBoxSlotByInstance.TryGetValue(instId, out int slot) || slot != 6)
                return true; // 只处理 PvE 布阵中的那一个巨人盲盒

            if (PvEBlindBoxZombie6 < 0)
                return true; // 未配置则保持原逻辑

            // 用完就移除，避免后续其它逻辑再次误用
            PveBlindBoxSlotByInstance.Remove(instId);

            var axis = __instance.axis;
            if (axis == null) return true;

            var pos = axis.position;
            int row = __instance.theZombieRow;
            var targetType = (ZombieType)PvEBlindBoxZombie6;

            if (__instance.isMindControlled)
                CreateZombie.Instance.SetZombieWithMindControl(row, targetType, pos.x);
            else
                CreateZombie.Instance.SetZombie(row, targetType, pos.x);

            // 模拟原逻辑的结尾：让巨人死亡（reason=2）
            __instance.Die(2);

            // 不再调用原始 FirstArmorFall（其中的随机逻辑和特效大部分为美术表现，可忽略）
            return false;
        }
    }
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
    public static bool UnlockAllPlants { get; set; } = false;
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
    /// <summary>自定义阴魂不散（Debuff #1005）复活概率</summary>
    public static bool ZombieReviveDebuffCustomEnabled { get; set; } = false;
    /// <summary>阴魂不散复活概率（0-100%，默认 33.33% 等同原版 1/3）</summary>
    public static float ZombieReviveDebuffChance { get; set; } = 33.333f;
    /// <summary>独立概率复活（无需词条，可重复触发）</summary>
    public static bool ZombieFreeReviveEnabled { get; set; } = false;
    public static float ZombieFreeReviveChance { get; set; } = 33.333f;
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
    /// 自动音游 - 自动按击音游关卡音符
    /// </summary>
    public static bool AutoRhythmGame { get; set; } = false;
    
    /// <summary>
    /// 老版黑曜石子弹（Bullet_steelPea）：击退 + 穿透两次
    /// </summary>
    public static bool OldObsidianBullet { get; set; } = false;

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
        // 注意：打开背包时（InGame_openBag.ShowCards）会将 theGameStatus 设为 3，并强制 Time.timeScale = 0。
        // 这里如果继续在 Selecting 状态下覆盖 Time.timeScale，会导致“背包打开后僵尸仍在移动”的问题。
        // 因此仅在真正的局内状态下处理修改器的速度逻辑，把 Selecting 交给游戏自身（0 或 gameSpeed）。
        if (GameAPP.theGameStatus is GameStatus.InGame or GameStatus.InInterlude)
        {
            bool timeStopKeyPressed = Input.GetKeyDown(Core.KeyTimeStop.Value.Value);

            // 使用时停快捷键时，自动开启“启用游戏速度修改”。
            if (timeStopKeyPressed && !GameSpeedEnabled)
            {
                GameSpeedEnabled = true;
            }

            // 只有在游戏速度功能开启时才允许时停/慢速操作
            if (GameSpeedEnabled)
        {
            if (timeStopKeyPressed)
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
                    float currentGameSpeed = GameAPP.config != null ? GameAPP.config.gameSpeed : 1f;
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
                    _lastGameSpeed = GameAPP.config != null ? GameAPP.config.gameSpeed : 1f;
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
                        Time.timeScale = GameAPP.config != null ? GameAPP.config.gameSpeed : 1f;
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
                var inGameCards = GameApiCompat.GetInGameCards(InGameUI.Instance);
                for (int i = 0; i < inGameCards.Count; i++)
                {
                    try
                    {
                        var index = Random.RandomRangeInt(0, randomPlant.Count);
                        var card = inGameCards[i];
                        card.thePlantType = randomPlant[index];
                        card.ChangeCardSprite();
                        card.theSeedCost = 0;
                        card.fullCD = 0;
                    }
                    catch { }
                }
            }
        }
        
        // 3.6：ZombieStatusCoexist 旧实现依赖 Zombie 字段（coldTimer/isJalaed 等），已停用。
#if false
        if (ZombieStatusCoexist) { }
#endif
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

    public static bool CanApplyRuntimeBuff()
    {
        // 部分高级词条在 OnSelect(Board board) 中直接访问 board，关卡未就绪时会 NRE。
        return Board.Instance != null && GameAPP.board != null;
    }

    public static IEnumerator PostInitBoard()
    {
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

            // 3.4.1：不再直接操作 TravelMgr.advancedUpgrades，改为在需要时通过 GetNormalBuff 应用

            // 3.4.1：不再直接操作 TravelMgr 内部的 ultimateUpgrades/debuff 数组，
            // 需要时通过 GetUltiBuff / GetDebuff 接口来应用词条。
            
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

        // 直接使用 TravelDictionary 的 Count 值初始化本地数组
        int advCount = TravelDictionary.advancedBuffsText?.Count ?? 0;
        int ultiCount = TravelDictionary.ultimateBuffsText?.Count ?? 0;
        int debuffCount = TravelDictionary.debuffData?.Count ?? 0;

        // 3.5: 直接访问 TravelMgr.InvestBuffsData 会触发 InvestBuff 泛型约束异常
        // 这里仅根据已有状态数组决定长度，避免触发该静态字典初始化。
        int investCount = Math.Max(
            InGameInvestBuffs?.Length ?? 0,
            DesiredInGameInvestBuffs?.Length ?? 0);

        // 记录数组之前是否已经初始化过；如果已经有值，说明是上一关/之前由修改器设置好的状态，
        // 不要在这里用游戏内状态强行覆盖（否则切换场景会把已开启的词条全部重置）。
        bool hadAdv    = InGameAdvBuffs    != null && InGameAdvBuffs.Length    > 0;
        bool hadUlti   = InGameUltiBuffs   != null && InGameUltiBuffs.Length   > 0;
        bool hadDebuff = InGameDebuffs     != null && InGameDebuffs.Length     > 0;
        bool hadInvest = InGameInvestBuffs != null && InGameInvestBuffs.Length > 0;

        // 只在需要时扩容，并尽量保留原有开关状态
        if (InGameAdvBuffs == null || InGameAdvBuffs.Length < advCount)
        {
            var newArr = new bool[advCount];
            if (InGameAdvBuffs != null)
                Array.Copy(InGameAdvBuffs, newArr, Math.Min(InGameAdvBuffs.Length, advCount));
            InGameAdvBuffs = newArr;
        }

        if (InGameUltiBuffs == null || InGameUltiBuffs.Length < ultiCount)
        {
            var newArr = new bool[ultiCount];
            if (InGameUltiBuffs != null)
                Array.Copy(InGameUltiBuffs, newArr, Math.Min(InGameUltiBuffs.Length, ultiCount));
            InGameUltiBuffs = newArr;
        }

        if (InGameDebuffs == null || InGameDebuffs.Length < debuffCount)
        {
            var newArr = new bool[debuffCount];
            if (InGameDebuffs != null)
                Array.Copy(InGameDebuffs, newArr, Math.Min(InGameDebuffs.Length, debuffCount));
            InGameDebuffs = newArr;
        }

        if (InGameInvestBuffs == null || InGameInvestBuffs.Length < investCount)
        {
            var newArr = new bool[investCount];
            if (InGameInvestBuffs != null)
                Array.Copy(InGameInvestBuffs, newArr, Math.Min(InGameInvestBuffs.Length, investCount));
            InGameInvestBuffs = newArr;
        }
        
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

        // 仅在“本地缓存尚未初始化”时，从游戏当前状态同步一次初始值；
        // 之后切换场景不再用游戏状态覆盖修改器里已经选好的词条。
        if (!hadAdv)
        {
        for (int i = 0; i < InGameAdvBuffs.Length; i++)
        {
            try { InGameAdvBuffs[i] = Lawnf.TravelAdvanced((AdvBuff)i); }
            catch { InGameAdvBuffs[i] = false; }
            }
        }

        if (!hadUlti)
        {
        for (int i = 0; i < InGameUltiBuffs.Length; i++)
        {
            try { InGameUltiBuffs[i] = Lawnf.TravelUltimate((UltiBuff)i); }
            catch { InGameUltiBuffs[i] = false; }
            }
        }

        if (!hadDebuff)
        {
        for (int i = 0; i < InGameDebuffs.Length; i++)
        {
            try { InGameDebuffs[i] = Lawnf.TravelDebuff((TravelDebuff)i); }
            catch { InGameDebuffs[i] = false; }
            }
        }

        if (!hadInvest)
        {
            // 避免调用 Lawnf.TravelInvest 触发 InvestBuff 泛型约束异常
            for (int i = 0; i < InGameInvestBuffs.Length; i++)
                InGameInvestBuffs[i] = false;
        }
        yield return null;
        new Thread(SyncInGameBuffs).Start();

        // 进入游戏后重新读取所有词条（包括二创插件延迟注册的），并发送给修改器 UI
        MLogger?.LogInfo("[PVZRHTools] PostInitBoard: 准备重新读取词条数据（第1次）");
        yield return new WaitForSeconds(1.5f);
        MLogger?.LogInfo("[PVZRHTools] PostInitBoard: 开始重新读取词条数据（第1次）");
        ReloadAndSendBuffsData();

        MLogger?.LogInfo("[PVZRHTools] PostInitBoard: 准备重新读取词条数据（第2次）");
        yield return new WaitForSeconds(1.5f);
        MLogger?.LogInfo("[PVZRHTools] PostInitBoard: 开始重新读取词条数据（第2次）");
        ReloadAndSendBuffsData();

        MLogger?.LogInfo("[PVZRHTools] PostInitBoard: 准备重新读取词条数据（第3次）");
        yield return new WaitForSeconds(1.0f);
        MLogger?.LogInfo("[PVZRHTools] PostInitBoard: 开始重新读取词条数据（第3次）");
        ReloadAndSendBuffsData();

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
            // 直接访问 InitZombieList.zombieList
            Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<ZombieSpawnData>>? zombieList = null;
            
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
                    zombieList = zombieListField.GetValue(null) as Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<ZombieSpawnData>>;
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

            // 直接修改列表
            var data = wave[zombieIndex];
            if (data != null)
                data.zombieType = value;
            
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
            Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<ZombieSpawnData>>? zombieList = null;
            
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
                    zombieList = zombieListField.GetValue(null) as Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<ZombieSpawnData>>;
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
                    var data = wave[i];
                    if (data == null) continue;
                    zombieTypes.Add((int)data.zombieType);
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
            if (InGameAdvBuffs == null || InGameUltiBuffs == null || InGameDebuffs == null || InGameInvestBuffs == null)
            {
                MLogger?.LogWarning("[PVZRHTools] SyncInGameBuffs: 本地词条缓存未初始化");
                return;
            }

            // 直接使用本地缓存的 InGame* 数组向工具端同步状态，
            // 不再每次都从 Lawnf.Travel* 重新读取，避免切换场景时把工具端的状态覆盖成“新场景默认值”。
            var adv = new bool[InGameAdvBuffs.Length];
            var ulti = new bool[InGameUltiBuffs.Length];
            var deb = new bool[InGameDebuffs.Length];
            var invest = new bool[InGameInvestBuffs.Length];

            Array.Copy(InGameAdvBuffs, adv, InGameAdvBuffs.Length);
            Array.Copy(InGameUltiBuffs, ulti, InGameUltiBuffs.Length);
            Array.Copy(InGameDebuffs, deb, InGameDebuffs.Length);
            Array.Copy(InGameInvestBuffs, invest, InGameInvestBuffs.Length);

            DataSync.Instance.SendData(new SyncTravelBuff
            {
                AdvInGame = adv.ToList(),
                UltiInGame = ulti.ToList(),
                DebuffsInGame = deb.ToList(),
                InvestInGame = invest.ToList()
            });
        }
        catch (System.Exception ex)
        {
            MLogger?.LogError($"[PVZRHTools] SyncInGameBuffs 异常: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// 实时同步游戏词条状态到修改器
    /// 当游戏中解锁或关闭词条时调用此方法，更新 InGame*Buffs 数组并发送到UI
    /// </summary>
    public static void SyncGameBuffsToModifier()
    {
        try
        {
            // 从游戏状态读取所有词条，更新 InGame*Buffs 数组
            if (InGameAdvBuffs != null && InGameAdvBuffs.Length > 0)
            {
                for (int i = 0; i < InGameAdvBuffs.Length; i++)
                {
                    try
                    {
                        bool gameState = Lawnf.TravelAdvanced((AdvBuff)i);
                        InGameAdvBuffs[i] = gameState;
                    }
                    catch { }
                }
            }
            
            if (InGameUltiBuffs != null && InGameUltiBuffs.Length > 0)
            {
                for (int i = 0; i < InGameUltiBuffs.Length; i++)
                {
                    try
                    {
                        bool gameState = Lawnf.TravelUltimate((UltiBuff)i);
                        InGameUltiBuffs[i] = gameState;
                    }
                    catch { }
                }
            }
            
            if (InGameDebuffs != null && InGameDebuffs.Length > 0)
            {
                for (int i = 0; i < InGameDebuffs.Length; i++)
                {
                    try
                    {
                        bool gameState = Lawnf.TravelDebuff((TravelDebuff)i);
                        InGameDebuffs[i] = gameState;
                    }
                    catch { }
                }
            }
            
            if (InGameInvestBuffs != null && InGameInvestBuffs.Length > 0)
            {
                for (int i = 0; i < InGameInvestBuffs.Length; i++)
                {
                    // 3.5: 避免调用 Lawnf.TravelInvest 导致 InvestBuff 泛型约束异常。
                    // InGameInvestBuffs 状态由 DataProcessor/UpdateInGameBuffs 维护，这里不再主动拉取游戏态。
                }
            }
            
            // 发送更新后的数据到UI
            // 1. 先发送词条列表（如果需要更新词条列表）
            ReloadAndSendBuffsData();
            
            // 2. 发送 InGame*Buffs 状态，更新UI中的复选框
            if (InGameAdvBuffs != null && InGameUltiBuffs != null && InGameDebuffs != null && InGameInvestBuffs != null)
            {
                var adv = new bool[InGameAdvBuffs.Length];
                var ulti = new bool[InGameUltiBuffs.Length];
                var deb = new bool[InGameDebuffs.Length];
                var invest = new bool[InGameInvestBuffs.Length];

                Array.Copy(InGameAdvBuffs, adv, InGameAdvBuffs.Length);
                Array.Copy(InGameUltiBuffs, ulti, InGameUltiBuffs.Length);
                Array.Copy(InGameDebuffs, deb, InGameDebuffs.Length);
                Array.Copy(InGameInvestBuffs, invest, InGameInvestBuffs.Length);

                DataSync.Instance.SendData(new SyncTravelBuff
                {
                    AdvInGame = adv.ToList(),
                    UltiInGame = ulti.ToList(),
                    DebuffsInGame = deb.ToList(),
                    InvestInGame = invest.ToList()
                });
            }
        }
        catch (System.Exception ex)
        {
            MLogger?.LogWarning($"[PVZRHTools] SyncGameBuffsToModifier 异常: {ex.Message}");
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
            if (TravelDictionary.advancedBuffsText == null ||
                TravelDictionary.ultimateBuffsText == null ||
                TravelDictionary.debuffData == null)
            {
                MLogger?.LogWarning("[PVZRHTools] ReloadAndSendBuffsData: TravelDictionary 尚未初始化，将尝试从 CustomizeLib 读取二创词条");
            }

            var advTexts = BuffDataCollector.GetAdvancedBuffTexts();
            var ultiTexts = BuffDataCollector.GetUltimateBuffTexts();
            var debuffTexts = BuffDataCollector.GetDebuffTexts();

            List<string> advBuffs = BuffDataCollector.ToLines(advTexts);
            List<string> ultiBuffs = BuffDataCollector.ToLines(ultiTexts);
            List<string> debuffs = BuffDataCollector.ToLines(debuffTexts);

            MLogger?.LogInfo($"[PVZRHTools] ReloadAndSendBuffsData: Advanced={advBuffs.Count} (max={BuffDataCollector.GetMaxKey(advTexts)}), " +
                             $"Ultimate={ultiBuffs.Count} (max={BuffDataCollector.GetMaxKey(ultiTexts)}), " +
                             $"Debuff={debuffs.Count} (max={BuffDataCollector.GetMaxKey(debuffTexts)})");

            int newAdvSize = BuffDataCollector.GetRequiredArraySize(advTexts);
            if (AdvBuffs == null || AdvBuffs.Length < newAdvSize)
            {
                var oldLength = AdvBuffs?.Length ?? 0;
                var newArray = new bool[newAdvSize];
                if (AdvBuffs != null)
                    Array.Copy(AdvBuffs, newArray, Math.Min(oldLength, newArray.Length));
                AdvBuffs = newArray;
            }

            int newUltiSize = BuffDataCollector.GetRequiredArraySize(ultiTexts);
            if (UltiBuffs == null || UltiBuffs.Length < newUltiSize)
            {
                var oldLength = UltiBuffs?.Length ?? 0;
                var newArray = new bool[newUltiSize];
                if (UltiBuffs != null)
                    Array.Copy(UltiBuffs, newArray, Math.Min(oldLength, newArray.Length));
                UltiBuffs = newArray;
            }

            int newDebuffSize = BuffDataCollector.GetRequiredArraySize(debuffTexts);
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
            InitData initData = new()
            {
                AdvBuffs = [.. advBuffs],   // 使用最新读取的高级词条数据
                UltiBuffs = [.. ultiBuffs], // 使用最新读取的究极词条数据
                Debuffs = [.. debuffs],     // 使用最新读取的负面词条数据
                // InvestBuffs 文本由 Core.LateInit 初次生成并写入，Reload 时如果旧文件中存在则在后面保留
                InvestBuffs = Array.Empty<string>()
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
                            initData.Plants = existingData.Plants;
                        }
                        if (existingData.Zombies != null && existingData.Zombies.Count > 0)
                        {
                            initData.Zombies = existingData.Zombies;
                        }
                        if (existingData.Bullets != null && existingData.Bullets.Count > 0)
                        {
                            initData.Bullets = existingData.Bullets;
                        }
                        if (existingData.FirstArmors != null && existingData.FirstArmors.Count > 0)
                        {
                            initData.FirstArmors = existingData.FirstArmors;
                        }
                        if (existingData.SecondArmors != null && existingData.SecondArmors.Count > 0)
                        {
                            initData.SecondArmors = existingData.SecondArmors;
                        }
                        // 如果旧文件中已经有 InvestBuffs 文本，则保留
                        if (existingData.InvestBuffs != null && existingData.InvestBuffs.Length > 0)
                        {
                            initData.InvestBuffs = existingData.InvestBuffs;
                        }
                    }
                    catch (System.Exception ex2)
                    {
                        MLogger?.LogWarning($"[PVZRHTools] 读取现有InitData失败: {ex2.Message}");
                    }
                }
                else
                {
                }
            }
            catch (System.Exception ex)
            {
                MLogger?.LogWarning($"[PVZRHTools] 读取现有InitData失败: {ex.Message}");
            }

            // 保存更新后的InitData
            Directory.CreateDirectory("./PVZRHTools");
            File.WriteAllText("./PVZRHTools/InitData.json", System.Text.Json.JsonSerializer.Serialize(initData));

            // 发送更新后的词条数据给UI
            try
            {
                DataSync.Instance.SendData(initData);
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
        if (travelMgr == null && GameAPP.Instance != null)
        {
            travelMgr = GameAPP.Instance.GetComponent<TravelMgr>();
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
        if (travelMgr == null && autoCreate && InGame() && GameAPP.Instance != null)
        {
            try
            {
                travelMgr = GameAPP.Instance.GetComponent<TravelMgr>();
                if (travelMgr == null)
                {
                    travelMgr = GameAPP.Instance.AddComponent<TravelMgr>();
                    
                    // 关键修复：自动创建 TravelMgr 时，同步旗帜波状态，避免旗帜波检测失效
                    // 确保 _lastHugeWaveState 与当前游戏状态一致
                    if (Board.Instance != null)
                    {
                        _lastHugeWaveState = Board.Instance.isHugeWave;
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
    
    // 控制是否允许 UpdateInGameBuffs 根据 shouldUnlock=false 去“关闭/移除”词条。
    // 初始词条功能会将其设置为 false，避免在场景切换时把已有词条清空；
    // UI 主动关闭词条时则允许移除。
    public static bool AllowBuffRemoval = true;
    
    /// <summary>
    /// 将修改器端缓存的 InGame* 状态应用到游戏中。
    /// 注意：这里应当以“修改器设置”为主，不要在一开始就用游戏当前状态覆盖 InGame*，
    /// 否则会导致实时修改页的勾选/取消完全失效。
    /// 游戏 -> 修改器 的同步已经由 SyncGameBuffsToModifier / TravelMgrBuffSyncPatch 负责。
    /// </summary>
    public static void UpdateInGameBuffs()
    {
        try
        {
            var travelMgr = ResolveTravelMgr(autoCreate: true);
            if (travelMgr == null)
            {
                MLogger?.LogWarning("[PVZRHTools] UpdateInGameBuffs: 无法找到 TravelMgr，可能未进入关卡/未初始化");
                return;
            }

            var data = travelMgr.data;
            if (data == null)
            {
                MLogger?.LogWarning("[PVZRHTools] UpdateInGameBuffs: travelMgr.data 为空，无法同步词条状态");
                return;
            }

            // 高级词条：以游戏为主，但对“手动取消勾选”的该词条执行一次关闭
            // - 勾选为 true 且当前未解锁 -> 只补充解锁这一条
            // - 从 true 取消勾选（上一次为 true、本次为 false） -> 只对这一条从当前局移除，相当于“关闭该词条”，不动其它词条。
            if (TravelDictionary.advancedBuffsText != null)
            {
                foreach (var kvp in TravelDictionary.advancedBuffsText)
                {
                    int id = (int)kvp.Key;

                    bool desired;
                    if (InGameAdvBuffsDict != null && InGameAdvBuffsDict.TryGetValue(id, out var dictValue))
                        desired = dictValue;
                    else if (InGameAdvBuffs != null && id >= 0 && id < InGameAdvBuffs.Length)
                        desired = InGameAdvBuffs[id];
                    else
                        continue; // 未知ID，跳过

                    var adv = (AdvBuff)id;
                    bool unlocked = data.advBuffs != null && data.advBuffs.Contains(adv);

                    // 上一次期望状态，用于判断是否从 true -> false
                    bool lastDesired = false;
                    if (LastInGameAdvBuffsDict != null && LastInGameAdvBuffsDict.TryGetValue(id, out var lastVal))
                        lastDesired = lastVal;

                    if (desired && !unlocked)
                    {
                        if (!CanApplyRuntimeBuff())
                        {
                            continue;
                        }

                        try
                        {
                            travelMgr.GetNormalBuff(adv);
                        }
                        catch (System.Exception ex)
                        {
                            MLogger?.LogWarning($"[PVZRHTools] UpdateInGameBuffs: 解锁高级词条 {adv} (id={id}) 失败: {ex.Message}");
                        }
                    }
                    // 仅当“上一次为 true、本次为 false”且允许移除、当前已解锁时，才关闭这一条高级词条
                    else if (!desired && lastDesired && AllowBuffRemoval && unlocked)
                    {
                        try
                        {
                            data.advBuffs?.Remove(adv);
                            MLogger?.LogInfo($"[PVZRHTools] UpdateInGameBuffs: 关闭高级词条 {adv} (id={id})，已从当前局移除");
                        }
                        catch (System.Exception ex)
                        {
                            MLogger?.LogWarning($"[PVZRHTools] UpdateInGameBuffs: 移除高级词条 {adv} (id={id}) 失败: {ex.Message}");
                        }
                    }
                }
            }

            // 究极词条：以游戏为主，但对“手动取消勾选”的该词条执行一次关闭
            // - 勾选 true 且未解锁 -> 解锁
            // - 从 true 取消勾选 -> 只对这一条从当前局移除，其它究极词条不受影响。
            if (TravelDictionary.ultimateBuffsText != null)
            {
                foreach (var kvp in TravelDictionary.ultimateBuffsText)
                {
                    int id = (int)kvp.Key;

                    bool desired;
                    if (DesiredInGameUltiBuffs != null && id >= 0 && id < DesiredInGameUltiBuffs.Length)
                        desired = DesiredInGameUltiBuffs[id];
                    else if (InGameUltiBuffs != null && id >= 0 && id < InGameUltiBuffs.Length)
                        desired = InGameUltiBuffs[id];
                    else
                        continue;

                    var ulti = (UltiBuff)id;
                    bool unlocked =
                        (data.ultiBuffs != null && data.ultiBuffs.Contains(ulti)) ||
                        (data.ultiBuffs_lv2 != null && data.ultiBuffs_lv2.Contains(ulti));

                    // 上一次期望状态，用于判断是否从 true -> false
                    bool lastDesired = false;
                    if (LastDesiredInGameUltiBuffs != null && id >= 0 && id < LastDesiredInGameUltiBuffs.Length)
                        lastDesired = LastDesiredInGameUltiBuffs[id];

                    if (desired && !unlocked)
                    {
                        try
                        {
                            travelMgr.GetUltiBuff(ulti, true);
                        }
                        catch (System.Exception ex)
                        {
                            MLogger?.LogWarning($"[PVZRHTools] UpdateInGameBuffs: 解锁究极词条 {ulti} (id={id}) 失败: {ex.Message}");
                        }
                    }
                    // 从 true 取消勾选时，且允许移除并且当前已解锁，则关闭这一条究极词条
                    else if (!desired && lastDesired && AllowBuffRemoval && unlocked)
                    {
                        try
                        {
                            data.ultiBuffs?.Remove(ulti);
                            data.ultiBuffs_lv2?.Remove(ulti);
                            MLogger?.LogInfo($"[PVZRHTools] UpdateInGameBuffs: 关闭究极词条 {ulti} (id={id})，已从当前局移除");
                        }
                        catch (System.Exception ex)
                        {
                            MLogger?.LogWarning($"[PVZRHTools] UpdateInGameBuffs: 移除究极词条 {ulti} (id={id}) 失败: {ex.Message}");
                        }
                    }
                }
            }

            // 负面词条（Debuff）：以游戏为主，但对“手动取消勾选”的该词条执行一次关闭
            // - 勾选 true 且未解锁 -> 解锁
            // - 从 true 取消勾选 -> 只对这一条从当前局移除，其它 Debuff 不受影响。
            if (TravelDictionary.debuffData != null)
            {
                foreach (var kvp in TravelDictionary.debuffData)
                {
                    int id = (int)kvp.Key;

                    bool desired;
                    if (DesiredInGameDebuffs != null && id >= 0 && id < DesiredInGameDebuffs.Length)
                        desired = DesiredInGameDebuffs[id];
                    else if (InGameDebuffs != null && id >= 0 && id < InGameDebuffs.Length)
                        desired = InGameDebuffs[id];
                    else
                        continue;

                    var debuff = (TravelDebuff)id;
                    bool unlocked = data.travelDebuffs != null && data.travelDebuffs.Contains(debuff);

                    // 上一次期望状态，用于判断是否从 true -> false
                    bool lastDesired = false;
                    if (LastDesiredInGameDebuffs != null && id >= 0 && id < LastDesiredInGameDebuffs.Length)
                        lastDesired = LastDesiredInGameDebuffs[id];

                    if (desired && !unlocked)
                    {
                        try
                        {
                            travelMgr.GetDebuff(debuff);
                        }
                        catch (System.Exception ex)
                        {
                            MLogger?.LogWarning($"[PVZRHTools] UpdateInGameBuffs: 解锁负面词条 {debuff} (id={id}) 失败: {ex.Message}");
                        }
                    }
                    // 从 true 取消勾选时，且允许移除并且当前已解锁，则关闭这一条 Debuff
                    else if (!desired && lastDesired && AllowBuffRemoval && unlocked)
                    {
                        try
                        {
                            data.travelDebuffs?.Remove(debuff);
                            MLogger?.LogInfo($"[PVZRHTools] UpdateInGameBuffs: 关闭负面词条 {debuff} (id={id})，已从当前局移除");
                        }
                        catch (System.Exception ex)
                        {
                            MLogger?.LogWarning($"[PVZRHTools] UpdateInGameBuffs: 移除负面词条 {debuff} (id={id}) 失败: {ex.Message}");
                        }
                    }
                }
            }

            // 投资词条（Invest）：以游戏为主，但对“手动取消勾选”的该词条执行一次关闭
            // - 勾选 true 且未解锁 -> 解锁
            // - 从 true 取消勾选 -> 只对这一条从当前局移除，其它投资词条不受影响。
            int investLen = System.Math.Max(DesiredInGameInvestBuffs?.Length ?? 0, InGameInvestBuffs?.Length ?? 0);
            if (investLen > 0)
            {
                for (int id = 0; id < investLen; id++)
                {
                    bool desired;
                    if (DesiredInGameInvestBuffs != null && id >= 0 && id < DesiredInGameInvestBuffs.Length)
                        desired = DesiredInGameInvestBuffs[id];
                    else if (InGameInvestBuffs != null && id >= 0 && id < InGameInvestBuffs.Length)
                        desired = InGameInvestBuffs[id];
                    else
                        continue;

                    var invest = (InvestBuff)id;
                    bool unlocked = data.investmentBuffs != null && data.investmentBuffs.Contains(invest);

                    // 上一次期望状态，用于判断是否从 true -> false
                    bool lastDesired = false;
                    if (LastDesiredInGameInvestBuffs != null && id >= 0 && id < LastDesiredInGameInvestBuffs.Length)
                        lastDesired = LastDesiredInGameInvestBuffs[id];

                    if (desired && !unlocked)
                    {
                        try
                        {
                            travelMgr.GetInvestBuff(invest);
                        }
                        catch (System.Exception ex)
                        {
                            MLogger?.LogWarning($"[PVZRHTools] UpdateInGameBuffs: 解锁投资词条 {invest} (id={id}) 失败: {ex.Message}");
                        }
                    }
                    // 从 true 取消勾选时，且允许移除并且当前已解锁，则关闭这一条投资词条
                    else if (!desired && lastDesired && AllowBuffRemoval && unlocked)
                    {
                        try
                        {
                            data.investmentBuffs?.Remove(invest);
                            MLogger?.LogInfo($"[PVZRHTools] UpdateInGameBuffs: 关闭投资词条 {invest} (id={id})，已从当前局移除");
                        }
                        catch (System.Exception ex)
                        {
                            MLogger?.LogWarning($"[PVZRHTools] UpdateInGameBuffs: 移除投资词条 {invest} (id={id}) 失败: {ex.Message}");
                        }
                    }
                }
            }

            // 更新“上一次期望状态”快照，用于下一次判断 true->false 变化
            if (DesiredInGameUltiBuffs != null)
            {
                if (LastDesiredInGameUltiBuffs == null || LastDesiredInGameUltiBuffs.Length < DesiredInGameUltiBuffs.Length)
                    LastDesiredInGameUltiBuffs = new bool[DesiredInGameUltiBuffs.Length];
                Array.Copy(DesiredInGameUltiBuffs, LastDesiredInGameUltiBuffs, DesiredInGameUltiBuffs.Length);
            }
            if (InGameAdvBuffsDict != null)
            {
                LastInGameAdvBuffsDict.Clear();
                foreach (var kvp in InGameAdvBuffsDict)
                {
                    LastInGameAdvBuffsDict[kvp.Key] = kvp.Value;
                }
            }
            if (DesiredInGameDebuffs != null)
            {
                if (LastDesiredInGameDebuffs == null || LastDesiredInGameDebuffs.Length < DesiredInGameDebuffs.Length)
                    LastDesiredInGameDebuffs = new bool[DesiredInGameDebuffs.Length];
                Array.Copy(DesiredInGameDebuffs, LastDesiredInGameDebuffs, DesiredInGameDebuffs.Length);
            }
            if (DesiredInGameInvestBuffs != null)
            {
                if (LastDesiredInGameInvestBuffs == null || LastDesiredInGameInvestBuffs.Length < DesiredInGameInvestBuffs.Length)
                    LastDesiredInGameInvestBuffs = new bool[DesiredInGameInvestBuffs.Length];
                Array.Copy(DesiredInGameInvestBuffs, LastDesiredInGameInvestBuffs, DesiredInGameInvestBuffs.Length);
            }

            // 关键：设置 BoardTag 标志，使游戏识别并应用词条系统
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
                MLogger?.LogError($"[PVZRHTools] UpdateInGameBuffs: 设置 BoardTag 失败: {ex.Message}\n{ex.StackTrace}");
            }
        }
        catch (System.Exception ex)
        {
            MLogger?.LogError($"[PVZRHTools] UpdateInGameBuffs 异常: {ex.Message}\n{ex.StackTrace}");
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
/// 监听游戏词条状态变化，实时同步到修改器
/// </summary>
[HarmonyPatch(typeof(TravelMgr))]
public static class TravelMgrBuffSyncPatch
{
    /// <summary>
    /// GetNormalBuff 后置补丁：解锁高级词条后实时同步到修改器
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(TravelMgr), "GetNormalBuff", new System.Type[] { typeof(AdvBuff) })]
    public static void PostGetNormalBuff(TravelMgr __instance, AdvBuff __0)
    {
        try
        {
            // 延迟一小段时间后同步，确保游戏状态已更新
            __instance.StartCoroutine(SyncBuffsDelayed());
        }
        catch (System.Exception ex)
        {
            MLogger?.LogWarning($"[PVZRHTools] PostGetNormalBuff 异常: {ex.Message}");
        }
    }

    /// <summary>
    /// GetUltiBuff 后置补丁：解锁究极词条后实时同步到修改器
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(TravelMgr), "GetUltiBuff", new System.Type[] { typeof(UltiBuff), typeof(bool) })]
    public static void PostGetUltiBuff(TravelMgr __instance, UltiBuff __0, bool __1)
    {
        try
        {
            // 延迟一小段时间后同步，确保游戏状态已更新
            __instance.StartCoroutine(SyncBuffsDelayed());
        }
        catch (System.Exception ex)
        {
            MLogger?.LogWarning($"[PVZRHTools] PostGetUltiBuff 异常: {ex.Message}");
        }
    }

    /// <summary>
    /// GetDebuff 后置补丁：解锁负面词条后实时同步到修改器
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(TravelMgr), "GetDebuff", new System.Type[] { typeof(TravelDebuff) })]
    public static void PostGetDebuff(TravelMgr __instance, TravelDebuff __0)
    {
        try
        {
            // 延迟一小段时间后同步，确保游戏状态已更新
            __instance.StartCoroutine(SyncBuffsDelayed());
        }
        catch (System.Exception ex)
        {
            MLogger?.LogWarning($"[PVZRHTools] PostGetDebuff 异常: {ex.Message}");
        }
    }

    /// <summary>
    /// GetInvestBuff 后置补丁：解锁投资词条后实时同步到修改器
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(TravelMgr), "GetInvestBuff", new System.Type[] { typeof(InvestBuff) })]
    public static void PostGetInvestBuff(TravelMgr __instance, InvestBuff __0)
    {
        try
        {
            // 延迟一小段时间后同步，确保游戏状态已更新
            __instance.StartCoroutine(SyncBuffsDelayed());
        }
        catch (System.Exception ex)
        {
            MLogger?.LogWarning($"[PVZRHTools] PostGetInvestBuff 异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 延迟同步词条状态，确保游戏状态已更新
    /// </summary>
    private static IEnumerator SyncBuffsDelayed()
    {
        yield return new WaitForSeconds(0.1f);
        PatchMgr.SyncGameBuffsToModifier();
    }
}

/// <summary>
/// 词条“锁定”补丁：
/// - 当修改器中取消勾选某个高级/究极/debuff/投资词条时，对应 InGame* 数组为 false；
/// - 这里在 TravelMgr.GetNormalBuff / GetUltiBuff / GetDebuff / GetInvestBuff 之前检查该数组，
///   如果是 false，则直接跳过原方法，从而在游戏内“锁定”对应词条，防止被获取。
/// </summary>
[HarmonyPatch(typeof(TravelMgr))]
public static class TravelMgrBuffLockPatch
{
    // 按你的最新要求：不再阻止游戏获取任何词条，一切以游戏为主；
    // 修改器只做“显示/同步”，不再通过前置补丁锁定 Get* 接口。

    [HarmonyPrefix]
    [HarmonyPatch("GetNormalBuff", new System.Type[] { typeof(AdvBuff) })]
    public static bool PrefixGetNormalBuff(AdvBuff __0) => true;

    [HarmonyPrefix]
    [HarmonyPatch("GetUltiBuff", new System.Type[] { typeof(UltiBuff), typeof(bool) })]
    public static bool PrefixGetUltiBuff(UltiBuff __0) => true;

    [HarmonyPrefix]
    [HarmonyPatch("GetDebuff", new System.Type[] { typeof(TravelDebuff) })]
    public static bool PrefixGetDebuff(TravelDebuff __0) => true;

    [HarmonyPrefix]
    [HarmonyPatch("GetInvestBuff", new System.Type[] { typeof(InvestBuff) })]
    public static bool PrefixGetInvestBuff(InvestBuff __0) => true;
}

/// <summary>
/// TravelMgr 安全兜底补丁：
/// - 在 TravelMgr.GetNormalBuff 与 UpdateSynergies 发生早期空引用时，吞掉异常并记录告警，避免崩溃
/// - 原因：某些情况下（例如极早期恢复、MOD词条延迟注册），TravelMgr 内部数据结构未就绪
/// </summary>
[HarmonyPatch(typeof(TravelMgr))]
public static class TravelMgrSafeGuardsPatch
{
    [HarmonyFinalizer]
    [HarmonyPatch("GetNormalBuff", new System.Type[] { typeof(AdvBuff) })]
    public static Exception Finalizer_GetNormalBuff(Exception __exception)
    {
        if (__exception != null)
        {
            try
            {
                PatchMgr.MLogger?.LogWarning($"[PVZRHTools] GetNormalBuff 发生异常，已忽略：{__exception.GetType().Name} - {__exception.Message}");
            }
            catch { }
            return null; // 吞掉异常，防止崩溃
        }
        return null;
    }

    [HarmonyFinalizer]
    [HarmonyPatch("UpdateSynergies")]
    public static Exception Finalizer_UpdateSynergies(Exception __exception)
    {
        if (__exception != null)
        {
            try
            {
                PatchMgr.MLogger?.LogWarning($"[PVZRHTools] UpdateSynergies 发生异常，已忽略：{__exception.GetType().Name} - {__exception.Message}");
            }
            catch { }
            return null; // 吞掉异常
        }
        return null;
    }
}

// 跨会话恢复功能已移除

/// <summary>
/// MNEntry词条注册 - 将词条注册到游戏的旅行词条系统中
/// 只有当修改器中开关开启时，才会注册词条到游戏中
/// </summary>
[HarmonyPatch(typeof(TravelMgr))]
public static class MNEntryTravelMgrPatch
{
    /// <summary>
    /// 词条1(坚不可摧)在自定义高级词条中的ID，-1表示未注册
    /// </summary>
    public static int TravelId1 = -1;

    /// <summary>
    /// 词条2(高级后勤)在自定义高级词条中的ID，-1表示未注册
    /// </summary>
    public static int TravelId2 = -1;

    /// <summary>
    /// 词条文本
    /// </summary>
    private const string BuffText1 = "坚不可摧: 鱼丸受到的伤害最多为200";
    private const string BuffText2 = "高级后勤: 鱼丸恢复血量时恢复双倍血量, 阳光磁力菇冷却时间大幅减少";

    /// <summary>
    /// TravelMgr.OnGameStart 后置补丁（3.4.1 中不存在 Awake）
    /// 在第一次进入关卡时根据修改器开关状态注册自定义 buff 词条文本
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(TravelMgr), "OnGameStart")]
    public static void PostOnGameStart(TravelMgr __instance)
    {
        try
        {
            // 重置词条ID
            TravelId1 = -1;
            TravelId2 = -1;

            // 只有开启时才注册两个词条
            if (!PatchMgr.MNEntryEnabled) return;

            // 检查 TravelDictionary.advancedBuffsText 是否已初始化
            if (TravelDictionary.advancedBuffsText == null)
            {
                MLogger.LogError("MNEntry词条注册失败: TravelDictionary.advancedBuffsText 为 null");
                return;
            }

            int baseId = TravelDictionary.advancedBuffsText.Count;

            // 注册两个词条
            TravelId1 = baseId;
            TravelId2 = baseId + 1;

            // 注册词条文本（TravelDictionary 会自动处理键值对）
            TravelDictionary.advancedBuffsText[(AdvBuff)TravelId1] = BuffText1;
            TravelDictionary.advancedBuffsText[(AdvBuff)TravelId2] = BuffText2;
            MLogger.LogInfo($"MNEntry词条注册成功（仅文本层面），ID1: {TravelId1}, ID2: {TravelId2}");
        }
        catch (Exception ex)
        {
            MLogger.LogError($"MNEntry词条注册失败: {ex.Message}");
        }
    }

    // 3.4.1 中 GetPlantTypeByAdvBuff 签名已变为静态方法：PlantType GetPlantTypeByAdvBuff(Il2CppSystem.Object buff)，
    // 无法安全地在 IL2CPP 下从参数中拿到词条 ID，这里先移除该补丁，避免 Harmony 注入失败导致整个插件无法加载。
}

/// <summary>
/// MNEntry词条效果 - 坚不可摧：鱼丸受到的伤害最多为200
/// </summary>
[HarmonyPatch(typeof(SuperMachineNut), "Instead", new Type[] { typeof(int) })]
public static class SuperMachineNutTakeDamageGameBuffPatch
{
    // 3.5 下该方法在 IL2CPP 环境中仍可能触发 native->managed trampoline 空引用，
    // 临时禁用该补丁以确保插件可稳定加载。
    [HarmonyPrepare]
    public static bool Prepare() => false;

    [HarmonyPrefix]
    public static bool Prefix(SuperMachineNut __instance, ref int theDamage)
    {
        try
        {
            if (__instance == null)
            {
                return true;
            }

        // 检查修改器开关（开启时两个效果都生效）
        if (PatchMgr.MNEntryEnabled)
        {
                if (theDamage > 200) theDamage = 200;
            return true;
        }

        // 检查游戏内词条是否激活（3.4.1：TravelAdvanced 接受 AdvBuff 枚举）
            if (MNEntryTravelMgrPatch.TravelId1 >= 0)
            {
                bool hasBuff = false;
                try
                {
                    if (Board.Instance != null)
                    {
                        hasBuff = Lawnf.TravelAdvanced((AdvBuff)MNEntryTravelMgrPatch.TravelId1);
                    }
                }
                catch
                {
                    // 词条查询异常时仅降级，不影响伤害流程
                }

                if (hasBuff && theDamage > 200)
                {
                    theDamage = 200;
        }
            }
        }
        catch (Exception ex)
        {
            MLogger?.LogWarning($"[PVZRHTools] SuperMachineNut.Instead 前置补丁异常: {ex.Message}");
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

        // 检查游戏内词条是否激活（3.4.1：TravelAdvanced 接受 AdvBuff 枚举）
        if (MNEntryTravelMgrPatch.TravelId2 >= 0 &&
            Lawnf.TravelAdvanced((AdvBuff)MNEntryTravelMgrPatch.TravelId2))
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

        // 检查游戏内词条是否激活（3.4.1：TravelAdvanced 接受 AdvBuff 枚举）
        if (MNEntryTravelMgrPatch.TravelId2 >= 0 &&
            Lawnf.TravelAdvanced((AdvBuff)MNEntryTravelMgrPatch.TravelId2))
        {
            if (__instance.attributeCountdown > 5f)
                __instance.attributeCountdown = 4.5f;
        }
    }
}