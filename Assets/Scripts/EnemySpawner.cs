using System.Collections;
using Harma.Combat;
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
        
        // 通过统一接口设置目标，不依赖具体敌人 AI 类型。
        if (playerTransform != null)
        {
            foreach (var behaviour in newEnemy.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is IPlayerTargetReceiver receiver)
                    receiver.SetPlayerTarget(playerTransform);
            }
        }

        currentEnemyCount++;
        GameLog.Verbose($"生成了新敌人，当前数量: {currentEnemyCount}");
    }
}
