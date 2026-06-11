using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils;
using HarmonyLib;
using UnityEngine;
using static ToolModBepInEx.PatchMgr;

namespace ToolModBepInEx.Patches;

[HarmonyPatch(typeof(TravelMgr))]
public static class TravelMgrPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(TravelMgr.GetNormalBuff))]
    public static void PostGetNormalBuff(TravelMgr __instance, AdvBuff __0)
    {
        try
        {
            __instance.StartCoroutine(SyncBuffsDelayed());
        }
        catch (Exception ex)
        {
            Core.Instance.Value.LoggerInstance.LogWarning($"[PVZRHTools] PostGetNormalBuff: {ex.Message}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(TravelMgr.GetUltiBuff))]
    public static void PostGetUltiBuff(TravelMgr __instance, UltiBuff __0, bool __1)
    {
        try
        {
            __instance.StartCoroutine(SyncBuffsDelayed());
        }
        catch (Exception ex)
        {
            Core.Instance.Value.LoggerInstance.LogWarning($"[PVZRHTools] PostGetUltiBuff: {ex.Message}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(TravelMgr.GetDebuff))]
    public static void PostGetDebuff(TravelMgr __instance, TravelDebuff __0)
    {
        try
        {
            __instance.StartCoroutine(SyncBuffsDelayed());
        }
        catch (Exception ex)
        {
            Core.Instance.Value.LoggerInstance.LogWarning($"[PVZRHTools] PostGetDebuff: {ex.Message}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(TravelMgr.GetInvestBuff))]
    public static void PostGetInvestBuff(TravelMgr __instance, InvestBuff __0)
    {
        try
        {
            __instance.StartCoroutine(SyncBuffsDelayed());
        }
        catch (Exception ex)
        {
            Core.Instance.Value.LoggerInstance.LogWarning($"[PVZRHTools] PostGetInvestBuff: {ex.Message}");
        }
    }

    [HarmonyFinalizer]
    [HarmonyPatch(nameof(TravelMgr.GetNormalBuff))]
    public static Exception FinalizerGetNormalBuff(Exception __exception)
    {
        if (__exception != null)
        {
            try
            {
                Core.Instance.Value.LoggerInstance.LogWarning(
                    $"[PVZRHTools] GetNormalBuff 异常已忽略: {__exception.GetType().Name} - {__exception.Message}");
            }
            catch
            {
            }

            return null;
        }

        return null;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(nameof(TravelMgr.UpdateSynergies))]
    public static Exception FinalizerUpdateSynergies(Exception __exception)
    {
        if (__exception != null)
        {
            try
            {
                Core.Instance.Value.LoggerInstance.LogWarning(
                    $"[PVZRHTools] UpdateSynergies 异常已忽略: {__exception.GetType().Name} - {__exception.Message}");
            }
            catch
            {
            }

            return null;
        }

        return null;
    }

    private static IEnumerator SyncBuffsDelayed()
    {
        yield return new WaitForSeconds(0.1f);
        SyncGameBuffsToModifier();
    }
}
