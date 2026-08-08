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
using Spine.Unity;

[InitializeOnLoad]
public class LevelEditorWindow : EditorWindow
{
    private enum InspectorSection
    {
        Overview,
        Background,
        Variables,
        Events
    }

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
    private string _selectedEnvironmentActorId;
    private string _selectedGroupId;
    private int _selectedStoryTriggerIndex = -1;
    private int _selectedFlowIndex = -1;
    private InspectorSection _inspectorSection = InspectorSection.Overview;
    private bool _showBottomList = true;
    private string _elementSearch = "";

    // ========== 画布 ==========
    private float _canvasScale = 8f;
    private Vector2 _canvasScroll = Vector2.zero;
    private bool _isDraggingElement;
    private Vector2 _dragOffset;
    private bool _isPanning;
    private Vector2 _panStartMouse;
    private Vector2 _panStartScroll;
    private Rect _lastCanvasRect;
    private const string TemplateScenePath = "Assets/Scenes/LevelEditorTestTemplate.unity";
    private const double FloorBoundsRefreshInterval = 1.0;
    private bool _hasPreviewFloorBounds;
    private Bounds _previewFloorBounds;
    private double _nextPreviewFloorRefreshTime;

    // ========== 预制体库 ==========
    private List<ElementPrefabEntry> _scannedEnemies = new List<ElementPrefabEntry>();
    private List<ElementPrefabEntry> _scannedItems = new List<ElementPrefabEntry>();
    private List<ElementPrefabEntry> _scannedObstacles = new List<ElementPrefabEntry>();
    private List<ElementPrefabEntry> _scannedEnvironmentActors = new List<ElementPrefabEntry>();

    // ========== 元素放置模式 ==========
    private ElementType _placingType;
    private GameObject _placingPrefab;
    private bool _isPlacingMode;
    private bool _placingEnvironmentActor;
    private GameObject _environmentLibraryCandidate;
    private float _backgroundPreviewCameraX;
    private float _backgroundPreviewTime;

    // ========== 颜色 ==========
    private static readonly Color EnemyColor = new Color(1f, 0.35f, 0.3f);
    private static readonly Color ItemColor = new Color(1f, 0.85f, 0.2f);
    private static readonly Color ObstacleColor = new Color(0.5f, 0.5f, 0.5f);
    private static readonly Color EnvironmentActorColor = new Color(0.25f, 0.9f, 1f);
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

        if (_level.MigrateLegacyStoryTriggers())
        {
            ClearObjectSelection();
            _inspectorSection = InspectorSection.Events;
            EditorUtility.SetDirty(_level);
        }
        if (_level.MigrateLegacyBackground())
            EditorUtility.SetDirty(_level);

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
        EditorGUILayout.BeginVertical(GUILayout.Width(380));
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
            if (_level != null && EditorUtility.DisplayDialog("清空关卡", "将删除所有元素、组、变量和演出事件，确定继续？", "清空", "取消"))
            {
                Undo.RecordObject(_level, "Clear Level");
                _level.elements.Clear();
                _level.groups.Clear();
                _level.storyTriggers.Clear();
                _level.events?.Clear();
                _level.variables.Clear();
                ShowSection(InspectorSection.Overview);
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
            GUILayout.Label($"演出: {_level.events?.Count ?? 0}", EditorStyles.toolbarButton);
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
        DrawEditorNavigation();
        EditorGUILayout.Space(8);

        _paletteScroll = EditorGUILayout.BeginScrollView(_paletteScroll);

        DrawPaletteCategory("敌人", ElementType.Enemy, _scannedEnemies, ref _level.enemyPrefabLibrary);
        DrawPaletteCategory("道具", ElementType.Item, _scannedItems, ref _level.itemPrefabLibrary);
        DrawPaletteCategory("障碍物", ElementType.Obstacle, _scannedObstacles, ref _level.obstaclePrefabLibrary);
        DrawEnvironmentActorPalette();

        GUILayout.Space(10);
        DrawGroupManagementPanel();

        GUILayout.Space(10);
        if (GUILayout.Button("扫描预制体"))
        {
            ScanPrefabs();
            Repaint();
        }

        EditorGUILayout.EndScrollView();
    }

    void DrawEditorNavigation()
    {
        EditorGUILayout.LabelField("编辑导航", EditorStyles.boldLabel);
        DrawNavigationButton("关卡概览", InspectorSection.Overview);
        DrawNavigationButton("背景", InspectorSection.Background);
        DrawNavigationButton($"变量 ({_level.variables.Count})", InspectorSection.Variables);
        DrawNavigationButton($"演出事件 ({(_level.events?.Count ?? 0)})", InspectorSection.Events);
    }

    void DrawNavigationButton(string label, InspectorSection section)
    {
        bool active = !HasObjectSelection() && _inspectorSection == section;
        if (GUILayout.Toggle(active, label, "Button") && !active)
            ShowSection(section);
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
                _placingEnvironmentActor = false;
                _placingType = type;
                _placingPrefab = entry.prefab;
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.BeginHorizontal();
        _environmentLibraryCandidate = (GameObject)EditorGUILayout.ObjectField(
            _environmentLibraryCandidate, typeof(GameObject), false);
        using (new EditorGUI.DisabledScope(_environmentLibraryCandidate == null))
        {
            if (GUILayout.Button("加入", GUILayout.Width(38)))
            {
                Undo.RecordObject(_level, "Add Environment Actor Prefab");
                _level.environmentActorPrefabLibrary.Add(new ElementPrefabEntry
                {
                    displayName = _environmentLibraryCandidate.name,
                    prefab = _environmentLibraryCandidate
                });
                _environmentLibraryCandidate = null;
                EditorUtility.SetDirty(_level);
            }
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(5);
    }

    void DrawEnvironmentActorPalette()
    {
        GUILayout.Label("环境角色", EditorStyles.boldLabel);
        var entries = _level.environmentActorPrefabLibrary.Count > 0
            ? _level.environmentActorPrefabLibrary
            : _scannedEnvironmentActors;
        foreach (var entry in entries)
        {
            if (entry?.prefab == null) continue;
            EditorGUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(entry.displayName ?? entry.prefab.name, GUILayout.Width(110));
            if (GUILayout.Button("+", GUILayout.Width(22)))
            {
                _isPlacingMode = true;
                _placingEnvironmentActor = true;
                _placingPrefab = entry.prefab;
            }
            EditorGUILayout.EndHorizontal();
        }
        GUILayout.Space(5);
    }

    void DrawGroupManagementPanel()
    {
        GUILayout.Label("元素组", EditorStyles.boldLabel);
        foreach (var group in _level.groups)
        {
            EditorGUILayout.BeginHorizontal(GUI.skin.box);
            bool selected = _selectedGroupId == group.groupId;
            if (GUILayout.Toggle(selected, group.groupName, "Button") && !selected)
                SelectGroup(group.groupId);
            EditorGUILayout.EndHorizontal();
        }

        if (GUILayout.Button("+ 新建元素组"))
        {
            Undo.RecordObject(_level, "Add Group");
            var group = new ElementGroup
            {
                groupId = Guid.NewGuid().ToString(),
                groupName = $"第{_level.groups.Count + 1}组",
                triggerMode = ElementGroupTriggerMode.None,
                triggerPositionX = Mathf.Clamp(_level.playerSpawnPosition.x + 3f, 0f, _level.levelLength)
            };
            _level.groups.Add(group);
            SelectGroup(group.groupId);
            EditorUtility.SetDirty(_level);
        }
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
        if (GUILayout.Button("适应关卡", GUILayout.Width(68))) FitCanvasToLevel();
        if (GUILayout.Button("回到起点", GUILayout.Width(68))) CenterCanvasOn(_level.playerSpawnPosition);
        EditorGUILayout.EndHorizontal();

        // 图例
        EditorGUILayout.BeginHorizontal();
        DrawLegendItem(PlayerColor, "玩家起点");
        DrawLegendItem(EndColor, "终点");
        DrawLegendItem(TriggerLineColor, "过场");
        DrawLegendItem(new Color(0.75f, 0.35f, 1f), "演出事件");
        DrawLegendItem(EnemyColor, "敌人");
        DrawLegendItem(ItemColor, "道具");
        DrawLegendItem(ObstacleColor, "障碍物");
        DrawLegendItem(EnvironmentActorColor, "环境角色");
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField("鼠标滚轮缩放 · 中键或 Alt+左键拖动 · 右键打开添加菜单 · Esc 返回上一级", EditorStyles.miniLabel);

        Rect canvasRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
            GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        _lastCanvasRect = canvasRect;
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
            Vector2 worldUnderMouse = ScreenToWorld(e.mousePosition, rect);
            float oldScale = _canvasScale;
            _canvasScale = Mathf.Clamp(_canvasScale - e.delta.y * 0.3f, 3f, 15f);
            _canvasScroll.x += worldUnderMouse.x * (_canvasScale - oldScale);
            _canvasScroll.y -= worldUnderMouse.y * (_canvasScale - oldScale);
            e.Use();
            Repaint();
            return;
        }

        bool panGesture = e.button == 2 || (e.button == 0 && e.alt);
        if (e.type == EventType.MouseDown && panGesture)
        {
            _isPanning = true;
            _panStartMouse = e.mousePosition;
            _panStartScroll = _canvasScroll;
            e.Use();
            return;
        }

        if (_isPanning && e.type == EventType.MouseDrag && (e.button == 2 || e.button == 0))
        {
            _canvasScroll = _panStartScroll + (_panStartMouse - e.mousePosition);
            e.Use();
            Repaint();
            return;
        }

        if (_isPanning && e.type == EventType.MouseUp && (e.button == 2 || e.button == 0))
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
                if (_placingEnvironmentActor) PlaceEnvironmentActor(worldPos);
                else PlaceElement(worldPos);
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

            ClearObjectSelection();
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
        else if (Event.current.type == EventType.MouseDrag && e.button == 0 &&
                 !string.IsNullOrEmpty(_selectedEnvironmentActorId))
        {
            var actor = _level.environmentActors.Find(x => x.actorId == _selectedEnvironmentActorId);
            if (actor != null)
            {
                Undo.RecordObject(_level, "Move Environment Actor");
                actor.position = SnapToGrid(ScreenToWorld(e.mousePosition, rect));
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
        SelectElement(el.elementId);
        EditorUtility.SetDirty(_level);
    }

    void PlaceEnvironmentActor(Vector2 worldPos)
    {
        Undo.RecordObject(_level, "Place Environment Actor");
        var actor = new EnvironmentActorData
        {
            actorId = Guid.NewGuid().ToString(),
            displayName = _placingPrefab != null ? _placingPrefab.name : "Environment Actor",
            prefab = _placingPrefab,
            position = SnapToGrid(worldPos)
        };
        _level.environmentActors.Add(actor);
        SelectEnvironmentActor(actor.actorId);
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

        foreach (var actor in _level.environmentActors ?? new List<EnvironmentActorData>())
        {
            if (actor == null) continue;
            float dist = Vector2.Distance(mousePos, WorldToScreen(actor.position, rect));
            if (dist < minDist)
            {
                minDist = dist;
                closest = null;
                SelectEnvironmentActor(actor.actorId);
                return true;
            }
        }

        if (closest != null)
        {
            SelectElement(closest.elementId);
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
                SelectStoryTrigger(i);
                return true;
            }
        }

        if (_level.events != null)
        {
            for (int i = 0; i < _level.events.Count; i++)
            {
                var flow = _level.events[i];
                if (flow == null) continue;
                if (flow.triggerMode == StoryTriggerMode.Position)
                {
                    Vector2 triggerScreen = WorldToScreen(new Vector2(flow.positionX, 0f), rect);
                    if (Mathf.Abs(mousePos.x - triggerScreen.x) < 8f)
                    {
                        SelectFlow(i);
                        return true;
                    }
                }
                if (flow.steps == null) continue;
                foreach (var step in flow.steps)
                {
                    if (step == null || (step.stepType != LevelFlowStepType.MovePlayer && step.stepType != LevelFlowStepType.MoveCamera)) continue;
                    if (Vector2.Distance(mousePos, WorldToScreen(step.targetPosition, rect)) < 12f)
                    {
                        SelectFlow(i);
                        return true;
                    }
                }
            }
        }

        // 检查组
        foreach (var g in _level.groups)
        {
            if (g.triggerMode != ElementGroupTriggerMode.Position) continue;
            Vector2 screenPos = WorldToScreen(new Vector2(g.triggerPositionX, 0), rect);
            if (Mathf.Abs(mousePos.x - screenPos.x) < 8f && mousePos.y > rect.y + rect.height * 0.3f)
            {
                SelectGroup(g.groupId);
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
                    SelectElement(el.elementId);
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
                    groupIds = new List<string>(src.groupIds ?? new List<string>()),
                    appearConditions = new List<LevelVariableCondition>(src.appearConditions),
                    customParameters = new List<ElementCustomParameter>(src.customParameters)
                };
                Undo.RecordObject(_level, "Duplicate Element");
                _level.elements.Add(copy);
                SelectElement(copy.elementId);
                EditorUtility.SetDirty(_level);
            });
        }

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("添加演出事件/直接播放过场"), false, () =>
        {
            if (_level.events == null) _level.events = new List<LevelFlowData>();
            Undo.RecordObject(_level, "Add Performance Event");
            _level.events.Add(new LevelFlowData
            {
                triggerMode = StoryTriggerMode.Position,
                positionX = worldPos.x,
                flowId = GetUniqueFlowId("event_story"),
                steps = new List<LevelFlowStep>
                {
                    new LevelFlowStep
                    {
                        stepType = LevelFlowStepType.PlayStory,
                        storyId = GetStoryIds().FirstOrDefault() ?? ""
                    }
                }
            });
            SelectFlow(_level.events.Count - 1);
            EditorUtility.SetDirty(_level);
        });
        menu.AddItem(new GUIContent("添加元素组"), false, () =>
        {
            Undo.RecordObject(_level, "Add Group");
            _level.groups.Add(new ElementGroup
            {
                groupId = Guid.NewGuid().ToString(),
                groupName = $"第{_level.groups.Count + 1}组",
                triggerMode = ElementGroupTriggerMode.Position,
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
        float screenY = rect.y + rect.height * 0.55f - _canvasScroll.y - world.y * _canvasScale;
        return new Vector2(screenX, screenY);
    }

    Vector2 ScreenToWorld(Vector2 screen, Rect rect)
    {
        float worldX = (screen.x - rect.x - 50 + _canvasScroll.x) / _canvasScale;
        float worldY = (rect.y + rect.height * 0.55f - _canvasScroll.y - screen.y) / _canvasScale;
        return new Vector2(worldX, worldY);
    }

    float WorldToScreenX(float worldX, Rect rect)
    {
        return rect.x + 50 - _canvasScroll.x + worldX * _canvasScale;
    }

    float WorldToScreenY(float worldY, Rect rect)
    {
        return rect.y + rect.height * 0.55f - _canvasScroll.y - worldY * _canvasScale;
    }

    void CenterCanvasOn(Vector2 worldPosition)
    {
        float width = _lastCanvasRect.width > 1f ? _lastCanvasRect.width : 600f;
        float height = _lastCanvasRect.height > 1f ? _lastCanvasRect.height : 400f;
        _canvasScroll.x = 50f + worldPosition.x * _canvasScale - width * 0.5f;
        _canvasScroll.y = height * 0.05f - worldPosition.y * _canvasScale;
        Repaint();
    }

    void FitCanvasToLevel()
    {
        float width = _lastCanvasRect.width > 1f ? _lastCanvasRect.width : 600f;
        float usableWidth = Mathf.Max(100f, width - 100f);
        _canvasScale = Mathf.Clamp(usableWidth / Mathf.Max(1f, _level.levelLength), 3f, 15f);
        _canvasScroll = new Vector2(0f, (_lastCanvasRect.height > 1f ? _lastCanvasRect.height : 400f) * 0.05f);
        Repaint();
    }

    void DrawCanvasContent(Rect rect)
    {
        if (_level == null) return;

        GUI.BeginClip(rect);
        Rect local = new Rect(0, 0, rect.width, rect.height);
        float centerY = rect.height * 0.55f;

        DrawPreviewBackground(local, false);
        DrawPreviewFloor(local);
        DrawCameraBounds(local);
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
        DrawFlowTargets(local);

        // 元素
        DrawElements(local);
        DrawEnvironmentActors(local);
        DrawPreviewBackground(local, true);

        GUI.EndClip();
    }

    void DrawPreviewBackground(Rect local, bool nearOnly)
    {
        var backgroundSettings = _level.backgroundSettings;
        if (backgroundSettings?.layers != null && backgroundSettings.dataVersion >= BackgroundSettings.CurrentDataVersion)
        {
            foreach (var layer in backgroundSettings.layers
                         .Where(item => item != null && (item.depthBand == BackgroundDepthBand.Near) == nearOnly)
                         .OrderBy(item => item.SortingOrder))
                DrawPreviewBackgroundLayer(local, layer);
            return;
        }
        if (nearOnly) return;
        if (backgroundSettings != null &&
            backgroundSettings.mode == BackgroundMode.SequentialTiles)
        {
            DrawPreviewBackgroundSequence(local, backgroundSettings);
            return;
        }

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

    void DrawPreviewBackgroundLayer(Rect local, BackgroundLayerData layer)
    {
        float initialCameraX = _level.useCustomInitialCameraPosition ? _level.initialCameraPosition.x : 0f;
        float cameraDelta = _backgroundPreviewCameraX - initialCameraX;
        Vector2 offset = new Vector2(cameraDelta * (1f - layer.MotionMultiplierX), 0f);
        Vector2 origin = layer.origin + offset + Vector2.right * (layer.horizontalScrollSpeed * _backgroundPreviewTime);
        Color oldColor = GUI.color;
        GUI.color = new Color(layer.color.r, layer.color.g, layer.color.b, layer.color.a * 0.55f);

        if (layer.contentType == BackgroundLayerContentType.SequentialTiles)
        {
            float cursor = origin.x;
            foreach (var entry in layer.sequence ?? new List<BackgroundSequenceEntry>())
            {
                if (entry?.sprite == null || entry.sprite.texture == null) continue;
                float width = entry.sprite.bounds.size.x * Mathf.Abs(layer.scale.x);
                float height = entry.sprite.bounds.size.y * Mathf.Abs(layer.scale.y);
                for (int i = 0; i < Mathf.Max(1, entry.repeatCount); i++)
                {
                    Rect worldRect = new Rect(cursor, origin.y - height * 0.5f, width, height);
                    GUI.DrawTextureWithTexCoords(WorldRectToScreenRect(worldRect, local),
                        entry.sprite.texture, GetSpriteUv(entry.sprite), true);
                    cursor += width;
                }
            }
        }
        else if (layer.sprite != null && layer.sprite.texture != null)
        {
            float width = layer.sprite.bounds.size.x * Mathf.Abs(layer.scale.x);
            float height = layer.sprite.bounds.size.y * Mathf.Abs(layer.scale.y);
            if (width > Mathf.Epsilon && height > Mathf.Epsilon)
            {
                if (layer.contentType == BackgroundLayerContentType.RepeatedSprite)
                {
                    Vector2 viewMin = ScreenToWorld(new Vector2(0f, local.height), local);
                    Vector2 viewMax = ScreenToWorld(new Vector2(local.width, 0f), local);
                    float first = origin.x + Mathf.Floor((viewMin.x - origin.x) / width) * width;
                    for (float x = first; x <= viewMax.x + width; x += width)
                    {
                        Rect worldRect = new Rect(x - width * 0.5f, origin.y - height * 0.5f, width, height);
                        GUI.DrawTextureWithTexCoords(WorldRectToScreenRect(worldRect, local),
                            layer.sprite.texture, GetSpriteUv(layer.sprite), true);
                    }
                }
                else
                {
                    Rect worldRect = new Rect(origin.x - width * 0.5f, origin.y - height * 0.5f, width, height);
                    GUI.DrawTextureWithTexCoords(WorldRectToScreenRect(worldRect, local),
                        layer.sprite.texture, GetSpriteUv(layer.sprite), true);
                }
            }
        }
        GUI.color = oldColor;
    }

    void DrawPreviewBackgroundSequence(Rect local, BackgroundSettings settings)
    {
        if (settings.sequence == null || settings.sequence.Count == 0) return;

        Vector2 viewMin = ScreenToWorld(new Vector2(0f, local.height), local);
        Vector2 viewMax = ScreenToWorld(new Vector2(local.width, 0f), local);
        float cursorX = settings.sequenceStartX;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;

        Color oldColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.55f);

        foreach (var entry in settings.sequence)
        {
            if (entry == null || entry.sprite == null || entry.sprite.texture == null)
                continue;

            Sprite sprite = entry.sprite;
            float width = sprite.bounds.size.x;
            float height = sprite.bounds.size.y;
            if (width <= 0f || height <= 0f) continue;

            int repeatCount = Mathf.Max(0, entry.repeatCount);
            Rect uv = GetSpriteUv(sprite);
            float bottomY = settings.sequenceCenterY - height * 0.5f;
            minY = Mathf.Min(minY, bottomY);
            maxY = Mathf.Max(maxY, bottomY + height);

            for (int repeatIndex = 0; repeatIndex < repeatCount; repeatIndex++)
            {
                float tileLeft = cursorX;
                float tileRight = cursorX + width;
                if (tileRight >= viewMin.x && tileLeft <= viewMax.x)
                {
                    Rect worldRect = new Rect(tileLeft, bottomY, width, height);
                    Rect screenRect = WorldRectToScreenRect(worldRect, local);
                    GUI.DrawTextureWithTexCoords(screenRect, sprite.texture, uv, true);
                    Handles.DrawSolidRectangleWithOutline(
                        screenRect,
                        Color.clear,
                        new Color(0.5f, 0.65f, 0.85f, 0.2f));
                }
                cursorX = tileRight;
            }
        }

        GUI.color = oldColor;

        if (!float.IsInfinity(minY) && cursorX > settings.sequenceStartX)
        {
            Rect sequenceRect = WorldRectToScreenRect(
                new Rect(settings.sequenceStartX, minY, cursorX - settings.sequenceStartX, maxY - minY),
                local);
            Handles.DrawSolidRectangleWithOutline(
                sequenceRect,
                new Color(0.05f, 0.08f, 0.12f, 0.06f),
                new Color(0.35f, 0.6f, 0.9f, 0.5f));
        }
    }

    void DrawFlowTargets(Rect local)
    {
        if (_level.events == null) return;
        for (int flowIndex = 0; flowIndex < _level.events.Count; flowIndex++)
        {
            var flow = _level.events[flowIndex];
            if (flow == null) continue;
            bool selected = _selectedFlowIndex == flowIndex;
            if (flow.triggerMode == StoryTriggerMode.Position)
            {
                float triggerX = WorldToScreenX(flow.positionX, local);
                Handles.color = selected ? new Color(0.75f, 0.35f, 1f, 1f) : new Color(0.75f, 0.35f, 1f, 0.45f);
                Handles.DrawAAPolyLine(selected ? 2f : 1f, new Vector3(triggerX, 0f), new Vector3(triggerX, local.height));
                Handles.Label(new Vector3(triggerX + 3f, 40f), $"演出:{flow.flowId}");
            }
            if (flow?.steps == null) continue;
            foreach (var step in flow.steps)
            {
                if (step == null) continue;
                if (step.stepType == LevelFlowStepType.PlayStory)
                {
                    DrawPerformanceCueTargets(flow, step, local, selected);
                    continue;
                }
                if (step.stepType != LevelFlowStepType.MovePlayer && step.stepType != LevelFlowStepType.MoveCamera)
                    continue;

                Vector2 point = WorldToScreen(step.targetPosition, local);
                bool isPlayer = step.stepType == LevelFlowStepType.MovePlayer;
                Handles.color = isPlayer
                    ? new Color(0.2f, 0.9f, 1f, selected ? 1f : 0.65f)
                    : new Color(1f, 0.45f, 0.9f, selected ? 1f : 0.65f);
                Handles.DrawWireDisc(point, Vector3.forward, 7f);
                Handles.DrawLine(point + Vector2.left * 9f, point + Vector2.right * 9f);
                Handles.DrawLine(point + Vector2.up * 9f, point + Vector2.down * 9f);
                Handles.Label(point + new Vector2(10f, -10f), $"{flow.flowId}:{(isPlayer ? "P" : "C")}");
            }
        }
    }

    void DrawPerformanceCueTargets(LevelFlowData flow, LevelFlowStep step, Rect local, bool selected)
    {
        foreach (var script in step.storyPerformanceScripts ?? new List<PerformanceScript>())
        {
            if (script?.clips == null) continue;
            foreach (var clip in script.clips)
            {
                if (clip == null ||
                    (clip.clipType != PerformanceClipType.MoveActor &&
                     clip.clipType != PerformanceClipType.MoveCamera))
                    continue;

                Vector2 target;
                string suffix;
                if (clip.clipType == PerformanceClipType.MoveCamera)
                {
                    Vector2 cameraBase = _level.useCustomInitialCameraPosition
                        ? _level.initialCameraPosition
                        : _level.playerSpawnPosition;
                    target = clip.positionMode == PerformancePositionMode.World
                        ? clip.targetPosition
                        : cameraBase + clip.targetPosition;
                    suffix = "Cam";
                }
                else
                {
                    var binding = step.storyCastBindings?.FirstOrDefault(item =>
                        item != null && item.scriptId == script.scriptId &&
                        item.slotId == clip.actorSlotId);
                    binding ??= step.storyCastBindings?.FirstOrDefault(item =>
                        item != null && string.IsNullOrEmpty(item.scriptId) &&
                        item.slotId == clip.actorSlotId);
                    if (!TryGetPerformanceActorStart(binding, out Vector2 actorStart))
                        continue;
                    target = clip.positionMode == PerformancePositionMode.World
                        ? clip.targetPosition
                        : actorStart + clip.targetPosition;
                    suffix = clip.actorSlotId;
                }

                Vector2 point = WorldToScreen(target, local);
                Handles.color = clip.clipType == PerformanceClipType.MoveCamera
                    ? new Color(1f, 0.45f, 0.9f, selected ? 1f : 0.65f)
                    : new Color(1f, 0.75f, 0.15f, selected ? 1f : 0.65f);
                Handles.DrawWireDisc(point, Vector3.forward, 6f);
                Handles.DrawLine(point + Vector2.left * 8f, point + Vector2.right * 8f);
                Handles.DrawLine(point + Vector2.up * 8f, point + Vector2.down * 8f);
                Handles.Label(point + new Vector2(9f, -9f), $"{flow.flowId}:{suffix}");
            }
        }
    }

    bool TryGetPerformanceActorStart(PerformanceActorBinding binding, out Vector2 position)
    {
        position = Vector2.zero;
        if (binding == null) return false;
        if (binding.targetType == PerformanceActorTargetType.Player)
        {
            position = _level.playerSpawnPosition;
            return true;
        }

        if (binding.targetType == PerformanceActorTargetType.EnvironmentActor)
        {
            var environmentActor = _level.environmentActors?.FirstOrDefault(item =>
                item != null && item.actorId == binding.environmentActorId);
            if (environmentActor == null) return false;
            position = environmentActor.position;
            return true;
        }

        var element = _level.elements?.FirstOrDefault(item =>
            item != null && item.elementId == binding.elementId);
        if (element == null) return false;
        position = element.position;
        return true;
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

    void DrawCameraBounds(Rect local)
    {
        var backgroundSettings = _level.backgroundSettings;
        if (backgroundSettings == null || !backgroundSettings.constrainCameraToBounds)
            return;

        float startX = WorldToScreenX(backgroundSettings.cameraBoundsStartX, local);
        float endX = WorldToScreenX(backgroundSettings.cameraBoundsEndX, local);
        Handles.color = new Color(1f, 0.55f, 0.1f, 0.9f);
        Handles.DrawAAPolyLine(3f, new Vector3(startX, 0f), new Vector3(startX, local.height));
        Handles.DrawAAPolyLine(3f, new Vector3(endX, 0f), new Vector3(endX, local.height));
        Handles.Label(new Vector3(startX + 4f, 18f), "Camera Start");
        Handles.Label(new Vector3(endX + 4f, 18f), "Camera End");
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
            if (g.triggerMode != ElementGroupTriggerMode.Position) continue;
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

    void DrawEnvironmentActors(Rect local)
    {
        Rect rect = new Rect(0, 0, local.width, local.height);
        foreach (var actor in _level.environmentActors ?? new List<EnvironmentActorData>())
        {
            if (actor == null) continue;
            Vector2 screen = WorldToScreen(actor.position, rect);
            if (screen.x < -30f || screen.x > local.width + 30f) continue;
            bool selected = actor.actorId == _selectedEnvironmentActorId;
            bool drewPrefab = DrawPrefabFootPreview(actor.prefab, screen, actor.faceRight,
                EnvironmentActorColor, selected);
            if (!drewPrefab)
            {
                Handles.color = EnvironmentActorColor;
                Handles.DrawSolidDisc(screen, Vector3.forward, selected ? 8f : 6f);
            }
            DrawFootMarker(screen, EnvironmentActorColor, selected);
            if (selected)
            {
                Handles.color = Color.white;
                Handles.DrawWireDisc(screen, Vector3.forward, 12f);
            }
            if (_canvasScale >= 5f)
                Handles.Label(new Vector3(screen.x + 10f, screen.y + 5f), actor.displayName,
                    selected ? EditorStyles.whiteBoldLabel : EditorStyles.miniLabel);
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

    bool HasObjectSelection()
    {
        return !string.IsNullOrEmpty(_selectedElementId) ||
               !string.IsNullOrEmpty(_selectedEnvironmentActorId) ||
               !string.IsNullOrEmpty(_selectedGroupId) ||
               _selectedStoryTriggerIndex >= 0 ||
               _selectedFlowIndex >= 0;
    }

    void ClearObjectSelection()
    {
        _selectedElementId = null;
        _selectedEnvironmentActorId = null;
        _selectedGroupId = null;
        _selectedStoryTriggerIndex = -1;
        _selectedFlowIndex = -1;
    }

    void ShowSection(InspectorSection section)
    {
        ClearObjectSelection();
        _inspectorSection = section;
        _propertyScroll = Vector2.zero;
        GUI.FocusControl(null);
        Repaint();
    }

    void SelectElement(string elementId)
    {
        ClearObjectSelection();
        _selectedElementId = elementId;
        _inspectorSection = InspectorSection.Overview;
        _propertyScroll = Vector2.zero;
    }

    void SelectEnvironmentActor(string actorId)
    {
        ClearObjectSelection();
        _selectedEnvironmentActorId = actorId;
        _inspectorSection = InspectorSection.Background;
        _propertyScroll = Vector2.zero;
    }

    void SelectGroup(string groupId)
    {
        ClearObjectSelection();
        _selectedGroupId = groupId;
        _inspectorSection = InspectorSection.Overview;
        _propertyScroll = Vector2.zero;
    }

    void SelectStoryTrigger(int index)
    {
        ClearObjectSelection();
        _selectedStoryTriggerIndex = index;
        _inspectorSection = InspectorSection.Events;
        _propertyScroll = Vector2.zero;
    }

    void SelectFlow(int index)
    {
        ClearObjectSelection();
        _selectedFlowIndex = index;
        _inspectorSection = InspectorSection.Events;
        _propertyScroll = Vector2.zero;
    }

    void ReturnToParent()
    {
        InspectorSection parent = _inspectorSection;
        if (_selectedStoryTriggerIndex >= 0 || _selectedFlowIndex >= 0) parent = InspectorSection.Events;
        else if (!string.IsNullOrEmpty(_selectedEnvironmentActorId)) parent = InspectorSection.Background;
        else parent = InspectorSection.Overview;
        ShowSection(parent);
    }

    string GetSelectionBreadcrumb()
    {
        if (_selectedFlowIndex >= 0 && _selectedFlowIndex < (_level.events?.Count ?? 0))
            return $"演出事件 / {_level.events[_selectedFlowIndex].flowId}";
        if (_selectedStoryTriggerIndex >= 0 && _selectedStoryTriggerIndex < _level.storyTriggers.Count)
            return $"过场触发 / {_level.storyTriggers[_selectedStoryTriggerIndex].storyId}";
        if (!string.IsNullOrEmpty(_selectedGroupId))
            return $"元素组 / {_level.FindGroup(_selectedGroupId)?.groupName}";
        if (!string.IsNullOrEmpty(_selectedElementId))
            return $"关卡元素 / {_level.elements.Find(e => e.elementId == _selectedElementId)?.displayName}";
        if (!string.IsNullOrEmpty(_selectedEnvironmentActorId))
            return $"环境角色 / {_level.environmentActors.Find(e => e.actorId == _selectedEnvironmentActorId)?.displayName}";
        return "关卡";
    }

    static string GetSectionLabel(InspectorSection section)
    {
        switch (section)
        {
            case InspectorSection.Background: return "背景";
            case InspectorSection.Variables: return "变量";
            case InspectorSection.Events: return "演出事件";
            default: return "概览";
        }
    }

    void DrawPropertyPanel()
    {
        DrawPropertyNavigationHeader();
        EditorGUILayout.Space(4);
        _propertyScroll = EditorGUILayout.BeginScrollView(_propertyScroll);

        if (!string.IsNullOrEmpty(_selectedElementId))
            DrawElementProperties();
        else if (!string.IsNullOrEmpty(_selectedEnvironmentActorId))
            DrawEnvironmentActorProperties();
        else if (_selectedStoryTriggerIndex >= 0 && _selectedStoryTriggerIndex < _level.storyTriggers.Count)
            DrawStoryTriggerProperties();
        else if (!string.IsNullOrEmpty(_selectedGroupId))
            DrawGroupProperties();
        else if (_selectedFlowIndex >= 0 && _selectedFlowIndex < (_level.events?.Count ?? 0))
            DrawFlowProperties();
        else
            DrawSelectedSection();

        EditorGUILayout.EndScrollView();
    }

    void DrawPropertyNavigationHeader()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        if (HasObjectSelection())
        {
            if (GUILayout.Button("← 返回", GUILayout.Width(72), GUILayout.Height(24)))
                ReturnToParent();
            GUILayout.Label(GetSelectionBreadcrumb(), EditorStyles.boldLabel);
        }
        else
        {
            GUILayout.Label($"关卡 / {GetSectionLabel(_inspectorSection)}", EditorStyles.boldLabel);
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    void DrawSelectedSection()
    {
        switch (_inspectorSection)
        {
            case InspectorSection.Background:
                DrawBackgroundPage();
                break;
            case InspectorSection.Variables:
                DrawVariablesPage();
                break;
            case InspectorSection.Events:
                DrawEventsPage();
                break;
            default:
                DrawLevelProperties();
                break;
        }
    }

    void DrawVariablesPage()
    {
        EditorGUILayout.LabelField("关卡变量", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("变量用于表达持久的关卡事实，例如“本波敌人已清空”。", MessageType.Info);
        DrawVariables();
        if (GUI.changed) EditorUtility.SetDirty(_level);
    }

    void DrawEventsPage()
    {
        EditorGUILayout.LabelField("演出事件", EditorStyles.boldLabel);
        _level.storyCollectionJson = (TextAsset)EditorGUILayout.ObjectField("过场动画集 JSON", _level.storyCollectionJson, typeof(TextAsset), false);
        DrawFlowQuickPanel();
        if (GUI.changed) EditorUtility.SetDirty(_level);
    }

    void DrawBackgroundPage()
    {
        var settings = _level.backgroundSettings ?? (_level.backgroundSettings = new BackgroundSettings());
        settings.MigrateLegacyData();
        if (settings.layers == null) settings.layers = new List<BackgroundLayerData>();

        Undo.RecordObject(_level, "Edit Background Layers");
        EditorGUILayout.LabelField("背景图层", EditorStyles.boldLabel);
        _backgroundPreviewCameraX = EditorGUILayout.FloatField("预览相机 X", _backgroundPreviewCameraX);
        _backgroundPreviewTime = Mathf.Max(0f, EditorGUILayout.FloatField("预览卷轴时间", _backgroundPreviewTime));
        EditorGUILayout.HelpBox("远景固定在画面；中景固定在世界；近景可调整画面运动倍率。", MessageType.Info);

        DrawBackgroundBand(settings, BackgroundDepthBand.Far, "远景");
        DrawBackgroundBand(settings, BackgroundDepthBand.Mid, "中景");
        DrawBackgroundBand(settings, BackgroundDepthBand.Near, "近景");

        if (GUI.changed)
        {
            EditorUtility.SetDirty(_level);
            Repaint();
        }
    }

    void DrawBackgroundBand(BackgroundSettings settings, BackgroundDepthBand band, string title)
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        for (int i = 0; i < settings.layers.Count; i++)
        {
            var layer = settings.layers[i];
            if (layer == null || layer.depthBand != band) continue;
            bool remove = DrawBackgroundLayerCard(layer, i, settings.layers);
            if (remove)
            {
                settings.layers.RemoveAt(i);
                GUI.changed = true;
                break;
            }
        }

        if (GUILayout.Button($"+ 添加{title}图层"))
        {
            settings.layers.Add(new BackgroundLayerData
            {
                layerId = Guid.NewGuid().ToString(),
                displayName = $"{title} {settings.layers.Count + 1}",
                depthBand = band,
                nearMotionMultiplier = band == BackgroundDepthBand.Near ? 1.25f : 1f,
                enableVerticalMotion = band == BackgroundDepthBand.Far
            });
            GUI.changed = true;
        }
    }

    bool DrawBackgroundLayerCard(BackgroundLayerData layer, int index, List<BackgroundLayerData> layers)
    {
        bool remove = false;
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        layer.displayName = EditorGUILayout.TextField(layer.displayName);
        using (new EditorGUI.DisabledScope(index == 0))
            if (GUILayout.Button("↑", GUILayout.Width(24))) SwapListEntries(layers, index, index - 1);
        using (new EditorGUI.DisabledScope(index == layers.Count - 1))
            if (GUILayout.Button("↓", GUILayout.Width(24))) SwapListEntries(layers, index, index + 1);
        if (GUILayout.Button("X", GUILayout.Width(24))) remove = true;
        EditorGUILayout.EndHorizontal();

        layer.contentType = (BackgroundLayerContentType)EditorGUILayout.EnumPopup("内容类型", layer.contentType);
        layer.origin = EditorGUILayout.Vector2Field("原点", layer.origin);
        layer.scale = EditorGUILayout.Vector2Field("缩放", layer.scale);
        layer.color = EditorGUILayout.ColorField("颜色", layer.color);
        layer.sortingOffset = EditorGUILayout.IntField("排序偏移", layer.sortingOffset);

        if (layer.depthBand == BackgroundDepthBand.Near)
            layer.nearMotionMultiplier = EditorGUILayout.Slider("画面运动倍率", layer.nearMotionMultiplier, 0f, 2f);
        else
            EditorGUILayout.LabelField("画面运动倍率", layer.depthBand == BackgroundDepthBand.Far ? "0（画面固定）" : "1（世界固定）");

        layer.enableVerticalMotion = EditorGUILayout.Toggle("启用垂直视差", layer.enableVerticalMotion);
        if (layer.enableVerticalMotion && layer.depthBand == BackgroundDepthBand.Near)
            layer.verticalMotionMultiplier = EditorGUILayout.Slider("垂直运动倍率", layer.verticalMotionMultiplier, 0f, 2f);

        if (layer.contentType == BackgroundLayerContentType.SequentialTiles)
            DrawLayerSequence(layer);
        else
            layer.sprite = (Sprite)EditorGUILayout.ObjectField("图片", layer.sprite, typeof(Sprite), false);

        if (layer.contentType == BackgroundLayerContentType.RepeatedSprite)
            layer.horizontalScrollSpeed = EditorGUILayout.FloatField("卷轴速度", layer.horizontalScrollSpeed);
        else
            layer.horizontalScrollSpeed = 0f;

        EditorGUILayout.LabelField("最终排序", layer.SortingOrder.ToString(), EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();
        return remove;
    }

    void DrawLayerSequence(BackgroundLayerData layer)
    {
        if (layer.sequence == null) layer.sequence = new List<BackgroundSequenceEntry>();
        for (int i = 0; i < layer.sequence.Count; i++)
        {
            var entry = layer.sequence[i] ?? (layer.sequence[i] = new BackgroundSequenceEntry());
            EditorGUILayout.BeginHorizontal();
            entry.sprite = (Sprite)EditorGUILayout.ObjectField(entry.sprite, typeof(Sprite), false);
            entry.repeatCount = Mathf.Max(1, EditorGUILayout.IntField(entry.repeatCount, GUILayout.Width(45)));
            if (GUILayout.Button("X", GUILayout.Width(24)))
            {
                layer.sequence.RemoveAt(i);
                GUI.changed = true;
                break;
            }
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("+ 添加顺序图片"))
            layer.sequence.Add(new BackgroundSequenceEntry());
        EditorGUILayout.LabelField($"总宽度：{layer.CalculateSequenceWidth():0.###}", EditorStyles.miniLabel);
    }

    static void SwapListEntries<T>(List<T> list, int a, int b)
    {
        if (list == null || a < 0 || b < 0 || a >= list.Count || b >= list.Count) return;
        T value = list[a];
        list[a] = list[b];
        list[b] = value;
        GUI.changed = true;
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
        var bg = _level.backgroundSettings ?? (_level.backgroundSettings = new BackgroundSettings());
        bg.constrainCameraToBounds = EditorGUILayout.Toggle("限制关卡范围（相机+玩家）", bg.constrainCameraToBounds);
        using (new EditorGUI.DisabledScope(!bg.constrainCameraToBounds))
        {
            bg.cameraBoundsStartX = EditorGUILayout.FloatField("关卡起始 X", bg.cameraBoundsStartX);
            bg.cameraBoundsEndX = EditorGUILayout.FloatField("关卡结束 X", bg.cameraBoundsEndX);
        }
        if (bg.constrainCameraToBounds)
        {
            if (bg.cameraBoundsEndX <= bg.cameraBoundsStartX)
                EditorGUILayout.HelpBox("关卡结束 X 必须大于关卡起始 X。", MessageType.Error);
            else
                EditorGUILayout.HelpBox(
                    "相机画面和玩家碰撞体都不能越界；普通移动、击退、关卡流程和剧情演出都会使用此范围。",
                    MessageType.Info);
        }
        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox($"已配置 {bg.layers?.Count ?? 0} 个统一背景图层。", MessageType.Info);
        if (GUILayout.Button("打开背景图层编辑"))
            ShowSection(InspectorSection.Background);

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("数据交换", EditorStyles.boldLabel);
        if (GUILayout.Button("导出JSON", GUILayout.Height(22))) ExportJson();
        if (GUILayout.Button("导入JSON", GUILayout.Height(22))) ImportJson();

        if (GUI.changed) EditorUtility.SetDirty(_level);
    }

    void DrawSequentialBackgroundSettings(BackgroundSettings bg)
    {
        bg.sequenceStartX = EditorGUILayout.FloatField("起始 X（左边缘）", bg.sequenceStartX);
        bg.sequenceCenterY = EditorGUILayout.FloatField("背景中心 Y", bg.sequenceCenterY);
        bg.sequenceSortingOrder = EditorGUILayout.IntField("排序层", bg.sequenceSortingOrder);

        if (bg.sequence == null)
            bg.sequence = new List<BackgroundSequenceEntry>();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("从左到右的图片顺序", EditorStyles.miniBoldLabel);

        for (int i = 0; i < bg.sequence.Count; i++)
        {
            var entry = bg.sequence[i] ?? (bg.sequence[i] = new BackgroundSequenceEntry());
            int moveTo = -1;
            bool remove = false;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{i + 1}.", GUILayout.Width(24));
            entry.sprite = (Sprite)EditorGUILayout.ObjectField(entry.sprite, typeof(Sprite), false);
            EditorGUILayout.LabelField("重复", GUILayout.Width(30));
            entry.repeatCount = Mathf.Max(1, EditorGUILayout.IntField(entry.repeatCount, GUILayout.Width(42)));

            using (new EditorGUI.DisabledScope(i == 0))
            {
                if (GUILayout.Button("↑", GUILayout.Width(24)))
                    moveTo = i - 1;
            }
            using (new EditorGUI.DisabledScope(i == bg.sequence.Count - 1))
            {
                if (GUILayout.Button("↓", GUILayout.Width(24)))
                    moveTo = i + 1;
            }
            if (GUILayout.Button("X", GUILayout.Width(24)))
                remove = true;
            EditorGUILayout.EndHorizontal();

            if (entry.sprite != null)
            {
                float itemWidth = entry.sprite.bounds.size.x * entry.repeatCount;
                EditorGUILayout.LabelField(
                    $"单图宽 {entry.sprite.bounds.size.x:0.###}，本段宽 {itemWidth:0.###}",
                    EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();

            if (remove)
            {
                bg.sequence.RemoveAt(i);
                GUI.changed = true;
                break;
            }
            if (moveTo >= 0)
            {
                var movedEntry = bg.sequence[i];
                bg.sequence[i] = bg.sequence[moveTo];
                bg.sequence[moveTo] = movedEntry;
                GUI.changed = true;
                break;
            }
        }

        if (GUILayout.Button("+ 添加背景段"))
        {
            bg.sequence.Add(new BackgroundSequenceEntry());
            GUI.changed = true;
        }

        if (bg.sequence.Count == 0)
        {
            EditorGUILayout.HelpBox("添加背景段，并为每段选择图片和重复次数。", MessageType.Info);
            return;
        }

        float totalWidth = bg.CalculateSequenceWidth();
        float endX = bg.sequenceStartX + totalWidth;
        EditorGUILayout.HelpBox(
            $"背景覆盖 X：{bg.sequenceStartX:0.###} → {endX:0.###}（总宽 {totalWidth:0.###}）",
            endX < Mathf.Max(_level.levelLength, _level.levelEndPositionX)
                ? MessageType.Warning
                : MessageType.Info);

        if (GUILayout.Button("将摄像机范围设为背景覆盖范围"))
        {
            bg.constrainCameraToBounds = true;
            bg.cameraBoundsStartX = bg.sequenceStartX;
            bg.cameraBoundsEndX = endX;
            GUI.changed = true;
        }
    }

    void DrawVariables()
    {
        for (int i = 0; i < _level.variables.Count; i++)
        {
            var v = _level.variables[i];
            EditorGUILayout.BeginHorizontal();
            v.variableName = EditorGUILayout.TextField(v.variableName, GUILayout.Width(80));
            v.type = DrawVariableTypePopup(v.type, GUILayout.Width(70));
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

    void DrawFlowQuickPanel()
    {
        EditorGUILayout.Space(12);
        EditorGUILayout.HelpBox("演出事件统一管理“何时触发”和“依次做什么”。直接播放过场是只包含一个“播放过场”步骤的简单事件。", MessageType.Info);

        if (_level.events == null) _level.events = new List<LevelFlowData>();
        for (int i = 0; i < _level.events.Count; i++)
        {
            var flow = _level.events[i];
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            bool selected = _selectedFlowIndex == i;
            if (GUILayout.Toggle(selected, string.IsNullOrEmpty(flow.flowId) ? "(未命名事件)" : flow.flowId, "Button") && !selected)
                SelectFlow(i);
            GUILayout.Label(GetStoryTriggerModeLabel(flow.triggerMode), GUILayout.Width(42));
            GUILayout.Label(GetEventStorySummary(flow), EditorStyles.miniLabel, GUILayout.Width(90));
            using (new EditorGUI.DisabledScope(flow.triggerMode != StoryTriggerMode.Position))
                if (GUILayout.Button("定位", GUILayout.Width(46)))
                {
                    SelectFlow(i);
                    CenterCanvasOn(new Vector2(flow.positionX, 0f));
                }
            if (GUILayout.Button("删除", GUILayout.Width(46)))
            {
                SelectFlow(i);
                DeleteSelectedObject();
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }

        var storyIds = GetStoryIds();
        using (new EditorGUI.DisabledScope(storyIds.Count == 0))
        if (GUILayout.Button("+ 直接播放过场"))
        {
            Undo.RecordObject(_level, "Add Direct Story Event");
            _level.events.Add(new LevelFlowData
            {
                flowId = GetUniqueFlowId("event_story"),
                triggerMode = StoryTriggerMode.Conditions,
                triggerOnce = true,
                steps = new List<LevelFlowStep>
                {
                    new LevelFlowStep { stepType = LevelFlowStepType.PlayStory, storyId = storyIds[0] }
                }
            });
            SelectFlow(_level.events.Count - 1);
            EditorUtility.SetDirty(_level);
        }

        if (storyIds.Count == 0)
            EditorGUILayout.HelpBox("需要先指定包含 storyId 的过场动画集 JSON，才能创建“直接播放过场”。", MessageType.Warning);

        if (GUILayout.Button("+ 自定义演出事件"))
        {
            Undo.RecordObject(_level, "Add Performance Event");
            _level.events.Add(new LevelFlowData
            {
                flowId = GetUniqueFlowId("event"),
                triggerMode = StoryTriggerMode.Conditions,
                triggerOnce = true,
                steps = new List<LevelFlowStep> { new LevelFlowStep { stepType = LevelFlowStepType.WaitForPlayerSafe } }
            });
            SelectFlow(_level.events.Count - 1);
            EditorUtility.SetDirty(_level);
        }
    }

    static string GetEventStorySummary(LevelFlowData levelEvent)
    {
        string storyId = levelEvent?.steps?
            .FirstOrDefault(s => s != null && s.stepType == LevelFlowStepType.PlayStory)?.storyId;
        return string.IsNullOrEmpty(storyId) ? "无过场" : $"过场:{storyId}";
    }

    void DrawStoryTriggerQuickPanel()
    {
        var storyIds = GetStoryIds();
        if (_level.storyCollectionJson == null)
        {
            EditorGUILayout.HelpBox("尚未指定过场 JSON。仍可查看和删除现有触发器，但无法新建有效的过场触发。", MessageType.Warning);
        }
        else if (storyIds.Count == 0)
        {
            EditorGUILayout.HelpBox("当前剧情 JSON 中没有可用的 storyId，或 JSON 解析失败。", MessageType.Warning);
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
            if (GUILayout.Toggle(selected, storyLabel, "Button", GUILayout.Width(120)) && !selected)
                SelectStoryTrigger(i);

            trigger.triggerMode = DrawStoryTriggerModePopup(trigger.triggerMode, GUILayout.Width(88));

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
                    SelectStoryTrigger(i);
                    _canvasScroll.x = trigger.positionX * _canvasScale - 200f;
                    Repaint();
                }
            }

            if (GUILayout.Button("删除", GUILayout.Width(46)))
            {
                SelectStoryTrigger(i);
                DeleteSelectedObject();
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }

        using (new EditorGUI.DisabledScope(storyIds.Count == 0))
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
            SelectStoryTrigger(_level.storyTriggers.Count - 1);
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

    StoryTriggerMode DrawStoryTriggerModePopup(StoryTriggerMode current, params GUILayoutOption[] options)
    {
        var values = new[]
        {
            StoryTriggerMode.Position,
            StoryTriggerMode.Conditions,
            StoryTriggerMode.LevelStart,
            StoryTriggerMode.LevelComplete
        };
        var labels = values.Select(GetStoryTriggerModeLabel).ToArray();
        int index = Mathf.Max(0, Array.IndexOf(values, current));
        index = EditorGUILayout.Popup(index, labels, options);
        return values[index];
    }

    StoryTriggerMode DrawStoryTriggerModePopup(string label, StoryTriggerMode current)
    {
        var values = new[]
        {
            StoryTriggerMode.Position,
            StoryTriggerMode.Conditions,
            StoryTriggerMode.LevelStart,
            StoryTriggerMode.LevelComplete
        };
        var labels = values.Select(GetStoryTriggerModeLabel).ToArray();
        int index = Mathf.Max(0, Array.IndexOf(values, current));
        index = EditorGUILayout.Popup(label, index, labels);
        return values[index];
    }

    ElementGroupTriggerMode DrawElementGroupTriggerModePopup(string label, ElementGroupTriggerMode current)
    {
        var values = new[]
        {
            ElementGroupTriggerMode.None,
            ElementGroupTriggerMode.Position,
            ElementGroupTriggerMode.Conditions
        };
        var labels = new[] { "无触发", "位置触发", "条件触发" };
        int index = Mathf.Max(0, Array.IndexOf(values, current));
        index = EditorGUILayout.Popup(label, index, labels);
        return values[index];
    }

    string GetElementGroupNames(LevelElement el)
    {
        if (el == null) return "无";
        var names = new List<string>();
        foreach (var group in _level.groups)
        {
            if (el.IsInGroup(group.groupId))
                names.Add(group.groupName);
        }
        return names.Count > 0 ? string.Join(",", names) : "无";
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
        el.elementType = DrawElementTypePopup("类型", el.elementType);
        el.prefab = (GameObject)EditorGUILayout.ObjectField("预制体", el.prefab, typeof(GameObject), false);
        el.position = EditorGUILayout.Vector2Field("位置", el.position);
        el.faceRight = EditorGUILayout.Toggle("朝向右边", el.faceRight);
        el.appearDelay = EditorGUILayout.FloatField("出现延迟(s)", el.appearDelay);

        DrawElementGroupMembership(el);

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
            ShowSection(InspectorSection.Overview);
        }

        if (GUI.changed) EditorUtility.SetDirty(_level);
    }

    void DrawEnvironmentActorProperties()
    {
        var actor = _level.environmentActors.Find(x => x.actorId == _selectedEnvironmentActorId);
        if (actor == null) return;
        Undo.RecordObject(_level, "Edit Environment Actor");

        EditorGUILayout.LabelField("环境角色", EditorStyles.boldLabel);
        actor.displayName = EditorGUILayout.TextField("名称", actor.displayName);
        EditorGUILayout.SelectableLabel(actor.actorId, EditorStyles.textField, GUILayout.Height(18));
        actor.prefab = (GameObject)EditorGUILayout.ObjectField("预制体", actor.prefab, typeof(GameObject), false);
        actor.position = EditorGUILayout.Vector2Field("位置", actor.position);
        actor.faceRight = EditorGUILayout.Toggle("朝向右侧", actor.faceRight);
        actor.depthBand = (BackgroundDepthBand)EditorGUILayout.EnumPopup("深度", actor.depthBand);
        actor.sortingOffset = EditorGUILayout.IntField("排序偏移", actor.sortingOffset);
        EditorGUILayout.LabelField("最终排序", actor.SortingOrder.ToString(), EditorStyles.miniLabel);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("启用条件", EditorStyles.boldLabel);
        DrawConditions(actor.activeConditions);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("连续 Animator 绑定", EditorStyles.boldLabel);
        for (int i = 0; i < actor.continuousBindings.Count; i++)
        {
            var binding = actor.continuousBindings[i] ?? (actor.continuousBindings[i] = new EnvironmentContinuousBinding());
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            binding.source = (EnvironmentContinuousSource)EditorGUILayout.EnumPopup("数据源", binding.source);
            binding.animatorParameter = EditorGUILayout.TextField("Float 参数", binding.animatorParameter);
            binding.inputMin = EditorGUILayout.FloatField("输入最小", binding.inputMin);
            binding.inputMax = EditorGUILayout.FloatField("输入最大", binding.inputMax);
            binding.outputMin = EditorGUILayout.FloatField("输出最小", binding.outputMin);
            binding.outputMax = EditorGUILayout.FloatField("输出最大", binding.outputMax);
            binding.clamp = EditorGUILayout.Toggle("限制范围", binding.clamp);
            bool removeBinding = GUILayout.Button("删除绑定");
            EditorGUILayout.EndVertical();
            if (removeBinding) { actor.continuousBindings.RemoveAt(i); break; }
        }
        if (GUILayout.Button("+ 添加连续绑定"))
            actor.continuousBindings.Add(new EnvironmentContinuousBinding());

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("互动触发", EditorStyles.boldLabel);
        for (int i = 0; i < actor.triggers.Count; i++)
        {
            var trigger = actor.triggers[i] ?? (actor.triggers[i] = new EnvironmentActorTrigger());
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            trigger.triggerType = (EnvironmentTriggerType)EditorGUILayout.EnumPopup("触发类型", trigger.triggerType);
            trigger.triggerOnce = EditorGUILayout.Toggle("仅一次", trigger.triggerOnce);
            if (trigger.triggerType == EnvironmentTriggerType.PlayerSignal)
                trigger.signalId = EditorGUILayout.TextField("玩家信号", trigger.signalId);
            else if (trigger.triggerType != EnvironmentTriggerType.LevelConditions)
            {
                trigger.minValue = EditorGUILayout.FloatField("范围最小", trigger.minValue);
                trigger.maxValue = EditorGUILayout.FloatField("范围最大", trigger.maxValue);
            }
            EditorGUILayout.LabelField("附加条件", EditorStyles.miniBoldLabel);
            DrawConditions(trigger.conditions);
            DrawEnvironmentActions(trigger.onEnterActions, "进入/触发动作");
            DrawEnvironmentActions(trigger.onExitActions, "离开动作");
            bool removeTrigger = GUILayout.Button("删除触发");
            EditorGUILayout.EndVertical();
            if (removeTrigger) { actor.triggers.RemoveAt(i); break; }
        }
        if (GUILayout.Button("+ 添加互动触发"))
            actor.triggers.Add(new EnvironmentActorTrigger());

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("实例参数", EditorStyles.boldLabel);
        var proxy = new LevelElement { prefab = actor.prefab, customParameters = actor.customParameters };
        DrawCustomParameters(proxy);
        actor.customParameters = proxy.customParameters;

        EditorGUILayout.Space(10);
        if (GUILayout.Button("删除环境角色", GUILayout.Height(25)))
        {
            _level.environmentActors.Remove(actor);
            ShowSection(InspectorSection.Background);
        }
        if (GUI.changed) EditorUtility.SetDirty(_level);
    }

    void DrawEnvironmentActions(List<EnvironmentActorAction> actions, string title)
    {
        EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
        for (int i = 0; i < actions.Count; i++)
        {
            var action = actions[i] ?? (actions[i] = new EnvironmentActorAction());
            EditorGUILayout.BeginVertical(GUI.skin.box);
            action.actionType = (EnvironmentActionType)EditorGUILayout.EnumPopup(action.actionType);
            action.name = EditorGUILayout.TextField("名称/参数", action.name);
            switch (action.actionType)
            {
                case EnvironmentActionType.SetAnimatorFloat:
                    action.floatValue = EditorGUILayout.FloatField("数值", action.floatValue);
                    break;
                case EnvironmentActionType.SetAnimatorBool:
                case EnvironmentActionType.SetVisualActive:
                    action.boolValue = EditorGUILayout.Toggle("值", action.boolValue);
                    break;
                case EnvironmentActionType.SetLevelVariable:
                    action.stringValue = EditorGUILayout.TextField("变量值", action.stringValue);
                    break;
                case EnvironmentActionType.PlayAnimation:
                    action.loop = EditorGUILayout.Toggle("循环", action.loop);
                    action.animationTrack = EditorGUILayout.IntField("轨道/层", action.animationTrack);
                    break;
            }
            bool removeAction = GUILayout.Button("删除动作");
            EditorGUILayout.EndVertical();
            if (removeAction) { actions.RemoveAt(i); break; }
        }
        if (GUILayout.Button("+ 添加动作")) actions.Add(new EnvironmentActorAction());
    }

    void DrawStoryTriggerProperties()
    {
        var st = _level.storyTriggers[_selectedStoryTriggerIndex];
        Undo.RecordObject(_level, "Edit StoryTrigger");

        EditorGUILayout.LabelField("过场触发器", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        st.storyId = DrawStoryIdField("故事ID", st.storyId);
        st.triggerMode = DrawStoryTriggerModePopup("触发方式", st.triggerMode);
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
            DeleteSelectedObject();

        if (GUI.changed) EditorUtility.SetDirty(_level);
    }

    void DrawElementGroupMembership(LevelElement el)
    {
        if (el.groupIds == null)
            el.groupIds = new List<string>();
        if (!string.IsNullOrEmpty(el.groupId) && !el.groupIds.Contains(el.groupId))
            el.groupIds.Add(el.groupId);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("所属元素组", EditorStyles.boldLabel);
        if (_level.groups.Count == 0)
        {
            EditorGUILayout.HelpBox("还没有元素组，可在关卡设置或左侧元素组面板中新建。", MessageType.Info);
            return;
        }

        foreach (var group in _level.groups)
        {
            bool has = el.IsInGroup(group.groupId);
            bool next = EditorGUILayout.ToggleLeft(group.groupName, has);
            if (next != has)
            {
                Undo.RecordObject(_level, "Edit Element Groups");
                if (next)
                    el.AddGroup(group.groupId);
                else
                    el.RemoveGroup(group.groupId);
                EditorUtility.SetDirty(_level);
            }
        }
    }

    void DrawFlowProperties()
    {
        var flow = _level.events[_selectedFlowIndex];
        if (flow == null) return;
        if (flow.steps == null) flow.steps = new List<LevelFlowStep>();
        if (flow.triggerConditions == null) flow.triggerConditions = new List<LevelVariableCondition>();

        Undo.RecordObject(_level, "Edit Performance Event");
        EditorGUILayout.LabelField("演出事件", EditorStyles.boldLabel);
        flow.flowId = EditorGUILayout.TextField("事件 ID", flow.flowId);
        flow.triggerMode = DrawStoryTriggerModePopup("触发方式", flow.triggerMode);
        flow.triggerOnce = EditorGUILayout.Toggle("只触发一次", flow.triggerOnce);
        if (flow.triggerMode == StoryTriggerMode.Position)
        {
            flow.positionX = EditorGUILayout.FloatField("触发位置 X", flow.positionX);
            flow.triggerFromLeft = EditorGUILayout.Toggle("从左向右触发", flow.triggerFromLeft);
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("触发条件", EditorStyles.boldLabel);
        DrawConditions(flow.triggerConditions);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("执行步骤", EditorStyles.boldLabel);
        for (int i = 0; i < flow.steps.Count; i++)
        {
            var step = flow.steps[i] ?? (flow.steps[i] = new LevelFlowStep());
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            step.stepType = DrawEventStepTypePopup($"步骤 {i + 1}", step.stepType);
            using (new EditorGUI.DisabledScope(i == 0))
                if (GUILayout.Button("↑", GUILayout.Width(24))) { Swap(flow.steps, i, i - 1); GUIUtility.ExitGUI(); }
            using (new EditorGUI.DisabledScope(i == flow.steps.Count - 1))
                if (GUILayout.Button("↓", GUILayout.Width(24))) { Swap(flow.steps, i, i + 1); GUIUtility.ExitGUI(); }
            if (GUILayout.Button("复制", GUILayout.Width(42)))
            {
                flow.steps.Insert(i + 1, CloneFlowStep(step));
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button("X", GUILayout.Width(24)))
            {
                flow.steps.RemoveAt(i);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            switch (step.stepType)
            {
                case LevelFlowStepType.Wait:
                    step.duration = Mathf.Max(0f, EditorGUILayout.FloatField("等待秒数", step.duration));
                    break;
                case LevelFlowStepType.MovePlayer:
                    step.targetPosition = EditorGUILayout.Vector2Field("目标位置", step.targetPosition);
                    step.speed = EditorGUILayout.FloatField("移动速度", step.speed);
                    step.tolerance = EditorGUILayout.FloatField("到达容差", step.tolerance);
                    break;
                case LevelFlowStepType.MoveCamera:
                    step.targetPosition = EditorGUILayout.Vector2Field("镜头目标", step.targetPosition);
                    step.duration = Mathf.Max(0f, EditorGUILayout.FloatField("持续时间", step.duration));
                    step.easing = DrawEventEasingPopup("缓动方式", step.easing);
                    break;
                case LevelFlowStepType.SetVariable:
                    if (step.setVariable == null) step.setVariable = new VariableSetAction();
                    DrawSingleVariableSetAction(step.setVariable);
                    break;
                case LevelFlowStepType.PlayStory:
                    step.storyId = DrawStoryIdField("过场 ID", step.storyId);
                    DrawStoryCastBindings(step);
                    break;
                case LevelFlowStepType.WaitForPlayerSafe:
                    EditorGUILayout.HelpBox("等待角色完成跳跃、反弹或击退，超时为 5 秒。", MessageType.None);
                    break;
                case LevelFlowStepType.ResumeCameraFollow:
                    EditorGUILayout.HelpBox("释放流程镜头接管，恢复跟随玩家。", MessageType.None);
                    break;
            }
            EditorGUILayout.EndVertical();
        }

        if (GUILayout.Button("+ 添加步骤"))
            flow.steps.Add(new LevelFlowStep());

        EditorGUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("复制事件"))
            DuplicateSelectedFlow();
        if (GUILayout.Button("删除事件"))
            DeleteSelectedObject();
        EditorGUILayout.EndHorizontal();

        if (GUI.changed) EditorUtility.SetDirty(_level);
    }

    void DrawStoryCastBindings(LevelFlowStep step)
    {
        var story = GetStorySequence(step.storyId);
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("剧情演出", EditorStyles.boldLabel);
        if (story == null)
        {
            EditorGUILayout.HelpBox("选择有效的剧情后才能查看演出和绑定演员。", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(
            $"Cue 在剧情编辑器中配置；本步骤只负责绑定本关演员。共 {story.performanceCues?.Count ?? 0} 个 Cue。",
            EditorStyles.wordWrappedMiniLabel);
        if (GUILayout.Button("编辑剧情演出", GUILayout.Width(110f)))
            StoryEditorWindow.OpenStory(_level.storyCollectionJson, step.storyId);
        EditorGUILayout.EndHorizontal();

        if (story.performanceCues == null || story.performanceCues.Count == 0)
        {
            EditorGUILayout.HelpBox("这段剧情还没有演出 Cue，可点击“编辑剧情演出”添加。", MessageType.Info);
            step.storyPerformanceScripts = new List<PerformanceScript>();
            step.storyCastBindings = new List<PerformanceActorBinding>();
            return;
        }

        var scripts = new List<PerformanceScript>();
        foreach (var cue in story.performanceCues)
        {
            if (cue == null) continue;
            var dialogue = story.dialogues?.FirstOrDefault(item =>
                item != null && item.id == cue.dialogueId);
            string summary = dialogue == null
                ? $"不存在的台词 #{cue.dialogueId}"
                : $"#{dialogue.id} {dialogue.speakerName}: {TruncateStoryText(dialogue.content, 18)}";
            string timing = cue.triggerTiming == StoryPerformanceCueTriggerTiming.AfterAdvanceInput
                ? "点击下一句后"
                : "台词出现时";
            var script = FindPerformanceScriptAsset(cue.scriptId);
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                $"{summary}  →  {cue.scriptId}（{timing}）",
                EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUI.DisabledScope(script == null))
                if (GUILayout.Button("编辑演出", GUILayout.Width(72f)))
                    PerformanceScriptEditorWindow.Open(script);
            EditorGUILayout.EndHorizontal();

            if (script == null)
                EditorGUILayout.HelpBox($"找不到演出脚本 '{cue.scriptId}'。", MessageType.Error);
            else if (!scripts.Contains(script))
                scripts.Add(script);
        }

        if (!AreSameScripts(step.storyPerformanceScripts, scripts))
        {
            step.storyPerformanceScripts = scripts;
            EditorUtility.SetDirty(_level);
        }

        if (step.storyCastBindings == null)
            step.storyCastBindings = new List<PerformanceActorBinding>();

        var slots = scripts
            .SelectMany(script => (script.actorSlots ?? new List<PerformanceActorSlot>())
                .Where(slot => slot != null && !string.IsNullOrWhiteSpace(slot.slotId))
                .Select(slot => (script, slot)))
            .ToList();
        step.storyCastBindings.RemoveAll(binding => binding == null ||
            slots.All(entry => entry.slot.slotId != binding.slotId ||
                (!string.IsNullOrEmpty(binding.scriptId) &&
                 entry.script.scriptId != binding.scriptId)));

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("本关演员绑定", EditorStyles.miniBoldLabel);
        foreach (var entry in slots)
        {
            var script = entry.script;
            var slot = entry.slot;
            var binding = step.storyCastBindings.FirstOrDefault(item =>
                item.scriptId == script.scriptId && item.slotId == slot.slotId);
            if (binding == null)
            {
                var legacy = step.storyCastBindings.FirstOrDefault(item =>
                    string.IsNullOrEmpty(item.scriptId) && item.slotId == slot.slotId);
                binding = new PerformanceActorBinding
                {
                    scriptId = script.scriptId,
                    slotId = slot.slotId,
                    targetType = legacy?.targetType ?? PerformanceActorTargetType.Player,
                    elementId = legacy?.elementId,
                    environmentActorId = legacy?.environmentActorId,
                    idleAnimationOverride = legacy?.idleAnimationOverride,
                    moveAnimationOverride = legacy?.moveAnimationOverride
                };
                step.storyCastBindings.Add(binding);
            }

            string displayName = string.IsNullOrEmpty(slot.displayName) ? slot.slotId : slot.displayName;
            string slotLabel = $"{displayName} [{script.scriptId}]";
            var labels = new List<string> { "主角" };
            var elements = (_level.elements ?? new List<LevelElement>())
                .Where(element => element != null && !string.IsNullOrEmpty(element.elementId)).ToList();
            labels.AddRange(elements.Select(element =>
                $"{element.displayName} ({element.elementId})"));
            var environmentActors = (_level.environmentActors ?? new List<EnvironmentActorData>())
                .Where(actor => actor != null && !string.IsNullOrEmpty(actor.actorId)).ToList();
            labels.AddRange(environmentActors.Select(actor =>
                $"环境角色: {actor.displayName} ({actor.actorId})"));

            int current = 0;
            if (binding.targetType == PerformanceActorTargetType.LevelElement)
            {
                int elementIndex = elements.FindIndex(element => element.elementId == binding.elementId);
                current = elementIndex >= 0 ? elementIndex + 1 : 0;
            }
            else if (binding.targetType == PerformanceActorTargetType.EnvironmentActor)
            {
                int actorIndex = environmentActors.FindIndex(actor => actor.actorId == binding.environmentActorId);
                current = actorIndex >= 0 ? 1 + elements.Count + actorIndex : 0;
            }
            int selected = EditorGUILayout.Popup(slotLabel, current, labels.ToArray());
            if (selected == 0)
            {
                binding.targetType = PerformanceActorTargetType.Player;
                binding.elementId = "";
                binding.environmentActorId = "";
            }
            else if (selected <= elements.Count)
            {
                binding.targetType = PerformanceActorTargetType.LevelElement;
                binding.elementId = elements[selected - 1].elementId;
                binding.environmentActorId = "";
            }
            else
            {
                binding.targetType = PerformanceActorTargetType.EnvironmentActor;
                binding.elementId = "";
                binding.environmentActorId = environmentActors[selected - 1 - elements.Count].actorId;
            }
            binding.idleAnimationOverride = EditorGUILayout.TextField(
                $"{slotLabel} 待机覆盖", binding.idleAnimationOverride);
            binding.moveAnimationOverride = EditorGUILayout.TextField(
                $"{slotLabel} 移动动画覆盖", binding.moveAnimationOverride);
        }

        foreach (var script in scripts)
        foreach (var clip in script.clips ?? new List<PerformanceClip>())
        {
            if (clip == null) continue;
            var binding = step.storyCastBindings.FirstOrDefault(item =>
                item != null && item.scriptId == script.scriptId &&
                item.slotId == clip.actorSlotId);
            string animationName = null;
            if (clip.clipType == PerformanceClipType.PlayAnimation)
                animationName = clip.animationName;
            else if (clip.clipType == PerformanceClipType.MoveActor && clip.playMoveAnimation)
            {
                var slot = script.FindSlot(clip.actorSlotId);
                animationName = !string.IsNullOrEmpty(clip.moveAnimationOverride)
                    ? clip.moveAnimationOverride
                    : !string.IsNullOrEmpty(binding?.moveAnimationOverride)
                        ? binding.moveAnimationOverride
                        : slot?.defaultMoveAnimation;
            }
            if (!string.IsNullOrEmpty(animationName) &&
                !PerformanceAnimationExists(binding, animationName))
                EditorGUILayout.HelpBox(
                    $"槽位 '{clip.actorSlotId}' 的目标预制体中找不到动画 '{animationName}'。",
                    MessageType.Warning);
        }

        step.storyCastBindings.RemoveAll(binding =>
            binding != null && string.IsNullOrEmpty(binding.scriptId) &&
            slots.Any(entry => entry.slot.slotId == binding.slotId));

        if (step.storyPerformanceCues != null && step.storyPerformanceCues.Count > 0)
            EditorGUILayout.HelpBox(
                "检测到旧版关卡内 Cue。新剧情 Cue 存在时运行时会使用剧情编辑器中的配置；旧数据仍保留用于兼容。",
                MessageType.Warning);
    }

    static bool AreSameScripts(List<PerformanceScript> current, List<PerformanceScript> expected)
    {
        current ??= new List<PerformanceScript>();
        expected ??= new List<PerformanceScript>();
        return current.Count == expected.Count && !current.Where((script, index) =>
            script != expected[index]).Any();
    }

    static PerformanceScript FindPerformanceScriptAsset(string scriptId)
    {
        if (string.IsNullOrWhiteSpace(scriptId)) return null;
        return AssetDatabase.FindAssets("t:PerformanceScript")
            .Select(guid => AssetDatabase.LoadAssetAtPath<PerformanceScript>(
                AssetDatabase.GUIDToAssetPath(guid)))
            .FirstOrDefault(asset => asset != null && asset.scriptId == scriptId);
    }

    bool PerformanceAnimationExists(PerformanceActorBinding binding, string animationName)
    {
        if (binding == null || string.IsNullOrEmpty(animationName)) return false;
        GameObject prefab = null;
        if (binding.targetType == PerformanceActorTargetType.Player)
            prefab = _level.playerPrefab;
        else if (binding.targetType == PerformanceActorTargetType.LevelElement)
            prefab = _level.elements?.FirstOrDefault(element =>
                element != null && element.elementId == binding.elementId)?.prefab;
        else
            prefab = _level.environmentActors?.FirstOrDefault(actor =>
                actor != null && actor.actorId == binding.environmentActorId)?.prefab;
        if (prefab == null) return false;

        var spine = prefab.GetComponentInChildren<SkeletonAnimation>(true);
        var skeletonData = spine != null && spine.skeletonDataAsset != null
            ? spine.skeletonDataAsset.GetSkeletonData(true)
            : null;
        if (skeletonData?.FindAnimation(animationName) != null)
            return true;

        var animator = prefab.GetComponentInChildren<Animator>(true);
        return animator != null && animator.runtimeAnimatorController != null &&
               animator.runtimeAnimatorController.animationClips.Any(clip =>
                   clip != null && clip.name == animationName);
    }

    StorySequence GetStorySequence(string storyId)
    {
        if (_level?.storyCollectionJson == null || string.IsNullOrEmpty(storyId)) return null;
        try
        {
            var collection = JsonUtility.FromJson<StoryDataCollection>(_level.storyCollectionJson.text);
            return collection?.stories?.FirstOrDefault(story => story != null && story.storyId == storyId);
        }
        catch
        {
            return null;
        }
    }

    static string TruncateStoryText(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
    }

    void DrawSingleVariableSetAction(VariableSetAction action)
    {
        var names = _level.variables.Select(v => v.variableName).ToList();
        if (names.Count == 0)
        {
            EditorGUILayout.HelpBox("请先定义关卡变量。", MessageType.Warning);
            return;
        }
        int index = Mathf.Max(0, names.IndexOf(action.variableName));
        index = EditorGUILayout.Popup("变量", index, names.ToArray());
        action.variableName = names[index];
        action.stringValue = EditorGUILayout.TextField("值", action.stringValue);
    }

    LevelFlowStepType DrawEventStepTypePopup(string label, LevelFlowStepType current)
    {
        var values = new[]
        {
            LevelFlowStepType.WaitForPlayerSafe,
            LevelFlowStepType.Wait,
            LevelFlowStepType.SetVariable,
            LevelFlowStepType.MovePlayer,
            LevelFlowStepType.MoveCamera,
            LevelFlowStepType.ResumeCameraFollow,
            LevelFlowStepType.PlayStory
        };
        var labels = new[]
        {
            "等待主角安全",
            "等待指定时间",
            "设置关卡变量",
            "主角自动移动",
            "镜头移动",
            "恢复镜头跟随",
            "播放过场"
        };
        int index = Mathf.Max(0, Array.IndexOf(values, current));
        index = EditorGUILayout.Popup(label, index, labels);
        return values[index];
    }

    LevelFlowEasing DrawEventEasingPopup(string label, LevelFlowEasing current)
    {
        var values = new[] { LevelFlowEasing.Linear, LevelFlowEasing.SmoothStep };
        var labels = new[] { "线性", "平滑过渡" };
        int index = Mathf.Max(0, Array.IndexOf(values, current));
        index = EditorGUILayout.Popup(label, index, labels);
        return values[index];
    }

    static LevelFlowStep CloneFlowStep(LevelFlowStep source)
    {
        return new LevelFlowStep
        {
            stepType = source.stepType,
            duration = source.duration,
            targetPosition = source.targetPosition,
            speed = source.speed,
            tolerance = source.tolerance,
            easing = source.easing,
            storyId = source.storyId,
            storyCastBindings = source.storyCastBindings?.Where(binding => binding != null)
                .Select(ClonePerformanceActorBinding).ToList()
                ?? new List<PerformanceActorBinding>(),
            storyPerformanceScripts = source.storyPerformanceScripts?.Where(script => script != null).ToList()
                ?? new List<PerformanceScript>(),
            storyPerformanceCues = source.storyPerformanceCues?.Select(CloneStoryPerformanceCue).ToList()
                ?? new List<StoryPerformanceCue>(),
            setVariable = source.setVariable == null ? new VariableSetAction() : new VariableSetAction
            {
                variableName = source.setVariable.variableName,
                stringValue = source.setVariable.stringValue
            }
        };
    }

    static StoryPerformanceCue CloneStoryPerformanceCue(StoryPerformanceCue source)
    {
        return new StoryPerformanceCue
        {
            dialogueId = source.dialogueId,
            delay = source.delay,
            performanceScript = source.performanceScript,
            blockDialogueAdvance = source.blockDialogueAdvance,
            triggerTiming = source.triggerTiming,
            actorBindings = source.actorBindings?.Where(binding => binding != null)
                .Select(ClonePerformanceActorBinding).ToList()
                ?? new List<PerformanceActorBinding>()
        };
    }

    static PerformanceActorBinding ClonePerformanceActorBinding(PerformanceActorBinding source)
    {
        return new PerformanceActorBinding
        {
            scriptId = source.scriptId,
            slotId = source.slotId,
            targetType = source.targetType,
            elementId = source.elementId,
            environmentActorId = source.environmentActorId,
            idleAnimationOverride = source.idleAnimationOverride,
            moveAnimationOverride = source.moveAnimationOverride
        };
    }

    void DuplicateSelectedFlow()
    {
        if (_selectedFlowIndex < 0 || _selectedFlowIndex >= _level.events.Count) return;
        var source = _level.events[_selectedFlowIndex];
        var copy = new LevelFlowData
        {
            flowId = GetUniqueFlowId(source.flowId + "_copy"),
            triggerMode = source.triggerMode,
            positionX = source.positionX,
            triggerFromLeft = source.triggerFromLeft,
            triggerOnce = source.triggerOnce,
            triggerConditions = source.triggerConditions?.Select(c => new LevelVariableCondition
            {
                variableName = c.variableName,
                mode = c.mode,
                compareValue = c.compareValue
            }).ToList() ?? new List<LevelVariableCondition>(),
            steps = source.steps?.Select(CloneFlowStep).ToList() ?? new List<LevelFlowStep>()
        };
        Undo.RecordObject(_level, "Duplicate Level Flow");
        _level.events.Insert(_selectedFlowIndex + 1, copy);
        SelectFlow(_selectedFlowIndex + 1);
        EditorUtility.SetDirty(_level);
    }

    string GetUniqueFlowId(string requested)
    {
        string root = string.IsNullOrWhiteSpace(requested) ? "flow" : requested;
        string candidate = root;
        int suffix = 2;
        while (_level.events.Any(f => f != null && f.flowId == candidate))
            candidate = $"{root}_{suffix++}";
        return candidate;
    }

    static void Swap<T>(List<T> list, int a, int b)
    {
        T value = list[a];
        list[a] = list[b];
        list[b] = value;
    }

    void DrawGroupMembersEditor(ElementGroup group)
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("组成员", EditorStyles.boldLabel);
        if (_level.elements.Count == 0)
        {
            EditorGUILayout.HelpBox("当前关卡还没有对象。", MessageType.Info);
            return;
        }

        foreach (var el in _level.elements)
        {
            string label = string.IsNullOrEmpty(el.displayName) ? el.elementId : el.displayName;
            bool has = el.IsInGroup(group.groupId);
            bool next = EditorGUILayout.ToggleLeft(label, has);
            if (next != has)
            {
                Undo.RecordObject(_level, "Edit Group Members");
                if (next)
                    el.AddGroup(group.groupId);
                else
                    el.RemoveGroup(group.groupId);
                EditorUtility.SetDirty(_level);
            }
        }
    }

    void DrawGroupProperties()
    {
        var g = _level.groups.Find(x => x.groupId == _selectedGroupId);
        if (g == null) return;

        Undo.RecordObject(_level, "Edit Group");

        EditorGUILayout.LabelField("元素组", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        g.groupName = EditorGUILayout.TextField("组名", g.groupName);
        g.triggerMode = DrawElementGroupTriggerModePopup("触发方式", g.triggerMode);
        if (g.triggerMode == ElementGroupTriggerMode.Position)
            g.triggerPositionX = EditorGUILayout.FloatField("触发位置 X", g.triggerPositionX);
        else if (g.triggerMode == ElementGroupTriggerMode.None)
            EditorGUILayout.HelpBox("无触发组不会控制对象生成，只作为逻辑集合使用，例如组内敌人全清后设置变量。", MessageType.Info);
        else
            EditorGUILayout.HelpBox("条件满足时触发该组，生成组内未出现的元素。", MessageType.Info);
        g.mustClearToProceed = EditorGUILayout.Toggle("必须清怪", g.mustClearToProceed);

        int groupElements = _level.elements.Count(e => e.IsInGroup(g.groupId));
        EditorGUILayout.LabelField("组内元素数", groupElements.ToString());

        DrawGroupMembersEditor(g);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("触发条件", EditorStyles.boldLabel);
        DrawConditions(g.triggerConditions);

        EditorGUILayout.Space(10);
        DrawVariableSetActions(g.onAllEnemiesClearedSetVariables, "组内敌人全清后设置变量");

        EditorGUILayout.Space(10);
        if (GUILayout.Button("删除组", GUILayout.Height(25)))
            DeleteSelectedObject();

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

            if (varNames.Count > 0)
            {
                int varIdx = varNames.IndexOf(c.variableName);
                varIdx = EditorGUILayout.Popup(varIdx >= 0 ? varIdx : 0, varNames.ToArray(), GUILayout.Width(100));
                if (varIdx >= 0) c.variableName = varNames[varIdx];
            }
            else
            {
                EditorGUILayout.LabelField(string.IsNullOrEmpty(c.variableName) ? "(无变量)" : c.variableName, GUILayout.Width(100));
            }

            c.mode = DrawConditionCompareModePopup(c.mode, GUILayout.Width(100));
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

    static BackgroundMode DrawBackgroundModePopup(string label, BackgroundMode value)
    {
        var values = new[]
        {
            BackgroundMode.SingleInfiniteScroll,
            BackgroundMode.ParallaxLayers,
            BackgroundMode.SequentialTiles
        };
        var labels = new[] { "单图无限滚动", "多层视差", "顺序铺图" };
        int index = System.Array.IndexOf(values, value);
        index = EditorGUILayout.Popup(label, Mathf.Max(0, index), labels);
        return values[Mathf.Clamp(index, 0, values.Length - 1)];
    }

    static ElementType DrawElementTypePopup(string label, ElementType value)
    {
        var values = new[] { ElementType.Enemy, ElementType.Item, ElementType.Obstacle };
        var labels = new[] { "敌人", "道具", "障碍物" };
        int index = System.Array.IndexOf(values, value);
        index = EditorGUILayout.Popup(label, Mathf.Max(0, index), labels);
        return values[Mathf.Clamp(index, 0, values.Length - 1)];
    }

    static LevelVariableType DrawVariableTypePopup(LevelVariableType value, params GUILayoutOption[] options)
    {
        var values = new[]
        {
            LevelVariableType.Bool,
            LevelVariableType.Int,
            LevelVariableType.Float,
            LevelVariableType.String
        };
        var labels = new[] { "布尔", "整数", "小数", "文本" };
        int index = System.Array.IndexOf(values, value);
        index = EditorGUILayout.Popup(Mathf.Max(0, index), labels, options);
        return values[Mathf.Clamp(index, 0, values.Length - 1)];
    }

    static LevelVariableCondition.CompareMode DrawConditionCompareModePopup(
        LevelVariableCondition.CompareMode value,
        params GUILayoutOption[] options)
    {
        var values = new[]
        {
            LevelVariableCondition.CompareMode.Equals,
            LevelVariableCondition.CompareMode.NotEquals,
            LevelVariableCondition.CompareMode.Greater,
            LevelVariableCondition.CompareMode.GreaterOrEqual,
            LevelVariableCondition.CompareMode.Less,
            LevelVariableCondition.CompareMode.LessOrEqual,
            LevelVariableCondition.CompareMode.Contains,
            LevelVariableCondition.CompareMode.IsTrue,
            LevelVariableCondition.CompareMode.IsFalse
        };
        var labels = new[]
        {
            "等于", "不等于", "大于", "大于或等于", "小于", "小于或等于", "包含", "为真", "为假"
        };
        int index = System.Array.IndexOf(values, value);
        index = EditorGUILayout.Popup(Mathf.Max(0, index), labels, options);
        return values[Mathf.Clamp(index, 0, values.Length - 1)];
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
            if (varNames.Count > 0)
            {
                int varIdx = varNames.IndexOf(a.variableName);
                varIdx = EditorGUILayout.Popup(varIdx >= 0 ? varIdx : 0, varNames.ToArray(), GUILayout.Width(100));
                if (varIdx >= 0) a.variableName = varNames[varIdx];
            }
            else
            {
                EditorGUILayout.LabelField(string.IsNullOrEmpty(a.variableName) ? "(无变量)" : a.variableName, GUILayout.Width(100));
            }
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
        if (_showBottomList)
        {
            GUILayout.Space(8);
            GUILayout.Label("搜索", GUILayout.Width(30));
            _elementSearch = GUILayout.TextField(_elementSearch, EditorStyles.toolbarSearchField, GUILayout.Width(160));
            GUILayout.Label($"共 {_level.elements.Count} 个元素", EditorStyles.miniLabel);
        }
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
        GUILayout.Label("操作", GUILayout.Width(48));
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < _level.elements.Count; i++)
        {
            var el = _level.elements[i];
            if (!string.IsNullOrWhiteSpace(_elementSearch) &&
                (el.displayName == null || el.displayName.IndexOf(_elementSearch, StringComparison.OrdinalIgnoreCase) < 0))
                continue;
            var bg = i % 2 == 0 ? GUI.skin.box : GUIStyle.none;
            EditorGUILayout.BeginHorizontal(bg);

            GUILayout.Label(i.ToString(), GUILayout.Width(30));
            if (GUILayout.Button(el.displayName, EditorStyles.miniButton, GUILayout.Width(100)))
                SelectElement(el.elementId);
            GUILayout.Label(el.elementType.ToString(), GUILayout.Width(50));
            GUILayout.Label(el.position.x.ToString("F1"), GUILayout.Width(55));
            GUILayout.Label(el.position.y.ToString("F1"), GUILayout.Width(55));
            GUILayout.Label($"{el.appearDelay:F1}s", GUILayout.Width(45));
            GUILayout.Label(GetElementGroupNames(el), GUILayout.Width(80));
            GUILayout.Label(el.appearConditions.Count > 0 ? "有" : "无", GUILayout.Width(50));
            if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(48)))
            {
                SelectElement(el.elementId);
                CenterCanvasOn(el.position);
            }

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

        if (e.keyCode == KeyCode.Escape)
        {
            if (EditorGUIUtility.editingTextField)
                GUI.FocusControl(null);
            else if (_isPlacingMode)
                _isPlacingMode = false;
            else if (HasObjectSelection())
                ReturnToParent();
            e.Use();
            Repaint();
            return;
        }

        if (!EditorGUIUtility.editingTextField && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace))
        {
            if (DeleteSelectedObject()) e.Use();
            Repaint();
            return;
        }

        if (e.control && e.keyCode == KeyCode.S)
        {
            SaveLevel();
            e.Use();
        }
    }

    bool DeleteSelectedObject()
    {
        if (!string.IsNullOrEmpty(_selectedElementId))
        {
            var element = _level.elements.Find(x => x.elementId == _selectedElementId);
            if (element == null) return false;
            Undo.RecordObject(_level, "Delete Element");
            _level.elements.Remove(element);
            ShowSection(InspectorSection.Overview);
        }
        else if (!string.IsNullOrEmpty(_selectedEnvironmentActorId))
        {
            var actor = _level.environmentActors.Find(x => x.actorId == _selectedEnvironmentActorId);
            if (actor == null) return false;
            Undo.RecordObject(_level, "Delete Environment Actor");
            _level.environmentActors.Remove(actor);
            ShowSection(InspectorSection.Background);
        }
        else if (_selectedFlowIndex >= 0 && _selectedFlowIndex < (_level.events?.Count ?? 0))
        {
            var flow = _level.events[_selectedFlowIndex];
            if (!EditorUtility.DisplayDialog("删除演出事件", $"确定删除 '{flow.flowId}'？", "删除", "取消")) return false;
            Undo.RecordObject(_level, "Delete Performance Event");
            _level.events.RemoveAt(_selectedFlowIndex);
            ShowSection(InspectorSection.Events);
        }
        else if (_selectedStoryTriggerIndex >= 0 && _selectedStoryTriggerIndex < _level.storyTriggers.Count)
        {
            var trigger = _level.storyTriggers[_selectedStoryTriggerIndex];
            if (!EditorUtility.DisplayDialog("删除过场触发", $"确定删除 '{trigger.storyId}' 的触发器？", "删除", "取消")) return false;
            Undo.RecordObject(_level, "Delete Story Trigger");
            _level.storyTriggers.RemoveAt(_selectedStoryTriggerIndex);
            ShowSection(InspectorSection.Events);
        }
        else if (!string.IsNullOrEmpty(_selectedGroupId))
        {
            var group = _level.FindGroup(_selectedGroupId);
            if (group == null || !EditorUtility.DisplayDialog("删除元素组", $"确定删除 '{group.groupName}'？组内元素不会被删除。", "删除", "取消")) return false;
            Undo.RecordObject(_level, "Delete Group");
            foreach (var element in _level.elements) element.RemoveGroup(group.groupId);
            _level.groups.Remove(group);
            ShowSection(InspectorSection.Overview);
        }
        else return false;

        EditorUtility.SetDirty(_level);
        return true;
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
        ShowSection(InspectorSection.Overview);
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
            ShowSection(InspectorSection.Overview);
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
        EnsureSceneInBuildSettings(path);
        Debug.Log($"关卡已导出到: {path}");

        EditorSceneManager.OpenScene(templatePath, OpenSceneMode.Single);
    }

    static void EnsureSceneInBuildSettings(string scenePath)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        int existingIndex = scenes.FindIndex(scene =>
            string.Equals(scene.path, scenePath, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            if (!scenes[existingIndex].enabled)
            {
                scenes[existingIndex] = new EditorBuildSettingsScene(scenePath, true);
                EditorBuildSettings.scenes = scenes.ToArray();
            }
            return;
        }

        scenes.Add(new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log($"[LevelEditorWindow] 已将导出场景加入 Build Settings: {scenePath}");
    }

    // ================================================================
    // 预制体扫描
    // ================================================================

    void ScanPrefabs()
    {
        _scannedEnemies.Clear();
        _scannedItems.Clear();
        _scannedObstacles.Clear();
        _scannedEnvironmentActors.Clear();

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
            if (prefab.GetComponent<EnvironmentActorMarker>() != null)
                _scannedEnvironmentActors.Add(new ElementPrefabEntry { displayName = prefab.name, prefab = prefab });
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
