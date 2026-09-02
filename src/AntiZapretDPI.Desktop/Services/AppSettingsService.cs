using System.IO;
using System.Text.Json;

namespace AntiZapretDPI.Services
{
    public class AppSettings
    {
        public bool HiddenMode { get; set; } = true;
        public bool AutoUpdate { get; set; } = true;
        public bool AutoStart { get; set; }
        public bool AutoSelectStrategy { get; set; }
        public bool AutoSelectPending { get; set; }
        public string? SelectedStrategy { get; set; }
        public bool PauseOnVpn { get; set; } = true;
    }

    public class AppSettingsService
    {
        private static readonly string SettingsFile = Path.Combine(
            AppContext.BaseDirectory,
            "settings.json"
        );

        public AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile));
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch
            {
            }

            return new AppSettings();
        }

        public void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
                File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
            }
        }
    }
}
