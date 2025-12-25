using System.Collections;
using System.Collections.Generic;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
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
        try
        {
            if (NewZombieUpdateCD > 0 && NewZombieUpdateCD < 30 &&
                Board.Instance != null && Board.Instance.newZombieWaveCountDown > NewZombieUpdateCD)
                Board.Instance.newZombieWaveCountDown = NewZombieUpdateCD;
        }
        catch { }
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
    }

    [HarmonyPostfix]
    [HarmonyPatch("Update")]
    public static void PostUpdate(CardUI __instance)
    {
        try
        {
            if (__instance == null) return;
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
            TimeSlow = !TimeSlow;
            TimeStop = false;
            Time.timeScale = TimeSlow ? 0.2f : SyncSpeed;
        }

        if (__instance.buttonNumber == 13) BottomEnabled = GameObject.Find("Bottom") is not null;
    }
}

[HarmonyPatch(typeof(InGameText), "ShowText")]
public static class InGameTextPatch
{
    public static void Postfix()
    {
        for (var i = 0; i < InGameAdvBuffs.Length; i++)
            if (InGameAdvBuffs[i] != GameAPP.gameAPP.GetOrAddComponent<TravelMgr>().advancedUpgrades[i])
            {
                SyncInGameBuffs();
                return;
            }

        for (var i = 0; i < InGameUltiBuffs.Length; i++)
            if (InGameUltiBuffs[i] != GetBoolArray(GameAPP.gameAPP.GetOrAddComponent<TravelMgr>().ultimateUpgrades)[i])
            {
                SyncInGameBuffs();
                return;
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
    public static bool Prefix(Plant __instance)
    {
        if (HardPlant)
        {
            return false;
        }
        return true;
    }
}

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
#if false
        foreach (var plant in __instance.board.plantArray)
            try
            {
                if (plant.thePlantRow == __instance.thePlantRow && plant.thePlantColumn == __instance.thePlantColumn)
                {
                    if(plant.thePlantType==__instance.thePlantType)
                        MelonLogger.Msg("TRUE");
                    var array = MixData.data.Cast<Array>();
                    for (int i = 0; i < array.Length; i++)
                    {
                        var element = array.GetValue(i);
                        MelonLogger.Msg($"{i}: {element}");
                    }

                    MelonLogger.Msg($"{plant.thePlantRow},{plant.thePlantColumn},{plant.thePlantType}");
                    return true;
                }
            }
            catch
            {
            }
#endif
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
                infoChild.GameObject().GetComponent<TextMeshProUGUI>().text =
                    $"波数: {Board.Instance.theWave}/{Board.Instance.theMaxWave} 刷新CD: {Board.Instance.newZombieWaveCountDown:N1}";
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

[HarmonyPatch(typeof(SuperSnowGatling), "Update")]
public static class SuperSnowGatlingPatchA
{
    public static void Postfix(SuperSnowGatling __instance)
    {
        if (!UltimateSuperGatling) return;
        try
        {
            if (__instance != null) __instance.timer = 0.1f;
        }
        catch { }
    }
}

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
        if (BuffRefreshNoLimit) __instance.refreshTimes = 2147483647;
    }
}

[HarmonyPatch(typeof(TravelStore), "RefreshBuff")]
public static class TravelStorePatch
{
    public static void Postfix(TravelStore __instance)
    {
        if (BuffRefreshNoLimit) __instance.count = 0;
    }
}

[HarmonyPatch(typeof(ShootingMenu), nameof(ShootingMenu.Refresh))]
public static class ShootingMenuPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (BuffRefreshNoLimit) ShootingManager.Instance.refreshCount = 2147483647;
    }
}
[HarmonyPatch(typeof(FruitNinjaManager),nameof(FruitNinjaManager.LoseScore))]
public static class FruitNinjaManagerPatch
{
    [HarmonyPrefix]
    public static void Postfix(ref float value)
    {
        if (BuffRefreshNoLimit) value = -1e-10f;
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
    private static Plant aa = null;
    
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
            if (Board.Instance.boardTag.isColumn)
            {
                CreatePlant.Instance.SetPlant(aa.thePlantColumn, aa.thePlantRow, aa.thePlantType);
            }
        }
    }
}

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
    public static Dictionary<Zombie.FirstArmorType, int> Health1st { get; set; } = [];
    public static Dictionary<Zombie.SecondArmorType, int> Health2nd { get; set; } = [];
    public static Dictionary<PlantType, int> HealthPlants { get; set; } = [];
    public static Dictionary<ZombieType, int> HealthZombies { get; set; } = [];
    public static bool HyponoEmperorNoCD { get; set; } = false;
    public static int ImpToBeThrown { get; set; } = 37;
    public static bool[] InGameAdvBuffs { get; set; } = [];
    public static bool[] InGameDebuffs { get; set; } = [];
    public static bool[] InGameUltiBuffs { get; set; } = [];
    public static bool ItemExistForever { get; set; } = false;
    public static int JachsonSummonType { get; set; } = 7;
    public static bool JackboxNotExplode { get; set; } = false;
    public static int LockBulletType { get; set; } = -2;
    public static bool LockMoney { get; set; } = false;
    public static int LockMoneyCount { get; set; } = 3000;
    public static int LockPresent { get; set; } = -1;
    public static int LockWheat { get; set; } = -1;
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

            if (Input.GetKeyDown(Core.KeyShowGameInfo.Value.Value)) ShowGameInfo = !ShowGameInfo;
            if (!TimeStop && !TimeSlow) Time.timeScale = SyncSpeed;

            if (!TimeStop && TimeSlow) Time.timeScale = 0.2f;
            if (InGameBtnPatch.BottomEnabled || (TimeStop && !TimeSlow)) Time.timeScale = 0;

            try
            {
                var slow = GameObject.Find("SlowTrigger").transform;
                slow.GetChild(0).gameObject.GetComponent<TextMeshProUGUI>().text = $"时停(x{Time.timeScale})";
                slow.GetChild(1).gameObject.GetComponent<TextMeshProUGUI>().text = $"时停(x{Time.timeScale})";
                if (Input.GetKeyDown(Core.KeyTopMostCardBank.Value.Value))
                {
                    if (GameAPP.canvas.GetComponent<Canvas>().sortingLayerName == "Default")
                        GameAPP.canvas.GetComponent<Canvas>().sortingLayerName = "UI";
                    else
                        GameAPP.canvas.GetComponent<Canvas>().sortingLayerName = "Default";
                }

                if (Input.GetKeyDown(Core.KeyAlmanacCreatePlant.Value.Value) && AlmanacSeedType != -1)
                    CreatePlant.Instance.SetPlant(Mouse.Instance.theMouseColumn, Mouse.Instance.theMouseRow,
                        (PlantType)AlmanacSeedType);
                if (Input.GetKeyDown(Core.KeyAlmanacZombieMindCtrl.Value.Value))
                    Core.AlmanacZombieMindCtrl.Value.Value = !Core.AlmanacZombieMindCtrl.Value.Value;
                if (Input.GetKeyDown(Core.KeyAlmanacCreateZombie.Value.Value) &&
                    AlmanacZombieType is not ZombieType.Nothing)
                {
                    if (Core.AlmanacZombieMindCtrl.Value.Value)
                        CreateZombie.Instance.SetZombieWithMindControl(Mouse.Instance.theMouseRow, AlmanacZombieType,
                            Mouse.Instance.mouseX);
                    else
                        CreateZombie.Instance.SetZombie(Mouse.Instance.theMouseRow, AlmanacZombieType,
                            Mouse.Instance.mouseX);
                }

                // 植物罐子 - 使用 ScaryPot_plant 类型
                if (Input.GetKeyDown(Core.KeyAlmanacCreatePlantVase.Value.Value) && AlmanacSeedType != -1)
                {
                    try
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
                    catch { }
                }

                // 僵尸罐子 - 使用 ScaryPot_zombie 类型
                if (Input.GetKeyDown(Core.KeyAlmanacCreateZombieVase.Value.Value) &&
                    AlmanacZombieType is not ZombieType.Nothing)
                {
                    try
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
                    catch { }
                }

                if (Input.GetKeyDown(Core.KeyRandomCard.Value.Value))
                    RandomCard = !RandomCard;
                var t = Board.Instance.boardTag;
                t.enableTravelPlant = t.enableTravelPlant || UnlockAllFusions;
                Board.Instance.boardTag = t;
            }
            catch (NullReferenceException)
            {
            }
        }

        if (!InGame()) return;
        if (LockSun) Board.Instance.theSun = LockSunCount;
        if (LockMoney) Board.Instance.theMoney = LockMoneyCount;
        if (StopSummon) Board.Instance.iceDoomFreezeTime = 1;
        if (ZombieSea)
            if (++seaTime >= ZombieSeaCD &&
                Board.Instance.theWave is not 0 && Board.Instance.theWave < Board.Instance.theMaxWave &&
                GameAPP.theGameStatus == (int)GameStatus.InGame)
            {
                foreach (var j in SeaTypes)
                {
                    if (j < 0) continue;
                    for (var i = 0; i < Board.Instance.rowNum; i++) CreateZombie.Instance.SetZombie(i, (ZombieType)j, 11f);
                }

                seaTime = 0;
            }

        if (GarlicDay && ++garlicDayTime >= 500 && GameAPP.theGameStatus == (int)GameStatus.InGame)
        {
            garlicDayTime = 0;
            _ = FindObjectsOfTypeAll(Il2CppType.Of<Zombie>()).All(b =>
            {
                b?.TryCast<Zombie>()?.StartCoroutine_Auto(b?.TryCast<Zombie>()?.DeLayGarliced(0.1f, false, false));
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
                    catch (Exception e) { }
                }
            }
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
        var travelMgr = GameAPP.gameAPP.GetOrAddComponent<TravelMgr>();
        Board.Instance.freeCD = FreeCD;
        yield return null;
        if (!(GameAPP.theBoardType == (LevelType)3 && Board.Instance.theCurrentSurvivalRound != 1))
        {
            yield return null;

            var advs = travelMgr.advancedUpgrades;

            for (var i = 0; i < advs.Count; i++)
            {
                advs[i] = AdvBuffs[i] || advs[i];
                yield return null;
            }

            var ultis = travelMgr.ultimateUpgrades;
            for (var i = 0; i < ultis.Count; i++)
            {
                ultis[i] = UltiBuffs[i] || ultis[i] is 1 ? 1 : 0;
                yield return null;
            }

            var deb = travelMgr.debuff;
            for (var i = 0; i < deb.Count; i++)
            {
                deb[i] = Debuffs[i] || deb[i];
                yield return null;
            }
        }

        InGameAdvBuffs = new bool[TravelMgr.advancedBuffs.Count];
        InGameUltiBuffs = new bool[TravelMgr.ultimateBuffs.Count];
        InGameDebuffs = new bool[TravelMgr.debuffs.Count];
        yield return null;

        InGameAdvBuffs = travelMgr.advancedUpgrades;
        InGameUltiBuffs = GetBoolArray(travelMgr.ultimateUpgrades);
        InGameDebuffs = travelMgr.debuff;
        yield return null;
        new Thread(SyncInGameBuffs).Start();

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
    public static unsafe void SetZombieList(int index, int wave, ZombieType value)
    {
        var fieldInfo = typeof(InitZombieList).GetField("NativeFieldInfoPtr_zombieList",
            BindingFlags.NonPublic | BindingFlags.Static);

        if (fieldInfo is not null)
        {
            var nativeFieldInfoPtr = (IntPtr)fieldInfo.GetValue(null)!;
            Unsafe.SkipInit(out IntPtr intPtr);
            IL2CPP.il2cpp_field_static_get_value(nativeFieldInfoPtr, &intPtr);
            if (intPtr == IntPtr.Zero) return;
            var arrayData = (ZombieType*)intPtr.ToPointer();
            arrayData[index * 101 + wave + 9] = value;
        }
    }

    public static void SyncInGameBuffs()
    {
        if (!InGame()) return;
        DataSync.Instance.Value.SendData(new SyncTravelBuff
        {
            AdvInGame = [.. GameAPP.gameAPP.GetOrAddComponent<TravelMgr>().advancedUpgrades!],
            UltiInGame = [.. GetBoolArray(GameAPP.gameAPP.GetOrAddComponent<TravelMgr>().ultimateUpgrades)!],
            DebuffsInGame = [.. GameAPP.gameAPP.GetOrAddComponent<TravelMgr>().debuff!]
        });
    }

    public static void UpdateInGameBuffs()
    {
        for (var i = 0; i < GameAPP.gameAPP.GetOrAddComponent<TravelMgr>().advancedUpgrades.Count; i++)
            GameAPP.gameAPP.GetOrAddComponent<TravelMgr>().advancedUpgrades![i] = InGameAdvBuffs[i];
        for (var i = 0; i < GameAPP.gameAPP.GetOrAddComponent<TravelMgr>().ultimateUpgrades.Count; i++)
            GameAPP.gameAPP.GetOrAddComponent<TravelMgr>().ultimateUpgrades![i] = GetIntArray(InGameUltiBuffs)[i];
        for (var i = 0; i < GameAPP.gameAPP.GetOrAddComponent<TravelMgr>().debuff.Count; i++)
            GameAPP.gameAPP.GetOrAddComponent<TravelMgr>().debuff![i] = InGameDebuffs[i];
    }
}