using UnityEngine;

public static class GameSettingsService
{
    private const string VolumeKey = "Settings.MasterVolume";
    private const string ResolutionWidthKey = "Settings.ResolutionWidth";
    private const string ResolutionHeightKey = "Settings.ResolutionHeight";
    private const string FullScreenKey = "Settings.FullScreen";
    private const string QualityKey = "Settings.Quality";

    public readonly struct SettingsSnapshot
    {
        public readonly float MasterVolume;
        public readonly int ResolutionWidth;
        public readonly int ResolutionHeight;
        public readonly bool FullScreen;
        public readonly int QualityLevel;

        public SettingsSnapshot(
            float masterVolume,
            int resolutionWidth,
            int resolutionHeight,
            bool fullScreen,
            int qualityLevel)
        {
            MasterVolume = masterVolume;
            ResolutionWidth = resolutionWidth;
            ResolutionHeight = resolutionHeight;
            FullScreen = fullScreen;
            QualityLevel = qualityLevel;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplySavedSettingsOnStartup()
    {
        Apply(Load());
    }

    public static SettingsSnapshot Load()
    {
        Resolution current = Screen.currentResolution;
        int currentWidth = Screen.width > 0 ? Screen.width : current.width;
        int currentHeight = Screen.height > 0 ? Screen.height : current.height;

        return new SettingsSnapshot(
            PlayerPrefs.GetFloat(VolumeKey, AudioListener.volume),
            PlayerPrefs.GetInt(ResolutionWidthKey, currentWidth),
            PlayerPrefs.GetInt(ResolutionHeightKey, currentHeight),
            PlayerPrefs.GetInt(FullScreenKey, Screen.fullScreen ? 1 : 0) == 1,
            PlayerPrefs.GetInt(QualityKey, QualitySettings.GetQualityLevel()));
    }

    public static void ApplyAndSave(SettingsSnapshot settings)
    {
        SettingsSnapshot sanitized = Sanitize(settings);
        Apply(sanitized);

        PlayerPrefs.SetFloat(VolumeKey, sanitized.MasterVolume);
        PlayerPrefs.SetInt(ResolutionWidthKey, sanitized.ResolutionWidth);
        PlayerPrefs.SetInt(ResolutionHeightKey, sanitized.ResolutionHeight);
        PlayerPrefs.SetInt(FullScreenKey, sanitized.FullScreen ? 1 : 0);
        PlayerPrefs.SetInt(QualityKey, sanitized.QualityLevel);
        PlayerPrefs.Save();
    }

    private static void Apply(SettingsSnapshot settings)
    {
        SettingsSnapshot sanitized = Sanitize(settings);
        AudioListener.volume = sanitized.MasterVolume;

        if (QualitySettings.names.Length > 0 &&
            QualitySettings.GetQualityLevel() != sanitized.QualityLevel)
        {
            QualitySettings.SetQualityLevel(sanitized.QualityLevel, true);
        }

        if (Screen.width != sanitized.ResolutionWidth ||
            Screen.height != sanitized.ResolutionHeight ||
            Screen.fullScreen != sanitized.FullScreen)
        {
            Screen.SetResolution(
                sanitized.ResolutionWidth,
                sanitized.ResolutionHeight,
                sanitized.FullScreen);
        }
    }

    private static SettingsSnapshot Sanitize(SettingsSnapshot settings)
    {
        Vector2Int resolution = FindSupportedResolution(
            settings.ResolutionWidth,
            settings.ResolutionHeight);
        int maximumQuality = Mathf.Max(0, QualitySettings.names.Length - 1);

        return new SettingsSnapshot(
            Mathf.Clamp01(settings.MasterVolume),
            resolution.x,
            resolution.y,
            settings.FullScreen,
            Mathf.Clamp(settings.QualityLevel, 0, maximumQuality));
    }

    private static Vector2Int FindSupportedResolution(int requestedWidth, int requestedHeight)
    {
        Resolution[] resolutions = Screen.resolutions;
        if (resolutions != null && resolutions.Length > 0)
        {
            for (int i = 0; i < resolutions.Length; i++)
            {
                if (resolutions[i].width == requestedWidth &&
                    resolutions[i].height == requestedHeight)
                {
                    return new Vector2Int(requestedWidth, requestedHeight);
                }
            }
        }

        int fallbackWidth = Screen.width > 0 ? Screen.width : Screen.currentResolution.width;
        int fallbackHeight = Screen.height > 0 ? Screen.height : Screen.currentResolution.height;
        return new Vector2Int(Mathf.Max(1, fallbackWidth), Mathf.Max(1, fallbackHeight));
    }
}
