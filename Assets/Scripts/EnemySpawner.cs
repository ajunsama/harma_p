using System.Collections;
using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [Header("配置")]
    public GameObject enemyPrefab;      // 敌人预制体
    public int maxEnemies = 2;          // 最大同时存在数量
    public float respawnDelay = 2.0f;   // 重生延迟
    
    [Header("生成范围")]
    public float leftSpawnX = -10f;     // 左侧生成点X
    public float rightSpawnX = 11f;     // 右侧生成点X
    public float spawnY = -3.5f;        // 生成高度Y

    [Header("目标引用")]
    public Transform playerTransform;   // 玩家Transform，赋给新生成的敌人

    private int currentEnemyCount = 0;

    void Start()
    {
        // 统计场景中初始的敌人数量
        // 注意：FindObjectsOfType开销较大，只在Start用一次
        Enemy[] existingEnemies = FindObjectsOfType<Enemy>();
        currentEnemyCount = existingEnemies.Length;

        // 如果初始数量不足，补齐
        if (currentEnemyCount < maxEnemies)
        {
            int needToSpawn = maxEnemies - currentEnemyCount;
            for (int i = 0; i < needToSpawn; i++)
            {
                SpawnEnemy();
            }
        }

        // 订阅死亡事件
        Enemy.OnEnemyDied += HandleEnemyDeath;
    }

    void OnDestroy()
    {
        // 取消订阅，防止内存泄漏
        Enemy.OnEnemyDied -= HandleEnemyDeath;
    }

    void HandleEnemyDeath(Enemy _)
    {
        currentEnemyCount--;
        // 启动协程生成新敌人
        StartCoroutine(RespawnRoutine());
    }

    IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(respawnDelay);
        
        // 再次检查数量，确保不会生成过多
        if (currentEnemyCount < maxEnemies)
        {
            SpawnEnemy();
        }
    }

    void SpawnEnemy()
    {
        if (enemyPrefab == null)
        {
            Debug.LogWarning("EnemySpawner: 未设置 Enemy Prefab！");
            return;
        }

        // 随机选择左边或右边
        float spawnX = (Random.value > 0.5f) ? leftSpawnX : rightSpawnX;
        Vector3 spawnPos = new Vector3(spawnX, spawnY, 0);

        GameObject newEnemy = Instantiate(enemyPrefab, spawnPos, Quaternion.identity);
        
        // 设置新敌人的目标为玩家
        // 尝试获取 MuscleP_AI_Movement
        MuscleP_AI_Movement muscleAI = newEnemy.GetComponent<MuscleP_AI_Movement>();
        if (muscleAI != null && playerTransform != null)
        {
            muscleAI.player = playerTransform;
        }
        
        // 如果有其他类型的AI脚本，也可以在这里赋值
        EnemySimpleAI2D simpleAI = newEnemy.GetComponent<EnemySimpleAI2D>();
        if (simpleAI != null && playerTransform != null)
        {
            simpleAI.player = playerTransform;
        }

        currentEnemyCount++;
        Debug.Log($"生成了新敌人，当前数量: {currentEnemyCount}");
    }
}
