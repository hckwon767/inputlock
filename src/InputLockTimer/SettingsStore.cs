using System.Text.Json;

namespace InputLockTimer;

/// <summary>설정 파일 입출력. 입력 내용은 저장하지 않으며 설정만 사용자 프로필 아래에 저장합니다.</summary>
internal static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InputLockTimer");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, Options);

    public static AppSettings? Deserialize(string json)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
            settings?.Normalize();
            return settings;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static AppSettings Load(out bool fileExisted, out string? warning)
    {
        warning = null;
        fileExisted = File.Exists(SettingsPath);
        if (!fileExisted) return new AppSettings();

        try
        {
            var loaded = Deserialize(File.ReadAllText(SettingsPath));
            if (loaded != null) return loaded;
            warning = "설정 파일 형식이 올바르지 않아 기본 설정을 사용합니다.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warning = "설정 파일을 읽지 못해 기본 설정을 사용합니다.";
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, Serialize(settings));
        File.Move(temp, SettingsPath, overwrite: true);
    }
}
