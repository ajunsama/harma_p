using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// 简化版关卡编辑器 - 直观易用
/// </summary>
public class LevelEditorWindow : EditorWindow
{
    private LevelData currentLevel;
    private Vector2 scrollPos;
    private int selectedWaveIndex = -1;
    
    // 预览设置
    private float previewScale = 8f;
    private Vector2 previewScroll = Vector2.zero;
    
    // 敌人预制体列表
    private List<GameObject> enemyPrefabList = new List<GameObject>();
    
    // 颜色
    private static readonly Color LeftEnemyColor = new Color(0.2f, 0.6f, 1f);      // 蓝色-左侧
    private static readonly Color RightEnemyColor = new Color(1f, 0.4f, 0.3f);     // 红色-右侧
    private static readonly Color TriggerLineColor = new Color(1f, 1f, 0.3f);      // 黄色-触发线
    private static readonly Color PlayerColor = new Color(0.3f, 1f, 0.3f);         // 绿色-玩家
    private static readonly Color BattleAreaColor = new Color(1f, 0.5f, 0.2f, 0.15f); // 橙色-战斗区域
    
    [MenuItem("Tools/关卡编辑器 %#L")]
    public static void ShowWindow()
    {
        LevelEditorWindow window = GetWindow<LevelEditorWindow>("关卡编辑器");
        window.minSize = new Vector2(900, 600);
    }
    
    void OnEnable()
    {
        RefreshEnemyPrefabList();
    }
    
    void OnGUI()
    {
        // 顶部工具栏
        DrawToolbar();
        
        if (currentLevel == null)
        {
            DrawWelcomeScreen();
            return;
        }
        
        EditorGUILayout.BeginHorizontal();
        
        // 左侧：波次列表和设置
        EditorGUILayout.BeginVertical(GUILayout.Width(350));
        DrawWaveListAndSettings();
        EditorGUILayout.EndVertical();
        
        // 右侧：可视化预览
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        DrawVisualPreview();
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.EndHorizontal();
    }
    
    void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        
        if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(50)))
            CreateNewLevel();
        
        if (GUILayout.Button("打开", EditorStyles.toolbarButton, GUILayout.Width(50)))
            OpenLevel();
        
        GUI.enabled = currentLevel != null;
        if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(50)))
            SaveLevel();
        GUI.enabled = true;
        
        GUILayout.FlexibleSpace();
        
        if (currentLevel != null)
        {
            GUILayout.Label($"📁 {currentLevel.levelName}", EditorStyles.toolbarButton);
            GUILayout.Label($"| 波次: {currentLevel.TotalWaveCount}", EditorStyles.toolbarButton);
            GUILayout.Label($"| 敌人: {currentLevel.TotalEnemyCount}", EditorStyles.toolbarButton);
        }
        
        EditorGUILayout.EndHorizontal();
    }
    
    void DrawWelcomeScreen()
    {
        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        
        EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(300), GUILayout.Height(180));
        GUILayout.Space(20);
        
        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
        titleStyle.fontSize = 18;
        titleStyle.alignment = TextAnchor.MiddleCenter;
        GUILayout.Label("🎮 关卡编辑器", titleStyle);
        
        GUILayout.Space(20);
        
        if (GUILayout.Button("新建关卡", GUILayout.Height(35)))
            CreateNewLevel();
        
        GUILayout.Space(5);
        
        if (GUILayout.Button("打开现有关卡", GUILayout.Height(35)))
            OpenLevel();
        
        GUILayout.Space(20);
        EditorGUILayout.EndVertical();
        
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
    }
    
    void DrawWaveListAndSettings()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        
        // 关卡基本设置
        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.LabelField("📋 关卡设置", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);
        
        currentLevel.levelName = EditorGUILayout.TextField("关卡名称", currentLevel.levelName);
        currentLevel.levelLength = EditorGUILayout.FloatField("关卡长度", currentLevel.levelLength);
        currentLevel.playerStartPosition = EditorGUILayout.Vector2Field("玩家起点", currentLevel.playerStartPosition);
        currentLevel.levelEndPositionX = EditorGUILayout.FloatField("终点位置X", currentLevel.levelEndPositionX);
        
        EditorGUILayout.Space(5);
        currentLevel.defaultEnemyPrefab = (GameObject)EditorGUILayout.ObjectField(
            "默认敌人预制体", currentLevel.defaultEnemyPrefab, typeof(GameObject), false);
        
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space(10);
        
        // 波次列表标题
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("⚔️ 战斗波次", EditorStyles.boldLabel);
        if (GUILayout.Button("+ 添加波次", GUILayout.Width(80)))
            AddNewWave();
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space(5);
        
        // 波次列表
        for (int i = 0; i < currentLevel.battleWaves.Count; i++)
        {
            DrawWaveItem(i);
        }
        
        EditorGUILayout.EndScrollView();
    }
    
    void DrawWaveItem(int index)
    {
        BattleWave wave = currentLevel.battleWaves[index];
        bool isSelected = (index == selectedWaveIndex);
        
        // 波次框
        Color bgColor = isSelected ? new Color(0.3f, 0.5f, 0.8f, 0.3f) : new Color(0.2f, 0.2f, 0.2f, 0.3f);
        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = MakeColorTexture(bgColor);
        
        EditorGUILayout.BeginVertical(boxStyle);
        
        // 标题行
        EditorGUILayout.BeginHorizontal();
        
        // 点击选择
        if (GUILayout.Button(isSelected ? "▼" : "▶", GUILayout.Width(25)))
        {
            selectedWaveIndex = isSelected ? -1 : index;
        }
        
        EditorGUILayout.LabelField($"波次 {index + 1}", EditorStyles.boldLabel, GUILayout.Width(60));
        
        // 快速信息显示
        GUILayout.Label($"触发X: {wave.triggerPositionX:F0}", GUILayout.Width(80));
        
        // 左侧敌人图标
        GUI.color = LeftEnemyColor;
        GUILayout.Label($"← {wave.leftEnemyCount}", GUILayout.Width(35));
        
        // 右侧敌人图标
        GUI.color = RightEnemyColor;
        GUILayout.Label($"→ {wave.rightEnemyCount}", GUILayout.Width(35));
        GUI.color = Color.white;
        
        GUILayout.FlexibleSpace();
        
        // 删除按钮
        if (GUILayout.Button("×", GUILayout.Width(22)))
        {
            if (EditorUtility.DisplayDialog("删除波次", $"确定删除波次 {index + 1}？", "删除", "取消"))
            {
                currentLevel.battleWaves.RemoveAt(index);
                if (selectedWaveIndex >= currentLevel.battleWaves.Count)
                    selectedWaveIndex = currentLevel.battleWaves.Count - 1;
                EditorUtility.SetDirty(currentLevel);
                return;
            }
        }
        
        EditorGUILayout.EndHorizontal();
        
        // 展开的详细设置
        if (isSelected)
        {
            EditorGUILayout.Space(5);
            EditorGUI.indentLevel++;
            
            // 触发位置
            EditorGUILayout.BeginHorizontal();
            wave.triggerPositionX = EditorGUILayout.FloatField("触发位置 X", wave.triggerPositionX);
            if (GUILayout.Button("设为前一波次+10", GUILayout.Width(110)))
            {
                if (index > 0)
                    wave.triggerPositionX = currentLevel.battleWaves[index - 1].triggerPositionX + 10f;
                else
                    wave.triggerPositionX = 0f;
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(8);
            
            // 敌人预制体
            wave.enemyPrefab = (GameObject)EditorGUILayout.ObjectField(
                "敌人预制体", wave.enemyPrefab, typeof(GameObject), false);
            
            if (wave.enemyPrefab == null && currentLevel.defaultEnemyPrefab != null)
            {
                EditorGUILayout.HelpBox("未设置将使用默认敌人预制体", MessageType.Info);
                wave.enemyPrefab = currentLevel.defaultEnemyPrefab;
            }
            
            EditorGUILayout.Space(8);
            
            // 左侧敌人设置
            EditorGUILayout.BeginVertical(GUI.skin.box);
            GUI.color = LeftEnemyColor;
            EditorGUILayout.LabelField("← 左侧敌人（从屏幕左边进入）", EditorStyles.boldLabel);
            GUI.color = Color.white;
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("数量", GUILayout.Width(80));
            wave.leftEnemyCount = EditorGUILayout.IntSlider(wave.leftEnemyCount, 0, 10);
            EditorGUILayout.EndHorizontal();
            
            if (wave.leftEnemyCount > 0)
            {
                wave.leftEnemyY = EditorGUILayout.FloatField("Y轴位置", wave.leftEnemyY);
            }
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(5);
            
            // 右侧敌人设置
            EditorGUILayout.BeginVertical(GUI.skin.box);
            GUI.color = RightEnemyColor;
            EditorGUILayout.LabelField("→ 右侧敌人（从屏幕右边进入）", EditorStyles.boldLabel);
            GUI.color = Color.white;
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("数量", GUILayout.Width(80));
            wave.rightEnemyCount = EditorGUILayout.IntSlider(wave.rightEnemyCount, 0, 10);
            EditorGUILayout.EndHorizontal();
            
            if (wave.rightEnemyCount > 0)
            {
                wave.rightEnemyY = EditorGUILayout.FloatField("Y轴位置", wave.rightEnemyY);
            }
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(5);
            
            // Y轴间隔（当有多个敌人时）
            if (wave.leftEnemyCount > 1 || wave.rightEnemyCount > 1)
            {
                wave.enemyYSpacing = EditorGUILayout.FloatField("敌人Y轴间隔", wave.enemyYSpacing);
            }
            
            EditorGUILayout.Space(5);
            
            // 战斗区域设置
            wave.mustClearToProceed = EditorGUILayout.Toggle("必须清怪才能前进", wave.mustClearToProceed);
            
            EditorGUI.indentLevel--;
        }
        
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(3);
    }
    
    void DrawVisualPreview()
    {
        EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandHeight(true));
        
        // 预览标题和控制
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("🗺️ 关卡预览", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        GUILayout.Label("缩放:", GUILayout.Width(40));
        previewScale = GUILayout.HorizontalSlider(previewScale, 3f, 15f, GUILayout.Width(100));
        if (GUILayout.Button("重置", GUILayout.Width(50)))
        {
            previewScale = 8f;
            previewScroll = Vector2.zero;
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space(5);
        
        // 图例
        EditorGUILayout.BeginHorizontal();
        DrawLegendItem(PlayerColor, "● 玩家起点");
        DrawLegendItem(TriggerLineColor, "| 触发线");
        DrawLegendItem(LeftEnemyColor, "◀ 左侧敌人");
        DrawLegendItem(RightEnemyColor, "▶ 右侧敌人");
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space(5);
        
        // 预览区域
        Rect previewRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, 
            GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        
        // 背景
        EditorGUI.DrawRect(previewRect, new Color(0.12f, 0.12f, 0.15f));
        
        // 处理滚动
        HandlePreviewScroll(previewRect);
        
        // 绘制预览内容
        DrawPreviewContent(previewRect);
        
        EditorGUILayout.EndVertical();
    }
    
    void DrawLegendItem(Color color, string text)
    {
        GUI.color = color;
        GUILayout.Label(text, GUILayout.Width(85));
        GUI.color = Color.white;
    }
    
    void DrawPreviewContent(Rect rect)
    {
        if (currentLevel == null) return;
        
        // 计算视图参数
        float viewWidth = currentLevel.levelLength * previewScale;
        float centerY = rect.y + rect.height * 0.6f; // 地面线位置
        float startX = rect.x + 50 - previewScroll.x;
        
        // 剪裁区域
        GUI.BeginClip(rect);
        Rect localRect = new Rect(0, 0, rect.width, rect.height);
        float localCenterY = rect.height * 0.6f;
        float localStartX = 50 - previewScroll.x;
        
        // 绘制地面线
        Handles.color = new Color(0.3f, 0.5f, 0.3f);
        Handles.DrawLine(
            new Vector3(0, localCenterY, 0),
            new Vector3(rect.width, localCenterY, 0)
        );
        
        // 绘制刻度
        Handles.color = new Color(0.3f, 0.3f, 0.3f);
        for (float x = 0; x <= currentLevel.levelLength; x += 5f)
        {
            float screenX = localStartX + x * previewScale;
            if (screenX >= 0 && screenX <= rect.width)
            {
                Handles.DrawLine(
                    new Vector3(screenX, localCenterY - 5, 0),
                    new Vector3(screenX, localCenterY + 5, 0)
                );
                
                // 刻度数字
                if (x % 10 == 0)
                {
                    Handles.Label(new Vector3(screenX - 10, localCenterY + 15, 0), x.ToString());
                }
            }
        }
        
        // 绘制玩家起点
        float playerScreenX = localStartX + currentLevel.playerStartPosition.x * previewScale;
        float playerScreenY = localCenterY - currentLevel.playerStartPosition.y * previewScale;
        if (playerScreenX >= 0 && playerScreenX <= rect.width)
        {
            Handles.color = PlayerColor;
            Handles.DrawSolidDisc(new Vector3(playerScreenX, playerScreenY, 0), Vector3.forward, 8);
            Handles.Label(new Vector3(playerScreenX + 12, playerScreenY - 5, 0), "起点");
        }
        
        // 绘制终点
        float endScreenX = localStartX + currentLevel.levelEndPositionX * previewScale;
        if (endScreenX >= 0 && endScreenX <= rect.width)
        {
            Handles.color = new Color(1f, 0.8f, 0.2f);
            Handles.DrawSolidDisc(new Vector3(endScreenX, localCenterY, 0), Vector3.forward, 8);
            Handles.Label(new Vector3(endScreenX + 12, localCenterY - 5, 0), "终点");
        }
        
        // 绘制波次
        for (int i = 0; i < currentLevel.battleWaves.Count; i++)
        {
            BattleWave wave = currentLevel.battleWaves[i];
            bool isSelected = (i == selectedWaveIndex);
            
            float triggerScreenX = localStartX + wave.triggerPositionX * previewScale;
            
            if (triggerScreenX >= -50 && triggerScreenX <= rect.width + 50)
            {
                // 触发线
                Handles.color = isSelected ? TriggerLineColor : new Color(0.6f, 0.6f, 0.3f, 0.5f);
                float lineWidth = isSelected ? 2f : 1f;
                Handles.DrawAAPolyLine(lineWidth,
                    new Vector3(triggerScreenX, 0, 0),
                    new Vector3(triggerScreenX, rect.height, 0)
                );
                
                // 波次标签
                GUIStyle labelStyle = isSelected ? EditorStyles.whiteBoldLabel : EditorStyles.miniLabel;
                Handles.Label(new Vector3(triggerScreenX + 5, 15, 0), $"波次{i + 1}", labelStyle);
                
                // 绘制敌人指示
                float enemyY = localCenterY;
                
                // 左侧敌人（蓝色箭头指向右）
                if (wave.leftEnemyCount > 0)
                {
                    Handles.color = LeftEnemyColor;
                    float leftEnemyScreenY = localCenterY - wave.leftEnemyY * previewScale;
                    
                    for (int j = 0; j < wave.leftEnemyCount; j++)
                    {
                        float yOffset = 0;
                        if (wave.leftEnemyCount > 1)
                            yOffset = (j - (wave.leftEnemyCount - 1) / 2f) * wave.enemyYSpacing * previewScale;
                        
                        float ex = triggerScreenX - 30 - j * 15;
                        float ey = leftEnemyScreenY + yOffset;
                        
                        // 绘制三角形指向右
                        DrawTriangle(new Vector2(ex, ey), 8, true);
                    }
                    
                    // 数量标签
                    Handles.Label(new Vector3(triggerScreenX - 60, leftEnemyScreenY - 20, 0), $"×{wave.leftEnemyCount}");
                }
                
                // 右侧敌人（红色箭头指向左）
                if (wave.rightEnemyCount > 0)
                {
                    Handles.color = RightEnemyColor;
                    float rightEnemyScreenY = localCenterY - wave.rightEnemyY * previewScale;
                    
                    for (int j = 0; j < wave.rightEnemyCount; j++)
                    {
                        float yOffset = 0;
                        if (wave.rightEnemyCount > 1)
                            yOffset = (j - (wave.rightEnemyCount - 1) / 2f) * wave.enemyYSpacing * previewScale;
                        
                        float ex = triggerScreenX + 30 + j * 15;
                        float ey = rightEnemyScreenY + yOffset;
                        
                        // 绘制三角形指向左
                        DrawTriangle(new Vector2(ex, ey), 8, false);
                    }
                    
                    // 数量标签
                    Handles.Label(new Vector3(triggerScreenX + 45, rightEnemyScreenY - 20, 0), $"×{wave.rightEnemyCount}");
                }
            }
        }
        
        GUI.EndClip();
    }
    
    void DrawTriangle(Vector2 center, float size, bool pointRight)
    {
        Vector3[] points = new Vector3[3];
        if (pointRight)
        {
            points[0] = new Vector3(center.x + size, center.y, 0);
            points[1] = new Vector3(center.x - size * 0.5f, center.y - size * 0.7f, 0);
            points[2] = new Vector3(center.x - size * 0.5f, center.y + size * 0.7f, 0);
        }
        else
        {
            points[0] = new Vector3(center.x - size, center.y, 0);
            points[1] = new Vector3(center.x + size * 0.5f, center.y - size * 0.7f, 0);
            points[2] = new Vector3(center.x + size * 0.5f, center.y + size * 0.7f, 0);
        }
        Handles.DrawAAConvexPolygon(points);
    }
    
    void HandlePreviewScroll(Rect rect)
    {
        Event e = Event.current;
        
        if (!rect.Contains(e.mousePosition)) return;
        
        // 滚轮缩放
        if (e.type == EventType.ScrollWheel)
        {
            previewScale -= e.delta.y * 0.3f;
            previewScale = Mathf.Clamp(previewScale, 3f, 15f);
            e.Use();
            Repaint();
        }
        
        // 拖拽滚动
        if (e.type == EventType.MouseDrag && e.button == 0)
        {
            previewScroll -= e.delta;
            previewScroll.x = Mathf.Max(0, previewScroll.x);
            e.Use();
            Repaint();
        }
    }
    
    Texture2D MakeColorTexture(Color color)
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }
    
    #region 关卡操作
    
    void CreateNewLevel()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "创建新关卡", "NewLevel", "asset", "选择保存位置", "Assets/LevelData"
        );
        
        if (string.IsNullOrEmpty(path)) return;
        
        LevelData newLevel = ScriptableObject.CreateInstance<LevelData>();
        newLevel.levelName = System.IO.Path.GetFileNameWithoutExtension(path);
        
        // 添加一个默认波次
        BattleWave defaultWave = new BattleWave();
        defaultWave.waveName = "第一波";
        defaultWave.triggerPositionX = 5f;
        defaultWave.rightEnemyCount = 2;
        
        // 尝试设置默认敌人预制体
        RefreshEnemyPrefabList();
        if (enemyPrefabList.Count > 0)
        {
            newLevel.defaultEnemyPrefab = enemyPrefabList[0];
            defaultWave.enemyPrefab = enemyPrefabList[0];
        }
        
        newLevel.battleWaves.Add(defaultWave);
        
        AssetDatabase.CreateAsset(newLevel, path);
        AssetDatabase.SaveAssets();
        
        currentLevel = newLevel;
        selectedWaveIndex = 0;
    }
    
    void OpenLevel()
    {
        string path = EditorUtility.OpenFilePanel("打开关卡", "Assets", "asset");
        
        if (string.IsNullOrEmpty(path)) return;
        
        if (path.StartsWith(Application.dataPath))
            path = "Assets" + path.Substring(Application.dataPath.Length);
        
        LevelData level = AssetDatabase.LoadAssetAtPath<LevelData>(path);
        
        if (level != null)
        {
            currentLevel = level;
            selectedWaveIndex = level.battleWaves.Count > 0 ? 0 : -1;
        }
        else
        {
            EditorUtility.DisplayDialog("错误", "无法加载关卡文件", "确定");
        }
    }
    
    void SaveLevel()
    {
        if (currentLevel == null) return;
        
        EditorUtility.SetDirty(currentLevel);
        AssetDatabase.SaveAssets();
        Debug.Log($"✓ 关卡 '{currentLevel.levelName}' 已保存");
    }
    
    void AddNewWave()
    {
        BattleWave newWave = new BattleWave();
        newWave.waveName = $"波次{currentLevel.battleWaves.Count + 1}";
        
        // 自动设置触发位置
        if (currentLevel.battleWaves.Count > 0)
        {
            float lastX = currentLevel.battleWaves[currentLevel.battleWaves.Count - 1].triggerPositionX;
            newWave.triggerPositionX = lastX + 10f;
        }
        else
        {
            newWave.triggerPositionX = 5f;
        }
        
        // 默认右侧2个敌人
        newWave.rightEnemyCount = 2;
        newWave.enemyPrefab = currentLevel.defaultEnemyPrefab;
        
        currentLevel.battleWaves.Add(newWave);
        selectedWaveIndex = currentLevel.battleWaves.Count - 1;
        
        EditorUtility.SetDirty(currentLevel);
    }
    
    void RefreshEnemyPrefabList()
    {
        enemyPrefabList.Clear();
        
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" });
        
        foreach (string guid in guids)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            
            if (prefab != null && prefab.GetComponent<Enemy>() != null)
            {
                enemyPrefabList.Add(prefab);
            }
        }
    }
    
    #endregion
}
