using System.Collections;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
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
/// 参考GoldImitater.BepInEx的实现方式
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
                        toRemove.Add(card);
                    }
                }
                else
                {
                    toRemove.Add(card);
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
    public static bool Prefix_UseSun(Board __instance, int count)
    {
        if (!UnlimitedSunlight) return true;

        try
        {
            if (__instance != null)
            {
                __instance.theSun -= count;
                __instance.theUsedSun += count;
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
    private static System.Reflection.FieldInfo _cachedCursedPlantsField = null;
    
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
/// 诅咒免疫补丁 - Board.Update
/// 定期清除植物的诅咒视觉效果，并设置踩踏免疫属性
/// </summary>
[HarmonyPatch(typeof(Board), nameof(Board.Update))]
public static class BoardUpdateCursePatch
{
    private static float _curseClearTimer = 0f;
    private const float _curseClearInterval = 1f;
    private static float _trampleImmunityTimer = 0f;
    private const float _trampleImmunityInterval = 0.1f;
    
    [HarmonyPostfix]
    public static void Postfix()
    {
        try
        {
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
/// 当启用状态并存时，只有在僵尸同时有红温和寒冷状态时才阻止Warm方法
/// 这样可以保护并存状态，同时允许正常的冻结解除
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
            
            // 只有当僵尸同时有红温状态和寒冷/冻结状态时才阻止Warm
            // 这样可以保护并存状态
            bool hasWarm = __instance.isJalaed || __instance.isEmbered;
            bool hasCold = __instance.coldTimer > 0 || __instance.freezeTimer > 0;
            
            if (hasWarm && hasCold)
            {
                return false; // 阻止原方法执行，保护并存状态
            }
        }
        catch { }
        
        return true; // 正常执行
    }
}

/// <summary>
/// 僵尸状态并存补丁 - Zombie.Unfreezing
/// 当启用状态并存时，只有在僵尸同时有红温和冻结状态时才阻止
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
            
            // 只有当僵尸同时有红温状态和冻结状态时才阻止Unfreezing
            bool hasWarm = __instance.isJalaed || __instance.isEmbered;
            bool hasFrozen = __instance.freezeTimer > 0;
            
            if (hasWarm && hasFrozen)
            {
                return false; // 阻止原方法执行，保护并存状态
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
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.SetJalaed))]
public static class ZombieSetJalaedCoexistPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Zombie __instance)
    {
        if (!ZombieStatusCoexist) return true; // 不启用时正常执行原方法
        
        try
        {
            if (__instance == null) return true;
            
            // 手动设置红温状态，不调用原方法（原方法会清除寒冷状态）
            __instance.isJalaed = true;
            
            // 原方法还会设置 isEmbered = false，我们也需要做这个
            // 但为了状态并存，我们不清除 isEmbered
            
            return false; // 阻止原方法执行
        }
        catch 
        { 
            return true; // 出错时执行原方法
        }
    }
}

/// <summary>
/// 僵尸状态并存补丁 - Zombie.SetEmbered (余烬状态)
/// 当启用状态并存时，完全阻止原方法执行，手动设置余烬状态以保留寒冷状态
/// </summary>
[HarmonyPatch(typeof(Zombie), nameof(Zombie.SetEmbered))]
public static class ZombieSetEmberedCoexistPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Zombie __instance)
    {
        if (!ZombieStatusCoexist) return true; // 不启用时正常执行原方法
        
        try
        {
            if (__instance == null) return true;
            
            // 手动设置余烬状态，不调用原方法（原方法会清除寒冷状态）
            __instance.isEmbered = true;
            
            return false; // 阻止原方法执行
        }
        catch 
        { 
            return true; // 出错时执行原方法
        }
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
            Plant targetPlant = null;
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
                    catch (Exception e) { }
                }
            }
        }
        
        // 僵尸状态并存 - 在每帧维护红温与寒冷状态的并存
        if (ZombieStatusCoexist)
        {
            try
            {
                foreach (var zombie in Board.Instance.zombieArray)
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

            int baseId = TravelMgr.advancedBuffs.Count;

            // 注册两个词条
            TravelId1 = baseId;
            TravelId2 = baseId + 1;

            // 扩展数组
            bool[] newUpgrades = new bool[__instance.advancedUpgrades.Count + 2];
            Array.Copy(__instance.advancedUpgrades.ToArray(), newUpgrades, __instance.advancedUpgrades.Count);
            __instance.advancedUpgrades = newUpgrades;

            // 注册词条文本
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