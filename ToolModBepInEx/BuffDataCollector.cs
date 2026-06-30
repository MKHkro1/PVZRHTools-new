using System.Collections;
using System.Reflection;
using BepInEx.Logging;

namespace ToolModBepInEx;

/// <summary>
/// 统一收集词条文本：TravelDictionary + CustomizeLib 二创插件缓存。
/// CustomizeLib 在 RegisterCustomBuff 时会清空 TravelDictionary，自定义词条可能只留在 Custom*Buffs 中。
/// </summary>
internal static class BuffDataCollector
{
    private static ManualLogSource? Logger => PatchMgr.MLogger;

    public static Dictionary<int, string> GetAdvancedBuffTexts()
    {
        var idToText = new Dictionary<int, string>();
        if (TravelDictionary.advancedBuffsText != null)
        {
            foreach (var kvp in TravelDictionary.advancedBuffsText)
            {
                if (!string.IsNullOrEmpty(kvp.Value))
                    idToText[(int)kvp.Key] = kvp.Value;
            }
        }
        MergeCustomBuffs(idToText, "CustomAdvancedBuffs", textItemIndex: 1);
        MergeCustomBuffText(idToText, customBuffTypeOrdinal: 0);
        return idToText;
    }

    public static Dictionary<int, string> GetUltimateBuffTexts()
    {
        var idToText = new Dictionary<int, string>();
        if (TravelDictionary.ultimateBuffsText != null)
        {
            foreach (var kvp in TravelDictionary.ultimateBuffsText)
            {
                if (!string.IsNullOrEmpty(kvp.Value))
                    idToText[(int)kvp.Key] = kvp.Value;
            }
        }
        MergeCustomBuffs(idToText, "CustomUltimateBuffs", textItemIndex: 1);
        MergeCustomBuffText(idToText, customBuffTypeOrdinal: 1);
        return idToText;
    }

    public static Dictionary<int, string> GetDebuffTexts()
    {
        var idToText = new Dictionary<int, string>();
        if (TravelDictionary.debuffData != null)
        {
            foreach (var kvp in TravelDictionary.debuffData)
            {
                int id = (int)kvp.Key;
                string? text = null;
                try { text = kvp.Value.Item1; }
                catch { }
                if (string.IsNullOrWhiteSpace(text))
                    text = ((TravelDebuff)id).ToString();
                idToText[id] = text;
            }
        }

        MergeCustomBuffs(idToText, "CustomDebuffs", textItemIndex: 0);
        MergeCustomBuffText(idToText, customBuffTypeOrdinal: 2);
        return idToText;
    }

    public static List<int> GetOrderedIds(Dictionary<int, string> idToText)
    {
        var ids = new List<int>();
        if (idToText.Count == 0) return ids;

        int maxKey = GetMaxKey(idToText);
        for (int id = 0; id <= maxKey; id++)
        {
            if (idToText.ContainsKey(id) && !string.IsNullOrEmpty(idToText[id]))
                ids.Add(id);
        }
        return ids;
    }

    public static List<string> ToLines(Dictionary<int, string> idToText)
    {
        var lines = new List<string>();
        foreach (var id in GetOrderedIds(idToText))
            lines.Add($"#{id} {idToText[id]}");
        return lines;
    }

    public static int GetMaxKey(Dictionary<int, string> idToText)
    {
        if (idToText.Count == 0) return -1;
        int maxKey = -1;
        foreach (var id in idToText.Keys)
            if (id > maxKey) maxKey = id;
        return maxKey;
    }

    public static int GetRequiredArraySize(Dictionary<int, string> idToText, int fallbackCount = 0)
    {
        int maxKey = GetMaxKey(idToText);
        return Math.Max(maxKey + 1, fallbackCount);
    }

    private static void MergeCustomBuffs(Dictionary<int, string> target, string propertyName, int textItemIndex)
    {
        try
        {
            var dict = ResolveCustomCoreProperty(propertyName);
            if (dict == null) return;

            int merged = 0;
            foreach (DictionaryEntry entry in dict)
            {
                if (entry.Key is not int id) continue;
                var text = ExtractTupleText(entry.Value, textItemIndex);
                if (string.IsNullOrEmpty(text)) continue;
                if (!target.ContainsKey(id))
                    merged++;
                target[id] = text;
            }

            if (merged > 0)
                Logger?.LogInfo($"[PVZRHTools] BuffDataCollector: 从 CustomizeLib.{propertyName} 合并 {merged} 条词条");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[PVZRHTools] BuffDataCollector: 读取 CustomizeLib.{propertyName} 失败: {ex.Message}");
        }
    }

    private static void MergeCustomBuffText(Dictionary<int, string> target, int customBuffTypeOrdinal)
    {
        try
        {
            var dict = ResolveCustomCoreProperty("CustomBuffText");
            if (dict == null) return;

            int merged = 0;
            foreach (DictionaryEntry entry in dict)
            {
                if (entry.Key == null) continue;
                if (!TryParseCustomBuffKey(entry.Key, out int buffType, out int id)) continue;
                if (buffType != customBuffTypeOrdinal) continue;
                if (entry.Value is not string text || string.IsNullOrEmpty(text)) continue;
                if (!target.ContainsKey(id))
                    merged++;
                target[id] = text;
            }

            if (merged > 0)
                Logger?.LogInfo($"[PVZRHTools] BuffDataCollector: 从 CustomBuffText(type={customBuffTypeOrdinal}) 合并 {merged} 条词条");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[PVZRHTools] BuffDataCollector: 读取 CustomBuffText 失败: {ex.Message}");
        }
    }

    private static IDictionary? ResolveCustomCoreProperty(string propertyName)
    {
        var customCoreType = ResolveCustomCoreType();
        if (customCoreType == null) return null;
        var prop = customCoreType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
        return prop?.GetValue(null) as IDictionary;
    }

    private static Type? ResolveCustomCoreType()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = asm.GetType("CustomizeLib.BepInEx.CustomCore");
            if (type != null) return type;
        }
        return null;
    }

    private static bool TryParseCustomBuffKey(object key, out int buffType, out int id)
    {
        buffType = -1;
        id = -1;
        var keyType = key.GetType();
        if (!keyType.IsValueType) return false;

        try
        {
            var item1 = keyType.GetProperty("Item1")?.GetValue(key);
            var item2 = keyType.GetProperty("Item2")?.GetValue(key);
            if (item1 == null || item2 == null) return false;
            buffType = Convert.ToInt32(item1);
            id = Convert.ToInt32(item2);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ExtractTupleText(object? value, int textItemIndex)
    {
        if (value == null) return null;
        var type = value.GetType();
        if (!type.IsValueType) return null;
        var prop = type.GetProperty($"Item{textItemIndex + 1}");
        return prop?.GetValue(value) as string;
    }
}
