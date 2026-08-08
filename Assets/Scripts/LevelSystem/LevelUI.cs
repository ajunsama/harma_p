using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LevelUI : MonoBehaviour
{
    [Header("波次信息")]
    public GameObject waveInfoPanel;
    public TextMeshProUGUI waveText;
    public TextMeshProUGUI enemyCountText;

    [Header("消息显示")]
    public GameObject messagePanel;
    public TextMeshProUGUI messageText;
    public float messageDisplayTime = 2f;

    [Header("进度条")]
    public Slider progressSlider;
    public TextMeshProUGUI progressText;

    [Header("关卡完成面板")]
    public GameObject levelCompletePanel;
    public TextMeshProUGUI levelCompleteTimeText;
    public TextMeshProUGUI levelCompleteEnemiesText;

    [Header("关卡失败面板")]
    public GameObject levelFailedPanel;

    private float messageTimer;

    void Start()
    {
        HideAllPanels();
    }

    void Update()
    {
        if (messageTimer > 0)
        {
            messageTimer -= Time.deltaTime;
            if (messageTimer <= 0 && messagePanel != null)
                messagePanel.SetActive(false);
        }
    }

    void HideAllPanels()
    {
        if (waveInfoPanel != null) waveInfoPanel.SetActive(false);
        if (messagePanel != null) messagePanel.SetActive(false);
        if (levelCompletePanel != null) levelCompletePanel.SetActive(false);
        if (levelFailedPanel != null) levelFailedPanel.SetActive(false);
    }

    public void ShowMessage(string message)
    {
        if (messagePanel == null || messageText == null) return;
        messagePanel.SetActive(true);
        messageText.text = message;
        messageTimer = messageDisplayTime;
    }

    public void OnRestartButtonClicked()
    {
        GameFlowService.RestartLastGameplayScene();
    }

    public void OnMainMenuButtonClicked()
    {
        GameFlowService.ReturnToMainMenu();
    }
}
