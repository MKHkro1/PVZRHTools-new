using System.IO;

namespace ToolModData;

public static class ModifierPaths
{
    public const string BootConfigPath = @"BepInEx\config\ModifierBootConfig.json";
    public const string InitDataPath = @"BepInEx\config\InitData.json";
    public const string SaveSettingsPath = @"BepInEx\config\ModifierSettings.json";
    public const string LegacyInitDataPath = @"PVZRHTools\InitData.json";
    public const string ModifierExeName = "PVZRHTools.exe";
    public const string LegacyModifierSubDir = "PVZRHTools";

    public static string GetInitDataPath(string? gameRoot = null)
    {
        gameRoot ??= Directory.GetCurrentDirectory();
        string primary = Path.Combine(gameRoot, InitDataPath);
        if (File.Exists(primary))
        {
            return primary;
        }

        return Path.Combine(gameRoot, LegacyInitDataPath);
    }

    public static string GetSaveSettingsPath(string? gameRoot = null)
    {
        gameRoot ??= Directory.GetCurrentDirectory();
        if (Directory.Exists(Path.Combine(gameRoot, "BepInEx")))
        {
            return Path.Combine(gameRoot, SaveSettingsPath);
        }

        return Path.Combine(gameRoot, "UserData", "ModifierSettings.json");
    }

    public static string ResolveModifierExe(string? gameRoot = null)
    {
        gameRoot ??= Directory.GetCurrentDirectory();
        string rootExe = Path.Combine(gameRoot, ModifierExeName);
        if (File.Exists(rootExe))
        {
            return rootExe;
        }

        string legacyExe = Path.Combine(gameRoot, LegacyModifierSubDir, ModifierExeName);
        if (File.Exists(legacyExe))
        {
            return legacyExe;
        }

        try
        {
            string bootPath = Path.Combine(gameRoot, BootConfigPath);
            if (File.Exists(bootPath))
            {
                string json = File.ReadAllText(bootPath);
                BootConfig boot = System.Text.Json.JsonSerializer.Deserialize<BootConfig>(json)!;
                if (!string.IsNullOrWhiteSpace(boot.ModifierPath) && File.Exists(boot.ModifierPath))
                {
                    return boot.ModifierPath;
                }
            }
        }
        catch
        {
        }

        return legacyExe;
    }

    public static void EnsureInitDataDirectory(string? gameRoot = null)
    {
        gameRoot ??= Directory.GetCurrentDirectory();
        Directory.CreateDirectory(Path.Combine(gameRoot, "BepInEx", "config"));
        Directory.CreateDirectory(Path.Combine(gameRoot, LegacyModifierSubDir));
    }

    public static void EnsureSaveSettingsDirectory(string? gameRoot = null)
    {
        string path = GetSaveSettingsPath(gameRoot);
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}

[Serializable]
public struct BootConfig
{
    public string ModifierPath { get; set; }
    public string GameVersion { get; set; }
    public bool ModifierEnabled { get; set; }
}
