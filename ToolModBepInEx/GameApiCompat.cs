using System.Collections.Generic;
using UnityEngine;

namespace ToolModBepInEx;

/// <summary>
/// 3.7 API 兼容辅助。
/// </summary>
internal static class GameApiCompat
{
    internal static void ShowInGameText(string text, float duration = 2f)
    {
        try
        {
            Debug.Log($"[PVZRHTools] {text}");
        }
        catch
        {
            // ignored
        }
    }

    internal static List<CardUI> GetInGameCards(InGameUI? ui)
    {
        var list = new List<CardUI>();
        if (ui?.cards == null)
            return list;

        foreach (var card in ui.cards)
        {
            if (card != null)
                list.Add(card);
        }

        return list;
    }

    internal static void ClearCursedPlants(Zombie zombie)
    {
        try
        {
            var field = typeof(Zombie).GetField(
                "cursedPlants",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (field?.GetValue(zombie) is Il2CppSystem.Collections.Generic.List<Plant> cursedPlants &&
                cursedPlants.Count > 0)
            {
                cursedPlants.Clear();
            }
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>
    /// 3.7：诅咒改为 Plant 上的 EffectType.Curse（PlantCurseEffect）。
    /// </summary>
    internal static void RemovePlantCurseEffect(Plant? plant)
    {
        if (plant == null)
            return;

        try
        {
            if (plant.HasBuff(EffectType.Curse))
                plant.RemoveBuff(EffectType.Curse);
        }
        catch
        {
            // ignored
        }
    }
}
