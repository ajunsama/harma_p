using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

[InitializeOnLoad]
public class LevelEditorWindow : EditorWindow
{
    private const string RestoreAfterPlayKey = "LevelEditorWindow.RestoreAfterPlay";
    private const string RestoreLevelPathKey = "LevelEditorWindow.RestoreLevelPath";

    static LevelEditorWindow()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    // ========== 当前编辑状态 ==========
    private LevelData _level;
    private Vector2 _paletteScroll;
    private Vector2 _propertyScroll;
    private Vector2 _bottomScroll;
    private string _selectedElementId;
    private string _selectedGroupId;
    private int _selectedStoryTriggerIndex = -1;
    private bool _showBottomList = true;

    // ========== 画布 ==========
    private float _canvasScale = 8f;
    private Vector2 _canvasScroll = Vector2.zero;
    private bool _isDraggingElement;
    private Vector2 _dragOffset;
    private bool _isPanning;
    private Vector2 _panStartMouse;
    private Vector2 _panStartScroll;
    private const string TemplateScenePath = "Assets/Scenes/LevelEditorTestTemplate.unity";
    private const double FloorBoundsRefreshInterval = 1.0;
    private bool _hasPreviewFloorBounds;
    private Bounds _previewFloorBounds;
    private double _nextPreviewFloorRefreshTime;

    // ========== 预制体库 ==========
    private List<ElementPrefabEntry> _scannedEnemies = new List<ElementPrefabEntry>();
    private List<ElementPrefabEntry> _scannedItems = new List<ElementPrefabEntry>();
    private List<ElementPrefabEntry> _scannedObstacles = new List<ElementPrefabEntry>();

    // ========== 元素放置模式 ==========
    private ElementType _placingType;
    private GameObject _placingPrefab;
    private bool _isPlacingMode;

    // ========== 颜色 ==========
    private static readonly Color EnemyColor = new Color(1f, 0.35f, 0.3f);
    private static readonly Color ItemColor = new Color(1f, 0.85f, 0.2f);
    private static readonly Color ObstacleColor = new Color(0.5f, 0.5f, 0.5f);
    private static readonly Color PlayerColor = new Color(0.3f, 1f, 0.3f);
    private static readonly Color TriggerLineColor = new Color(0.3f, 0.6f, 1f);
    private static readonly Color GroupLineColor = new Color(1f, 0.55f, 0.1f);
    private static readonly Color EndColor = new Color(0.8f, 0.8f, 0.8f);
    private static readonly Regex SceneYamlBlockRegex = new Regex(
        @"--- !u!\d+ &(?<id>-?\d+)\r?\n(?<type>[A-Za-z0-9_]+):(?<body>.*?)(?=\r?\n--- !u!|\z)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    [MenuItem("Tools/关卡编辑器 %#L")]
    public static void ShowWindow()
    {
        var window = GetWindow<LevelEditorWindow>("关卡编辑器");
        window.minSize = new Vector2(1000, 650);
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;
        if (!SessionState.GetBool(RestoreAfterPlayKey, false)) return;

        SessionState.SetBool(RestoreAfterPlayKey, false);
        var window = GetWindow<LevelEditorWindow>("关卡编辑器");
        window.minSize = new Vector2(1000, 650);

        string levelPath = SessionState.GetString(RestoreLevelPathKey, "");
        if (!string.IsNullOrEmpty(levelPath))
            window._level = AssetDatabase.LoadAssetAtPath<LevelData>(levelPath);

        window.ScanPrefabs();
        window.Show();
    }

    void OnEnable()
    {
        ScanPrefabs();
        SceneView.duringSceneGui += OnSceneGUI;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    void OnGUI()
    {
        DrawToolbar();

        if (_level == null)
        {
            DrawWelcomeScreen();
            return;
        }

        EditorGUILayout.BeginHorizontal();

        // 左侧面板
        EditorGUILayout.BeginVertical(GUILayout.Width(180));
        DrawElementPalette();
        EditorGUILayout.EndVertical();

        // 中间画布
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        DrawCanvas();
        EditorGUILayout.EndVertical();

        // 右侧属性面板
        EditorGUILayout.BeginVertical(GUILayout.Width(260));
        DrawPropertyPanel();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();

        // 底部面板
        DrawBottomPanel();

        HandleKeyboardShortcuts();
    }

    // ================================================================
    // 工具栏
    // ================================================================

    void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(45)))
            CreateNewLevel();
        if (GUILayout.Button("打开", EditorStyles.toolbarButton, GUILayout.Width(45)))
            OpenLevel();
        if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(45)))
            SaveLevel();

        GUILayout.Space(10);

        if (GUILayout.Button("Play测试", EditorStyles.toolbarButton, GUILayout.Width(60)))
            PlayTest();

        if (GUILayout.Button("导出Scene", EditorStyles.toolbarButton, GUILayout.Width(65)))
            ExportScene();

        GUILayout.Space(10);

        if (GUILayout.Button("清空", EditorStyles.toolbarButton, GUILayout.Width(40)))
        {
            if (EditorUtility.DisplayDialog("清空关卡", "确定要清空所有元素？", "清空", "取消"))
            {
                Undo.RecordObject(_level, "Clear Level");
                _level.elements.Clear();
                _level.groups.Clear();
                _level.storyTriggers.Clear();
                _level.variables.Clear();
                EditorUtility.SetDirty(_level);
            }
        }

        if (GUILayout.Button("校验", EditorStyles.toolbarButton, GUILayout.Width(40)))
            ValidateLevel();

        GUILayout.FlexibleSpace();

        if (_level != null)
        {
            GUILayout.Label(_level.levelName, EditorStyles.toolbarButton);
            GUILayout.Label($"元素: {_level.elements.Count}", EditorStyles.toolbarButton);
            GUILayout.Label($"组: {_level.groups.Count}", EditorStyles.toolbarButton);
        }

        EditorGUILayout.EndHorizontal();
    }

    void DrawWelcomeScreen()
    {
        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginVertical(GUILayout.Width(300), GUILayout.Height(180));
        GUILayout.Space(20);
        var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        GUILayout.Label("关卡编辑器", titleStyle);
        GUILayout.Space(20);
        if (GUILayout.Button("新建关卡", GUILayout.Height(35))) CreateNewLevel();
        GUILayout.Space(5);
        if (GUILayout.Button("打开现有关卡", GUILayout.Height(35))) OpenLevel();
        GUILayout.Space(20);
        EditorGUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
    }

    // ================================================================
    // 左侧 — 元素面板
    // ================================================================

    void DrawElementPalette()
    {
        _paletteScroll = EditorGUILayout.BeginScrollView(_paletteScroll);

        DrawPaletteCategory("敌人", ElementType.Enemy, _scannedEnemies, ref _level.enemyPrefabLibrary);
        DrawPaletteCategory("道具", ElementType.Item, _scannedItems, ref _level.itemPrefabLibrary);
        DrawPaletteCategory("障碍物", ElementType.Obstacle, _scannedObstacles, ref _level.obstaclePrefabLibrary);

        GUILayout.Space(10);
        if (GUILayout.Button("扫描预制体"))
        {
            ScanPrefabs();
            Repaint();
        }

        EditorGUILayout.EndScrollView();
    }

    void DrawPaletteCategory(string title, ElementType type, List<ElementPrefabEntry> scanned, ref List<ElementPrefabEntry> library)
    {
        GUILayout.Label(title, EditorStyles.boldLabel);
        var entries = library.Count > 0 ? library : scanned;
        foreach (var entry in entries)
        {
            if (entry.prefab == null) continue;
            var rect = EditorGUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(entry.displayName ?? entry.prefab.name, GUILayout.Width(110));
            if (GUILayout.Button("+", GUILayout.Width(22)))
            {
                _isPlacingMode = true;
                _placingType = type;
                _placingPrefab = entry.prefab;
            }
            EditorGUILayout.EndHorizontal();
        }
        GUILayout.Space(5);
    }

    // ================================================================
    // 中央 — 2D 画布
    // ================================================================

    void DrawCanvas()
    {
        EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandHeight(true));

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("关卡预览", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        GUILayout.Label("缩放:", GUILayout.Width(35));
        _canvasScale = GUILayout.HorizontalSlider(_canvasScale, 3f, 15f, GUILayout.Width(80));
        if (GUILayout.Button("复位", GUILayout.Width(45))) { _canvasScale = 8f; _canvasScroll = Vector2.zero; }
        EditorGUILayout.EndHorizontal();

        // 图例
        EditorGUILayout.BeginHorizontal();
        DrawLegendItem(PlayerColor, "玩家起点");
        DrawLegendItem(EndColor, "终点");
        DrawLegendItem(TriggerLineColor, "过场");
        DrawLegendItem(EnemyColor, "敌人");
        DrawLegendItem(ItemColor, "道具");
        DrawLegendItem(ObstacleColor, "障碍物");
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        Rect canvasRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
            GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(canvasRect, new Color(0.1f, 0.1f, 0.13f));

        HandleCanvasInput(canvasRect);
        DrawCanvasContent(canvasRect);

        EditorGUILayout.EndVertical();

        if (_isPlacingMode)
            EditorGUILayout.HelpBox($"放置模式：点击画布放置 {(_placingPrefab != null ? _placingPrefab.name : "")}（右键取消）", MessageType.Info);
    }

    void DrawLegendItem(Color c, string text)
    {
        GUI.color = c;
        GUILayout.Label(text, GUILayout.Width(60));
        GUI.color = Color.white;
    }

    void HandleCanvasInput(Rect rect)
    {
        Event e = Event.current;
        if (!rect.Contains(e.mousePosition)) return;

        if (e.type == EventType.ScrollWheel)
        {
            _canvasScale -= e.delta.y * 0.3f;
            _canvasScale = Mathf.Clamp(_canvasScale, 3f, 15f);
            e.Use();
            Repaint();
            return;
        }

        if (e.type == EventType.MouseDown && e.button == 2)
        {
            _isPanning = true;
            _panStartMouse = e.mousePosition;
            _panStartScroll = _canvasScroll;
            e.Use();
            return;
        }

        if (_isPanning && e.type == EventType.MouseDrag && e.button == 2)
        {
            _canvasScroll = _panStartScroll + (_panStartMouse - e.mousePosition);
            e.Use();
            Repaint();
            return;
        }

        if (e.type == EventType.MouseUp && e.button == 2)
        {
            _isPanning = false;
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            Vector2 worldPos = ScreenToWorld(e.mousePosition, rect);

            if (_isPlacingMode)
            {
                PlaceElement(worldPos);
                _isPlacingMode = false;
                e.Use();
                Repaint();
                return;
            }

            if (TrySelectElement(e.mousePosition, rect))
            {
                e.Use();
                Repaint();
                return;
            }

            _selectedElementId = null;
            _selectedGroupId = null;
            _selectedStoryTriggerIndex = -1;
            e.Use();
            Repaint();
        }

        if (Event.current.type == EventType.MouseDrag && e.button == 0 && !string.IsNullOrEmpty(_selectedElementId))
        {
            var el = _level.elements.Find(x => x.elementId == _selectedElementId);
            if (el != null)
            {
                Undo.RecordObject(_level, "Move Element");
                Vector2 worldPos = ScreenToWorld(e.mousePosition, rect);
                el.position = SnapToGrid(worldPos);
                EditorUtility.SetDirty(_level);
                e.Use();
                Repaint();
            }
        }

        if (e.type == EventType.MouseDown && e.button == 1)
        {
            _isPlacingMode = false;
            ShowCanvasContextMenu(e.mousePosition, rect);
            e.Use();
            Repaint();
        }
    }

    void PlaceElement(Vector2 worldPos)
    {
        Undo.RecordObject(_level, "Place Element");
        var el = new LevelElement
        {
            elementId = Guid.NewGuid().ToString(),
            displayName = _placingPrefab != null ? _placingPrefab.name : "New Element",
            elementType = _placingType,
            prefab = _placingPrefab,
            position = SnapToGrid(worldPos)
        };
        _level.elements.Add(el);
        _selectedElementId = el.elementId;
        EditorUtility.SetDirty(_level);
    }

    Vector2 SnapToGrid(Vector2 pos)
    {
        return new Vector2(Mathf.Round(pos.x * 2f) / 2f, Mathf.Round(pos.y * 2f) / 2f);
    }

    bool TrySelectElement(Vector2 mousePos, Rect rect)
    {
        float minDist = 12f;
        LevelElement closest = null;

        foreach (var el in _level.elements)
        {
            Vector2 screenPos = WorldToScreen(el.position, rect);
            float dist = Vector2.Distance(mousePos, screenPos);
            if (dist < minDist)
            {
                minDist = dist;
                closest = el;
            }
        }

        if (closest != null)
        {
            _selectedElementId = closest.elementId;
            _selectedGroupId = null;
            _selectedStoryTriggerIndex = -1;
            return true;
        }

        // 检查过场触发器
        for (int i = 0; i < _level.storyTriggers.Count; i++)
        {
            var st = _level.storyTriggers[i];
            if (st.triggerMode != StoryTriggerMode.Position) continue;
            Vector2 screenPos = WorldToScreen(new Vector2(st.positionX, 0), rect);
            if (Vector2.Distance(mousePos, screenPos) < 10f)
            {
                _selectedStoryTriggerIndex = i;
                _selectedElementId = null;
                _selectedGroupId = null;
                return true;
            }
        }

        // 检查组
        foreach (var g in _level.groups)
        {
            Vector2 screenPos = WorldToScreen(new Vector2(g.triggerPositionX, 0), rect);
            if (Mathf.Abs(mousePos.x - screenPos.x) < 8f && mousePos.y > rect.y + rect.height * 0.3f)
            {
                _selectedGroupId = g.groupId;
                _selectedElementId = null;
                _selectedStoryTriggerIndex = -1;
                return true;
            }
        }

        return false;
    }

    void ShowCanvasContextMenu(Vector2 mousePos, Rect rect)
    {
        var menu = new GenericMenu();
        Vector2 worldPos = ScreenToWorld(mousePos, rect);

        if (_scannedEnemies.Count > 0)
        {
            foreach (var entry in _scannedEnemies)
            {
                var capturedEntry = entry;
                var capturedPos = worldPos;
                menu.AddItem(new GUIContent($"放置敌人/{entry.displayName ?? entry.prefab.name}"), false, () =>
                {
                    var el = new LevelElement
                    {
                        elementId = Guid.NewGuid().ToString(),
                        displayName = capturedEntry.prefab.name,
                        elementType = ElementType.Enemy,
                        prefab = capturedEntry.prefab,
                        position = SnapToGrid(capturedPos)
                    };
                    Undo.RecordObject(_level, "Place Enemy");
                    _level.elements.Add(el);
                    EditorUtility.SetDirty(_level);
                    _selectedElementId = el.elementId;
                });
            }
        }

        if (!string.IsNullOrEmpty(_selectedElementId))
        {
            menu.AddItem(new GUIContent("复制选中元素"), false, () =>
            {
                var src = _level.elements.Find(x => x.elementId == _selectedElementId);
                if (src == null) return;
                var copy = new LevelElement
                {
                    elementId = Guid.NewGuid().ToString(),
                    displayName = src.displayName + "_Copy",
                    elementType = src.elementType,
                    prefab = src.prefab,
                    position = src.position + Vector2.right * 0.5f,
                    faceRight = src.faceRight,
                    appearDelay = src.appearDelay,
                    groupId = src.groupId,
                    appearConditions = new List<LevelVariableCondition>(src.appearConditions),
                    customParameters = new List<ElementCustomParameter>(src.customParameters)
                };
                Undo.RecordObject(_level, "Duplicate Element");
                _level.elements.Add(copy);
                _selectedElementId = copy.elementId;
                EditorUtility.SetDirty(_level);
            });
        }

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("添加过场触发器"), false, () =>
        {
            Undo.RecordObject(_level, "Add StoryTrigger");
            _level.storyTriggers.Add(new StoryTriggerPoint
            {
                triggerMode = StoryTriggerMode.Position,
                positionX = worldPos.x,
                storyId = GetStoryIds().FirstOrDefault() ?? ""
            });
            _selectedStoryTriggerIndex = _level.storyTriggers.Count - 1;
            _selectedElementId = null;
            _selectedGroupId = null;
            EditorUtility.SetDirty(_level);
        });
        menu.AddItem(new GUIContent("添加元素组"), false, () =>
        {
            Undo.RecordObject(_level, "Add Group");
            _level.groups.Add(new ElementGroup
            {
                groupId = Guid.NewGuid().ToString(),
                groupName = $"第{_level.groups.Count + 1}组",
                triggerPositionX = worldPos.x
            });
            EditorUtility.SetDirty(_level);
        });
        menu.ShowAsContext();
    }

    // ================================================================
    // 画布绘制
    // ================================================================

    Vector2 WorldToScreen(Vector2 world, Rect rect)
    {
        float screenX = rect.x + 50 - _canvasScroll.x + world.x * _canvasScale;
        float screenY = rect.y + rect.height * 0.55f - world.y * _canvasScale;
        return new Vector2(screenX, screenY);
    }

    Vector2 ScreenToWorld(Vector2 screen, Rect rect)
    {
        float worldX = (screen.x - rect.x - 50 + _canvasScroll.x) / _canvasScale;
        float worldY = (rect.y + rect.height * 0.55f - screen.y) / _canvasScale;
        return new Vector2(worldX, worldY);
    }

    float WorldToScreenX(float worldX, Rect rect)
    {
        return rect.x + 50 - _canvasScroll.x + worldX * _canvasScale;
    }

    float WorldToScreenY(float worldY, Rect rect)
    {
        return rect.y + rect.height * 0.55f - worldY * _canvasScale;
    }

    void DrawCanvasContent(Rect rect)
    {
        if (_level == null) return;

        GUI.BeginClip(rect);
        Rect local = new Rect(0, 0, rect.width, rect.height);
        float centerY = rect.height * 0.55f;

        DrawPreviewBackground(local);
        DrawPreviewFloor(local);
        DrawInitialCameraFrame(local);

        // 地面线
        Handles.color = new Color(0.25f, 0.45f, 0.25f);
        Handles.DrawLine(new Vector3(0, centerY), new Vector3(rect.width, centerY));
        DrawGridMarks(local);

        // X轴刻度
        DrawGridMarks(local);

        // 背景范围指示

        // 玩家起点
        DrawPlayerStart(local);

        // 终点
        DrawEndPoint(local);

        // 组触发线
        DrawGroupLines(local);

        // 过场触发器
        DrawStoryTriggers(local);

        // 元素
        DrawElements(local);

        GUI.EndClip();
    }

    void DrawPreviewBackground(Rect local)
    {
        Sprite sprite = GetPreviewBackgroundSprite();
        if (sprite == null || sprite.texture == null) return;

        Vector2 spriteSize = sprite.bounds.size;
        if (spriteSize.x <= 0f || spriteSize.y <= 0f) return;

        Vector2 viewMin = ScreenToWorld(new Vector2(0f, local.height), local);
        Vector2 viewMax = ScreenToWorld(new Vector2(local.width, 0f), local);
        float firstTileCenterX = Mathf.Floor((viewMin.x - spriteSize.x * 0.5f) / spriteSize.x) * spriteSize.x;
        float lastTileCenterX = viewMax.x + spriteSize.x;
        Rect uv = GetSpriteUv(sprite);

        Color oldColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.55f);
        for (float tileCenterX = firstTileCenterX; tileCenterX <= lastTileCenterX; tileCenterX += spriteSize.x)
        {
            Rect worldRect = new Rect(
                tileCenterX - spriteSize.x * 0.5f,
                -spriteSize.y * 0.5f,
                spriteSize.x,
                spriteSize.y);
            Rect screenRect = WorldRectToScreenRect(worldRect, local);
            GUI.DrawTextureWithTexCoords(screenRect, sprite.texture, uv, true);
        }
        GUI.color = oldColor;

        Rect levelRect = WorldRectToScreenRect(new Rect(0f, -spriteSize.y * 0.5f, _level.levelLength, spriteSize.y), local);
        Handles.DrawSolidRectangleWithOutline(levelRect, new Color(0.05f, 0.08f, 0.12f, 0.08f), new Color(0.35f, 0.45f, 0.6f, 0.35f));
    }

    Sprite GetPreviewBackgroundSprite()
    {
        var bg = _level.backgroundSettings;
        if (bg == null) return null;

        if (bg.mode == BackgroundMode.SingleInfiniteScroll)
            return bg.singleBackground;

        var layer = bg.parallaxLayers?
            .Where(x => x != null && x.sprite != null)
            .OrderBy(x => x.sortingOrder)
            .FirstOrDefault();
        return layer?.sprite;
    }

    Rect GetSpriteUv(Sprite sprite)
    {
        Texture2D texture = sprite.texture;
        try
        {
            Rect tr = sprite.textureRect;
            return new Rect(tr.x / texture.width, tr.y / texture.height, tr.width / texture.width, tr.height / texture.height);
        }
        catch
        {
            return new Rect(0f, 0f, 1f, 1f);
        }
    }

    void DrawPreviewFloor(Rect local)
    {
        if (!TryGetPreviewFloorBounds(out Bounds floorBounds)) return;

        Rect floorRect = WorldBoundsToScreenRect(floorBounds, local);
        Handles.DrawSolidRectangleWithOutline(
            floorRect,
            new Color(0.1f, 0.75f, 0.35f, 0.20f),
            new Color(0.2f, 0.95f, 0.5f, 0.85f));

        float topY = WorldToScreenY(floorBounds.max.y, local);
        Handles.color = new Color(0.6f, 1f, 0.7f, 0.9f);
        Handles.DrawAAPolyLine(2f, new Vector3(floorRect.xMin, topY), new Vector3(floorRect.xMax, topY));
        Handles.Label(new Vector3(Mathf.Max(4f, floorRect.xMin + 6f), topY + 4f), "Floor");
    }

    void DrawInitialCameraFrame(Rect local)
    {
        if (!_level.useCustomInitialCameraPosition) return;

        float orthographicSize = 5f;
        float aspect = 16f / 9f;
        Camera sceneCamera = Camera.main;
        if (sceneCamera != null)
        {
            orthographicSize = sceneCamera.orthographicSize;
            aspect = sceneCamera.aspect;
        }

        float height = orthographicSize * 2f;
        float width = height * aspect;
        Rect worldRect = new Rect(
            _level.initialCameraPosition.x - width * 0.5f,
            _level.initialCameraPosition.y - height * 0.5f,
            width,
            height);
        Rect screenRect = WorldRectToScreenRect(worldRect, local);

        Handles.DrawSolidRectangleWithOutline(
            screenRect,
            new Color(1f, 1f, 1f, 0.04f),
            new Color(1f, 1f, 1f, 0.7f));
        Handles.Label(new Vector3(screenRect.xMin + 6f, screenRect.yMin + 6f), "Initial Camera");
    }

    Rect WorldBoundsToScreenRect(Bounds bounds, Rect rect)
    {
        return WorldRectToScreenRect(new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y), rect);
    }

    Rect WorldRectToScreenRect(Rect worldRect, Rect rect)
    {
        float x = WorldToScreenX(worldRect.xMin, rect);
        float y = WorldToScreenY(worldRect.yMax, rect);
        float width = worldRect.width * _canvasScale;
        float height = worldRect.height * _canvasScale;
        return new Rect(x, y, width, height);
    }

    bool TryGetPreviewFloorBounds(out Bounds bounds)
    {
        double now = EditorApplication.timeSinceStartup;
        if (!_hasPreviewFloorBounds || now >= _nextPreviewFloorRefreshTime)
            RefreshPreviewFloorBounds();

        bounds = _previewFloorBounds;
        return _hasPreviewFloorBounds;
    }

    void RefreshPreviewFloorBounds()
    {
        _nextPreviewFloorRefreshTime = EditorApplication.timeSinceStartup + FloorBoundsRefreshInterval;
        if (TryGetFloorBoundsFromOpenScenes(out _previewFloorBounds) ||
            TryGetFloorBoundsFromTemplateScene(out _previewFloorBounds))
        {
            _hasPreviewFloorBounds = true;
            return;
        }

        _hasPreviewFloorBounds = false;
    }

    bool TryGetFloorBoundsFromOpenScenes(out Bounds bounds)
    {
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go == null || go.name != "Floor") continue;
            if (!go.scene.IsValid() || !go.scene.isLoaded) continue;
            if (EditorUtility.IsPersistent(go)) continue;

            var collider = go.GetComponent<BoxCollider2D>();
            if (collider == null) continue;

            bounds = collider.bounds;
            return true;
        }

        bounds = default;
        return false;
    }

    bool TryGetFloorBoundsFromTemplateScene(out Bounds bounds)
    {
        string fullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, TemplateScenePath);
        if (!File.Exists(fullPath))
        {
            bounds = default;
            return false;
        }

        string sceneText = File.ReadAllText(fullPath);
        var blocks = SceneYamlBlockRegex.Matches(sceneText)
            .Cast<Match>()
            .ToDictionary(
                m => m.Groups["id"].Value,
                m => new SceneYamlBlock
                {
                    Type = m.Groups["type"].Value,
                    Body = m.Groups["body"].Value
                });

        string floorGameObjectId = blocks
            .Where(x => x.Value.Type == "GameObject" && Regex.IsMatch(x.Value.Body, @"(?m)^\s*m_Name:\s*Floor\s*$"))
            .Select(x => x.Key)
            .FirstOrDefault();

        if (string.IsNullOrEmpty(floorGameObjectId) ||
            !TryFindSceneComponentBlock(blocks, floorGameObjectId, "Transform", out SceneYamlBlock transformBlock) ||
            !TryFindSceneComponentBlock(blocks, floorGameObjectId, "BoxCollider2D", out SceneYamlBlock colliderBlock) ||
            !TryParseVector3(transformBlock.Body, "m_LocalPosition", out Vector3 position) ||
            !TryParseVector3(transformBlock.Body, "m_LocalScale", out Vector3 scale) ||
            !TryParseVector2(colliderBlock.Body, "m_Offset", out Vector2 offset) ||
            !TryParseVector2(colliderBlock.Body, "m_Size", out Vector2 size))
        {
            bounds = default;
            return false;
        }

        Vector2 center = new Vector2(position.x + offset.x * scale.x, position.y + offset.y * scale.y);
        Vector2 worldSize = new Vector2(Mathf.Abs(size.x * scale.x), Mathf.Abs(size.y * scale.y));
        bounds = new Bounds(new Vector3(center.x, center.y, 0f), new Vector3(worldSize.x, worldSize.y, 0f));
        return worldSize.x > 0f && worldSize.y > 0f;
    }

    bool TryFindSceneComponentBlock(Dictionary<string, SceneYamlBlock> blocks, string gameObjectId, string componentType, out SceneYamlBlock block)
    {
        foreach (var id in Regex.Matches(blocks[gameObjectId].Body, @"- component: \{fileID: (?<id>-?\d+)\}")
            .Cast<Match>()
            .Select(m => m.Groups["id"].Value))
        {
            if (blocks.TryGetValue(id, out block) && block.Type == componentType)
                return true;
        }

        block = default;
        return false;
    }

    bool TryParseVector2(string body, string fieldName, out Vector2 value)
    {
        if (TryParseVector(body, fieldName, out float x, out float y, out _))
        {
            value = new Vector2(x, y);
            return true;
        }

        value = default;
        return false;
    }

    bool TryParseVector3(string body, string fieldName, out Vector3 value)
    {
        if (TryParseVector(body, fieldName, out float x, out float y, out float z))
        {
            value = new Vector3(x, y, z);
            return true;
        }

        value = default;
        return false;
    }

    bool TryParseVector(string body, string fieldName, out float x, out float y, out float z)
    {
        var match = Regex.Match(body, Regex.Escape(fieldName) + @": \{x: (?<x>[-+0-9.eE]+), y: (?<y>[-+0-9.eE]+)(?:, z: (?<z>[-+0-9.eE]+))?\}");
        if (match.Success &&
            float.TryParse(match.Groups["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
            float.TryParse(match.Groups["y"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out y))
        {
            if (!float.TryParse(match.Groups["z"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                z = 0f;
            return true;
        }

        x = y = z = 0f;
        return false;
    }

    struct SceneYamlBlock
    {
        public string Type;
        public string Body;
    }

    void DrawGridMarks(Rect local)
    {
        Handles.color = new Color(0.25f, 0.25f, 0.25f);
        float centerY = local.height * 0.55f;
        Rect rect = new Rect(0, 0, local.width, local.height);

        for (float x = 0; x <= _level.levelLength; x += 5f)
        {
            float sx = WorldToScreenX(x, rect);
            if (sx < 0 || sx > local.width) continue;

            Handles.DrawLine(new Vector3(sx, centerY - 5), new Vector3(sx, centerY + 5));
            if (x % 10 == 0)
                Handles.Label(new Vector3(sx - 8, centerY + 8), x.ToString());
        }
    }

    void DrawPlayerStart(Rect local)
    {
        Rect rect = new Rect(0, 0, local.width, local.height);
        Vector2 sp = WorldToScreen(_level.playerSpawnPosition, rect);
        if (sp.x < -20 || sp.x > local.width + 20) return;

        if (!DrawPrefabFootPreview(_level.playerPrefab, sp, _level.playerFaceRight, PlayerColor, false))
        {
            Handles.color = PlayerColor;
            Handles.DrawSolidDisc(new Vector3(sp.x, sp.y - 8f), Vector3.forward, 7);
        }

        DrawFootMarker(sp, PlayerColor, false);
        float dir = _level.playerFaceRight ? 1 : -1;
        Vector3 arrowTip = new Vector3(sp.x + dir * 10, sp.y);
        Handles.DrawAAPolyLine(2f, new Vector3(sp.x, sp.y), arrowTip);
        Handles.Label(new Vector3(sp.x + 10, sp.y - 10), "起点");
    }

    void DrawEndPoint(Rect local)
    {
        Rect rect = new Rect(0, 0, local.width, local.height);
        float sx = WorldToScreenX(_level.levelEndPositionX, rect);
        float centerY = local.height * 0.55f;
        if (sx < 0 || sx > local.width) return;

        Handles.color = EndColor;
        Handles.DrawSolidDisc(new Vector3(sx, centerY), Vector3.forward, 7);
        Handles.Label(new Vector3(sx + 10, centerY - 10), "终点");
    }

    void DrawGroupLines(Rect local)
    {
        Rect rect = new Rect(0, 0, local.width, local.height);
        foreach (var g in _level.groups)
        {
            float sx = WorldToScreenX(g.triggerPositionX, rect);
            if (sx < 0 || sx > local.width) continue;

            bool selected = _selectedGroupId == g.groupId;
            Handles.color = selected ? GroupLineColor : new Color(1f, 0.55f, 0.1f, 0.4f);
            Handles.DrawAAPolyLine(selected ? 2f : 1f,
                new Vector3(sx, 0), new Vector3(sx, local.height));
            Handles.Label(new Vector3(sx + 3, 10), $"组:{g.groupName}");
        }
    }

    void DrawStoryTriggers(Rect local)
    {
        Rect rect = new Rect(0, 0, local.width, local.height);
        for (int i = 0; i < _level.storyTriggers.Count; i++)
        {
            var st = _level.storyTriggers[i];
            if (st.triggerMode != StoryTriggerMode.Position) continue;
            float sx = WorldToScreenX(st.positionX, rect);
            if (sx < 0 || sx > local.width) continue;

            bool selected = _selectedStoryTriggerIndex == i;
            Handles.color = selected ? TriggerLineColor : new Color(0.3f, 0.6f, 1f, 0.5f);
            Handles.DrawAAPolyLine(selected ? 2f : 1f,
                new Vector3(sx, 0), new Vector3(sx, local.height));
            Handles.Label(new Vector3(sx + 3, 25), $"过场:{st.storyId}");
        }
    }

    void DrawElements(Rect local)
    {
        Rect rect = new Rect(0, 0, local.width, local.height);
        foreach (var el in _level.elements)
        {
            Vector2 sp = WorldToScreen(el.position, rect);
            if (sp.x < -20 || sp.x > local.width + 20) continue;

            bool selected = _selectedElementId == el.elementId;
            float size = selected ? 8f : 6f;

            Color c;
            switch (el.elementType)
            {
                case ElementType.Item: c = ItemColor; break;
                case ElementType.Obstacle: c = ObstacleColor; break;
                default: c = EnemyColor; break;
            }

            bool drewPrefab = DrawPrefabFootPreview(el.prefab, sp, el.faceRight, c, selected);
            if (!drewPrefab)
            {
                Vector2 symbolCenter = new Vector2(sp.x, sp.y - size - 2f);
                Handles.color = c;
                if (el.elementType == ElementType.Enemy)
                    DrawTriangle(symbolCenter, size, el.faceRight);
                else if (el.elementType == ElementType.Item)
                    DrawStar(symbolCenter, size);
                else
                    DrawSquare(symbolCenter, size);
            }

            DrawFootMarker(sp, c, selected);

            if (selected)
            {
                Handles.color = Color.white;
                Handles.DrawWireDisc(new Vector3(sp.x, sp.y), Vector3.forward, size + 4);
            }

            if (_canvasScale >= 5f)
            {
                var style = selected ? EditorStyles.whiteBoldLabel : EditorStyles.miniLabel;
                Handles.Label(new Vector3(sp.x + 10, sp.y + 5), el.displayName, style);
            }
        }
    }

    bool DrawPrefabFootPreview(GameObject prefab, Vector2 footScreen, bool faceRight, Color fallbackColor, bool selected)
    {
        if (prefab == null) return false;
        if (HasSpinePreview(prefab)) return false;

        Texture2D texture = AssetPreview.GetAssetPreview(prefab);
        if (texture == null)
            texture = AssetPreview.GetMiniThumbnail(prefab) as Texture2D;
        if (texture == null) return false;

        Vector2 worldSize = GetPrefabPreviewWorldSize(prefab);
        float width = Mathf.Clamp(worldSize.x * _canvasScale, 14f, 120f);
        float height = Mathf.Clamp(worldSize.y * _canvasScale, 18f, 140f);
        Rect drawRect = new Rect(footScreen.x - width * 0.5f, footScreen.y - height, width, height);

        Color oldColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, selected ? 1f : 0.9f);
        GUI.DrawTexture(drawRect, texture, ScaleMode.ScaleToFit, true);
        GUI.color = oldColor;

        if (selected)
            Handles.DrawSolidRectangleWithOutline(drawRect, Color.clear, Color.white);

        return true;
    }

    bool HasSpinePreview(GameObject prefab)
    {
        foreach (var behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null) continue;

            var type = behaviour.GetType();
            if (type.FullName != null && type.FullName.StartsWith("Spine.", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    Vector2 GetPrefabPreviewWorldSize(GameObject prefab)
    {
        Bounds bounds = default;
        bool hasBounds = false;

        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null) continue;
            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds = EncapsulateDefault(bounds, renderer.bounds);
            }
        }

        if (hasBounds)
            return ClampPreviewSize(bounds.size);

        foreach (var collider in prefab.GetComponentsInChildren<Collider2D>(true))
        {
            if (collider == null) continue;
            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds = EncapsulateDefault(bounds, collider.bounds);
            }
        }

        if (hasBounds)
            return ClampPreviewSize(bounds.size);

        return new Vector2(1f, 2f);
    }

    Bounds EncapsulateDefault(Bounds current, Bounds next)
    {
        current.Encapsulate(next);
        return current;
    }

    Vector2 ClampPreviewSize(Vector3 size)
    {
        float width = Mathf.Max(0.8f, Mathf.Abs(size.x));
        float height = Mathf.Max(1f, Mathf.Abs(size.y));
        return new Vector2(width, height);
    }

    void DrawFootMarker(Vector2 footScreen, Color color, bool selected)
    {
        Handles.color = selected ? Color.white : color;
        Handles.DrawSolidDisc(new Vector3(footScreen.x, footScreen.y), Vector3.forward, selected ? 4f : 3f);
        Handles.DrawAAPolyLine(1f,
            new Vector3(footScreen.x - 5f, footScreen.y),
            new Vector3(footScreen.x + 5f, footScreen.y));
    }

    void DrawTriangle(Vector2 center, float size, bool faceRight)
    {
        Vector3[] pts = new Vector3[3];
        if (faceRight)
        {
            pts[0] = new Vector3(center.x + size, center.y);
            pts[1] = new Vector3(center.x - size * 0.5f, center.y - size * 0.7f);
            pts[2] = new Vector3(center.x - size * 0.5f, center.y + size * 0.7f);
        }
        else
        {
            pts[0] = new Vector3(center.x - size, center.y);
            pts[1] = new Vector3(center.x + size * 0.5f, center.y - size * 0.7f);
            pts[2] = new Vector3(center.x + size * 0.5f, center.y + size * 0.7f);
        }
        Handles.DrawAAConvexPolygon(pts);
    }

    void DrawStar(Vector2 center, float size)
    {
        Vector3[] pts = new Vector3[5];
        for (int i = 0; i < 5; i++)
        {
            float angle = Mathf.Deg2Rad * (i * 72f - 90f);
            pts[i] = new Vector3(center.x + Mathf.Cos(angle) * size, center.y + Mathf.Sin(angle) * size);
        }
        Handles.DrawAAConvexPolygon(pts);
    }

    void DrawSquare(Vector2 center, float size)
    {
        Vector3[] pts = new Vector3[4];
        pts[0] = new Vector3(center.x - size, center.y - size);
        pts[1] = new Vector3(center.x + size, center.y - size);
        pts[2] = new Vector3(center.x + size, center.y + size);
        pts[3] = new Vector3(center.x - size, center.y + size);
        Handles.DrawAAConvexPolygon(pts);
    }

    // ================================================================
    // 右侧 — 属性面板
    // ================================================================

    void DrawPropertyPanel()
    {
        _propertyScroll = EditorGUILayout.BeginScrollView(_propertyScroll);

        if (!string.IsNullOrEmpty(_selectedElementId))
            DrawElementProperties();
        else if (_selectedStoryTriggerIndex >= 0 && _selectedStoryTriggerIndex < _level.storyTriggers.Count)
            DrawStoryTriggerProperties();
        else if (!string.IsNullOrEmpty(_selectedGroupId))
            DrawGroupProperties();
        else
            DrawLevelProperties();

        EditorGUILayout.EndScrollView();
    }

    void DrawLevelProperties()
    {
        EditorGUILayout.LabelField("关卡设置", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        Undo.RecordObject(_level, "Edit Level Properties");
        _level.levelName = EditorGUILayout.TextField("关卡名称", _level.levelName);
        _level.difficulty = EditorGUILayout.IntSlider("难度", _level.difficulty, 1, 5);
        _level.levelLength = EditorGUILayout.FloatField("关卡长度", _level.levelLength);
        _level.playerPrefab = (GameObject)EditorGUILayout.ObjectField("玩家预制体", _level.playerPrefab, typeof(GameObject), false);
        _level.playerSpawnPosition = EditorGUILayout.Vector2Field("玩家起点", _level.playerSpawnPosition);
        _level.playerFaceRight = EditorGUILayout.Toggle("朝向右边", _level.playerFaceRight);
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Camera Settings", EditorStyles.boldLabel);
        _level.useCustomInitialCameraPosition = EditorGUILayout.Toggle("Custom Initial Camera", _level.useCustomInitialCameraPosition);
        using (new EditorGUI.DisabledScope(!_level.useCustomInitialCameraPosition))
        {
            _level.initialCameraPosition = EditorGUILayout.Vector2Field("Initial Camera Pos", _level.initialCameraPosition);
        }
        _level.cameraDeadZone = EditorGUILayout.Slider("Camera Dead Zone", _level.cameraDeadZone, 0f, 0.45f);
        _level.levelEndPositionX = EditorGUILayout.FloatField("终点位置 X", _level.levelEndPositionX);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("通关条件", EditorStyles.boldLabel);
        DrawConditions(_level.endConditions);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("背景设置", EditorStyles.boldLabel);
        var bg = _level.backgroundSettings;
        bg.mode = (BackgroundMode)EditorGUILayout.EnumPopup("模式", bg.mode);
        if (bg.mode == BackgroundMode.SingleInfiniteScroll)
        {
            bg.singleBackground = (Sprite)EditorGUILayout.ObjectField("背景图", bg.singleBackground, typeof(Sprite), false);
            bg.singleParallaxFactor = EditorGUILayout.Slider("视差系数", bg.singleParallaxFactor, 0f, 1f);
            bg.singleSortingOrder = EditorGUILayout.IntField("排序层", bg.singleSortingOrder);
        }

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("关卡变量", EditorStyles.boldLabel);
        DrawVariables();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("剧情数据", EditorStyles.boldLabel);
        _level.storyCollectionJson = (TextAsset)EditorGUILayout.ObjectField("过场动画集 JSON", _level.storyCollectionJson, typeof(TextAsset), false);
        DrawStoryTriggerQuickPanel();

        EditorGUILayout.Space(8);
        if (GUILayout.Button("导出JSON", GUILayout.Height(22))) ExportJson();
        if (GUILayout.Button("导入JSON", GUILayout.Height(22))) ImportJson();

        if (GUI.changed) EditorUtility.SetDirty(_level);
    }

    void DrawVariables()
    {
        for (int i = 0; i < _level.variables.Count; i++)
        {
            var v = _level.variables[i];
            EditorGUILayout.BeginHorizontal();
            v.variableName = EditorGUILayout.TextField(v.variableName, GUILayout.Width(80));
            v.type = (LevelVariableType)EditorGUILayout.EnumPopup(v.type, GUILayout.Width(55));
            v.defaultValue = EditorGUILayout.TextField(v.defaultValue, GUILayout.Width(60));
            if (GUILayout.Button("X", GUILayout.Width(22)))
            {
                Undo.RecordObject(_level, "Remove Variable");
                _level.variables.RemoveAt(i);
                EditorUtility.SetDirty(_level);
                break;
            }
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("+ 添加变量"))
        {
            Undo.RecordObject(_level, "Add Variable");
            _level.variables.Add(new LevelVariableDefinition
            {
                variableName = $"var{_level.variables.Count + 1}",
                type = LevelVariableType.Bool,
                defaultValue = "false"
            });
            EditorUtility.SetDirty(_level);
        }
    }

    void DrawStoryTriggerQuickPanel()
    {
        var storyIds = GetStoryIds();
        if (_level.storyCollectionJson == null)
        {
            EditorGUILayout.HelpBox("先导入剧情编辑器导出的 JSON，再为其中的剧情添加触发器。", MessageType.Info);
            return;
        }

        if (storyIds.Count == 0)
        {
            EditorGUILayout.HelpBox("当前剧情 JSON 中没有可用的 storyId，或 JSON 解析失败。", MessageType.Warning);
            return;
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("剧情触发器", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("触发方式可选：位置触发、条件满足、关卡开始、关卡完成。选中触发器后可配置条件和剧情前后变量变化。", MessageType.Info);

        for (int i = 0; i < _level.storyTriggers.Count; i++)
        {
            var trigger = _level.storyTriggers[i];
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            string storyLabel = string.IsNullOrEmpty(trigger.storyId) ? "(未选择剧情)" : trigger.storyId;
            bool selected = _selectedStoryTriggerIndex == i;
            if (GUILayout.Toggle(selected, storyLabel, "Button", GUILayout.Width(120)))
            {
                _selectedStoryTriggerIndex = i;
                _selectedElementId = null;
                _selectedGroupId = null;
            }

            trigger.triggerMode = (StoryTriggerMode)EditorGUILayout.EnumPopup(trigger.triggerMode, GUILayout.Width(88));

            if (trigger.triggerMode == StoryTriggerMode.Position)
            {
                EditorGUILayout.LabelField("X", GUILayout.Width(16));
                trigger.positionX = EditorGUILayout.FloatField(trigger.positionX, GUILayout.Width(64));
            }
            else
            {
                GUILayout.Label(GetStoryTriggerModeLabel(trigger.triggerMode), EditorStyles.miniLabel, GUILayout.Width(74));
            }

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(trigger.triggerMode != StoryTriggerMode.Position))
            {
                if (GUILayout.Button("定位", GUILayout.Width(46)))
                {
                    _selectedStoryTriggerIndex = i;
                    _selectedElementId = null;
                    _selectedGroupId = null;
                    _canvasScroll.x = trigger.positionX * _canvasScale - 200f;
                    Repaint();
                }
            }

            if (GUILayout.Button("删除", GUILayout.Width(46)))
            {
                Undo.RecordObject(_level, "Remove Story Trigger");
                _level.storyTriggers.RemoveAt(i);
                _selectedStoryTriggerIndex = -1;
                EditorUtility.SetDirty(_level);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }

        if (GUILayout.Button("+ 为剧情添加位置触发器", GUILayout.Height(30)))
        {
            Undo.RecordObject(_level, "Add Story Trigger");
            _level.storyTriggers.Add(new StoryTriggerPoint
            {
                triggerMode = StoryTriggerMode.Position,
                storyId = storyIds[0],
                positionX = Mathf.Clamp(_level.playerSpawnPosition.x + 3f, 0f, _level.levelLength),
                triggerOnce = true,
                triggerFromLeft = true
            });
            _selectedStoryTriggerIndex = _level.storyTriggers.Count - 1;
            _selectedElementId = null;
            _selectedGroupId = null;
            EditorUtility.SetDirty(_level);
        }
    }

    string GetStoryTriggerModeLabel(StoryTriggerMode mode)
    {
        switch (mode)
        {
            case StoryTriggerMode.Position: return "位置";
            case StoryTriggerMode.Conditions: return "条件";
            case StoryTriggerMode.LevelStart: return "开始";
            case StoryTriggerMode.LevelComplete: return "完成";
            default: return mode.ToString();
        }
    }

    void ExportJson()
    {
        if (_level == null) return;
        string json = LevelDataJsonUtility.Export(_level);
        string path = EditorUtility.SaveFilePanel("导出关卡JSON", "", _level.levelName + ".json", "json");
        if (!string.IsNullOrEmpty(path))
            System.IO.File.WriteAllText(path, json);
    }

    void ImportJson()
    {
        if (_level == null) return;
        string path = EditorUtility.OpenFilePanel("导入关卡JSON", "", "json");
        if (string.IsNullOrEmpty(path)) return;
        string json = System.IO.File.ReadAllText(path);
        Undo.RecordObject(_level, "Import JSON");
        LevelDataJsonUtility.Import(_level, json);
        EditorUtility.SetDirty(_level);
    }

    void DrawElementProperties()
    {
        var el = _level.elements.Find(x => x.elementId == _selectedElementId);
        if (el == null) return;

        Undo.RecordObject(_level, "Edit Element");

        EditorGUILayout.LabelField("元素属性", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        el.displayName = EditorGUILayout.TextField("名称", el.displayName);
        el.elementType = (ElementType)EditorGUILayout.EnumPopup("类型", el.elementType);
        el.prefab = (GameObject)EditorGUILayout.ObjectField("预制体", el.prefab, typeof(GameObject), false);
        el.position = EditorGUILayout.Vector2Field("位置", el.position);
        el.faceRight = EditorGUILayout.Toggle("朝向右边", el.faceRight);
        el.appearDelay = EditorGUILayout.FloatField("出现延迟(s)", el.appearDelay);

        // 所属组
        var groupNames = new List<string> { "无" };
        foreach (var g in _level.groups) groupNames.Add(g.groupName);
        int currentGroupIdx = string.IsNullOrEmpty(el.groupId) ? 0 :
            groupNames.FindIndex(n => _level.groups.Any(g => g.groupName == n && g.groupId == el.groupId));
        if (currentGroupIdx < 0) currentGroupIdx = 0;
        int newGroupIdx = EditorGUILayout.Popup("所属组", currentGroupIdx, groupNames.ToArray());
        el.groupId = newGroupIdx > 0 ? _level.groups[newGroupIdx - 1].groupId : "";

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("出现条件", EditorStyles.boldLabel);
        DrawConditions(el.appearConditions);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("自定义参数", EditorStyles.boldLabel);
        DrawCustomParameters(el);

        EditorGUILayout.Space(10);
        if (GUILayout.Button("删除元素", GUILayout.Height(25)))
        {
            _level.elements.Remove(el);
            _selectedElementId = null;
        }

        if (GUI.changed) EditorUtility.SetDirty(_level);
    }

    void DrawStoryTriggerProperties()
    {
        var st = _level.storyTriggers[_selectedStoryTriggerIndex];
        Undo.RecordObject(_level, "Edit StoryTrigger");

        EditorGUILayout.LabelField("过场触发器", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        st.storyId = DrawStoryIdField("故事ID", st.storyId);
        st.triggerMode = (StoryTriggerMode)EditorGUILayout.EnumPopup("触发方式", st.triggerMode);
        st.triggerOnce = EditorGUILayout.Toggle("只触发一次", st.triggerOnce);

        switch (st.triggerMode)
        {
            case StoryTriggerMode.Position:
                st.positionX = EditorGUILayout.FloatField("触发位置 X", st.positionX);
                st.triggerFromLeft = EditorGUILayout.Toggle("从左向右触发", st.triggerFromLeft);
                EditorGUILayout.HelpBox("玩家越过触发位置 X 时播放剧情。下方触发条件可作为额外门槛。", MessageType.Info);
                break;
            case StoryTriggerMode.Conditions:
                EditorGUILayout.HelpBox("当下方触发条件全部满足时播放剧情。变量变化后会自动重新检查。", MessageType.Info);
                break;
            case StoryTriggerMode.LevelStart:
                EditorGUILayout.HelpBox("关卡构建完成后播放剧情。下方触发条件可作为额外门槛。", MessageType.Info);
                break;
            case StoryTriggerMode.LevelComplete:
                EditorGUILayout.HelpBox("关卡完成时播放剧情。下方触发条件可作为额外门槛。", MessageType.Info);
                break;
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField(st.triggerMode == StoryTriggerMode.Conditions ? "条件触发规则" : "附加触发条件", EditorStyles.boldLabel);
        DrawConditions(st.triggerConditions);

        EditorGUILayout.Space(10);
        DrawVariableSetActions(st.onStoryStartSetVariables, "剧情开始时设置变量");

        EditorGUILayout.Space(10);
        DrawVariableSetActions(st.onStoryCompleteSetVariables, "剧情结束时设置变量");

        EditorGUILayout.Space(10);
        if (GUILayout.Button("删除触发器", GUILayout.Height(25)))
        {
            _level.storyTriggers.RemoveAt(_selectedStoryTriggerIndex);
            _selectedStoryTriggerIndex = -1;
        }

        if (GUI.changed) EditorUtility.SetDirty(_level);
    }

    void DrawGroupProperties()
    {
        var g = _level.groups.Find(x => x.groupId == _selectedGroupId);
        if (g == null) return;

        Undo.RecordObject(_level, "Edit Group");

        EditorGUILayout.LabelField("元素组", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        g.groupName = EditorGUILayout.TextField("组名", g.groupName);
        g.triggerPositionX = EditorGUILayout.FloatField("触发位置 X", g.triggerPositionX);
        g.mustClearToProceed = EditorGUILayout.Toggle("必须清怪", g.mustClearToProceed);

        int groupElements = _level.elements.Count(e => e.groupId == g.groupId);
        EditorGUILayout.LabelField("组内元素数", groupElements.ToString());

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("触发条件", EditorStyles.boldLabel);
        DrawConditions(g.triggerConditions);

        EditorGUILayout.Space(10);
        if (GUILayout.Button("删除组", GUILayout.Height(25)))
        {
            _level.groups.Remove(g);
            _selectedGroupId = null;
        }

        if (GUI.changed) EditorUtility.SetDirty(_level);
    }

    // ================================================================
    // 通用条件/变量/参数编辑器
    // ================================================================

    void DrawConditions(List<LevelVariableCondition> conditions)
    {
        if (conditions == null) return;

        var varNames = new List<string>();
        foreach (var v in _level.variables) varNames.Add(v.variableName);

        for (int i = 0; i < conditions.Count; i++)
        {
            var c = conditions[i];
            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.BeginHorizontal();

            int varIdx = varNames.IndexOf(c.variableName);
            varIdx = EditorGUILayout.Popup(varIdx >= 0 ? varIdx : 0, varNames.ToArray(), GUILayout.Width(80));
            if (varNames.Count > 0 && varIdx >= 0)
                c.variableName = varNames[varIdx];

            c.mode = (LevelVariableCondition.CompareMode)EditorGUILayout.EnumPopup(c.mode, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            if (c.mode != LevelVariableCondition.CompareMode.IsTrue &&
                c.mode != LevelVariableCondition.CompareMode.IsFalse)
            {
                c.compareValue = EditorGUILayout.TextField("比较值", c.compareValue);
            }

            if (GUILayout.Button("删除条件", GUILayout.Height(18)))
            {
                conditions.RemoveAt(i);
                break;
            }
            EditorGUILayout.EndVertical();
        }

        if (varNames.Count == 0)
        {
            EditorGUILayout.HelpBox("先定义关卡变量才能添加条件", MessageType.Info);
        }
        else if (GUILayout.Button("+ 添加条件"))
        {
            conditions.Add(new LevelVariableCondition
            {
                variableName = varNames[0],
                mode = LevelVariableCondition.CompareMode.Equals,
                compareValue = ""
            });
        }
    }

    void DrawVariableSetActions(List<VariableSetAction> actions, string label)
    {
        if (actions == null) return;

        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        var varNames = new List<string>();
        foreach (var v in _level.variables) varNames.Add(v.variableName);

        for (int i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            EditorGUILayout.BeginHorizontal();
            int varIdx = varNames.IndexOf(a.variableName);
            varIdx = EditorGUILayout.Popup(varIdx >= 0 ? varIdx : 0, varNames.ToArray(), GUILayout.Width(80));
            if (varNames.Count > 0 && varIdx >= 0)
                a.variableName = varNames[varIdx];
            a.stringValue = EditorGUILayout.TextField(a.stringValue, GUILayout.Width(90));
            if (GUILayout.Button("X", GUILayout.Width(22)))
            {
                actions.RemoveAt(i);
                break;
            }
            EditorGUILayout.EndHorizontal();
        }

        if (varNames.Count == 0)
            EditorGUILayout.HelpBox("先定义关卡变量", MessageType.Info);
        else if (GUILayout.Button("+ 添加"))
            actions.Add(new VariableSetAction { variableName = varNames[0], stringValue = "" });
    }

    void DrawCustomParameters(LevelElement el)
    {
        if (el.prefab == null)
        {
            EditorGUILayout.HelpBox("先设置预制体才能编辑自定义参数", MessageType.Info);
            return;
        }

        // 简单列出可覆盖字段
        foreach (var comp in el.prefab.GetComponents<MonoBehaviour>())
        {
            if (comp == null) continue;
            var type = comp.GetType();
            var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var serialFields = type.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            foreach (var f in fields)
            {
                DrawParameterField(el, type, comp, f);
            }
        }

        EditorGUILayout.Space(5);
        if (GUILayout.Button("清除所有自定义参数"))
        {
            el.customParameters.Clear();
        }
    }

    void DrawParameterField(LevelElement el, System.Type type, MonoBehaviour comp, System.Reflection.FieldInfo field)
    {
        string typeName = type.FullName;
        string fieldName = field.Name;
        var existing = el.customParameters.Find(p => p.componentTypeName == typeName && p.fieldName == fieldName);
        bool overrideEnabled = existing != null;

        object currentValue = field.GetValue(comp);
        if (existing != null)
        {
            try
            {
                if (field.FieldType == typeof(int)) currentValue = int.Parse(existing.serializedValue);
                else if (field.FieldType == typeof(float)) currentValue = float.Parse(existing.serializedValue);
                else if (field.FieldType == typeof(bool)) currentValue = bool.Parse(existing.serializedValue);
                else if (field.FieldType == typeof(string)) currentValue = existing.serializedValue;
                else if (field.FieldType == typeof(Vector2))
                {
                    var parts = existing.serializedValue.Trim('(', ')').Split(',');
                    currentValue = new Vector2(float.Parse(parts[0]), float.Parse(parts[1]));
                }
                else if (field.FieldType == typeof(Vector3))
                {
                    var parts = existing.serializedValue.Trim('(', ')').Split(',');
                    currentValue = new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
                }
            }
            catch { }
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"{type.Name}.{fieldName}", GUILayout.Width(140));

        object newValue = currentValue;
        if (field.FieldType == typeof(int))
            newValue = EditorGUILayout.IntField((int)currentValue, GUILayout.Width(60));
        else if (field.FieldType == typeof(float))
            newValue = EditorGUILayout.FloatField((float)currentValue, GUILayout.Width(60));
        else if (field.FieldType == typeof(bool))
            newValue = EditorGUILayout.Toggle((bool)currentValue, GUILayout.Width(20));
        else if (field.FieldType == typeof(string))
            newValue = EditorGUILayout.TextField((string)currentValue, GUILayout.Width(60));
        else if (field.FieldType == typeof(Vector2))
            newValue = EditorGUILayout.Vector2Field("", (Vector2)currentValue, GUILayout.Width(90));
        else if (field.FieldType == typeof(Vector3))
            newValue = EditorGUILayout.Vector3Field("", (Vector3)currentValue, GUILayout.Width(120));
        else
            EditorGUILayout.LabelField("不支持", GUILayout.Width(60));

        bool newOverride = GUILayout.Toggle(overrideEnabled, "覆盖", GUILayout.Width(40));
        EditorGUILayout.EndHorizontal();

        if (newOverride && !overrideEnabled)
        {
            el.customParameters.Add(new ElementCustomParameter
            {
                componentTypeName = typeName,
                fieldName = fieldName,
                valueTypeName = field.FieldType.FullName,
                serializedValue = SerializeParameterValue(newValue)
            });
        }
        else if (overrideEnabled && !newOverride)
        {
            el.customParameters.Remove(existing);
        }
        else if (overrideEnabled && !Equals(newValue, currentValue))
        {
            existing.serializedValue = SerializeParameterValue(newValue);
        }
    }

    string SerializeParameterValue(object value)
    {
        if (value == null) return "";
        if (value is Vector2 v2) return $"{v2.x},{v2.y}";
        if (value is Vector3 v3) return $"{v3.x},{v3.y},{v3.z}";
        return value.ToString();
    }

    string DrawStoryIdField(string label, string current)
    {
        var ids = GetStoryIds();
        if (ids.Count == 0)
            return EditorGUILayout.TextField(label, current);

        if (!ids.Contains(current))
            ids.Insert(0, string.IsNullOrEmpty(current) ? "" : current);

        int idx = Mathf.Max(0, ids.IndexOf(current));
        idx = EditorGUILayout.Popup(label, idx, ids.ToArray());
        return ids[idx];
    }

    List<string> GetStoryIds()
    {
        var ids = new List<string>();
        if (_level == null || _level.storyCollectionJson == null)
            return ids;

        try
        {
            var data = JsonUtility.FromJson<StoryDataCollection>(_level.storyCollectionJson.text);
            if (data?.stories != null)
                ids.AddRange(data.stories.Where(s => s != null && !string.IsNullOrEmpty(s.storyId)).Select(s => s.storyId));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LevelEditorWindow] 剧情 JSON 解析失败: {e.Message}");
        }
        return ids;
    }

    // ================================================================
    // 底部面板
    // ================================================================

    void DrawBottomPanel()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        _showBottomList = GUILayout.Toggle(_showBottomList, "列表视图", EditorStyles.toolbarButton, GUILayout.Width(80));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        if (!_showBottomList) return;

        _bottomScroll = EditorGUILayout.BeginScrollView(_bottomScroll, GUILayout.Height(120));
        EditorGUILayout.BeginHorizontal(GUI.skin.box);
        GUILayout.Label("#", GUILayout.Width(30));
        GUILayout.Label("名称", GUILayout.Width(100));
        GUILayout.Label("类型", GUILayout.Width(50));
        GUILayout.Label("位置X", GUILayout.Width(55));
        GUILayout.Label("位置Y", GUILayout.Width(55));
        GUILayout.Label("延迟", GUILayout.Width(45));
        GUILayout.Label("组", GUILayout.Width(80));
        GUILayout.Label("条件", GUILayout.Width(50));
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < _level.elements.Count; i++)
        {
            var el = _level.elements[i];
            var bg = i % 2 == 0 ? GUI.skin.box : GUIStyle.none;
            EditorGUILayout.BeginHorizontal(bg);

            if (GUILayout.Button(i.ToString(), GUILayout.Width(30)))
            {
                _selectedElementId = el.elementId;
                _selectedGroupId = null;
                _selectedStoryTriggerIndex = -1;
            }
            GUILayout.Label(el.displayName, GUILayout.Width(100));
            GUILayout.Label(el.elementType.ToString(), GUILayout.Width(50));
            GUILayout.Label(el.position.x.ToString("F1"), GUILayout.Width(55));
            GUILayout.Label(el.position.y.ToString("F1"), GUILayout.Width(55));
            GUILayout.Label($"{el.appearDelay:F1}s", GUILayout.Width(45));
            var group = _level.FindGroup(el.groupId);
            GUILayout.Label(group?.groupName ?? "无", GUILayout.Width(80));
            GUILayout.Label(el.appearConditions.Count > 0 ? "有" : "无", GUILayout.Width(50));

            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    // ================================================================
    // 快捷键
    // ================================================================

    void HandleKeyboardShortcuts()
    {
        Event e = Event.current;
        if (e.type != EventType.KeyDown) return;

        if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
        {
            if (!string.IsNullOrEmpty(_selectedElementId))
            {
                var el = _level.elements.Find(x => x.elementId == _selectedElementId);
                if (el != null)
                {
                    Undo.RecordObject(_level, "Delete Element");
                    _level.elements.Remove(el);
                    _selectedElementId = null;
                    EditorUtility.SetDirty(_level);
                    e.Use();
                    Repaint();
                }
            }
        }

        if (e.control && e.keyCode == KeyCode.S)
        {
            SaveLevel();
            e.Use();
        }
    }

    // ================================================================
    // 关卡操作
    // ================================================================

    void CreateNewLevel()
    {
        string path = EditorUtility.SaveFilePanelInProject("创建新关卡", "NewLevel", "asset", "选择保存位置", "Assets/LevelData");
        if (string.IsNullOrEmpty(path)) return;

        var newLevel = ScriptableObject.CreateInstance<LevelData>();
        newLevel.levelName = System.IO.Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(newLevel, path);
        AssetDatabase.SaveAssets();
        _level = newLevel;
        _selectedElementId = null;
        _selectedGroupId = null;
        ScanPrefabs();
    }

    void OpenLevel()
    {
        string path = EditorUtility.OpenFilePanel("打开关卡", "Assets/LevelData", "asset");
        if (string.IsNullOrEmpty(path)) return;
        if (path.StartsWith(Application.dataPath))
            path = "Assets" + path.Substring(Application.dataPath.Length);

        var level = AssetDatabase.LoadAssetAtPath<LevelData>(path);
        if (level != null)
        {
            _level = level;
            _selectedElementId = null;
            _selectedGroupId = null;
            ScanPrefabs();
        }
        else
        {
            EditorUtility.DisplayDialog("错误", "无法加载关卡文件", "确定");
        }
    }

    void SaveLevel()
    {
        if (_level == null) return;
        EditorUtility.SetDirty(_level);
        AssetDatabase.SaveAssets();
        Debug.Log($"关卡 '{_level.levelName}' 已保存");
    }

    void ValidateLevel()
    {
        if (_level == null) return;
        var result = _level.Validate();
        if (result.isValid)
        {
            string msg = "关卡校验通过!";
            if (result.warnings.Count > 0)
                msg += $"\n\n警告 ({result.warnings.Count}):\n" + string.Join("\n", result.warnings);
            EditorUtility.DisplayDialog("校验结果", msg, "确定");
        }
        else
        {
            string msg = $"关卡校验失败!\n\n错误 ({result.errors.Count}):\n" + string.Join("\n", result.errors);
            if (result.warnings.Count > 0)
                msg += $"\n\n警告 ({result.warnings.Count}):\n" + string.Join("\n", result.warnings);
            EditorUtility.DisplayDialog("校验结果", msg, "确定");
        }
    }

    void RemoveExistingLevelSceneBuilders()
    {
        var existing = GameObject.FindObjectsOfType<LevelSceneBuilder>();
        foreach (var b in existing)
        {
            if (b != null && b.gameObject != null)
            {
                DestroyImmediate(b);
                Debug.Log("[LevelEditorWindow] 清理模板中残留的 LevelSceneBuilder");
            }
        }
    }

    void PlayTest()
    {
        if (_level == null) return;

        var result = _level.Validate();
        if (!result.isValid)
        {
            string msg = $"关卡校验失败，无法 Play 测试:\n\n" + string.Join("\n", result.errors);
            EditorUtility.DisplayDialog("校验失败", msg, "确定");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EditorUtility.SetDirty(_level);
        AssetDatabase.SaveAssets();

        string templatePath = "Assets/Scenes/LevelEditorTestTemplate.unity";
        var templateScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(templatePath);
        if (templateScene == null)
        {
            EditorUtility.DisplayDialog("错误", $"找不到模板场景: {templatePath}", "确定");
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.OpenScene(templatePath, OpenSceneMode.Additive);
        var templateSceneObj = SceneManager.GetSceneByPath(templatePath);
        SceneManager.MergeScenes(templateSceneObj, scene);

        RemoveExistingLevelSceneBuilders();

        var builderGo = new GameObject("LevelSceneBuilder");
        var builder = builderGo.AddComponent<LevelSceneBuilder>();
        builder.levelData = _level;

        SessionState.SetBool(RestoreAfterPlayKey, true);
        SessionState.SetString(RestoreLevelPathKey, AssetDatabase.GetAssetPath(_level));
        FocusGameView();
        Close();

        EditorApplication.EnterPlaymode();
    }

    static void FocusGameView()
    {
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gameViewType != null)
            EditorWindow.GetWindow(gameViewType).Focus();
    }

    void ExportScene()
    {
        if (_level == null) return;

        var result = _level.Validate();
        if (!result.isValid)
        {
            string msg = $"关卡校验失败，无法导出 Scene:\n\n" + string.Join("\n", result.errors);
            EditorUtility.DisplayDialog("校验失败", msg, "确定");
            return;
        }

        string path = EditorUtility.SaveFilePanelInProject("导出Scene", _level.levelName, "unity", "选择保存位置", "Assets/Scenes");
        if (string.IsNullOrEmpty(path)) return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        string templatePath = "Assets/Scenes/LevelEditorTestTemplate.unity";
        var exportedScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.OpenScene(templatePath, OpenSceneMode.Additive);
        var templateSceneObj = SceneManager.GetSceneByPath(templatePath);
        SceneManager.MergeScenes(templateSceneObj, exportedScene);

        RemoveExistingLevelSceneBuilders();

        var builderGo = new GameObject("LevelSceneBuilder");
        var builder = builderGo.AddComponent<LevelSceneBuilder>();
        builder.levelData = _level;

        EditorSceneManager.SaveScene(exportedScene, path);
        Debug.Log($"关卡已导出到: {path}");

        EditorSceneManager.OpenScene(templatePath, OpenSceneMode.Single);
    }

    // ================================================================
    // 预制体扫描
    // ================================================================

    void ScanPrefabs()
    {
        _scannedEnemies.Clear();
        _scannedItems.Clear();
        _scannedObstacles.Clear();

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            if (prefab.GetComponent<Enemy>() != null)
                _scannedEnemies.Add(new ElementPrefabEntry { displayName = prefab.name, prefab = prefab });
            if (prefab.GetComponent<ItemPickup>() != null)
                _scannedItems.Add(new ElementPrefabEntry { displayName = prefab.name, prefab = prefab });
            if (prefab.GetComponent<Obstacle>() != null)
                _scannedObstacles.Add(new ElementPrefabEntry { displayName = prefab.name, prefab = prefab });
        }
    }

    // ================================================================
    // Scene View 联动 (可选)
    // ================================================================

    void OnSceneGUI(SceneView sceneView)
    {
        if (_level == null) return;
        // 可在Scene视图绘制关卡元素位置指示
        foreach (var el in _level.elements)
        {
            Handles.color = el.elementType == ElementType.Enemy ? EnemyColor :
                el.elementType == ElementType.Item ? ItemColor : ObstacleColor;
            Handles.DrawSolidDisc(new Vector3(el.position.x, el.position.y, 0), Vector3.forward, 0.3f);
        }
    }
}
