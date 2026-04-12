using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 击杀计数器 - 监听特定敌人的死亡事件，全部击杀后设置剧情标志位
/// 在Inspector中将场景里需要追踪的敌人拖进 targetEnemies 列表
/// 配合 StoryTrigger(Flag类型) 使用来触发剧情
/// </summary>
public class EnemyKillStoryFlag : MonoBehaviour
{
    [Header("配置")]
    [Tooltip("需要击杀的特定敌人列表（从场景中拖入）")]
    public List<Enemy> targetEnemies = new List<Enemy>();

    [Tooltip("全部击杀后设置的标志位名称")]
    public string flagName = "killed_3_enemies";

    [Tooltip("是否只触发一次")]
    public bool triggerOnce = true;

    [Header("状态（只读）")]
    [SerializeField] private int killedCount;
    [SerializeField] private int totalRequired;
    [SerializeField] private bool hasTriggered;

    // 运行时追踪哪些目标敌人还存活
    private HashSet<Enemy> _pendingEnemies = new HashSet<Enemy>();

    void OnEnable()
    {
        Enemy.OnEnemyDied += OnEnemyKilled;
    }

    void OnDisable()
    {
        Enemy.OnEnemyDied -= OnEnemyKilled;
    }

    void Start()
    {
        // 初始化待追踪集合（过滤掉空引用）
        _pendingEnemies.Clear();
        foreach (var enemy in targetEnemies)
        {
            if (enemy != null)
                _pendingEnemies.Add(enemy);
        }
        totalRequired = _pendingEnemies.Count;
        killedCount = 0;
        Debug.Log($"[EnemyKillStoryFlag] 初始化: 需击杀 {totalRequired} 个特定敌人 (flag: {flagName})");
    }

    void OnEnemyKilled(Enemy deadEnemy)
    {
        if (hasTriggered && triggerOnce) return;
        if (deadEnemy == null) return;

        // 只计数目标列表中的敌人
        if (!_pendingEnemies.Remove(deadEnemy)) return;

        killedCount++;
        Debug.Log($"[EnemyKillStoryFlag] 目标击杀: {killedCount}/{totalRequired} (flag: {flagName})");

        if (_pendingEnemies.Count == 0)
        {
            hasTriggered = true;
            StartCoroutine(DelayedSetFlag());
        }
    }

    /// <summary>
    /// 延时设置标志位
    /// </summary>
    private System.Collections.IEnumerator DelayedSetFlag()
    {
        yield return new WaitForSeconds(0.5f);

        if (StoryManager.Instance != null)
        {
            StoryManager.Instance.SetFlag(flagName);
            Debug.Log($"[EnemyKillStoryFlag] 全部目标已击杀，延时0.5秒后设置标志位: {flagName}");
        }
        else
        {
            Debug.LogError("[EnemyKillStoryFlag] StoryManager.Instance 为空，无法设置标志位!");
        }
    }

    /// <summary>
    /// 重置计数器（可从外部调用）
    /// </summary>
    public void ResetCounter()
    {
        hasTriggered = false;
        Start(); // 重新初始化追踪集合
    }
}
