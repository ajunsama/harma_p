using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 关卡UI管理器 - 显示波次信息、进度等
/// </summary>
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
    
    private BattleWaveManager battleWaveManager;
    private LevelProgressManager progressManager;
    private float messageTimer = 0f;
    
    void Start()
    {
        battleWaveManager = BattleWaveManager.Instance;
        progressManager = LevelProgressManager.Instance;
        
        // 订阅事件
        if (battleWaveManager != null)
        {
            battleWaveManager.OnWaveStart.AddListener(OnWaveStart);
            battleWaveManager.OnWaveComplete.AddListener(OnWaveComplete);
            battleWaveManager.OnShowMessage.AddListener(ShowMessage);
        }
        
        if (progressManager != null)
        {
            progressManager.OnLevelComplete.AddListener(OnLevelComplete);
            progressManager.OnLevelFailed.AddListener(OnLevelFailed);
        }
        
        // 初始隐藏面板
        HideAllPanels();
    }
    
    void Update()
    {
        // 更新敌人数量
        if (battleWaveManager != null && battleWaveManager.IsInBattle)
        {
            if (enemyCountText != null)
            {
                enemyCountText.text = $"剩余敌人: {battleWaveManager.RemainingEnemies}";
            }
        }
        
        // 更新进度
        if (progressManager != null && progressSlider != null)
        {
            progressSlider.value = progressManager.GetProgress();
            
            if (progressText != null)
            {
                int current = battleWaveManager != null ? battleWaveManager.CurrentWaveIndex + 1 : 0;
                int total = progressManager.currentLevelData != null ? 
                    progressManager.currentLevelData.TotalWaveCount : 0;
                progressText.text = $"{current}/{total}";
            }
        }
        
        // 消息计时
        if (messageTimer > 0)
        {
            messageTimer -= Time.deltaTime;
            if (messageTimer <= 0 && messagePanel != null)
            {
                messagePanel.SetActive(false);
            }
        }
    }
    
    void HideAllPanels()
    {
        if (waveInfoPanel != null) waveInfoPanel.SetActive(false);
        if (messagePanel != null) messagePanel.SetActive(false);
        if (levelCompletePanel != null) levelCompletePanel.SetActive(false);
        if (levelFailedPanel != null) levelFailedPanel.SetActive(false);
    }
    
    void OnWaveStart(int waveIndex)
    {
        if (waveInfoPanel != null)
        {
            waveInfoPanel.SetActive(true);
        }
        
        if (waveText != null)
        {
            waveText.text = $"波次 {waveIndex + 1}";
        }
    }
    
    void OnWaveComplete(int waveIndex)
    {
        // 波次完成后短暂隐藏波次信息
        // 可以添加动画效果
    }
    
    public void ShowMessage(string message)
    {
        if (messagePanel == null || messageText == null) return;
        
        messagePanel.SetActive(true);
        messageText.text = message;
        messageTimer = messageDisplayTime;
    }
    
    void OnLevelComplete()
    {
        if (waveInfoPanel != null) waveInfoPanel.SetActive(false);
        
        if (levelCompletePanel != null)
        {
            levelCompletePanel.SetActive(true);
            
            if (levelCompleteTimeText != null && progressManager != null)
            {
                float time = progressManager.LevelTime;
                int minutes = (int)(time / 60);
                int seconds = (int)(time % 60);
                levelCompleteTimeText.text = $"用时: {minutes:00}:{seconds:00}";
            }
            
            if (levelCompleteEnemiesText != null && progressManager != null)
            {
                levelCompleteEnemiesText.text = $"击败敌人: {progressManager.DefeatedEnemies}";
            }
        }
    }
    
    void OnLevelFailed()
    {
        if (waveInfoPanel != null) waveInfoPanel.SetActive(false);
        
        if (levelFailedPanel != null)
        {
            levelFailedPanel.SetActive(true);
        }
    }
    
    // 按钮回调
    public void OnRestartButtonClicked()
    {
        if (progressManager != null)
        {
            progressManager.RestartLevel();
        }
    }
    
    public void OnMainMenuButtonClicked()
    {
        Time.timeScale = 1f;
        UnityEngine.SceneManagement.SceneManager.LoadScene(0);
    }
}
