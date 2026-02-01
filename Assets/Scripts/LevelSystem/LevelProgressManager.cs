using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// 关卡进度管理器 - 管理整个关卡的流程和进度
/// </summary>
public class LevelProgressManager : MonoBehaviour
{
    public static LevelProgressManager Instance { get; private set; }
    
    [Header("引用")]
    public BattleWaveManager battleWaveManager;
    public ParallaxBackground parallaxBackground;
    public Transform playerTransform;
    
    [Header("当前关卡")]
    public LevelData currentLevelData;
    
    [Header("关卡状态")]
    [SerializeField] private LevelState currentState = LevelState.NotStarted;
    [SerializeField] private float levelStartTime;
    [SerializeField] private int defeatedEnemies = 0;
    
    [Header("相机设置")]
    public Camera mainCamera;
    public float cameraFixedY = 0f;          // 相机固定Y坐标
    public float cameraZ = -10f;             // 相机Z坐标
    
    [Header("滚动触发区域")]
    [Tooltip("屏幕边缘触发滚动的比例 (0.2 = 20%)")]
    [Range(0.1f, 0.4f)]
    public float scrollTriggerZone = 0.2f;   // 屏幕边缘20%触发滚动
    
    [Header("关卡边界")]
    // public float cameraLeftLimit = -10f; // 已禁用边界
    // public float cameraRightLimit = 100f; // 已禁用边界
    
    [Header("摄像机平滑移动")]
    [Tooltip("战斗结束后摄像机平滑移动的速度")]
    public float cameraSmoothSpeed = 5f;
    
    // 目标摄像机位置（用于平滑过渡）
    private float targetCameraX;
    private bool needSmoothTransition = false;
    
    // 摄像机绑定控制：玩家越过屏幕中心后才开始跟随
    private bool cameraBindingEnabled = false;
    
    [Header("事件")]
    public UnityEvent OnLevelStart;
    public UnityEvent OnLevelComplete;
    public UnityEvent OnLevelFailed;
    public UnityEvent<int, int> OnProgressUpdate;  // 当前波次, 总波次
    
    // 关卡状态枚举
    public enum LevelState
    {
        NotStarted,
        Playing,
        Paused,
        Completed,
        Failed
    }
    
    // 属性
    public LevelState CurrentState => currentState;
    public float LevelTime => Time.time - levelStartTime;
    public int DefeatedEnemies => defeatedEnemies;
    
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    
    void Start()
    {
        // 自动获取引用
        if (battleWaveManager == null)
            battleWaveManager = FindFirstObjectByType<BattleWaveManager>();
        
        if (parallaxBackground == null)
            parallaxBackground = FindFirstObjectByType<ParallaxBackground>();
        
        if (mainCamera == null)
            mainCamera = Camera.main;
        
        // 订阅事件
        if (battleWaveManager != null)
        {
            battleWaveManager.OnWaveStart.AddListener(OnWaveStarted);
            battleWaveManager.OnWaveComplete.AddListener(OnWaveCompleted);
            battleWaveManager.OnAllWavesComplete.AddListener(OnAllWavesCompleted);
        }
        
        // 订阅敌人死亡事件
        Enemy.OnEnemyDied += OnEnemyDefeated;
        
        // 如果有关卡数据，开始关卡
        if (currentLevelData != null)
        {
            StartLevel(currentLevelData);
        }
    }
    
    void OnDestroy()
    {
        Enemy.OnEnemyDied -= OnEnemyDefeated;
    }
    
    void LateUpdate()
    {
        // 相机跟随 - 始终运行，不依赖关卡状态
        UpdateCameraFollow();
        
        // 以下只在关卡进行中检查
        if (currentState != LevelState.Playing) return;
        
        // 检查玩家是否到达终点
        CheckLevelEnd();
    }
    
    /// <summary>
    /// 开始关卡
    /// </summary>
    public void StartLevel(LevelData levelData)
    {
        currentLevelData = levelData;
        currentState = LevelState.Playing;
        levelStartTime = Time.time;
        defeatedEnemies = 0;
        cameraBindingEnabled = false;  // 重置摄像机绑定状态
        
        // 设置相机边界
        // if (levelData != null)
        // {
        //     cameraRightLimit = levelData.levelLength;
        // }
        
        // 加载到战斗波次管理器
        if (battleWaveManager != null)
        {
            battleWaveManager.LoadLevel(levelData);
        }
        
        Debug.Log($"[LevelProgressManager] 开始关卡: {levelData.levelName}");
        
        OnLevelStart?.Invoke();
        
        // 摄像机保持原位置不动，直到玩家走过屏幕中心才开始跟随
    }
    
    /// <summary>
    /// 更新相机位置 - 固定视角，战斗中不动，非战斗时玩家接近边缘触发滚动
    /// 战斗结束时摄像机平滑过渡到玩家位置
    /// </summary>
    void UpdateCameraFollow()
    {
        if (mainCamera == null || playerTransform == null) 
        {
            // 尝试自动获取玩家
            if (playerTransform == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    playerTransform = player.transform;
                }
                else Debug.LogWarning("[LevelProgressManager] 找不到Player!");
            }
            if (mainCamera == null)
            {
                Debug.LogWarning("[LevelProgressManager] mainCamera 为空!");
            }
            return;
        }
        
        float playerX = playerTransform.position.x;
        float cameraHalfWidth = mainCamera.orthographicSize * mainCamera.aspect;
        float currentCameraX = mainCamera.transform.position.x;
        
        // 检查是否需要激活摄像机绑定：玩家从左边越过屏幕中心时激活
        if (!cameraBindingEnabled)
        {
            // 玩家位置超过摄像机中心位置时，激活绑定
            if (playerX >= currentCameraX)
            {
                cameraBindingEnabled = true;
                Debug.Log("[LevelProgressManager] 玩家越过屏幕中心，摄像机开始跟随");
            }
            else
            {
                // 尚未激活绑定，摄像机保持不动
                return;
            }
        }
        
        // 战斗中：记录需要平滑过渡（战斗结束后使用）
        if (battleWaveManager != null && battleWaveManager.IsInBattle)
        {
            needSmoothTransition = true;
            return;
        }
        
        // 计算玩家允许的最大偏移距离（相对于相机中心）
        // scrollTriggerZone = 0.2 表示玩家最多可以偏移到屏幕80%的位置（中心到边缘的80%）
        float maxPlayerOffset = cameraHalfWidth * (1f - scrollTriggerZone * 2f);
        
        // 计算玩家当前相对于相机中心的偏移
        float playerOffset = playerX - currentCameraX;
        
        float newCameraX = currentCameraX;
        
        // 如果玩家超出右侧允许范围，相机跟随移动
        if (playerOffset > maxPlayerOffset)
        {
            // 相机移动到让玩家刚好在右侧边界上
            newCameraX = playerX - maxPlayerOffset;
        }
        // 如果玩家超出左侧允许范围，相机跟随移动
        else if (playerOffset < -maxPlayerOffset)
        {
            // 相机移动到让玩家刚好在左侧边界上
            newCameraX = playerX + maxPlayerOffset;
        }
        
        // 不再限制相机在关卡边界内
        float clampedX = newCameraX;
        // float clampedX = Mathf.Clamp(newCameraX, 
        //     cameraLeftLimit + cameraHalfWidth, 
        //     cameraRightLimit - cameraHalfWidth);
        
        // 如果需要平滑过渡（战斗刚结束）
        if (needSmoothTransition)
        {
            // 使用Lerp平滑移动到目标位置
            float smoothedX = Mathf.Lerp(currentCameraX, clampedX, cameraSmoothSpeed * Time.deltaTime);
            
            // 当接近目标位置时，结束平滑过渡
            if (Mathf.Abs(smoothedX - clampedX) < 0.01f)
            {
                smoothedX = clampedX;
                needSmoothTransition = false;
            }
            
            mainCamera.transform.position = new Vector3(smoothedX, cameraFixedY, cameraZ);
        }
        else if (clampedX != currentCameraX)
        {
            // 正常跟随（玩家推动边缘）
            mainCamera.transform.position = new Vector3(clampedX, cameraFixedY, cameraZ);
        }
    }
    
    /// <summary>
    /// 检查是否到达终点
    /// </summary>
    void CheckLevelEnd()
    {
        if (currentLevelData == null || playerTransform == null) return;
        
        // 玩家到达终点且所有波次已完成
        if (playerTransform.position.x >= currentLevelData.levelEndPositionX)
        {
            if (battleWaveManager != null && 
                battleWaveManager.CurrentWaveIndex >= currentLevelData.battleWaves.Count - 1 &&
                !battleWaveManager.IsInBattle)
            {
                CompleteLevel();
            }
        }
    }
    
    /// <summary>
    /// 完成关卡
    /// </summary>
    void CompleteLevel()
    {
        if (currentState == LevelState.Completed) return;
        
        currentState = LevelState.Completed;
        
        Debug.Log($"[LevelProgressManager] 关卡完成! 用时: {LevelTime:F1}秒, 击败敌人: {defeatedEnemies}");
        
        OnLevelComplete?.Invoke();
    }
    
    /// <summary>
    /// 关卡失败
    /// </summary>
    public void FailLevel()
    {
        if (currentState == LevelState.Failed) return;
        
        currentState = LevelState.Failed;
        
        Debug.Log("[LevelProgressManager] 关卡失败!");
        
        OnLevelFailed?.Invoke();
    }
    
    /// <summary>
    /// 暂停关卡
    /// </summary>
    public void PauseLevel()
    {
        if (currentState != LevelState.Playing) return;
        
        currentState = LevelState.Paused;
        Time.timeScale = 0f;
    }
    
    /// <summary>
    /// 继续关卡
    /// </summary>
    public void ResumeLevel()
    {
        if (currentState != LevelState.Paused) return;
        
        currentState = LevelState.Playing;
        Time.timeScale = 1f;
    }
    
    /// <summary>
    /// 重新开始关卡
    /// </summary>
    public void RestartLevel()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
    
    /// <summary>
    /// 加载下一关
    /// </summary>
    public void LoadNextLevel(LevelData nextLevel)
    {
        if (nextLevel != null)
        {
            currentLevelData = nextLevel;
            RestartLevel();
        }
    }
    
    #region 事件处理
    
    void OnWaveStarted(int waveIndex)
    {
        OnProgressUpdate?.Invoke(waveIndex + 1, currentLevelData.battleWaves.Count);
    }
    
    void OnWaveCompleted(int waveIndex)
    {
        Debug.Log($"[LevelProgressManager] 波次 {waveIndex + 1} 完成");
    }
    
    void OnAllWavesCompleted()
    {
        Debug.Log("[LevelProgressManager] 所有波次完成，前往终点!");
    }
    
    void OnEnemyDefeated()
    {
        defeatedEnemies++;
    }
    
    #endregion
    
    /// <summary>
    /// 获取当前进度 (0-1)
    /// </summary>
    public float GetProgress()
    {
        if (currentLevelData == null || battleWaveManager == null) return 0f;
        
        int totalWaves = currentLevelData.battleWaves.Count;
        if (totalWaves == 0) return 1f;
        
        int completedWaves = battleWaveManager.CurrentWaveIndex;
        if (!battleWaveManager.IsInBattle && completedWaves >= 0)
        {
            completedWaves++;
        }
        
        return (float)completedWaves / totalWaves;
    }
}
