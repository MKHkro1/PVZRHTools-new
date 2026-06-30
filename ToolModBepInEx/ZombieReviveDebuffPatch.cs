using HarmonyLib;
using UnityEngine;

namespace ToolModBepInEx;

/// <summary>
/// 自定义 Debuff #1005（阴魂不散）的僵尸满血复活概率。
/// 原版逻辑：Lawnf.TravelDebuff(1005) 时 Random.Range(0, 3) == 1，即 1/3 概率。
/// </summary>
public static class ZombieReviveDebuffPatch
{
    public const int DebuffId = 1005;

    [ThreadStatic] private static bool s_interceptReviveRoll;
    [ThreadStatic] private static bool s_forceReviveHit;
    [ThreadStatic] private static bool s_pendingReviveRoll;

    [HarmonyPatch(typeof(Zombie), nameof(Zombie.Die))]
    public static class DiePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Zombie __instance)
        {
            s_interceptReviveRoll = false;
            s_pendingReviveRoll = false;
            if (!PatchMgr.ZombieReviveDebuffCustomEnabled) return;

            try
            {
                if (!Lawnf.TravelDebuff((TravelDebuff)DebuffId)) return;
                if (__instance.revived || __instance.isMindControlled) return;

                float chance = Mathf.Clamp(PatchMgr.ZombieReviveDebuffChance, 0f, 100f);
                s_forceReviveHit = UnityEngine.Random.Range(0f, 100f) < chance;
                s_interceptReviveRoll = true;
                s_pendingReviveRoll = true;
            }
            catch
            {
                s_interceptReviveRoll = false;
                s_pendingReviveRoll = false;
            }
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception)
        {
            s_interceptReviveRoll = false;
            s_pendingReviveRoll = false;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(UnityEngine.Random), nameof(UnityEngine.Random.Range), new[] { typeof(int), typeof(int) })]
    public static class RandomRangeIntPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(int minInclusive, int maxExclusive, ref int __result)
        {
            if (!s_interceptReviveRoll || !s_pendingReviveRoll) return true;
            if (minInclusive != 0 || maxExclusive != 3) return true;

            // 仅拦截阴魂不散判定用的第一次 Random.Range(0, 3)，不影响后续音效等随机
            s_pendingReviveRoll = false;
            __result = s_forceReviveHit ? 1 : 0;
            return false;
        }
    }
}

/// <summary>
/// 不依赖词条的自定义概率复活：跳过 Die，原地满血恢复，不设置 revived，可重复触发。
/// </summary>
public static class ZombieFreeRevivePatch
{
    [HarmonyPatch(typeof(Zombie), nameof(Zombie.Die))]
    public static class DiePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Zombie __instance, int reason)
        {
            if (!PatchMgr.ZombieFreeReviveEnabled) return true;
            if (__instance == null) return true;
            if (__instance.isMindControlled) return true;
            // 与阴魂不散一致：部分即死类 reason 不参与复活
            if (reason == 1) return true;

            float chance = Mathf.Clamp(PatchMgr.ZombieFreeReviveChance, 0f, 100f);
            if (chance <= 0f) return true;
            if (UnityEngine.Random.Range(0f, 100f) >= chance) return true;

            try
            {
                RestoreFullHealth(__instance);
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    private static void RestoreFullHealth(Zombie z)
    {
        z.theHealth = Mathf.Max(1, z.theMaxHealth);
        if (z.theFirstArmorMaxHealth > 0)
            z.theFirstArmorHealth = z.theFirstArmorMaxHealth;
        if (z.theSecondArmorMaxHealth > 0)
            z.theSecondArmorHealth = z.theSecondArmorMaxHealth;
        z.theStatus = ZombieStatus.Default;
        try { z.UpdateHealthText(); } catch { }
    }
}
