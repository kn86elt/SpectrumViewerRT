using System.Text.Json;
using System.IO;

namespace SpectrumViewerRT;

public sealed class AppSettings
{
    public int SettingsVersion { get; set; } = Defaults.SettingsVersion;
    public double Gain { get; set; } = Defaults.Gain;
    public double RangeDb { get; set; } = Defaults.RangeDb;
    public double Fps { get; set; } = Defaults.Fps;
    public double TimeDivisionSeconds { get; set; } = Defaults.TimeDivisionSeconds;
    public int SourceIndex { get; set; } = Defaults.SourceIndex;
    public int ScaleIndex { get; set; } = Defaults.ScaleIndex;
    public int MaxFrequencyIndex { get; set; } = Defaults.MaxFrequencyIndex;
    public int MeterColorIndex { get; set; } = Defaults.MeterColorIndex;
    public int MeterStyleIndex { get; set; } = Defaults.MeterStyleIndex;
    public int StatusDisplayStyleIndex { get; set; } = Defaults.StatusDisplayStyleIndex;
    public int DisplayModeIndex { get; set; } = Defaults.DisplayModeIndex;
    public int AnalyzerModeIndex { get; set; } = Defaults.AnalyzerModeIndex;
    public int MonoAnalyzerBandCount { get; set; } = Defaults.AnalyzerBandCount;
    public int StereoAnalyzerBandCount { get; set; } = Defaults.AnalyzerBandCount;
    public int MonoCustomAnalyzerBandCount { get; set; } = Defaults.AnalyzerBandCount;
    public int StereoCustomAnalyzerBandCount { get; set; } = Defaults.AnalyzerBandCount;
    public double MonoAnalyzerMaxBandWidth { get; set; } = Defaults.MonoAnalyzerMaxBandWidth;
    public double StereoAnalyzerMaxBandWidth { get; set; } = Defaults.StereoAnalyzerMaxBandWidth;
    public double MonoAnalyzerMaxBandGap { get; set; } = Defaults.MonoAnalyzerMaxBandGap;
    public double StereoAnalyzerMaxBandGap { get; set; } = Defaults.StereoAnalyzerMaxBandGap;
    public bool AlwaysOnTop { get; set; } = Defaults.AlwaysOnTop;
    public bool ShowTransportPanel { get; set; } = Defaults.ShowTransportPanel;
    public bool ShowSettingsPanel { get; set; } = Defaults.ShowSettingsPanel;
    public bool ShowMainDisplay { get; set; } = Defaults.ShowMainDisplay;
    public bool ShowWaveform { get; set; } = Defaults.ShowWaveform;
    public bool ShowLevelMeter { get; set; } = Defaults.ShowLevelMeter;
    public bool CompactShowTransportPanel { get; set; } = Defaults.CompactShowTransportPanel;
    public bool CompactShowSettingsPanel { get; set; } = Defaults.CompactShowSettingsPanel;
    public bool CompactShowMainDisplay { get; set; } = Defaults.CompactShowMainDisplay;
    public bool CompactShowWaveform { get; set; } = Defaults.CompactShowWaveform;
    public bool CompactShowLevelMeter { get; set; } = Defaults.CompactShowLevelMeter;
    public bool CompactMode { get; set; } = Defaults.CompactMode;
    public bool GridEnabled { get; set; } = Defaults.GridEnabled;
    public bool ShowUnlitSegments { get; set; } = Defaults.ShowUnlitSegments;
    public bool GlowEnabled { get; set; } = Defaults.GlowEnabled;
    public bool TextureEnabled { get; set; } = Defaults.TextureEnabled;
    public bool VuNormalizeEnabled { get; set; } = Defaults.VuNormalizeEnabled;
    public bool CompensateSystemOutputVolume { get; set; } = Defaults.CompensateSystemOutputVolume;
    public int StereoSplitModeIndex { get; set; } = Defaults.StereoSplitModeIndex;
    public Dictionary<int, WindowSizeSettings> CompactWindowSizes { get; set; } = new();
    public List<CustomLayoutSettings> CustomLayouts { get; set; } = new();
    public CustomLayoutSettings? CustomLayout { get; set; }

    public static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpectrumViewerRT", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
            if (settings != null && settings.SettingsVersion < 2)
            {
                settings.DisplayModeIndex = settings.DisplayModeIndex switch
                {
                    1 => 2,
                    2 => 3,
                    _ => 0
                };
                settings.SettingsVersion = 2;
            }
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
        RangeDb = Math.Clamp(RangeDb, Defaults.MinRangeDb, Defaults.MaxRangeDb);
        Fps = Math.Clamp(Fps, Defaults.MinFps, Defaults.MaxFps);
        TimeDivisionSeconds = Math.Clamp(TimeDivisionSeconds, Defaults.MinTimeDivisionSeconds, Defaults.MaxTimeDivisionSeconds);
        SourceIndex = Math.Clamp(SourceIndex, 0, 1);
        ScaleIndex = Math.Clamp(ScaleIndex, 0, 1);
        MaxFrequencyIndex = Math.Clamp(MaxFrequencyIndex, 0, 4);
        MeterColorIndex = Math.Clamp(MeterColorIndex, 0, 3);
        MeterStyleIndex = Math.Clamp(MeterStyleIndex, 0, 3);
        StatusDisplayStyleIndex = Math.Clamp(StatusDisplayStyleIndex, 0, 1);
        SettingsVersion = Defaults.SettingsVersion;
        DisplayModeIndex = Math.Clamp(DisplayModeIndex, 0, 3);
        AnalyzerModeIndex = Math.Clamp(AnalyzerModeIndex, 0, 1);
        MonoAnalyzerBandCount = Math.Clamp(MonoAnalyzerBandCount, Defaults.MinAnalyzerBandCount, Defaults.MaxAnalyzerBandCount);
        StereoAnalyzerBandCount = Math.Clamp(StereoAnalyzerBandCount, Defaults.MinAnalyzerBandCount, Defaults.MaxAnalyzerBandCount);
        MonoCustomAnalyzerBandCount = Math.Clamp(MonoCustomAnalyzerBandCount, Defaults.MinAnalyzerBandCount, Defaults.MaxAnalyzerBandCount);
        StereoCustomAnalyzerBandCount = Math.Clamp(StereoCustomAnalyzerBandCount, Defaults.MinAnalyzerBandCount, Defaults.MaxAnalyzerBandCount);
        MonoAnalyzerMaxBandWidth = Math.Clamp(MonoAnalyzerMaxBandWidth, 4, 320);
        StereoAnalyzerMaxBandWidth = Math.Clamp(StereoAnalyzerMaxBandWidth, 4, 160);
        MonoAnalyzerMaxBandGap = Math.Clamp(MonoAnalyzerMaxBandGap, 1, 48);
        StereoAnalyzerMaxBandGap = Math.Clamp(StereoAnalyzerMaxBandGap, 1, 32);
        StereoSplitModeIndex = Math.Clamp(StereoSplitModeIndex, 0, 1);
        CompactWindowSizes = (CompactWindowSizes ?? new Dictionary<int, WindowSizeSettings>())
            .Where(pair => pair.Key is >= 0 and <= 15 && pair.Value != null)
            .ToDictionary(
                pair => pair.Key,
                pair => new WindowSizeSettings
                {
                    Width = Math.Clamp(pair.Value.Width, 480, 10000),
                    Height = Math.Clamp(pair.Value.Height, 120, 10000)
                });
        CustomLayouts ??= new List<CustomLayoutSettings>();
        if (CustomLayouts.Count == 0 && CustomLayout != null)
        {
            CustomLayout.Name = "Custom 1";
            CustomLayouts.Add(CustomLayout);
        }
        CustomLayouts = CustomLayouts
            .Where(layout => layout != null && !string.IsNullOrWhiteSpace(layout.Name))
            .Select(SanitizeCustomLayout)
            .GroupBy(layout => layout.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
        CustomLayout = null;
        return this;
    }

    private static CustomLayoutSettings SanitizeCustomLayout(CustomLayoutSettings layout)
    {
        layout.Name = layout.Name.Trim();
        if (layout.Name.Length > 40)
            layout.Name = layout.Name[..40];
        layout.DisplayModeIndex = Math.Clamp(layout.DisplayModeIndex, 0, 3);
        layout.StereoSplitModeIndex = Math.Clamp(layout.StereoSplitModeIndex, 0, 1);
        layout.Width = Math.Clamp(layout.Width, 480, 10000);
        layout.Height = Math.Clamp(layout.Height, 120, 10000);
        return layout;
    }
}

public sealed class WindowSizeSettings
{
    public double Width { get; set; }
    public double Height { get; set; }
}

public sealed class CustomLayoutSettings
{
    public string Name { get; set; } = "";
    public bool ShowTransportPanel { get; set; }
    public bool ShowSettingsPanel { get; set; }
    public bool ShowMainDisplay { get; set; }
    public bool ShowWaveform { get; set; }
    public bool ShowLevelMeter { get; set; }
    public bool CompactMode { get; set; }
    public int DisplayModeIndex { get; set; }
    public int StereoSplitModeIndex { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public static class Defaults
{
    public const int SettingsVersion = 4;
    public const double Gain = 2.4;
    public const double MinGain = 0.2;
    public const double MaxGain = 8.0;
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
    public const int StatusDisplayStyleIndex = 0;
    public const int DisplayModeIndex = 0;
    public const int AnalyzerModeIndex = 0;
    public const int StereoSplitModeIndex = 0;
    public const int AnalyzerBandCount = 48;
    public const int MinAnalyzerBandCount = 3;
    public const int MaxAnalyzerBandCount = 96;
    public const double MonoAnalyzerMaxBandWidth = 240;
    public const double StereoAnalyzerMaxBandWidth = 120;
    public const double MonoAnalyzerMaxBandGap = 24;
    public const double StereoAnalyzerMaxBandGap = 16;
    public const bool GridEnabled = true;
    public const bool AlwaysOnTop = false;
    public const bool ShowTransportPanel = true;
    public const bool ShowSettingsPanel = true;
    public const bool ShowMainDisplay = true;
    public const bool ShowWaveform = true;
    public const bool ShowLevelMeter = true;
    public const bool CompactShowTransportPanel = true;
    public const bool CompactShowSettingsPanel = false;
    public const bool CompactShowMainDisplay = true;
    public const bool CompactShowWaveform = true;
    public const bool CompactShowLevelMeter = true;
    public const bool CompactMode = false;
    public const bool ShowUnlitSegments = true;
    public const bool GlowEnabled = true;
    public const bool TextureEnabled = true;
    public const bool VuNormalizeEnabled = false;
    public const bool CompensateSystemOutputVolume = false;
}
