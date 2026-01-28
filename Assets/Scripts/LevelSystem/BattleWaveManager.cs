using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 战斗波次管理器 - 控制每个波次的敌人生成和战斗流程
/// </summary>
public class BattleWaveManager : MonoBehaviour
{
    public static BattleWaveManager Instance { get; private set; }
    
    [Header("引用")]
    [Tooltip("玩家Transform")]
    public Transform playerTransform;
    
    [Tooltip("当前关卡数据")]
    public LevelData currentLevelData;
    
    [Header("状态")]
    [SerializeField] private int currentWaveIndex = -1;
    [SerializeField] private bool isInBattle = false;
    [SerializeField] private int remainingEnemies = 0;
    
    [Header("战斗边界")]
    [SerializeField] private float currentBattleLeftBound;
    [SerializeField] private float currentBattleRightBound;
    
    [Header("事件")]
    public UnityEvent<int> OnWaveStart;           // 波次开始，参数为波次索引
    public UnityEvent<int> OnWaveComplete;        // 波次完成
    public UnityEvent OnAllWavesComplete;          // 所有波次完成
    public UnityEvent<string> OnShowMessage;       // 显示消息
    
    // 当前波次生成的敌人列表
    private List<GameObject> currentWaveEnemies = new List<GameObject>();
    
    // 原始的关卡边界
    private float originalLeftBound;
    private float originalRightBound;
    
    // 属性
    public bool IsInBattle => isInBattle;
    public int CurrentWaveIndex => currentWaveIndex;
    public int RemainingEnemies => remainingEnemies;
    public float BattleLeftBound => currentBattleLeftBound;
    public float BattleRightBound => currentBattleRightBound;
    
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
        // 保存原始边界
        if (LevelManager.Instance != null)
        {
            originalLeftBound = LevelManager.Instance.LeftBound;
            originalRightBound = LevelManager.Instance.RightBound;
        }
        
        // 初始化战斗边界为原始边界
        currentBattleLeftBound = originalLeftBound;
        currentBattleRightBound = originalRightBound;
        
        // 订阅敌人死亡事件
        Enemy.OnEnemyDied += HandleEnemyDeath;
        
        // 如果有关卡数据，初始化玩家位置
        if (currentLevelData != null && playerTransform != null)
        {
            playerTransform.position = new Vector3(
                currentLevelData.playerStartPosition.x,
                currentLevelData.playerStartPosition.y,
                playerTransform.position.z
            );
        }
    }
    
    void OnDestroy()
    {
        Enemy.OnEnemyDied -= HandleEnemyDeath;
    }
    
    void Update()
    {
        if (currentLevelData == null || playerTransform == null) return;
        if (isInBattle) return; // 战斗中不检测新波次
        
        // 检测是否触发新波次
        CheckWaveTrigger();
    }
    
    /// <summary>
    /// 加载关卡数据
    /// </summary>
    public void LoadLevel(LevelData levelData)
    {
        currentLevelData = levelData;
        currentWaveIndex = -1;
        isInBattle = false;
        
        // 清理现有敌人
        ClearCurrentWaveEnemies();
        
        // 初始化波次的敌人列表
        levelData.InitializeWaves();
        
        // 初始化玩家位置
        if (playerTransform != null)
        {
            playerTransform.position = new Vector3(
                levelData.playerStartPosition.x,
                levelData.playerStartPosition.y,
                playerTransform.position.z
            );
        }
        
        Debug.Log($"[BattleWaveManager] 加载关卡: {levelData.levelName}，共 {levelData.TotalWaveCount} 波战斗");
    }
    
    /// <summary>
    /// 检测是否应该触发波次
    /// </summary>
    void CheckWaveTrigger()
    {
        if (currentLevelData.battleWaves.Count == 0) return;
        
        int nextWaveIndex = currentWaveIndex + 1;
        if (nextWaveIndex >= currentLevelData.battleWaves.Count) return;
        
        BattleWave nextWave = currentLevelData.battleWaves[nextWaveIndex];
        
        // 玩家到达触发位置
        if (playerTransform.position.x >= nextWave.triggerPositionX)
        {
            StartWave(nextWaveIndex);
        }
    }
    
    /// <summary>
    /// 开始指定波次
    /// </summary>
    public void StartWave(int waveIndex)
    {
        if (waveIndex < 0 || waveIndex >= currentLevelData.battleWaves.Count)
        {
            Debug.LogError($"[BattleWaveManager] 无效的波次索引: {waveIndex}");
            return;
        }
        
        currentWaveIndex = waveIndex;
        BattleWave wave = currentLevelData.battleWaves[waveIndex];
        
        isInBattle = true;
        
        // 设置战斗边界：使用当前摄像机范围
        if (wave.mustClearToProceed)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                float cameraHalfWidth = cam.orthographicSize * cam.aspect;
                float cameraX = cam.transform.position.x;
                currentBattleLeftBound = cameraX - cameraHalfWidth;
                currentBattleRightBound = cameraX + cameraHalfWidth;
            }
        }
        else
        {
            currentBattleLeftBound = originalLeftBound;
            currentBattleRightBound = originalRightBound;
        }
        
        Debug.Log($"[BattleWaveManager] 开始波次 {waveIndex + 1}: {wave.waveName}，敌人数量: {wave.TotalEnemyCount}");
        
        // 触发事件
        OnWaveStart?.Invoke(waveIndex);
        
        // 开始生成敌人
        StartCoroutine(SpawnWaveEnemies(wave));
    }
    
    /// <summary>
    /// 生成波次敌人
    /// </summary>
    IEnumerator SpawnWaveEnemies(BattleWave wave)
    {
        remainingEnemies = wave.enemies.Count;
        
        // 按延迟时间排序敌人
        List<EnemySpawnData> sortedEnemies = new List<EnemySpawnData>(wave.enemies);
        sortedEnemies.Sort((a, b) => a.spawnDelay.CompareTo(b.spawnDelay));
        
        float lastDelay = 0f;
        
        foreach (var enemyData in sortedEnemies)
        {
            // 等待延迟
            float waitTime = enemyData.spawnDelay - lastDelay;
            if (waitTime > 0)
            {
                yield return new WaitForSeconds(waitTime);
            }
            lastDelay = enemyData.spawnDelay;
            
            // 生成敌人
            SpawnEnemy(enemyData, wave.triggerPositionX);
        }
    }
    
    /// <summary>
    /// 生成单个敌人
    /// </summary>
    void SpawnEnemy(EnemySpawnData enemyData, float baseX)
    {
        if (enemyData.enemyPrefab == null)
        {
            Debug.LogWarning("[BattleWaveManager] 敌人预制体为空！");
            return;
        }
        
        // 计算敌人的目标位置（原本设定的位置）
        Vector3 targetPos = new Vector3(
            baseX + enemyData.spawnOffset.x,
            enemyData.spawnOffset.y,
            0
        );
        
        // 计算实际生成位置（屏幕外），使用fromLeft决定方向
        Vector3 spawnPos = CalculateOffscreenSpawnPosition(targetPos, enemyData.fromLeft);
        
        GameObject enemy = Instantiate(enemyData.enemyPrefab, spawnPos, Quaternion.identity);
        
        // 设置玩家目标和入场目标
        MuscleP_AI_Movement muscleAI = enemy.GetComponent<MuscleP_AI_Movement>();
        if (muscleAI != null)
        {
            if (playerTransform != null) muscleAI.player = playerTransform;
            muscleAI.SetEntranceTarget(targetPos);
        }
        
        EnemySimpleAI2D simpleAI = enemy.GetComponent<EnemySimpleAI2D>();
        if (simpleAI != null)
        {
            if (playerTransform != null) simpleAI.player = playerTransform;
            simpleAI.SetEntranceTarget(targetPos);
        }
        
        currentWaveEnemies.Add(enemy);
        
        Debug.Log($"[BattleWaveManager] 生成敌人: {enemyData.enemyPrefab.name} 在位置 {spawnPos}, 从{(enemyData.fromLeft ? "左" : "右")}侧进入");
    }
    
    /// <summary>
    /// 计算屏幕外的生成位置
    /// </summary>
    Vector3 CalculateOffscreenSpawnPosition(Vector3 targetPos, bool fromLeft)
    {
        Camera cam = Camera.main;
        if (cam == null) return targetPos;
        
        float cameraHalfWidth = cam.orthographicSize * cam.aspect;
        float cameraX = cam.transform.position.x;
        float screenLeft = cameraX - cameraHalfWidth;
        float screenRight = cameraX + cameraHalfWidth;
        
        float spawnX;
        float offscreenOffset = 2f; // 屏幕外额外偏移量
        
        if (fromLeft)
        {
            // 从屏幕左边进入
            spawnX = screenLeft - offscreenOffset;
        }
        else
        {
            // 从屏幕右边进入
            spawnX = screenRight + offscreenOffset;
        }
        
        return new Vector3(spawnX, targetPos.y, targetPos.z);
    }
    
    /// <summary>
    /// 处理敌人死亡
    /// </summary>
    void HandleEnemyDeath()
    {
        if (!isInBattle) return;
        
        remainingEnemies--;
        
        // 清理已销毁的敌人引用
        currentWaveEnemies.RemoveAll(e => e == null);
        
        Debug.Log($"[BattleWaveManager] 敌人死亡，剩余: {remainingEnemies}");
        
        if (remainingEnemies <= 0)
        {
            CompleteCurrentWave();
        }
    }
    
    /// <summary>
    /// 完成当前波次
    /// </summary>
    void CompleteCurrentWave()
    {
        BattleWave wave = currentLevelData.battleWaves[currentWaveIndex];
        
        isInBattle = false;
        
        // 恢复原始边界
        currentBattleLeftBound = originalLeftBound;
        currentBattleRightBound = originalRightBound;
        
        Debug.Log($"[BattleWaveManager] 波次 {currentWaveIndex + 1} 完成!");
        
        // 触发事件
        OnWaveComplete?.Invoke(currentWaveIndex);
        
        // 检查是否所有波次都完成了
        if (currentWaveIndex >= currentLevelData.battleWaves.Count - 1)
        {
            Debug.Log("[BattleWaveManager] 所有波次完成！关卡胜利！");
            OnAllWavesComplete?.Invoke();
        }
    }
    
    /// <summary>
    /// 清理当前波次的所有敌人
    /// </summary>
    void ClearCurrentWaveEnemies()
    {
        foreach (var enemy in currentWaveEnemies)
        {
            if (enemy != null)
            {
                Destroy(enemy);
            }
        }
        currentWaveEnemies.Clear();
        remainingEnemies = 0;
    }
    
    /// <summary>
    /// 跳过当前波次（调试用）
    /// </summary>
    [ContextMenu("跳过当前波次")]
    public void SkipCurrentWave()
    {
        if (!isInBattle) return;
        
        ClearCurrentWaveEnemies();
        CompleteCurrentWave();
    }
    
    /// <summary>
    /// 重置关卡
    /// </summary>
    public void ResetLevel()
    {
        ClearCurrentWaveEnemies();
        currentWaveIndex = -1;
        isInBattle = false;
        
        if (currentLevelData != null && playerTransform != null)
        {
            playerTransform.position = new Vector3(
                currentLevelData.playerStartPosition.x,
                currentLevelData.playerStartPosition.y,
                playerTransform.position.z
            );
        }
    }
    
    /// <summary>
    /// 获取当前战斗边界（供其他系统使用）
    /// </summary>
    public void GetCurrentBattleBounds(out float left, out float right)
    {
        left = currentBattleLeftBound;
        right = currentBattleRightBound;
    }
}
