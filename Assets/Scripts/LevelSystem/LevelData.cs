using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单个敌人的生成配置（内部使用，由波次自动生成）
/// </summary>
[Serializable]
public class EnemySpawnData
{
    [Tooltip("敌人预制体")]
    public GameObject enemyPrefab;
    
    [Tooltip("生成位置（相对于波次触发点的偏移）")]
    public Vector2 spawnOffset;
    
    [Tooltip("生成延迟（秒）")]
    public float spawnDelay = 0f;
    
    [Tooltip("是否从左侧进入")]
    public bool fromLeft = false;
}

/// <summary>
/// 战斗波次数据 - 简化版
/// </summary>
[Serializable]
public class BattleWave
{
    [Tooltip("波次名称（用于编辑器显示）")]
    public string waveName = "Wave";
    
    [Tooltip("触发位置X（玩家到达此位置触发波次）")]
    public float triggerPositionX;
    
    [Header("左侧敌人（从屏幕左边进入）")]
    [Tooltip("左侧敌人数量")]
    public int leftEnemyCount = 0;
    
    [Tooltip("左侧敌人Y轴位置")]
    public float leftEnemyY = -3.5f;
    
    [Header("右侧敌人（从屏幕右边进入）")]
    [Tooltip("右侧敌人数量")]
    public int rightEnemyCount = 1;
    
    [Tooltip("右侧敌人Y轴位置")]
    public float rightEnemyY = -3.5f;
    
    [Header("敌人配置")]
    [Tooltip("使用的敌人预制体")]
    public GameObject enemyPrefab;
    
    [Tooltip("敌人之间的Y轴间隔")]
    public float enemyYSpacing = 1.0f;
    
    [Header("战斗区域")]
    [Tooltip("是否需要清除所有敌人才能继续前进")]
    public bool mustClearToProceed = true;
    
    // 内部使用的敌人列表（运行时生成）
    [HideInInspector]
    public List<EnemySpawnData> enemies = new List<EnemySpawnData>();
    
    /// <summary>
    /// 获取该波次的敌人总数
    /// </summary>
    public int TotalEnemyCount => leftEnemyCount + rightEnemyCount;
    
    /// <summary>
    /// 根据简化配置生成敌人列表
    /// </summary>
    public void GenerateEnemyList()
    {
        enemies.Clear();
        
        if (enemyPrefab == null) return;
        
        // 生成左侧敌人
        for (int i = 0; i < leftEnemyCount; i++)
        {
            EnemySpawnData enemy = new EnemySpawnData();
            enemy.enemyPrefab = enemyPrefab;
            enemy.fromLeft = true;
            
            // Y轴位置：如果多个敌人，稍微错开
            float yOffset = 0;
            if (leftEnemyCount > 1)
            {
                yOffset = (i - (leftEnemyCount - 1) / 2f) * enemyYSpacing;
            }
            
            // 目标位置在触发点左侧
            enemy.spawnOffset = new Vector2(-3f - i * 1.5f, leftEnemyY + yOffset);
            enemy.spawnDelay = i * 0.2f;
            enemies.Add(enemy);
        }
        
        // 生成右侧敌人
        for (int i = 0; i < rightEnemyCount; i++)
        {
            EnemySpawnData enemy = new EnemySpawnData();
            enemy.enemyPrefab = enemyPrefab;
            enemy.fromLeft = false;
            
            // Y轴位置
            float yOffset = 0;
            if (rightEnemyCount > 1)
            {
                yOffset = (i - (rightEnemyCount - 1) / 2f) * enemyYSpacing;
            }
            
            // 目标位置在触发点右侧
            enemy.spawnOffset = new Vector2(3f + i * 1.5f, rightEnemyY + yOffset);
            enemy.spawnDelay = i * 0.2f;
            enemies.Add(enemy);
        }
    }
}

/// <summary>
/// 关卡数据 - ScriptableObject，可在编辑器中创建和编辑
/// </summary>
[CreateAssetMenu(fileName = "NewLevel", menuName = "Game/Level Data", order = 1)]
public class LevelData : ScriptableObject
{
    [Header("关卡基本信息")]
    [Tooltip("关卡名称")]
    public string levelName = "New Level";
    
    [Tooltip("关卡难度 (1-5)")]
    [Range(1, 5)]
    public int difficulty = 1;
    
    [Header("关卡地图设置")]
    [Tooltip("关卡总长度")]
    public float levelLength = 50f;
    
    [Tooltip("玩家起始位置")]
    public Vector2 playerStartPosition = new Vector2(-8f, -3.5f);
    
    [Tooltip("关卡终点位置X")]
    public float levelEndPositionX = 45f;
    
    [Header("默认敌人预制体")]
    [Tooltip("新波次默认使用的敌人预制体")]
    public GameObject defaultEnemyPrefab;
    
    [Header("战斗波次")]
    [Tooltip("所有战斗波次列表")]
    public List<BattleWave> battleWaves = new List<BattleWave>();
    
    /// <summary>
    /// 获取波次总数
    /// </summary>
    public int TotalWaveCount => battleWaves.Count;
    
    /// <summary>
    /// 获取关卡中所有敌人的总数
    /// </summary>
    public int TotalEnemyCount
    {
        get
        {
            int total = 0;
            foreach (var wave in battleWaves)
            {
                total += wave.TotalEnemyCount;
            }
            return total;
        }
    }
    
    /// <summary>
    /// 根据位置获取当前应触发的波次索引
    /// </summary>
    public int GetWaveIndexAtPosition(float positionX)
    {
        for (int i = battleWaves.Count - 1; i >= 0; i--)
        {
            if (positionX >= battleWaves[i].triggerPositionX)
            {
                return i;
            }
        }
        return -1;
    }
    
    /// <summary>
    /// 初始化所有波次的敌人列表（在加载关卡时调用）
    /// </summary>
    public void InitializeWaves()
    {
        foreach (var wave in battleWaves)
        {
            // 如果波次没有设置敌人预制体，使用默认的
            if (wave.enemyPrefab == null)
                wave.enemyPrefab = defaultEnemyPrefab;
                
            wave.GenerateEnemyList();
        }
    }
    
    /// <summary>
    /// 验证关卡数据是否有效
    /// </summary>
    public bool Validate(out string errorMessage)
    {
        errorMessage = "";
        
        if (string.IsNullOrEmpty(levelName))
        {
            errorMessage = "关卡名称不能为空";
            return false;
        }
        
        if (battleWaves.Count == 0)
        {
            errorMessage = "关卡至少需要一个战斗波次";
            return false;
        }
        
        for (int i = 0; i < battleWaves.Count; i++)
        {
            var wave = battleWaves[i];
            if (wave.TotalEnemyCount == 0)
            {
                errorMessage = $"波次 {i + 1} 没有配置敌人";
                return false;
            }
            
            if (wave.enemyPrefab == null && defaultEnemyPrefab == null)
            {
                errorMessage = $"波次 {i + 1} 没有设置敌人预制体";
                return false;
            }
        }
        
        return true;
    }
}
