using System;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace ToolModBepInEx
{
    /// <summary>
    /// 游戏版本审核闸（4.0）：本插件按 4.0 interop 编译（补丁目标/字段布局均为 4.0）。
    /// 判据 = 4.0 专属特征 API「PlantDataManager.IsUnlocked(PlantType)」——纯元数据探测
    /// （Assembly.GetType + GetMethod），不触发 IL2CPP 静态构造；游戏自报版本串仅作提示、不参与判定
    /// （本机 3.9 时代 GameAPP.version 曾停报旧值，字符串不可信是既定结论）。
    /// fail-safe：探测自身不可用 → 放行并告警（绝不把正确的游戏误杀）；特征明确缺失 → 拦截。
    /// 逃生阀：BepInEx\config\tyyh.toolmod.cfg 的 SkipVersionCheck=true 跳过审核。
    /// 拦截效果 = Core.Load 提前 return → 不注册组件、不打补丁 → 插件惰性、修改器不启动。
    /// </summary>
    internal static class GameVersionGate
    {
        internal static bool Check(ManualLogSource logger, ConfigFile config)
        {
            ConfigEntry<bool> skip = config.Bind("PVZRHTools", "SkipVersionCheck", false,
                "跳过游戏版本审核（仅当确认游戏是 4.0 却被误拦时改为 true）");

            string ver = "未知";
            try { ver = GameAPP.version ?? "未知"; } catch { /* 版本串读不到不影响判定 */ }

            bool hasFeature;
            try
            {
                Type? t = typeof(GameAPP).Assembly.GetType("PlantDataManager", false);
                hasFeature = t != null && t.GetMethod("IsUnlocked", new[] { typeof(PlantType) }) != null;
            }
            catch (Exception ex)
            {
                logger.LogWarning($"[PVZRHTools] 版本审核：特征探测不可用（{ex.Message}），放行");
                return true;
            }

            if (hasFeature)
            {
                if (!string.IsNullOrEmpty(ver) && ver.Contains("4.0", StringComparison.Ordinal))
                    logger.LogInfo($"[PVZRHTools] 版本审核通过：4.0 特征 API 存在，游戏自报 {ver}");
                else
                    logger.LogWarning($"[PVZRHTools] 版本审核：特征 API 存在但游戏自报 {ver}（字符串可能过期，按特征放行）");
                return true;
            }

            if (skip.Value)
            {
                logger.LogWarning($"[PVZRHTools] 版本审核被 SkipVersionCheck 跳过（游戏自报 {ver}）");
                return true;
            }

            logger.LogError($"[PVZRHTools] 游戏版本不符：期望 4.0（未找到 PlantDataManager.IsUnlocked），游戏自报 {ver}。已停用修改器全部功能。");
            try
            {
                Core.MessageBox(0,
                    $"当前游戏不是 4.0（游戏自报版本：{ver}）。\n\n" +
                    "本修改器按 4.0 编译，已停用全部功能以避免异常。\n" +
                    "请在植物大战僵尸融合版 4.0 中使用本修改器。\n\n" +
                    "（若确认游戏就是 4.0 却被误拦：编辑 BepInEx\\config\\tyyh.toolmod.cfg，\n把 SkipVersionCheck 改为 true 后重启游戏）",
                    "PVZRHTools 版本审核", 0x10u);
            }
            catch { }
            return false;
        }
    }
}
