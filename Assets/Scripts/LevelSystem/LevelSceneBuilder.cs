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

    // 生成追踪
    private readonly List<GameObject> _spawnedObjects = new List<GameObject>();
    private readonly Dictionary<string, List<GameObject>> _groupEnemyInstances = new Dictionary<string, List<GameObject>>();
    private readonly HashSet<string> _triggeredGroups = new HashSet<string>();
    private readonly HashSet<string> _finishedStoryTriggers = new HashSet<string>();

    // 元素缓存
    private Dictionary<string, List<LevelElement>> _elementsByGroup;
    private List<LevelElement> _ungroupedElements;
    private List<LevelElement> _pendingConditionElements;

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

        _variableManager = GetComponent<LevelVariableManager>();
        if (_variableManager == null)
            _variableManager = gameObject.AddComponent<LevelVariableManager>();

        _variableManager.Initialize(levelData.variables);
        _variableManager.OnVariableChanged("*", OnAnyVariableChanged);

        BuildSceneInfrastructure();
        CreateBackground();
        CreatePlayer();
        BuildLevel();

        Enemy.OnEnemyDied += HandleEnemyDeath;

        // 备用：定时检查关卡结束，防止 Update 因异常情况漏检
        StartCoroutine(PeriodicCheckLevelEnd());

        OnLevelReady?.Invoke();
        Debug.Log($"[LevelSceneBuilder] 关卡 '{levelData.levelName}' 构建完成");
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

        if (_cameraController != null)
            _cameraController.target = playerTransform;

        // 尝试为 PlayerHP 绑定场景中的 HPBar（即使未激活）
        TryBindPlayerHPBar(playerGo);
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

    // ================================================================
    // 关卡构建
    // ================================================================

    void BuildLevel()
    {
        _elementsByGroup = new Dictionary<string, List<LevelElement>>();
        _ungroupedElements = new List<LevelElement>();
        _pendingConditionElements = new List<LevelElement>();

        foreach (var el in levelData.elements)
        {
            if (string.IsNullOrEmpty(el.groupId))
            {
                if (_variableManager.CheckAllConditions(el.appearConditions))
                    SpawnElement(el);
                else
                    _pendingConditionElements.Add(el);
            }
            else
            {
                if (!_elementsByGroup.ContainsKey(el.groupId))
                    _elementsByGroup[el.groupId] = new List<LevelElement>();
                _elementsByGroup[el.groupId].Add(el);
            }
        }

        CreateStoryTriggerObjects();
    }

    // ================================================================
    // 元素组触发
    // ================================================================

    void CheckGroupTriggers()
    {
        if (playerTransform == null) return;

        float playerX = playerTransform.position.x;

        foreach (var group in levelData.groups)
        {
            if (_triggeredGroups.Contains(group.groupId)) continue;
            if (!_variableManager.CheckAllConditions(group.triggerConditions)) continue;

            if (playerX >= group.triggerPositionX)
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
                _cameraController.LockAt(playerTransform.position.x);
        }

        if (_elementsByGroup.TryGetValue(group.groupId, out var elements))
        {
            StartCoroutine(SpawnGroupElements(group, elements));
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
    }

    // ================================================================
    // 元素生成
    // ================================================================

    GameObject SpawnElement(LevelElement el, ElementGroup group = null)
    {
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

        ApplyCustomParameters(go, el);
        _spawnedObjects.Add(go);

        // 敌人追踪
        if (el.elementType == ElementType.Enemy)
        {
            var enemy = go.GetComponent<Enemy>();
            if (enemy == null) enemy = go.GetComponentInChildren<Enemy>();
            if (enemy != null && group != null && group.mustClearToProceed)
            {
                if (!_groupEnemyInstances.ContainsKey(group.groupId))
                    _groupEnemyInstances[group.groupId] = new List<GameObject>();
                _groupEnemyInstances[group.groupId].Add(go);
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

    void ApplyCustomParameters(GameObject go, LevelElement el)
    {
        if (el.customParameters == null || el.customParameters.Count == 0) return;

        foreach (var param in el.customParameters)
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
            SetupStoryTrigger(stp);
        }
    }

    void SetupStoryTrigger(StoryTriggerPoint stp)
    {
        if (string.IsNullOrEmpty(stp.storyId)) return;

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

    void CompleteLevel()
    {
        if (_levelComplete) return;
        _levelComplete = true;
        Debug.Log($"[LevelSceneBuilder] 关卡完成!");
        OnLevelComplete?.Invoke();
    }

    public void FailLevel()
    {
        if (_levelFailed) return;
        _levelFailed = true;
        Debug.Log($"[LevelSceneBuilder] 关卡失败!");
        OnLevelFailed?.Invoke();
    }

    // ================================================================
    // 战斗锁屏
    // ================================================================

    void UpdateCameraLock()
    {
        if (_activeLockGroup == null) return;

        if (_groupEnemyInstances.TryGetValue(_activeLockGroup.groupId, out var enemies))
        {
            enemies.RemoveAll(e => e == null);
            if (enemies.Count <= 0)
            {
                _activeLockGroup = null;
                if (_cameraController != null)
                    _cameraController.Unlock();
                Debug.Log($"[LevelSceneBuilder] 组内敌人清除，解锁");
            }
        }
        else
        {
            _activeLockGroup = null;
            if (_cameraController != null)
                _cameraController.Unlock();
        }
    }

    // ================================================================
    // 变量变化回调
    // ================================================================

    void OnAnyVariableChanged()
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

    // ================================================================
    // 清理
    // ================================================================

    void OnDestroy()
    {
        Enemy.OnEnemyDied -= HandleEnemyDeath;
        ClearAll();
    }

    void HandleEnemyDeath(Enemy _)
    {
        UpdateCameraLock();
    }

    void ClearAll()
    {
        foreach (var go in _spawnedObjects)
        {
            if (go != null) Destroy(go);
        }
        _spawnedObjects.Clear();
        _groupEnemyInstances.Clear();
    }
}
