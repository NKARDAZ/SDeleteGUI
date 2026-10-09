namespace SDeleteGUI.Services;

internal static class AppPaths
{
	public static string DataDirectory => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"Nkardaz",
		"SDeleteGUI");

	public static string SettingsFilePath => Path.Combine(DataDirectory, "settings.json");

	public static string CrashLogPath => Path.Combine(DataDirectory, "crash.log");

	public static void AppendCrashLog(string source, object? error)
	{
		try
		{
			Directory.CreateDirectory(DataDirectory);
			File.AppendAllText(CrashLogPath, $"{DateTime.Now} [{source}]: {error}\n\n");
		}
		catch
		{
		}
	}
}
