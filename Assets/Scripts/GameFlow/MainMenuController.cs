using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class MainMenuController : MonoBehaviour
{
    [Header("Main Menu")]
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private CanvasGroup mainMenuCanvasGroup;
    [SerializeField] private RectTransform titleGroup;

    [Header("Settings Panel")]
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullScreenToggle;
    [SerializeField] private TMP_Dropdown qualityDropdown;
    [SerializeField] private Button applyButton;
    [SerializeField] private Button backButton;

    private readonly List<Vector2Int> _resolutions = new List<Vector2Int>();
    private Sequence _introSequence;

    private void Awake()
    {
        newGameButton.onClick.AddListener(GameFlowService.StartNewGame);
        settingsButton.onClick.AddListener(OpenSettings);
        quitButton.onClick.AddListener(GameFlowService.QuitGame);
        applyButton.onClick.AddListener(ApplySettings);
        backButton.onClick.AddListener(CloseSettings);

        settingsPanel.SetActive(false);
        BuildResolutionOptions();
        BuildQualityOptions();
    }

    private void Start()
    {
        PlayIntro();
        Select(newGameButton);
    }

    private void OnDestroy()
    {
        newGameButton.onClick.RemoveListener(GameFlowService.StartNewGame);
        settingsButton.onClick.RemoveListener(OpenSettings);
        quitButton.onClick.RemoveListener(GameFlowService.QuitGame);
        applyButton.onClick.RemoveListener(ApplySettings);
        backButton.onClick.RemoveListener(CloseSettings);
        _introSequence?.Kill();
    }

    public void OpenSettings()
    {
        PopulateSettingsControls(GameSettingsService.Load());
        settingsPanel.SetActive(true);
        Select(volumeSlider);
    }

    public void CloseSettings()
    {
        settingsPanel.SetActive(false);
        Select(settingsButton);
    }

    public void ApplySettings()
    {
        Vector2Int resolution = _resolutions.Count > 0
            ? _resolutions[Mathf.Clamp(resolutionDropdown.value, 0, _resolutions.Count - 1)]
            : new Vector2Int(Screen.width, Screen.height);

        GameSettingsService.ApplyAndSave(
            new GameSettingsService.SettingsSnapshot(
                volumeSlider.value,
                resolution.x,
                resolution.y,
                fullScreenToggle.isOn,
                qualityDropdown.value));

        CloseSettings();
    }

    private void BuildResolutionOptions()
    {
        _resolutions.Clear();
        var labels = new List<string>();
        var seen = new HashSet<string>();
        Resolution[] available = Screen.resolutions;

        if (available != null)
        {
            for (int i = 0; i < available.Length; i++)
            {
                string key = $"{available[i].width}x{available[i].height}";
                if (!seen.Add(key))
                    continue;

                _resolutions.Add(new Vector2Int(available[i].width, available[i].height));
                labels.Add($"{available[i].width} × {available[i].height}");
            }
        }

        if (_resolutions.Count == 0)
        {
            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            _resolutions.Add(new Vector2Int(width, height));
            labels.Add($"{width} × {height}");
        }

        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(labels);
    }

    private void BuildQualityOptions()
    {
        qualityDropdown.ClearOptions();
        qualityDropdown.AddOptions(new List<string>(QualitySettings.names));
    }

    private void PopulateSettingsControls(GameSettingsService.SettingsSnapshot settings)
    {
        volumeSlider.SetValueWithoutNotify(settings.MasterVolume);
        fullScreenToggle.SetIsOnWithoutNotify(settings.FullScreen);

        int resolutionIndex = _resolutions.FindIndex(
            resolution => resolution.x == settings.ResolutionWidth &&
                          resolution.y == settings.ResolutionHeight);
        resolutionDropdown.SetValueWithoutNotify(Mathf.Max(0, resolutionIndex));
        resolutionDropdown.RefreshShownValue();

        int qualityIndex = Mathf.Clamp(
            settings.QualityLevel,
            0,
            Mathf.Max(0, qualityDropdown.options.Count - 1));
        qualityDropdown.SetValueWithoutNotify(qualityIndex);
        qualityDropdown.RefreshShownValue();
    }

    private void PlayIntro()
    {
        _introSequence?.Kill();
        mainMenuCanvasGroup.alpha = 0f;
        titleGroup.localScale = new Vector3(1.18f, 0.72f, 1f);

        _introSequence = DOTween.Sequence()
            .SetUpdate(true)
            .Append(mainMenuCanvasGroup.DOFade(1f, 0.18f))
            .Join(titleGroup.DOScale(Vector3.one, 0.35f).SetEase(Ease.OutBack))
            .Append(titleGroup.DOShakeAnchorPos(0.18f, 18f, 18, 90f, false, true));
    }

    private static void Select(Selectable selectable)
    {
        if (EventSystem.current == null || selectable == null)
            return;

        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(selectable.gameObject);
    }
}
