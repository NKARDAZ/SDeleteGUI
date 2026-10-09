using System.Text.Json;
using SDeleteGUI.Models;

namespace SDeleteGUI.Services;

/// <summary>
/// Loads and saves user settings at
/// %LOCALAPPDATA%\Nkardaz\SDeleteGUI\settings.json.
/// </summary>
internal static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string SettingsFilePath => AppPaths.SettingsFilePath;

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsFilePath;
            if (!File.Exists(path))
                return new AppSettings();

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return settings ?? new AppSettings();
        }
        catch (Exception ex)
        {
            LogError("SettingsService.Load", ex);
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var path = SettingsFilePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            LogError("SettingsService.Save", ex);
        }
    }

    private static void LogError(string source, Exception ex) =>
        AppPaths.AppendCrashLog(source, ex);
}
