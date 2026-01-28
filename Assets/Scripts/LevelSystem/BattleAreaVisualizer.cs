using UnityEngine;

/// <summary>
/// 战斗区域可视化 - 在Scene视图中显示战斗区域边界
/// </summary>
public class BattleAreaVisualizer : MonoBehaviour
{
    [Header("可视化设置")]
    public bool showInEditor = true;
    public bool showInGame = false;
    public Color battleAreaColor = new Color(1f, 0.5f, 0f, 0.3f);
    public Color boundaryColor = new Color(1f, 0f, 0f, 0.8f);
    
    [Header("边界墙")]
    public bool createBoundaryWalls = false; // [修改] 默认为false，不再创建物理墙
    public GameObject leftWall;
    public GameObject rightWall;
    
    private BattleWaveManager battleWaveManager;
    private bool wallsCreated = false;
    
    void Start()
    {
        battleWaveManager = BattleWaveManager.Instance;
        
        if (createBoundaryWalls && !wallsCreated)
        {
            CreateBoundaryWalls();
        }
    }
    
    void Update()
    {
        if (battleWaveManager == null) return;
        
        // 更新边界墙位置
        UpdateBoundaryWalls();
    }
    
    void CreateBoundaryWalls()
    {
        // 创建左边界墙
        if (leftWall == null)
        {
            leftWall = new GameObject("LeftBoundaryWall");
            leftWall.transform.SetParent(transform);
            BoxCollider2D leftCol = leftWall.AddComponent<BoxCollider2D>();
            leftCol.size = new Vector2(1f, 20f);
            leftWall.layer = LayerMask.NameToLayer("Default");
        }
        
        // 创建右边界墙
        if (rightWall == null)
        {
            rightWall = new GameObject("RightBoundaryWall");
            rightWall.transform.SetParent(transform);
            BoxCollider2D rightCol = rightWall.AddComponent<BoxCollider2D>();
            rightCol.size = new Vector2(1f, 20f);
            rightWall.layer = LayerMask.NameToLayer("Default");
        }
        
        wallsCreated = true;
        
        // 初始时隐藏
        leftWall.SetActive(false);
        rightWall.SetActive(false);
    }
    
    void UpdateBoundaryWalls()
    {
        if (leftWall == null || rightWall == null) return;
        
        // [修改] 如果不启用边界墙，则始终隐藏
        if (!createBoundaryWalls)
        {
            leftWall.SetActive(false);
            rightWall.SetActive(false);
            return;
        }
        
        bool inBattle = battleWaveManager.IsInBattle;
        
        leftWall.SetActive(inBattle);
        rightWall.SetActive(inBattle);
        
        if (inBattle)
        {
            float left = battleWaveManager.BattleLeftBound;
            float right = battleWaveManager.BattleRightBound;
            
            leftWall.transform.position = new Vector3(left - 0.5f, 0, 0);
            rightWall.transform.position = new Vector3(right + 0.5f, 0, 0);
        }
    }
    
    void OnDrawGizmos()
    {
        if (!showInEditor) return;
        
        // 在编辑器中绘制关卡数据中的战斗区域
        LevelProgressManager progressManager = FindFirstObjectByType<LevelProgressManager>();
        if (progressManager == null || progressManager.currentLevelData == null) return;
        
        LevelData levelData = progressManager.currentLevelData;
        
        // 绘制关卡范围
        Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
        Vector3 levelCenter = new Vector3(levelData.levelLength / 2, 0, 0);
        Vector3 levelSize = new Vector3(levelData.levelLength, 10f, 1f);
        Gizmos.DrawCube(levelCenter, levelSize);
        
        // 绘制每个波次的触发点和战斗区域
        for (int i = 0; i < levelData.battleWaves.Count; i++)
        {
            BattleWave wave = levelData.battleWaves[i];
            
            // 触发线
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(
                new Vector3(wave.triggerPositionX, -5f, 0),
                new Vector3(wave.triggerPositionX, 5f, 0)
            );
            
            // 战斗区域标记（运行时使用摄像机范围，这里只显示触发点）
            if (wave.mustClearToProceed)
            {
                Gizmos.color = battleAreaColor;
                // 在触发点显示一个标记表示这里会锁定战斗
                Vector3 markerPos = new Vector3(wave.triggerPositionX, 0, 0);
                Gizmos.DrawWireCube(markerPos, new Vector3(1f, 8f, 1f));
            }
            
            // 敌人位置
            Gizmos.color = Color.red;
            foreach (var enemy in wave.enemies)
            {
                Vector3 enemyPos = new Vector3(
                    wave.triggerPositionX + enemy.spawnOffset.x,
                    enemy.spawnOffset.y,
                    0
                );
                Gizmos.DrawSphere(enemyPos, 0.3f);
            }
        }
        
        // 绘制起点
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(new Vector3(levelData.playerStartPosition.x, levelData.playerStartPosition.y, 0), 0.5f);
        
        // 绘制终点
        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(new Vector3(levelData.levelEndPositionX, 0, 0), 0.5f);
    }
    
    void OnDrawGizmosSelected()
    {
        if (!showInGame || !Application.isPlaying) return;
        if (battleWaveManager == null) return;
        
        // 运行时绘制当前战斗区域
        if (battleWaveManager.IsInBattle)
        {
            float left = battleWaveManager.BattleLeftBound;
            float right = battleWaveManager.BattleRightBound;
            
            Gizmos.color = battleAreaColor;
            float width = right - left;
            Vector3 center = new Vector3(left + width / 2, 0, 0);
            Gizmos.DrawCube(center, new Vector3(width, 10f, 1f));
            
            Gizmos.color = boundaryColor;
            Gizmos.DrawLine(new Vector3(left, -5f, 0), new Vector3(left, 5f, 0));
            Gizmos.DrawLine(new Vector3(right, -5f, 0), new Vector3(right, 5f, 0));
        }
    }
}
