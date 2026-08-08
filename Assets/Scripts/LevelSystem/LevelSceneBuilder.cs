using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public class LevelSceneBuilder : MonoBehaviour
{
    [Header("关卡数据")]
    public LevelData levelData;

    [Header("运行时引用（自动查找）")]
    public Camera mainCamera;
    public Transform playerTransform;

    [Header("事件")]
    public UnityEngine.Events.UnityEvent OnLevelReady;
    public UnityEngine.Events.UnityEvent OnLevelComplete;
    public UnityEngine.Events.UnityEvent OnLevelFailed;

    // 内部组件
    private LevelVariableManager _variableManager;
    private LevelCameraController _cameraController;
    private LevelFlowRunner _flowRunner;
    private PerformanceRunner _performanceRunner;
    private PlayerHP _playerHp;
    private Coroutine _gameOverTransitionCoroutine;
    private Coroutine _gameClearTransitionCoroutine;

    // 生成追踪
    private readonly List<GameObject> _spawnedObjects = new List<GameObject>();
    private readonly Dictionary<string, List<GameObject>> _groupEnemyInstances = new Dictionary<string, List<GameObject>>();
    private readonly HashSet<string> _triggeredGroups = new HashSet<string>();
    private readonly HashSet<string> _clearedGroups = new HashSet<string>();
    private readonly HashSet<string> _finishedSpawningGroups = new HashSet<string>();
    private readonly HashSet<string> _spawnedElementIds = new HashSet<string>();
    private readonly Dictionary<string, GameObject> _elementInstances = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, GameObject> _environmentActorInstances = new Dictionary<string, GameObject>();
    private readonly HashSet<string> _finishedStoryTriggers = new HashSet<string>();

    // 元素缓存
    private Dictionary<string, List<LevelElement>> _elementsByGroup;
    private List<LevelElement> _ungroupedElements;
    private List<LevelElement> _pendingConditionElements;
    private List<StoryTriggerPoint> _pendingStoryTriggers;
    private readonly HashSet<StoryTriggerPoint> _createdStoryTriggers = new HashSet<StoryTriggerPoint>();
    private readonly HashSet<StoryTriggerPoint> _completedStoryTriggers = new HashSet<StoryTriggerPoint>();

    // 关卡状态
    private bool _levelComplete;
    private bool _levelFailed;

    // 战斗锁屏
    private ElementGroup _activeLockGroup;

    void Start()
    {
        if (levelData == null)
        {
            Debug.LogError("[LevelSceneBuilder] LevelData 为空!");
            return;
        }

        levelData.MigrateLegacyStoryTriggers();
        levelData.MigrateLegacyBackground();

        _variableManager = GetComponent<LevelVariableManager>();
        if (_variableManager == null)
            _variableManager = gameObject.AddComponent<LevelVariableManager>();

        _variableManager.Initialize(levelData.variables);
        _variableManager.OnVariableChanged("*", OnAnyVariableChanged);

        BuildSceneInfrastructure();
        CreateBackground();
        CreatePlayer();
        CreateEnvironmentActors();
        BuildLevel();

        _flowRunner = GetComponent<LevelFlowRunner>();
        if (_flowRunner == null)
            _flowRunner = gameObject.AddComponent<LevelFlowRunner>();
        _performanceRunner = GetComponent<PerformanceRunner>();
        if (_performanceRunner == null)
            _performanceRunner = gameObject.AddComponent<PerformanceRunner>();
        _performanceRunner.Initialize(this, _cameraController);
        _flowRunner.Initialize(levelData, _variableManager,
            playerTransform != null ? playerTransform.GetComponent<PlayerMovement>() : null,
            _cameraController, _performanceRunner);

        Enemy.OnEnemyDied += HandleEnemyDeath;

        // 备用：定时检查关卡结束，防止 Update 因异常情况漏检
        StartCoroutine(PeriodicCheckLevelEnd());

        OnLevelReady?.Invoke();
        StartCoroutine(TriggerStoriesByModeNextFrame(StoryTriggerMode.LevelStart));
        Debug.Log($"[LevelSceneBuilder] 关卡 '{levelData.levelName}' 构建完成");
    }

    IEnumerator TriggerStoriesByModeNextFrame(StoryTriggerMode mode)
    {
        yield return null;
        LoadStoryCollection();
        _flowRunner?.TriggerMode(mode);
    }

    void LoadStoryCollection()
    {
        if (levelData.storyCollectionJson == null) return;

        var manager = StoryManager.Instance ?? GameObject.FindObjectOfType<StoryManager>();
        if (manager == null)
        {
            Debug.LogWarning("[LevelSceneBuilder] LevelData has a story collection, but no StoryManager exists in the scene.");
            return;
        }

        manager.LoadStoryData(levelData.storyCollectionJson);
    }

    IEnumerator PeriodicCheckLevelEnd()
    {
        var wait = new WaitForSeconds(0.1f);
        while (!_levelComplete && !_levelFailed)
        {
            if (playerTransform == null)
                playerTransform = ValidatePlayer();
            if (playerTransform != null)
                CheckLevelEnd();
            yield return wait;
        }
    }

    void Update()
    {
        if (_levelComplete || _levelFailed) return;

        // 健壮的玩家引用：优先用缓存，丢失时按标签重新查找
        if (playerTransform == null)
            playerTransform = ValidatePlayer();
        if (playerTransform == null) return;

        try
        {
            CheckGroupTriggers();
            CheckLevelEnd();
            UpdateCameraLock();
            _flowRunner?.Tick(playerTransform.position.x);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LevelSceneBuilder] Update 异常: {e}\n{e.StackTrace}");
        }
    }

    Transform ValidatePlayer()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            if (_cameraController != null)
                _cameraController.target = player.transform;
            ConfigurePlayerBounds(player.GetComponent<PlayerMovement>());
            return player.transform;
        }
        Debug.LogWarning("[LevelSceneBuilder] 找不到玩家，等待玩家生成...");
        return null;
    }

    void OnDrawGizmosSelected()
    {
        if (levelData == null) return;

        // 关卡结束线
        Gizmos.color = Color.green;
        Vector3 endTop = new Vector3(levelData.levelEndPositionX, 10f, 0f);
        Vector3 endBottom = new Vector3(levelData.levelEndPositionX, -10f, 0f);
        Gizmos.DrawLine(endBottom, endTop);

        // 玩家出生点
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(new Vector3(levelData.playerSpawnPosition.x, levelData.playerSpawnPosition.y, 0f), 0.5f);

        // 元素组触发位置
        if (levelData.groups != null)
        {
            Gizmos.color = Color.yellow;
            foreach (var group in levelData.groups)
            {
                if (group == null) continue;
                Vector3 gTop = new Vector3(group.triggerPositionX, 10f, 0f);
                Vector3 gBottom = new Vector3(group.triggerPositionX, -10f, 0f);
                Gizmos.DrawLine(gBottom, gTop);
            }
        }
    }

    // ================================================================
    // 场景基础设施
    // ================================================================

    void BuildSceneInfrastructure()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera != null)
        {
            _cameraController = mainCamera.GetComponent<LevelCameraController>();
            if (_cameraController == null)
                _cameraController = mainCamera.gameObject.AddComponent<LevelCameraController>();
            _cameraController.target = playerTransform;
            _cameraController.minX = -20f;
            _cameraController.maxX = levelData.levelLength + 20f;
            var backgroundSettings = levelData.backgroundSettings;
            if (backgroundSettings != null && backgroundSettings.constrainCameraToBounds)
            {
                _cameraController.SetWorldBounds(
                    backgroundSettings.cameraBoundsStartX,
                    backgroundSettings.cameraBoundsEndX);

                float viewportWidth = _cameraController.GetCameraHalfWidth() * 2f;
                float boundsWidth =
                    backgroundSettings.cameraBoundsEndX - backgroundSettings.cameraBoundsStartX;
                if (boundsWidth < viewportWidth)
                    Debug.LogWarning(
                        $"[LevelSceneBuilder] 摄像机边界宽度({boundsWidth:0.###})小于当前画面宽度({viewportWidth:0.###})，" +
                        "相机将固定在边界中点，画面仍会超出边界。");
            }
            else
            {
                _cameraController.ClearWorldBounds();
            }
            _cameraController.deadZone = levelData.cameraDeadZone;
            _cameraController.useCustomInitialPosition = levelData.useCustomInitialCameraPosition;
            _cameraController.initialPosition = levelData.initialCameraPosition;
            if (levelData.useCustomInitialCameraPosition)
                _cameraController.fixedY = levelData.initialCameraPosition.y;
        }
    }

    // ================================================================
    // 背景
    // ================================================================

    void CreateBackground()
    {
        var bg = levelData.backgroundSettings;
        if (bg == null) return;

        bg.MigrateLegacyData();
        if (bg.layers != null)
        {
            var root = new GameObject("LevelBackgroundLayers");
            var controller = root.AddComponent<LayeredBackgroundController>();
            controller.Initialize(mainCamera, bg.layers);
            _spawnedObjects.Add(root);
            return;
        }

        if (bg.mode == BackgroundMode.SingleInfiniteScroll && bg.singleBackground != null)
        {
            var bgGo = new GameObject("LevelBackground");
            bgGo.transform.position = new Vector3(0, 0, 10);

            var sr = bgGo.AddComponent<SpriteRenderer>();
            sr.sprite = bg.singleBackground;
            sr.sortingOrder = Mathf.Min(bg.singleSortingOrder, BackgroundSettings.DefaultSortingOrder);

            var isb = bgGo.AddComponent<InfiniteScrollBackground>();
            isb.backgroundSprite = sr;
            isb.cameraTransform = mainCamera?.transform;
            isb.fixedY = 0f;
            isb.parallaxFactor = bg.singleParallaxFactor;

            _spawnedObjects.Add(bgGo);
        }
        else if (bg.mode == BackgroundMode.SequentialTiles &&
                 bg.sequence != null &&
                 bg.sequence.Count > 0)
        {
            CreateSequentialBackground(bg);
        }
    }

    void CreateSequentialBackground(BackgroundSettings bg)
    {
        var root = new GameObject("LevelBackgroundSequence");
        float cursorX = bg.sequenceStartX;
        int tileIndex = 0;

        foreach (var entry in bg.sequence)
        {
            if (entry == null || entry.sprite == null || entry.repeatCount <= 0)
                continue;

            Sprite sprite = entry.sprite;
            float width = sprite.bounds.size.x;
            if (width <= Mathf.Epsilon)
            {
                Debug.LogWarning($"[LevelSceneBuilder] 顺序背景图片 '{sprite.name}' 的宽度无效，已跳过");
                continue;
            }

            for (int repeatIndex = 0; repeatIndex < entry.repeatCount; repeatIndex++)
            {
                var tile = new GameObject($"BackgroundTile_{tileIndex:D3}_{sprite.name}");
                tile.transform.SetParent(root.transform, false);

                // sequenceStartX 表示第一张图的左边缘；通过 bounds 抵消非居中 Pivot。
                float positionX = cursorX - sprite.bounds.min.x;
                float positionY = bg.sequenceCenterY - sprite.bounds.center.y;
                tile.transform.position = new Vector3(positionX, positionY, 10f);

                var renderer = tile.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = Mathf.Min(
                    bg.sequenceSortingOrder,
                    BackgroundSettings.DefaultSortingOrder);

                cursorX += width;
                tileIndex++;
            }
        }

        if (tileIndex == 0)
        {
            Destroy(root);
            Debug.LogWarning("[LevelSceneBuilder] 顺序背景没有可生成的图片");
            return;
        }

        _spawnedObjects.Add(root);
    }

    // ================================================================
    // 玩家
    // ================================================================

    void CreatePlayer()
    {
        if (levelData.playerPrefab == null)
        {
            Debug.LogError("[LevelSceneBuilder] playerPrefab 为空!");
            return;
        }

        var playerGo = Instantiate(levelData.playerPrefab,
            new Vector3(levelData.playerSpawnPosition.x, levelData.playerSpawnPosition.y, 0),
            Quaternion.identity);

        playerGo.name = "Player";
        playerGo.tag = "Player";

        var scale = playerGo.transform.localScale;
        scale.x = levelData.playerFaceRight ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
        playerGo.transform.localScale = scale;

        playerTransform = playerGo.transform;
        _spawnedObjects.Add(playerGo);

        if (playerGo.GetComponent<PlayerGameplaySignalHub>() == null)
            playerGo.AddComponent<PlayerGameplaySignalHub>();

        ConfigurePlayerBounds(playerGo.GetComponent<PlayerMovement>());

        if (_cameraController != null)
            _cameraController.target = playerTransform;

        // 尝试为 PlayerHP 绑定场景中的 HPBar（即使未激活）
        TryBindPlayerHPBar(playerGo);
        BindPlayerDeath(playerGo);
    }

    void BindPlayerDeath(GameObject playerGo)
    {
        if (_playerHp != null)
            _playerHp.Died -= HandlePlayerDied;

        _playerHp = playerGo.GetComponent<PlayerHP>();
        if (_playerHp == null)
            _playerHp = playerGo.GetComponentInChildren<PlayerHP>();
        if (_playerHp != null)
            _playerHp.Died += HandlePlayerDied;
    }

    void HandlePlayerDied(PlayerHP playerHp)
    {
        FailLevel();
    }

    void ConfigurePlayerBounds(PlayerMovement playerMovement)
    {
        if (playerMovement == null) return;

        var backgroundSettings = levelData?.backgroundSettings;
        if (backgroundSettings != null && backgroundSettings.constrainCameraToBounds)
        {
            playerMovement.SetLevelHorizontalBounds(
                backgroundSettings.cameraBoundsStartX,
                backgroundSettings.cameraBoundsEndX);
        }
        else
        {
            playerMovement.ClearLevelHorizontalBounds();
        }
    }

    void TryBindPlayerHPBar(GameObject playerGo)
    {
        var playerHp = playerGo.GetComponent<PlayerHP>();
        if (playerHp == null) return;

        // 通过反射检查 hpSlider 是否已赋值
        var hpSliderField = typeof(PlayerHP).GetField("hpSlider", BindingFlags.NonPublic | BindingFlags.Instance);
        var currentSlider = hpSliderField?.GetValue(playerHp) as UnityEngine.UI.Slider;
        if (currentSlider != null) return;

        // 在全部 Canvas 中查找名为 HPBar 的 Slider（包括未激活对象）
        var canvases = GameObject.FindObjectsOfType<Canvas>(true);
        foreach (var canvas in canvases)
        {
            var slider = canvas.GetComponentInChildren<UnityEngine.UI.Slider>(true);
            if (slider != null && slider.gameObject.name == "HPBar")
            {
                hpSliderField?.SetValue(playerHp, slider);
                Debug.Log($"[LevelSceneBuilder] 已为玩家绑定 HPBar: {slider.gameObject.name}");
                return;
            }
        }
    }

    void CreateEnvironmentActors()
    {
        if (levelData.environmentActors == null || playerTransform == null) return;
        var signalHub = playerTransform.GetComponent<PlayerGameplaySignalHub>();

        foreach (var actorData in levelData.environmentActors)
        {
            if (actorData == null || actorData.prefab == null ||
                string.IsNullOrWhiteSpace(actorData.actorId) ||
                _environmentActorInstances.ContainsKey(actorData.actorId))
                continue;

            var actor = Instantiate(actorData.prefab,
                new Vector3(actorData.position.x, actorData.position.y, 0f),
                Quaternion.identity);
            actor.name = string.IsNullOrWhiteSpace(actorData.displayName)
                ? actorData.prefab.name
                : actorData.displayName;

            Vector3 scale = actor.transform.localScale;
            scale.x = (actorData.faceRight ? 1f : -1f) * Mathf.Abs(scale.x);
            actor.transform.localScale = scale;
            ApplyCustomParameters(actor, actorData.customParameters);

            var controller = actor.GetComponent<EnvironmentActorController>();
            if (controller == null) controller = actor.AddComponent<EnvironmentActorController>();
            controller.Initialize(actorData, playerTransform, _variableManager, signalHub);

            _environmentActorInstances[actorData.actorId] = actor;
            _spawnedObjects.Add(actor);
        }
    }

    // ================================================================
    // 关卡构建
    // ================================================================

    void BuildLevel()
    {
        _elementsByGroup = new Dictionary<string, List<LevelElement>>();
        _ungroupedElements = new List<LevelElement>();
        _pendingConditionElements = new List<LevelElement>();
        _pendingStoryTriggers = new List<StoryTriggerPoint>();

        foreach (var el in levelData.elements)
        {
            NormalizeElementGroups(el);

            var spawnGroups = GetElementSpawnGroups(el);
            if (spawnGroups.Count == 0)
            {
                if (_variableManager.CheckAllConditions(el.appearConditions))
                    SpawnElement(el);
                else
                    _pendingConditionElements.Add(el);
            }
            else
            {
                foreach (var group in spawnGroups)
                {
                    if (!_elementsByGroup.ContainsKey(group.groupId))
                        _elementsByGroup[group.groupId] = new List<LevelElement>();
                    _elementsByGroup[group.groupId].Add(el);
                }
            }
        }

        foreach (var group in levelData.groups)
        {
            if (group.triggerMode == ElementGroupTriggerMode.None)
            {
                _triggeredGroups.Add(group.groupId);
                _finishedSpawningGroups.Add(group.groupId);
            }
        }

        // Story playback is represented by PlayStory steps in level events.
    }

    void NormalizeElementGroups(LevelElement el)
    {
        if (el == null) return;
        if (el.groupIds == null) el.groupIds = new List<string>();
        if (!string.IsNullOrEmpty(el.groupId) && !el.groupIds.Contains(el.groupId))
            el.groupIds.Add(el.groupId);
    }

    List<ElementGroup> GetElementSpawnGroups(LevelElement el)
    {
        var result = new List<ElementGroup>();
        foreach (var groupId in GetAllElementGroupIds(el))
        {
            var group = levelData.FindGroup(groupId);
            if (group != null && group.triggerMode != ElementGroupTriggerMode.None)
                result.Add(group);
        }
        return result;
    }

    List<string> GetAllElementGroupIds(LevelElement el)
    {
        var ids = new List<string>();
        if (el == null) return ids;
        if (!string.IsNullOrEmpty(el.groupId)) ids.Add(el.groupId);
        if (el.groupIds != null) ids.AddRange(el.groupIds.Where(id => !string.IsNullOrEmpty(id)));
        return ids.Distinct().ToList();
    }

    // ================================================================
    // 元素组触发
    // ================================================================

    void CheckGroupTriggers()
    {
        float playerX = playerTransform != null ? playerTransform.position.x : float.NegativeInfinity;

        foreach (var group in levelData.groups)
        {
            if (group.triggerMode == ElementGroupTriggerMode.None) continue;
            if (_triggeredGroups.Contains(group.groupId)) continue;
            if (!_variableManager.CheckAllConditions(group.triggerConditions)) continue;

            if (group.triggerMode == ElementGroupTriggerMode.Conditions ||
                (group.triggerMode == ElementGroupTriggerMode.Position && playerX >= group.triggerPositionX))
            {
                TriggerGroup(group);
            }
        }
    }

    void TriggerGroup(ElementGroup group)
    {
        _triggeredGroups.Add(group.groupId);
        Debug.Log($"[LevelSceneBuilder] 触发元素组: {group.groupName}");

        if (group.mustClearToProceed)
        {
            _activeLockGroup = group;
            if (_cameraController != null)
                _cameraController.LockCurrentView();
        }

        if (_elementsByGroup.TryGetValue(group.groupId, out var elements))
        {
            StartCoroutine(SpawnGroupElements(group, elements));
        }
        else
        {
            _finishedSpawningGroups.Add(group.groupId);
            CheckGroupCleared(group);
        }
    }

    IEnumerator SpawnGroupElements(ElementGroup group, List<LevelElement> elements)
    {
        var sorted = elements.OrderBy(e => e.appearDelay).ToList();
        float lastDelay = 0f;

        foreach (var el in sorted)
        {
            float wait = el.appearDelay - lastDelay;
            if (wait > 0f) yield return new WaitForSeconds(wait);
            lastDelay = el.appearDelay;

            if (_variableManager.CheckAllConditions(el.appearConditions))
                SpawnElement(el, group);
        }

        _finishedSpawningGroups.Add(group.groupId);
        CheckGroupCleared(group);
    }

    // ================================================================
    // 元素生成
    // ================================================================

    GameObject SpawnElement(LevelElement el, ElementGroup group = null)
    {
        if (!string.IsNullOrEmpty(el.elementId) && _spawnedElementIds.Contains(el.elementId))
            return null;

        if (el.prefab == null)
        {
            Debug.LogWarning($"[LevelSceneBuilder] 元素'{el.displayName}'的预制体为空");
            return null;
        }

        var go = Instantiate(el.prefab,
            new Vector3(el.position.x, el.position.y, 0),
            Quaternion.identity);

        go.name = el.displayName;

        var scale = go.transform.localScale;
        scale.x = el.faceRight ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
        go.transform.localScale = scale;

        ApplyCustomParameters(go, el.customParameters);
        _spawnedObjects.Add(go);
        if (!string.IsNullOrEmpty(el.elementId))
        {
            _spawnedElementIds.Add(el.elementId);
            _elementInstances[el.elementId] = go;
        }

        // 敌人追踪
        if (el.elementType == ElementType.Enemy)
        {
            var enemy = go.GetComponent<Enemy>();
            if (enemy == null) enemy = go.GetComponentInChildren<Enemy>();
            if (enemy != null)
            {
                foreach (var groupId in GetAllElementGroupIds(el))
                {
                    if (!_groupEnemyInstances.ContainsKey(groupId))
                        _groupEnemyInstances[groupId] = new List<GameObject>();
                    if (!_groupEnemyInstances[groupId].Contains(go))
                        _groupEnemyInstances[groupId].Add(go);
                }
            }

            // 自动设置玩家引用
            SetPlayerRefOnAI(go);
        }

        return go;
    }

    void SetPlayerRefOnAI(GameObject go)
    {
        var ai = go.GetComponent<MuscleP_AI_Movement>();
        if (ai != null && ai.player == null)
            ai.player = playerTransform;

        var simpleAi = go.GetComponent<EnemySimpleAI2D>();
        if (simpleAi != null && simpleAi.player == null)
            simpleAi.player = playerTransform;

        var punkAi = go.GetComponent<PunkPThrowAttack>();
        if (punkAi != null && punkAi.player == null)
            punkAi.player = playerTransform;

        var fatAi = go.GetComponent<FatP_AI_Movement>();
        if (fatAi != null)
        {
            var playerField = typeof(FatP_AI_Movement).GetField("player", BindingFlags.Public | BindingFlags.Instance);
            if (playerField != null && playerField.GetValue(fatAi) == null)
                playerField.SetValue(fatAi, playerTransform);
        }
    }

    void ApplyCustomParameters(GameObject go, List<ElementCustomParameter> parameters)
    {
        if (parameters == null || parameters.Count == 0) return;

        foreach (var param in parameters)
        {
            if (string.IsNullOrEmpty(param.componentTypeName) || string.IsNullOrEmpty(param.fieldName))
                continue;

            var comp = go.GetComponent(param.componentTypeName);
            if (comp == null) comp = go.GetComponentInChildren(Type.GetType(param.componentTypeName));
            if (comp == null) continue;

            try
            {
                var type = comp.GetType();
                var field = type.GetField(param.fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null) continue;

                object value;
                if (param.valueTypeName == "System.Int32") value = int.Parse(param.serializedValue);
                else if (param.valueTypeName == "System.Single") value = float.Parse(param.serializedValue);
                else if (param.valueTypeName == "System.Boolean") value = bool.Parse(param.serializedValue);
                else if (param.valueTypeName == "System.String") value = param.serializedValue;
                else if (param.valueTypeName == "UnityEngine.Vector2")
                {
                    var parts = param.serializedValue.Trim('(', ')').Split(',');
                    value = new Vector2(float.Parse(parts[0]), float.Parse(parts[1]));
                }
                else if (param.valueTypeName == "UnityEngine.Vector3")
                {
                    var parts = param.serializedValue.Trim('(', ')').Split(',');
                    value = new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
                }
                else continue;

                field.SetValue(comp, value);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LevelSceneBuilder] 自定义参数写入失败: {param.componentTypeName}.{param.fieldName} = {param.serializedValue}, 错误: {e.Message}");
            }
        }
    }

    // ================================================================
    // 过场触发器
    // ================================================================

    void CreateStoryTriggerObjects()
    {
        foreach (var stp in levelData.storyTriggers)
        {
            switch (stp.triggerMode)
            {
                case StoryTriggerMode.Position:
                    if (_variableManager.CheckAllConditions(stp.triggerConditions))
                        SetupStoryTrigger(stp);
                    else
                        _pendingStoryTriggers.Add(stp);
                    break;
                case StoryTriggerMode.Conditions:
                    _pendingStoryTriggers.Add(stp);
                    TryTriggerConditionStory(stp);
                    break;
            }
        }
    }

    void SetupStoryTrigger(StoryTriggerPoint stp)
    {
        if (string.IsNullOrEmpty(stp.storyId)) return;
        if (_createdStoryTriggers.Contains(stp)) return;

        var go = new GameObject($"StoryTrigger_{stp.storyId}");
        go.transform.position = new Vector3(stp.positionX, 0, 0);

        var trigger = go.AddComponent<StoryTrigger>();
        trigger.triggerType = StoryTrigger.TriggerType.Position;
        trigger.storyId = stp.storyId;
        trigger.triggerOnce = stp.triggerOnce;
        trigger.useTransformPosition = false;
        trigger.triggerPositionX = stp.positionX;
        trigger.triggerFromLeft = stp.triggerFromLeft;

        // 变量回调
        if (stp.onStoryStartSetVariables != null && stp.onStoryStartSetVariables.Count > 0)
            trigger.OnBeforeStory.AddListener(() => _variableManager.ApplySetActions(stp.onStoryStartSetVariables));
        if (stp.onStoryCompleteSetVariables != null && stp.onStoryCompleteSetVariables.Count > 0)
            trigger.OnAfterStory.AddListener(() => _variableManager.ApplySetActions(stp.onStoryCompleteSetVariables));

        _spawnedObjects.Add(go);
        _createdStoryTriggers.Add(stp);
    }

    // ================================================================
    // 关卡结束
    // ================================================================

    void CheckLevelEnd()
    {
        if (playerTransform == null) return;
        if (playerTransform.position.x < levelData.levelEndPositionX) return;
        if (!_variableManager.CheckAllConditions(levelData.endConditions)) return;

        CompleteLevel();
    }

    public void CompleteLevel()
    {
        if (_levelComplete || _levelFailed) return;
        _levelComplete = true;
        Debug.Log($"[LevelSceneBuilder] 关卡完成!");
        _flowRunner?.TriggerMode(StoryTriggerMode.LevelComplete);
        OnLevelComplete?.Invoke();

        if (_gameClearTransitionCoroutine == null)
            _gameClearTransitionCoroutine = StartCoroutine(TransitionToGameClear());
    }

    IEnumerator TransitionToGameClear()
    {
        yield return new WaitForSecondsRealtime(GameFlowService.GameClearDelay);
        GameFlowService.LoadGameClear();
    }

    public void FailLevel()
    {
        if (_levelFailed) return;
        _levelFailed = true;
        Debug.Log($"[LevelSceneBuilder] 关卡失败!");
        OnLevelFailed?.Invoke();

        if (_gameOverTransitionCoroutine == null)
            _gameOverTransitionCoroutine = StartCoroutine(TransitionToGameOver());
    }

    IEnumerator TransitionToGameOver()
    {
        yield return new WaitForSecondsRealtime(GameFlowService.GameOverDelay);
        GameFlowService.LoadGameOver();
    }

    // ================================================================
    // 战斗锁屏
    // ================================================================

    void UpdateCameraLock()
    {
        if (_activeLockGroup == null) return;

        string groupId = _activeLockGroup.groupId;
        if (!_finishedSpawningGroups.Contains(groupId))
            return;

        if (_groupEnemyInstances.TryGetValue(groupId, out var enemies))
        {
            enemies.RemoveAll(e => e == null);
            if (enemies.Count > 0)
                return;
        }

        var clearedGroup = _activeLockGroup;
        _activeLockGroup = null;
        if (_cameraController != null)
            _cameraController.Unlock();
        Debug.Log("[LevelSceneBuilder] 组内敌人清除，解锁镜头");
        ApplyGroupClearedActions(clearedGroup);
    }

    // ================================================================
    // 变量变化回调
    // ================================================================

    void OnAnyVariableChanged()
    {
        CheckGroupTriggers();
        TrySpawnPendingConditionElements();
        _flowRunner?.NotifyConditionsChanged();
    }

    void TrySpawnPendingConditionElements()
    {
        if (_pendingConditionElements == null || _pendingConditionElements.Count == 0) return;
        for (int i = _pendingConditionElements.Count - 1; i >= 0; i--)
        {
            var el = _pendingConditionElements[i];
            if (_variableManager.CheckAllConditions(el.appearConditions))
            {
                SpawnElement(el);
                _pendingConditionElements.RemoveAt(i);
            }
        }
    }

    void TryCreatePendingStoryTriggers()
    {
        if (_pendingStoryTriggers == null || _pendingStoryTriggers.Count == 0) return;

        for (int i = _pendingStoryTriggers.Count - 1; i >= 0; i--)
        {
            var stp = _pendingStoryTriggers[i];
            if (stp.triggerMode == StoryTriggerMode.Position && _variableManager.CheckAllConditions(stp.triggerConditions))
            {
                SetupStoryTrigger(stp);
                _pendingStoryTriggers.RemoveAt(i);
            }
            else if (stp.triggerMode == StoryTriggerMode.Conditions && TryTriggerConditionStory(stp))
            {
                _pendingStoryTriggers.RemoveAt(i);
            }
        }
    }

    bool TryTriggerConditionStory(StoryTriggerPoint stp)
    {
        if (!_variableManager.CheckAllConditions(stp.triggerConditions))
            return false;

        TriggerStoryPoint(stp);
        return true;
    }

    void TriggerStoriesByMode(StoryTriggerMode mode)
    {
        foreach (var stp in levelData.storyTriggers)
        {
            if (stp.triggerMode != mode) continue;
            if (!_variableManager.CheckAllConditions(stp.triggerConditions)) continue;
            TriggerStoryPoint(stp);
        }
    }

    void TriggerStoryPoint(StoryTriggerPoint stp)
    {
        if (stp == null || string.IsNullOrEmpty(stp.storyId)) return;
        if (stp.triggerOnce && _completedStoryTriggers.Contains(stp)) return;
        if (StoryManager.Instance == null)
        {
            Debug.LogWarning($"[LevelSceneBuilder] Cannot trigger story '{stp.storyId}' because StoryManager is missing.");
            return;
        }
        if (StoryManager.Instance.IsPlaying) return;

        _completedStoryTriggers.Add(stp);
        _variableManager.ApplySetActions(stp.onStoryStartSetVariables);
        StoryManager.Instance.PlayStory(stp.storyId, () =>
        {
            _variableManager.ApplySetActions(stp.onStoryCompleteSetVariables);
        });
    }

    // ================================================================
    // 清理
    // ================================================================

    void OnDestroy()
    {
        if (_playerHp != null)
            _playerHp.Died -= HandlePlayerDied;
        Enemy.OnEnemyDied -= HandleEnemyDeath;
        ClearAll();
    }

    void HandleEnemyDeath(Enemy _)
    {
        if (_ != null)
        {
            foreach (var enemies in _groupEnemyInstances.Values)
                enemies.Remove(_.gameObject);
        }

        UpdateCameraLock();
        CheckAllClearedGroups();
    }

    void CheckAllClearedGroups()
    {
        foreach (var group in levelData.groups)
            CheckGroupCleared(group);
    }

    void CheckGroupCleared(ElementGroup group)
    {
        if (group == null) return;
        if (!_triggeredGroups.Contains(group.groupId)) return;
        if (!_finishedSpawningGroups.Contains(group.groupId)) return;
        if (_clearedGroups.Contains(group.groupId)) return;
        if (!_groupEnemyInstances.TryGetValue(group.groupId, out var enemies)) return;

        enemies.RemoveAll(e => e == null);
        if (enemies.Count <= 0)
            ApplyGroupClearedActions(group);
    }

    void ApplyGroupClearedActions(ElementGroup group)
    {
        if (group == null || _clearedGroups.Contains(group.groupId)) return;

        _clearedGroups.Add(group.groupId);
        _variableManager.ApplySetActions(group.onAllEnemiesClearedSetVariables);
        Debug.Log($"[LevelSceneBuilder] 元素组 '{group.groupName}' 已清空，应用变量变化");
    }

    void ClearAll()
    {
        foreach (var go in _spawnedObjects)
        {
            if (go != null) Destroy(go);
        }
        _spawnedObjects.Clear();
        _groupEnemyInstances.Clear();
        _clearedGroups.Clear();
        _finishedSpawningGroups.Clear();
        _spawnedElementIds.Clear();
        _elementInstances.Clear();
        _environmentActorInstances.Clear();
        _createdStoryTriggers.Clear();
        _completedStoryTriggers.Clear();
    }

    public bool TryResolvePerformanceActor(PerformanceActorBinding binding, out GameObject actor)
    {
        actor = null;
        if (binding == null) return false;

        if (binding.targetType == PerformanceActorTargetType.Player)
        {
            actor = playerTransform != null ? playerTransform.gameObject : null;
            return actor != null;
        }

        if (binding.targetType == PerformanceActorTargetType.EnvironmentActor)
        {
            if (string.IsNullOrEmpty(binding.environmentActorId)) return false;
            if (!_environmentActorInstances.TryGetValue(binding.environmentActorId, out actor) || actor == null)
            {
                _environmentActorInstances.Remove(binding.environmentActorId);
                actor = null;
                return false;
            }
            return true;
        }

        if (string.IsNullOrEmpty(binding.elementId)) return false;
        if (!_elementInstances.TryGetValue(binding.elementId, out actor) || actor == null)
        {
            _elementInstances.Remove(binding.elementId);
            actor = null;
            return false;
        }
        return true;
    }
}
