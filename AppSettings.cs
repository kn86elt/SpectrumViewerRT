using System.Text.Json;
using System.IO;

namespace SpectrumViewerRT;

public sealed class AppSettings
{
    public double Gain { get; set; } = Defaults.Gain;
    public double RecordGainDb { get; set; } = Defaults.RecordGainDb;
    public double RangeDb { get; set; } = Defaults.RangeDb;
    public double Fps { get; set; } = Defaults.Fps;
    public double TimeDivisionSeconds { get; set; } = Defaults.TimeDivisionSeconds;
    public int SourceIndex { get; set; } = Defaults.SourceIndex;
    public int ScaleIndex { get; set; } = Defaults.ScaleIndex;
    public int MaxFrequencyIndex { get; set; } = Defaults.MaxFrequencyIndex;
    public int MeterColorIndex { get; set; } = Defaults.MeterColorIndex;
    public int MeterStyleIndex { get; set; } = Defaults.MeterStyleIndex;
    public int DisplayModeIndex { get; set; } = Defaults.DisplayModeIndex;
    public bool AlwaysOnTop { get; set; } = Defaults.AlwaysOnTop;
    public bool MeterOnly { get; set; } = Defaults.MeterOnly;
    public bool GridEnabled { get; set; } = Defaults.GridEnabled;
    public bool ShowUnlitSegments { get; set; } = Defaults.ShowUnlitSegments;
    public bool GlowEnabled { get; set; } = Defaults.GlowEnabled;
    public bool TextureEnabled { get; set; } = Defaults.TextureEnabled;

    public static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpectrumViewerRT", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
            return settings?.Sanitized() ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Sanitized(), new JsonSerializerOptions { WriteIndented = true }));
    }

    public AppSettings Sanitized()
    {
        Gain = Math.Clamp(Gain, Defaults.MinGain, Defaults.MaxGain);
        RecordGainDb = Math.Clamp(RecordGainDb, Defaults.MinRecordGainDb, Defaults.MaxRecordGainDb);
        RangeDb = Math.Clamp(RangeDb, Defaults.MinRangeDb, Defaults.MaxRangeDb);
        Fps = Math.Clamp(Fps, Defaults.MinFps, Defaults.MaxFps);
        TimeDivisionSeconds = Math.Clamp(TimeDivisionSeconds, Defaults.MinTimeDivisionSeconds, Defaults.MaxTimeDivisionSeconds);
        SourceIndex = Math.Clamp(SourceIndex, 0, 1);
        ScaleIndex = Math.Clamp(ScaleIndex, 0, 1);
        MaxFrequencyIndex = Math.Clamp(MaxFrequencyIndex, 0, 4);
        MeterColorIndex = Math.Clamp(MeterColorIndex, 0, 3);
        MeterStyleIndex = Math.Clamp(MeterStyleIndex, 0, 1);
        DisplayModeIndex = Math.Clamp(DisplayModeIndex, 0, 1);
        return this;
    }
}

public static class Defaults
{
    public const double Gain = 2.4;
    public const double MinGain = 0.2;
    public const double MaxGain = 8.0;
    public const double RecordGainDb = 0.0;
    public const double MinRecordGainDb = -24.0;
    public const double MaxRecordGainDb = 12.0;
    public const double RangeDb = 85.0;
    public const double MinRangeDb = 45.0;
    public const double MaxRangeDb = 120.0;
    public const double Fps = 60.0;
    public const double MinFps = 12.0;
    public const double MaxFps = 60.0;
    public const double TimeDivisionSeconds = 1.0;
    public const double MinTimeDivisionSeconds = 0.25;
    public const double MaxTimeDivisionSeconds = 5.0;
    public const int ScaleIndex = 0;
    public const int SourceIndex = 0;
    public const int MaxFrequencyIndex = 4;
    public const int MeterColorIndex = 0;
    public const int MeterStyleIndex = 0;
    public const int DisplayModeIndex = 0;
    public const bool GridEnabled = true;
    public const bool AlwaysOnTop = false;
    public const bool MeterOnly = false;
    public const bool ShowUnlitSegments = true;
    public const bool GlowEnabled = true;
    public const bool TextureEnabled = true;
}
