using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

// ============================================================
// 枚举定义
// ============================================================

public enum ElementType
{
    Enemy,
    Item,
    Obstacle
}

public enum LevelVariableType
{
    Bool,
    Int,
    Float,
    String
}

public enum BackgroundMode
{
    SingleInfiniteScroll,
    ParallaxLayers,
    SequentialTiles
}

public enum BackgroundDepthBand
{
    Far,
    Mid,
    Near
}

public enum BackgroundLayerContentType
{
    SingleSprite,
    RepeatedSprite,
    SequentialTiles
}

public enum EnvironmentContinuousSource
{
    PlayerX,
    PlayerY,
    HorizontalDistance,
    Distance
}

public enum EnvironmentTriggerType
{
    PlayerXRange,
    PlayerDistance,
    LevelConditions,
    PlayerSignal
}

public enum EnvironmentActionType
{
    PlayAnimation,
    SetAnimatorFloat,
    SetAnimatorBool,
    SetAnimatorTrigger,
    SetLevelVariable,
    SetVisualActive,
    EmitActorSignal
}

public enum StoryTriggerMode
{
    Position,
    Conditions,
    LevelStart,
    LevelComplete
}

public enum ElementGroupTriggerMode
{
    None,
    Position,
    Conditions
}

public enum LevelFlowStepType
{
    WaitForPlayerSafe,
    Wait,
    SetVariable,
    MovePlayer,
    MoveCamera,
    ResumeCameraFollow,
    PlayStory
}

public enum LevelFlowEasing
{
    Linear,
    SmoothStep
}

// ============================================================
// 背景设置
// ============================================================

[Serializable]
public class ParallaxLayerData
{
    public Sprite sprite;
    public float parallaxFactor = 0.5f;
    public int sortingOrder;
    public bool infiniteHorizontal = true;
}

[Serializable]
public class BackgroundSequenceEntry
{
    public Sprite sprite;
    [Min(1)] public int repeatCount = 1;
}

[Serializable]
public class BackgroundLayerData
{
    public string layerId;
    public string displayName = "Background Layer";
    public BackgroundDepthBand depthBand = BackgroundDepthBand.Mid;
    public BackgroundLayerContentType contentType = BackgroundLayerContentType.SingleSprite;
    public Sprite sprite;
    public List<BackgroundSequenceEntry> sequence = new List<BackgroundSequenceEntry>();
    public Vector2 origin;
    public Vector2 scale = Vector2.one;
    public Color color = Color.white;
    public int sortingOffset;
    [Range(0f, 2f)] public float nearMotionMultiplier = 1.25f;
    public bool enableVerticalMotion;
    [Range(0f, 2f)] public float verticalMotionMultiplier = 1f;
    public float horizontalScrollSpeed;

    public float MotionMultiplierX => depthBand == BackgroundDepthBand.Far
        ? 0f
        : depthBand == BackgroundDepthBand.Mid ? 1f : Mathf.Clamp(nearMotionMultiplier, 0f, 2f);

    public float MotionMultiplierY => enableVerticalMotion
        ? (depthBand == BackgroundDepthBand.Far
            ? 0f
            : depthBand == BackgroundDepthBand.Mid ? 1f : Mathf.Clamp(verticalMotionMultiplier, 0f, 2f))
        : 1f;

    public int SortingOrder => BackgroundSettings.GetBandSortingBase(depthBand) + sortingOffset;

    public float CalculateSequenceWidth()
    {
        if (sequence == null) return 0f;
        float width = 0f;
        foreach (var entry in sequence)
        {
            if (entry == null || entry.sprite == null || entry.repeatCount <= 0) continue;
            width += entry.sprite.bounds.size.x * Mathf.Abs(scale.x) * entry.repeatCount;
        }
        return width;
    }
}

[Serializable]
public class BackgroundSettings
{
    public const int DefaultSortingOrder = -10000;
    public const int CurrentDataVersion = 2;

    public int dataVersion;
    public List<BackgroundLayerData> layers = new List<BackgroundLayerData>();

    public BackgroundMode mode = BackgroundMode.SingleInfiniteScroll;

    [Tooltip("启用后，相机画面和玩家碰撞体都不会越过下面的世界坐标范围")]
    public bool constrainCameraToBounds;
    [Tooltip("相机画面允许到达的最左侧世界坐标 X")]
    public float cameraBoundsStartX;
    [Tooltip("相机画面允许到达的最右侧世界坐标 X")]
    public float cameraBoundsEndX = 50f;

    public Sprite singleBackground;
    public float singleParallaxFactor = 0f;
    public int singleSortingOrder = DefaultSortingOrder;

    public List<ParallaxLayerData> parallaxLayers = new List<ParallaxLayerData>();

    [Tooltip("第一张顺序背景图左边缘的世界坐标 X")]
    public float sequenceStartX;
    [Tooltip("顺序背景图的视觉中心世界坐标 Y")]
    public float sequenceCenterY;
    public int sequenceSortingOrder = DefaultSortingOrder;
    public List<BackgroundSequenceEntry> sequence = new List<BackgroundSequenceEntry>();

    public float CalculateSequenceWidth()
    {
        if (sequence == null) return 0f;

        float width = 0f;
        foreach (var entry in sequence)
        {
            if (entry == null || entry.sprite == null || entry.repeatCount <= 0)
                continue;
            width += entry.sprite.bounds.size.x * entry.repeatCount;
        }
        return width;
    }

    public static int GetBandSortingBase(BackgroundDepthBand band)
    {
        switch (band)
        {
            case BackgroundDepthBand.Far: return -30000;
            case BackgroundDepthBand.Near: return 10000;
            default: return DefaultSortingOrder;
        }
    }

    public bool MigrateLegacyData()
    {
        if (layers == null) layers = new List<BackgroundLayerData>();
        if (dataVersion >= CurrentDataVersion) return false;

        if (layers.Count == 0)
        {
            if (mode == BackgroundMode.SingleInfiniteScroll && singleBackground != null)
            {
                float motion = Mathf.Clamp01(1f - singleParallaxFactor);
                var band = motion <= 0.001f
                    ? BackgroundDepthBand.Far
                    : motion >= 0.999f ? BackgroundDepthBand.Mid : BackgroundDepthBand.Near;
                layers.Add(new BackgroundLayerData
                {
                    layerId = Guid.NewGuid().ToString(),
                    displayName = singleBackground.name,
                    depthBand = band,
                    contentType = BackgroundLayerContentType.RepeatedSprite,
                    sprite = singleBackground,
                    nearMotionMultiplier = motion,
                    enableVerticalMotion = band == BackgroundDepthBand.Far,
                    sortingOffset = singleSortingOrder - GetBandSortingBase(band)
                });
            }
            else if (mode == BackgroundMode.SequentialTiles && sequence != null && sequence.Count > 0)
            {
                layers.Add(new BackgroundLayerData
                {
                    layerId = Guid.NewGuid().ToString(),
                    displayName = "Legacy Sequence",
                    depthBand = BackgroundDepthBand.Mid,
                    contentType = BackgroundLayerContentType.SequentialTiles,
                    sequence = sequence.Select(entry => entry == null ? null : new BackgroundSequenceEntry
                    {
                        sprite = entry.sprite,
                        repeatCount = entry.repeatCount
                    }).ToList(),
                    origin = new Vector2(sequenceStartX, sequenceCenterY),
                    sortingOffset = sequenceSortingOrder - GetBandSortingBase(BackgroundDepthBand.Mid)
                });
            }
            else if (mode == BackgroundMode.ParallaxLayers && parallaxLayers != null)
            {
                foreach (var legacy in parallaxLayers)
                {
                    if (legacy == null || legacy.sprite == null) continue;
                    float motion = Mathf.Clamp01(1f - legacy.parallaxFactor);
                    var band = motion <= 0.001f
                        ? BackgroundDepthBand.Far
                        : motion >= 0.999f ? BackgroundDepthBand.Mid : BackgroundDepthBand.Near;
                    layers.Add(new BackgroundLayerData
                    {
                        layerId = Guid.NewGuid().ToString(),
                        displayName = legacy.sprite.name,
                        depthBand = band,
                        contentType = legacy.infiniteHorizontal
                            ? BackgroundLayerContentType.RepeatedSprite
                            : BackgroundLayerContentType.SingleSprite,
                        sprite = legacy.sprite,
                        nearMotionMultiplier = motion,
                        enableVerticalMotion = band == BackgroundDepthBand.Far,
                        sortingOffset = legacy.sortingOrder - GetBandSortingBase(band)
                    });
                }
            }
        }

        dataVersion = CurrentDataVersion;
        return true;
    }
}

// ============================================================
// 关卡变量系统
// ============================================================

[Serializable]
public class LevelVariableDefinition
{
    public string variableName;
    public LevelVariableType type;
    public string defaultValue;
    public string description;
}

[Serializable]
public class LevelVariableCondition
{
    public enum CompareMode
    {
        Equals,
        NotEquals,
        Greater,
        GreaterOrEqual,
        Less,
        LessOrEqual,
        Contains,
        IsTrue,
        IsFalse
    }

    public string variableName;
    public CompareMode mode;
    public string compareValue;
}

[Serializable]
public class VariableSetAction
{
    public string variableName;
    public string stringValue;
}

[Serializable]
public class EnvironmentContinuousBinding
{
    public EnvironmentContinuousSource source = EnvironmentContinuousSource.PlayerX;
    public string animatorParameter;
    public float inputMin;
    public float inputMax = 10f;
    public float outputMin;
    public float outputMax = 1f;
    public bool clamp = true;
}

[Serializable]
public class EnvironmentActorAction
{
    public EnvironmentActionType actionType = EnvironmentActionType.PlayAnimation;
    public string name;
    public string stringValue;
    public float floatValue;
    public bool boolValue = true;
    public bool loop;
    [Min(0)] public int animationTrack;
}

[Serializable]
public class EnvironmentActorTrigger
{
    public EnvironmentTriggerType triggerType = EnvironmentTriggerType.PlayerXRange;
    public float minValue;
    public float maxValue = 5f;
    public string signalId;
    public bool triggerOnce;
    public List<LevelVariableCondition> conditions = new List<LevelVariableCondition>();
    public List<EnvironmentActorAction> onEnterActions = new List<EnvironmentActorAction>();
    public List<EnvironmentActorAction> onExitActions = new List<EnvironmentActorAction>();
}

[Serializable]
public class EnvironmentActorData
{
    public string actorId;
    public string displayName;
    public GameObject prefab;
    public Vector2 position;
    public bool faceRight = true;
    public BackgroundDepthBand depthBand = BackgroundDepthBand.Mid;
    public int sortingOffset;
    public List<LevelVariableCondition> activeConditions = new List<LevelVariableCondition>();
    public List<ElementCustomParameter> customParameters = new List<ElementCustomParameter>();
    public List<EnvironmentContinuousBinding> continuousBindings = new List<EnvironmentContinuousBinding>();
    public List<EnvironmentActorTrigger> triggers = new List<EnvironmentActorTrigger>();

    public int SortingOrder => BackgroundSettings.GetBandSortingBase(depthBand) + sortingOffset;
}

// ============================================================
// 元素类型定义
// ============================================================

[Serializable]
public class ElementCustomParameter
{
    public string componentTypeName;
    public string fieldName;
    public string valueTypeName;
    public string serializedValue;
}

[Serializable]
public class ElementPrefabEntry
{
    public string displayName;
    public GameObject prefab;
    public Sprite icon;
}

[Serializable]
public class LevelElement
{
    public string elementId;
    public string displayName;
    public ElementType elementType;
    public GameObject prefab;

    public Vector2 position;
    public bool faceRight = true;

    public float appearDelay;
    public string groupId;
    public List<string> groupIds = new List<string>();

    public List<LevelVariableCondition> appearConditions = new List<LevelVariableCondition>();
    public List<ElementCustomParameter> customParameters = new List<ElementCustomParameter>();

    public bool IsInGroup(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (groupId == id) return true;
        return groupIds != null && groupIds.Contains(id);
    }

    public bool HasAnyGroup()
    {
        return !string.IsNullOrEmpty(groupId) || (groupIds != null && groupIds.Count > 0);
    }

    public void AddGroup(string id)
    {
        if (string.IsNullOrEmpty(id) || IsInGroup(id)) return;
        if (groupIds == null) groupIds = new List<string>();
        groupIds.Add(id);
    }

    public void RemoveGroup(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (groupId == id) groupId = "";
        groupIds?.RemoveAll(g => g == id);
    }
}

// ============================================================
// 过场动画触发器
// ============================================================

[Serializable]
public class StoryTriggerPoint
{
    public StoryTriggerMode triggerMode = StoryTriggerMode.Position;
    public float positionX;
    public string storyId;
    public bool triggerOnce = true;
    public bool triggerFromLeft = true;
    public List<LevelVariableCondition> triggerConditions = new List<LevelVariableCondition>();

    public List<VariableSetAction> onStoryStartSetVariables = new List<VariableSetAction>();
    public List<VariableSetAction> onStoryCompleteSetVariables = new List<VariableSetAction>();
}

[Serializable]
public class LevelFlowStep
{
    public LevelFlowStepType stepType = LevelFlowStepType.WaitForPlayerSafe;
    public float duration = 1f;
    public Vector2 targetPosition;
    public float speed = 3f;
    public float tolerance = 0.05f;
    public LevelFlowEasing easing = LevelFlowEasing.SmoothStep;
    public VariableSetAction setVariable = new VariableSetAction();
    public string storyId;

    [Tooltip("本次播放剧情时，剧情演出槽位到本关角色的绑定")]
    public List<PerformanceActorBinding> storyCastBindings = new List<PerformanceActorBinding>();

    [Tooltip("剧情 Cue 引用的演出资源缓存，供运行时通过 scriptId 解析")]
    [HideInInspector]
    public List<PerformanceScript> storyPerformanceScripts = new List<PerformanceScript>();

    [Tooltip("旧版关卡内台词 Cue，仅用于兼容已有数据")]
    [HideInInspector]
    public List<StoryPerformanceCue> storyPerformanceCues = new List<StoryPerformanceCue>();
}

[Serializable]
public class LevelFlowData
{
    public string flowId;
    public StoryTriggerMode triggerMode = StoryTriggerMode.Conditions;
    public float positionX;
    public bool triggerFromLeft = true;
    public bool triggerOnce = true;
    public List<LevelVariableCondition> triggerConditions = new List<LevelVariableCondition>();
    public List<LevelFlowStep> steps = new List<LevelFlowStep>();
}

// ============================================================
// 元素组
// ============================================================

[Serializable]
public class ElementGroup
{
    public string groupId;
    public string groupName;
    public ElementGroupTriggerMode triggerMode = ElementGroupTriggerMode.Position;
    public float triggerPositionX;
    public bool mustClearToProceed;
    public List<LevelVariableCondition> triggerConditions = new List<LevelVariableCondition>();
    public List<VariableSetAction> onAllEnemiesClearedSetVariables = new List<VariableSetAction>();
}

// ============================================================
// 校验结果
// ============================================================

[Serializable]
public class LevelValidationResult
{
    public bool isValid;
    public List<string> errors = new List<string>();
    public List<string> warnings = new List<string>();

    public void AddError(string msg) { errors.Add(msg); isValid = false; }
    public void AddWarning(string msg) { warnings.Add(msg); }
}

// ============================================================
// 关卡数据 ScriptableObject
// ============================================================

[CreateAssetMenu(fileName = "NewLevel", menuName = "Game/Level Data", order = 1)]
public class LevelData : ScriptableObject
{
    [Header("基本信息")]
    public string levelName = "New Level";
    [Range(1, 5)] public int difficulty = 1;
    public float levelLength = 50f;

    [Header("玩家预制体")]
    public GameObject playerPrefab;

    [Header("背景图")]
    public BackgroundSettings backgroundSettings = new BackgroundSettings();

    [Header("玩家出生点")]
    public Vector2 playerSpawnPosition = new Vector2(-8f, -3.5f);
    public bool playerFaceRight = true;

    [Header("Camera Settings")]
    public bool useCustomInitialCameraPosition;
    public Vector2 initialCameraPosition = Vector2.zero;
    [Range(0f, 0.45f)] public float cameraDeadZone = 0.2f;

    [Header("关卡结束条件")]
    public float levelEndPositionX = 45f;
    public List<LevelVariableCondition> endConditions = new List<LevelVariableCondition>();

    [Header("关卡变量")]
    public List<LevelVariableDefinition> variables = new List<LevelVariableDefinition>();

    [Header("剧情数据")]
    public TextAsset storyCollectionJson;

    [Header("元素列表")]
    public List<LevelElement> elements = new List<LevelElement>();

    [Header("Environment Actors")]
    public List<EnvironmentActorData> environmentActors = new List<EnvironmentActorData>();

    [HideInInspector]
    public List<StoryTriggerPoint> storyTriggers = new List<StoryTriggerPoint>();

    [Header("演出事件")]
    [FormerlySerializedAs("flows")]
    public List<LevelFlowData> events = new List<LevelFlowData>();

    [Header("元素组")]
    public List<ElementGroup> groups = new List<ElementGroup>();

    [Header("预制体库（编辑器辅助）")]
    public List<ElementPrefabEntry> enemyPrefabLibrary = new List<ElementPrefabEntry>();
    public List<ElementPrefabEntry> itemPrefabLibrary = new List<ElementPrefabEntry>();
    public List<ElementPrefabEntry> obstaclePrefabLibrary = new List<ElementPrefabEntry>();
    public List<ElementPrefabEntry> environmentActorPrefabLibrary = new List<ElementPrefabEntry>();

    public bool MigrateLegacyBackground()
    {
        if (backgroundSettings == null)
            backgroundSettings = new BackgroundSettings();
        return backgroundSettings.MigrateLegacyData();
    }

    // ========== 查找辅助 ==========

    public ElementGroup FindGroup(string groupId)
    {
        return groups.Find(g => g.groupId == groupId);
    }

    public List<LevelElement> GetElementsByGroup(string groupId)
    {
        return elements.Where(e => e.IsInGroup(groupId)).ToList();
    }

    public List<LevelElement> GetUngroupedElements()
    {
        return elements.Where(e => !e.HasAnyGroup()).ToList();
    }

    public LevelVariableDefinition FindVariable(string name)
    {
        return variables.Find(v => v.variableName == name);
    }

    public bool MigrateLegacyStoryTriggers()
    {
        if (storyTriggers == null || storyTriggers.Count == 0) return false;
        if (events == null) events = new List<LevelFlowData>();

        for (int i = 0; i < storyTriggers.Count; i++)
        {
            var trigger = storyTriggers[i];
            if (trigger == null) continue;
            string eventId = $"legacy_story_{i + 1}_{trigger.storyId}";
            if (events.Any(e => e != null && e.flowId == eventId)) continue;

            var steps = new List<LevelFlowStep>();
            if (trigger.onStoryStartSetVariables != null)
                foreach (var action in trigger.onStoryStartSetVariables)
                    steps.Add(new LevelFlowStep
                    {
                        stepType = LevelFlowStepType.SetVariable,
                        setVariable = CloneSetAction(action)
                    });

            steps.Add(new LevelFlowStep
            {
                stepType = LevelFlowStepType.PlayStory,
                storyId = trigger.storyId
            });

            if (trigger.onStoryCompleteSetVariables != null)
                foreach (var action in trigger.onStoryCompleteSetVariables)
                    steps.Add(new LevelFlowStep
                    {
                        stepType = LevelFlowStepType.SetVariable,
                        setVariable = CloneSetAction(action)
                    });

            events.Add(new LevelFlowData
            {
                flowId = eventId,
                triggerMode = trigger.triggerMode,
                positionX = trigger.positionX,
                triggerFromLeft = trigger.triggerFromLeft,
                triggerOnce = trigger.triggerOnce,
                triggerConditions = trigger.triggerConditions?.Select(c => new LevelVariableCondition
                {
                    variableName = c.variableName,
                    mode = c.mode,
                    compareValue = c.compareValue
                }).ToList() ?? new List<LevelVariableCondition>(),
                steps = steps
            });
        }

        storyTriggers.Clear();
        return true;
    }

    static VariableSetAction CloneSetAction(VariableSetAction action)
    {
        return action == null ? new VariableSetAction() : new VariableSetAction
        {
            variableName = action.variableName,
            stringValue = action.stringValue
        };
    }

    static IEnumerable<EnvironmentActorAction> EnumerateEnvironmentActions(EnvironmentActorData actor)
    {
        if (actor?.triggers == null) yield break;
        foreach (var trigger in actor.triggers)
        {
            if (trigger == null) continue;
            foreach (var action in trigger.onEnterActions ?? new List<EnvironmentActorAction>())
                if (action != null) yield return action;
            foreach (var action in trigger.onExitActions ?? new List<EnvironmentActorAction>())
                if (action != null) yield return action;
        }
    }

    // ========== 校验 ==========

    public LevelValidationResult Validate()
    {
        MigrateLegacyStoryTriggers();
        MigrateLegacyBackground();
        var result = new LevelValidationResult();

        if (string.IsNullOrEmpty(levelName))
            result.AddError("关卡名称不能为空");

        if (playerPrefab == null)
            result.AddError("玩家预制体不能为空");

        if (levelEndPositionX <= playerSpawnPosition.x)
            result.AddError($"终点位置X({levelEndPositionX})必须大于起点X({playerSpawnPosition.x})");

        if (backgroundSettings != null && backgroundSettings.constrainCameraToBounds &&
            backgroundSettings.cameraBoundsEndX <= backgroundSettings.cameraBoundsStartX)
            result.AddError(
                $"摄像机边界终点 X({backgroundSettings.cameraBoundsEndX})必须大于起点 X({backgroundSettings.cameraBoundsStartX})");

        var elementIds = new HashSet<string>();
        for (int i = 0; i < elements.Count; i++)
        {
            var el = elements[i];
            if (el.prefab == null)
                result.AddError($"元素 #{i} '{el.displayName}' 引用的预制体为空");
            if (string.IsNullOrEmpty(el.elementId))
                result.AddError($"元素 #{i} '{el.displayName}' 缺少elementId");
            else if (!elementIds.Add(el.elementId))
                result.AddError($"重复的元素 ID: {el.elementId}");
        }

        for (int i = 0; i < storyTriggers.Count; i++)
        {
            var st = storyTriggers[i];
            if (string.IsNullOrEmpty(st.storyId))
                result.AddError($"过场触发器 #{i} 的storyId为空");
        }

        for (int i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            if (g.triggerPositionX < 0 || g.triggerPositionX > levelLength)
                result.AddError($"元素组 '{g.groupName}' 的触发位置X({g.triggerPositionX})超出关卡范围(0~{levelLength})");
        }

        var environmentActorIds = new HashSet<string>();
        foreach (var actor in environmentActors ?? new List<EnvironmentActorData>())
        {
            if (actor == null) continue;
            if (actor.prefab == null)
                result.AddError($"Environment actor '{actor.displayName}' has no prefab");
            if (string.IsNullOrWhiteSpace(actor.actorId))
                result.AddError($"Environment actor '{actor.displayName}' has no actor ID");
            else if (!environmentActorIds.Add(actor.actorId))
                result.AddError($"Duplicate environment actor ID: {actor.actorId}");
            if (actor.SortingOrder < short.MinValue || actor.SortingOrder > short.MaxValue)
                result.AddError($"Environment actor '{actor.displayName}' has an out-of-range sorting order");
            foreach (var binding in actor.continuousBindings ?? new List<EnvironmentContinuousBinding>())
                if (binding != null && string.IsNullOrWhiteSpace(binding.animatorParameter))
                    result.AddError($"Environment actor '{actor.displayName}' has an empty Animator parameter");
            foreach (var trigger in actor.triggers ?? new List<EnvironmentActorTrigger>())
            {
                if (trigger == null) continue;
                if (trigger.maxValue < trigger.minValue)
                    result.AddError($"Environment actor '{actor.displayName}' has an inverted trigger range");
                if (trigger.triggerType == EnvironmentTriggerType.PlayerSignal &&
                    string.IsNullOrWhiteSpace(trigger.signalId))
                    result.AddError($"Environment actor '{actor.displayName}' has an empty player signal");
            }
        }

        var variableNames = new HashSet<string>(variables.Select(v => v.variableName));
        foreach (var el in elements)
        {
            foreach (var cond in el.appearConditions)
            {
                if (!string.IsNullOrEmpty(cond.variableName) && !variableNames.Contains(cond.variableName))
                    result.AddError($"元素 '{el.displayName}' 引用了未定义的变量'{cond.variableName}'");
            }
        }
        foreach (var actor in environmentActors ?? new List<EnvironmentActorData>())
        {
            if (actor == null) continue;
            var conditions = new List<LevelVariableCondition>();
            if (actor.activeConditions != null) conditions.AddRange(actor.activeConditions);
            foreach (var trigger in actor.triggers ?? new List<EnvironmentActorTrigger>())
                if (trigger?.conditions != null) conditions.AddRange(trigger.conditions);
            foreach (var cond in conditions)
                if (cond != null && !string.IsNullOrEmpty(cond.variableName) && !variableNames.Contains(cond.variableName))
                    result.AddError($"Environment actor '{actor.displayName}' references undefined variable '{cond.variableName}'");

            Animator animator = actor.prefab != null
                ? actor.prefab.GetComponentInChildren<Animator>(true)
                : null;
            var animatorParameters = animator != null
                ? animator.parameters.ToDictionary(parameter => parameter.name, parameter => parameter.type)
                : new Dictionary<string, AnimatorControllerParameterType>();

            foreach (var binding in actor.continuousBindings ?? new List<EnvironmentContinuousBinding>())
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.animatorParameter)) continue;
                if (!animatorParameters.TryGetValue(binding.animatorParameter, out var parameterType) ||
                    parameterType != AnimatorControllerParameterType.Float)
                    result.AddError(
                        $"Environment actor '{actor.displayName}' references missing/non-float Animator parameter '{binding.animatorParameter}'");
            }

            foreach (var action in EnumerateEnvironmentActions(actor))
            {
                if (action.actionType == EnvironmentActionType.SetLevelVariable &&
                    !string.IsNullOrWhiteSpace(action.name) && !variableNames.Contains(action.name))
                    result.AddError(
                        $"Environment actor '{actor.displayName}' action references undefined variable '{action.name}'");

                AnimatorControllerParameterType? expectedType = null;
                switch (action.actionType)
                {
                    case EnvironmentActionType.SetAnimatorFloat:
                        expectedType = AnimatorControllerParameterType.Float;
                        break;
                    case EnvironmentActionType.SetAnimatorBool:
                        expectedType = AnimatorControllerParameterType.Bool;
                        break;
                    case EnvironmentActionType.SetAnimatorTrigger:
                        expectedType = AnimatorControllerParameterType.Trigger;
                        break;
                }
                if (expectedType.HasValue &&
                    (!animatorParameters.TryGetValue(action.name ?? "", out var actualType) ||
                     actualType != expectedType.Value))
                    result.AddError(
                        $"Environment actor '{actor.displayName}' references missing/wrong-type Animator parameter '{action.name}'");
            }
        }
        foreach (var cond in endConditions)
        {
            if (!string.IsNullOrEmpty(cond.variableName) && !variableNames.Contains(cond.variableName))
                result.AddError($"结束条件引用了未定义的变量'{cond.variableName}'");
        }
        foreach (var st in storyTriggers)
        {
            foreach (var cond in st.triggerConditions)
            {
                if (!string.IsNullOrEmpty(cond.variableName) && !variableNames.Contains(cond.variableName))
                    result.AddError($"过场触发器(storyId={st.storyId})引用了未定义的变量'{cond.variableName}'");
            }
        }
        foreach (var g in groups)
        {
            foreach (var cond in g.triggerConditions)
            {
                if (!string.IsNullOrEmpty(cond.variableName) && !variableNames.Contains(cond.variableName))
                    result.AddError($"元素组'{g.groupName}'引用了未定义的变量'{cond.variableName}'");
            }
        }


        var flowIds = new HashSet<string>();
        HashSet<string> storyIds = null;
        var storiesById = new Dictionary<string, StorySequence>();
        var dialogueIdsByStory = new Dictionary<string, HashSet<int>>();
        if (storyCollectionJson != null)
        {
            try
            {
                var storyData = JsonUtility.FromJson<StoryDataCollection>(storyCollectionJson.text);
                storyIds = new HashSet<string>((storyData?.stories ?? new List<StorySequence>())
                    .Where(s => s != null && !string.IsNullOrEmpty(s.storyId))
                    .Select(s => s.storyId));
                foreach (var story in storyData?.stories ?? new List<StorySequence>())
                {
                    if (story == null || string.IsNullOrEmpty(story.storyId)) continue;
                    var dialogueIds = new HashSet<int>();
                    foreach (var dialogue in story.dialogues ?? new List<StoryDialogue>())
                    {
                        if (dialogue == null || dialogue.id <= 0 || !dialogueIds.Add(dialogue.id))
                            result.AddError($"Story '{story.storyId}' contains an invalid or duplicate dialogue ID");
                    }
                    dialogueIdsByStory[story.storyId] = dialogueIds;
                    storiesById[story.storyId] = story;
                }
            }
            catch (Exception e)
            {
                result.AddError($"Story collection JSON is invalid: {e.Message}");
            }
        }
        foreach (var flow in events ?? new List<LevelFlowData>())
        {
            if (flow == null || string.IsNullOrWhiteSpace(flow.flowId))
            {
                result.AddError("Level flow ID cannot be empty");
                continue;
            }
            if (!flowIds.Add(flow.flowId))
                result.AddError($"Duplicate level flow ID: {flow.flowId}");
            if (flow.steps == null || flow.steps.Count == 0)
                result.AddError($"Level flow '{flow.flowId}' has no steps");

            foreach (var condition in flow.triggerConditions)
                if (!string.IsNullOrEmpty(condition.variableName) && !variableNames.Contains(condition.variableName))
                    result.AddError($"Level flow '{flow.flowId}' references undefined variable '{condition.variableName}'");

            if (flow.steps == null) continue;
            foreach (var step in flow.steps)
            {
                if (step == null) continue;
                if (step.stepType == LevelFlowStepType.MovePlayer && step.speed <= 0f)
                    result.AddError($"Level flow '{flow.flowId}' has a player move step with invalid speed");
                if ((step.stepType == LevelFlowStepType.MovePlayer || step.stepType == LevelFlowStepType.MoveCamera) &&
                    (float.IsNaN(step.targetPosition.x) || float.IsInfinity(step.targetPosition.x) ||
                     float.IsNaN(step.targetPosition.y) || float.IsInfinity(step.targetPosition.y)))
                    result.AddError($"Level flow '{flow.flowId}' has an invalid target position");
                if (step.stepType == LevelFlowStepType.MoveCamera && step.duration < 0f)
                    result.AddError($"Level flow '{flow.flowId}' has a camera move step with invalid duration");
                if (step.stepType == LevelFlowStepType.PlayStory && string.IsNullOrWhiteSpace(step.storyId))
                    result.AddError($"Level flow '{flow.flowId}' has an empty story ID");
                else if (step.stepType == LevelFlowStepType.PlayStory && storyIds != null && !storyIds.Contains(step.storyId))
                    result.AddError($"Level flow '{flow.flowId}' references unknown story '{step.storyId}'");
                storiesById.TryGetValue(step.storyId ?? "", out var selectedStory);
                bool usesStoryCues = selectedStory?.performanceCues != null &&
                                     selectedStory.performanceCues.Count > 0;
                if (step.stepType == LevelFlowStepType.PlayStory && usesStoryCues)
                {
                    var scriptsById = new Dictionary<string, PerformanceScript>();
                    foreach (var script in step.storyPerformanceScripts ?? new List<PerformanceScript>())
                    {
                        if (script == null || string.IsNullOrWhiteSpace(script.scriptId)) continue;
                        if (!scriptsById.TryAdd(script.scriptId, script))
                            result.AddError($"Duplicate performance script ID: {script.scriptId}");
                    }

                    var requiredSlots = new HashSet<(string scriptId, string slotId)>();
                    foreach (var cue in selectedStory.performanceCues)
                    {
                        if (cue == null || cue.delay < 0f)
                        {
                            result.AddError($"Story '{selectedStory.storyId}' contains an invalid performance cue");
                            continue;
                        }
                        if (!dialogueIdsByStory.TryGetValue(selectedStory.storyId, out var dialogueIds) ||
                            !dialogueIds.Contains(cue.dialogueId))
                            result.AddError($"Story '{selectedStory.storyId}' cue references unknown dialogue ID {cue.dialogueId}");
                        if (string.IsNullOrWhiteSpace(cue.scriptId) ||
                            !scriptsById.TryGetValue(cue.scriptId, out var script) || script == null)
                        {
                            result.AddError($"Story '{selectedStory.storyId}' references missing performance script '{cue.scriptId}'");
                            continue;
                        }

                        var slotIds = new HashSet<string>();
                        foreach (var slot in script.actorSlots ?? new List<PerformanceActorSlot>())
                        {
                            if (slot == null || string.IsNullOrWhiteSpace(slot.slotId) || !slotIds.Add(slot.slotId))
                                result.AddError($"Performance script '{script.scriptId}' has an invalid or duplicate actor slot");
                            else
                                requiredSlots.Add((script.scriptId, slot.slotId));
                        }
                        ValidatePerformanceClips(result, script, slotIds);
                    }

                    foreach (var requiredSlot in requiredSlots)
                    {
                        var binding = step.storyCastBindings?.FirstOrDefault(item =>
                            item != null && item.scriptId == requiredSlot.scriptId &&
                            item.slotId == requiredSlot.slotId);
                        binding ??= step.storyCastBindings?.FirstOrDefault(item =>
                            item != null && string.IsNullOrEmpty(item.scriptId) &&
                            item.slotId == requiredSlot.slotId);
                        if (binding == null)
                            result.AddError(
                                $"Story '{selectedStory.storyId}' does not bind cast slot " +
                                $"'{requiredSlot.scriptId}/{requiredSlot.slotId}'");
                        else if (binding.targetType == PerformanceActorTargetType.LevelElement &&
                                 !elementIds.Contains(binding.elementId))
                            result.AddError(
                                $"Story cast slot '{requiredSlot.scriptId}/{requiredSlot.slotId}' " +
                                $"references unknown element '{binding.elementId}'");
                        else if (binding.targetType == PerformanceActorTargetType.EnvironmentActor &&
                                 !environmentActorIds.Contains(binding.environmentActorId))
                            result.AddError(
                                $"Story cast slot '{requiredSlot.scriptId}/{requiredSlot.slotId}' " +
                                $"references unknown environment actor '{binding.environmentActorId}'");
                    }
                }
                else if (step.stepType == LevelFlowStepType.PlayStory && step.storyPerformanceCues != null)
                {
                    var referencedScripts = new Dictionary<string, PerformanceScript>();
                    foreach (var cue in step.storyPerformanceCues)
                    {
                        if (cue == null || cue.performanceScript == null)
                        {
                            result.AddError($"Level flow '{flow.flowId}' has a story performance cue without a script");
                            continue;
                        }
                        if (dialogueIdsByStory.TryGetValue(step.storyId ?? "", out var dialogueIds) &&
                            !dialogueIds.Contains(cue.dialogueId))
                            result.AddError($"Level flow '{flow.flowId}' cue references unknown dialogue ID {cue.dialogueId}");

                        var script = cue.performanceScript;
                        if (string.IsNullOrWhiteSpace(script.scriptId))
                            result.AddError($"Performance script '{script.name}' has an empty script ID");
                        else if (referencedScripts.TryGetValue(script.scriptId, out var duplicate) && duplicate != script)
                            result.AddError($"Duplicate performance script ID: {script.scriptId}");
                        else
                            referencedScripts[script.scriptId] = script;

                        var slotIds = new HashSet<string>();
                        foreach (var slot in script.actorSlots ?? new List<PerformanceActorSlot>())
                        {
                            if (slot == null || string.IsNullOrWhiteSpace(slot.slotId) || !slotIds.Add(slot.slotId))
                                result.AddError($"Performance script '{script.scriptId}' has an invalid or duplicate actor slot");
                        }
                        foreach (var slotId in slotIds)
                        {
                            var binding = cue.actorBindings?.FirstOrDefault(item => item != null && item.slotId == slotId);
                            if (binding == null)
                                result.AddError($"Performance cue for '{script.scriptId}' does not bind slot '{slotId}'");
                            else if (binding.targetType == PerformanceActorTargetType.LevelElement &&
                                     !elementIds.Contains(binding.elementId))
                                result.AddError($"Performance cue slot '{slotId}' references unknown element '{binding.elementId}'");
                            else if (binding.targetType == PerformanceActorTargetType.EnvironmentActor &&
                                     !environmentActorIds.Contains(binding.environmentActorId))
                                result.AddError($"Performance cue slot '{slotId}' references unknown environment actor '{binding.environmentActorId}'");
                        }
                        ValidatePerformanceClips(result, script, slotIds);
                    }
                }
                if (step.stepType == LevelFlowStepType.SetVariable &&
                    (step.setVariable == null || !variableNames.Contains(step.setVariable.variableName)))
                    result.AddError($"Level flow '{flow.flowId}' has an invalid set-variable step");
            }
        }

        // 警告
        if (elements.Count == 0)
            result.AddWarning("关卡没有任何元素");

        if (events == null || events.Count == 0)
            result.AddWarning("没有配置任何演出事件");

        if (backgroundSettings == null)
        {
            result.AddWarning("没有配置背景图");
        }
        else if (backgroundSettings.layers != null)
        {
            if (backgroundSettings.layers.Count == 0)
                result.AddWarning("No background layers are configured");

            var layerIds = new HashSet<string>();
            foreach (var layer in backgroundSettings.layers)
            {
                if (layer == null) continue;
                if (string.IsNullOrWhiteSpace(layer.layerId) || !layerIds.Add(layer.layerId))
                    result.AddError("Background layer IDs must be non-empty and unique");
                if (layer.scale.x == 0f || layer.scale.y == 0f)
                    result.AddError($"Background layer '{layer.displayName}' has a zero scale");
                if (layer.SortingOrder < short.MinValue || layer.SortingOrder > short.MaxValue)
                    result.AddError($"Background layer '{layer.displayName}' has an out-of-range sorting order");
                if (layer.contentType == BackgroundLayerContentType.SequentialTiles)
                {
                    if (layer.sequence == null || layer.sequence.Count == 0)
                        result.AddError($"Background layer '{layer.displayName}' has no sequence tiles");
                    else if (layer.sequence.Any(entry => entry == null || entry.sprite == null || entry.repeatCount < 1))
                        result.AddError($"Background layer '{layer.displayName}' has an invalid sequence tile");
                }
                else if (layer.sprite == null)
                {
                    result.AddError($"Background layer '{layer.displayName}' has no sprite");
                }
                else if (layer.contentType == BackgroundLayerContentType.RepeatedSprite &&
                         layer.sprite.bounds.size.x * Mathf.Abs(layer.scale.x) <= Mathf.Epsilon)
                {
                    result.AddError($"Repeated background layer '{layer.displayName}' has an invalid repeat width");
                }

                if (layer.contentType != BackgroundLayerContentType.RepeatedSprite &&
                    !Mathf.Approximately(layer.horizontalScrollSpeed, 0f))
                    result.AddError($"Scrolling background layer '{layer.displayName}' must use repeated content");
            }
        }
        else if (backgroundSettings.mode == BackgroundMode.SingleInfiniteScroll)
        {
            if (backgroundSettings.singleBackground == null)
                result.AddWarning("没有配置背景图");
        }
        else if (backgroundSettings.mode == BackgroundMode.SequentialTiles)
        {
            if (backgroundSettings.sequence == null || backgroundSettings.sequence.Count == 0)
            {
                result.AddWarning("顺序背景列表为空");
            }
            else
            {
                for (int i = 0; i < backgroundSettings.sequence.Count; i++)
                {
                    var entry = backgroundSettings.sequence[i];
                    if (entry == null || entry.sprite == null)
                        result.AddError($"顺序背景 #{i + 1} 没有配置图片");
                    else if (entry.repeatCount < 1)
                        result.AddError($"顺序背景 #{i + 1} 的重复次数必须大于等于 1");
                }

                float sequenceEndX = backgroundSettings.sequenceStartX + backgroundSettings.CalculateSequenceWidth();
                float playableStartX = Mathf.Min(0f, playerSpawnPosition.x);
                float playableEndX = Mathf.Max(levelLength, levelEndPositionX);
                if (backgroundSettings.sequenceStartX > playableStartX)
                    result.AddWarning($"顺序背景起点 X({backgroundSettings.sequenceStartX})晚于可玩区域起点 X({playableStartX})");
                if (sequenceEndX < playableEndX)
                    result.AddWarning($"顺序背景终点 X({sequenceEndX})未覆盖可玩区域终点 X({playableEndX})");
            }
        }
        else if (backgroundSettings.parallaxLayers == null || backgroundSettings.parallaxLayers.Count == 0)
        {
            result.AddWarning("没有配置背景图");
        }

        foreach (var g in groups)
        {
            int count = elements.Count(e => e.IsInGroup(g.groupId));
            if (count == 0)
                result.AddWarning($"元素组'{g.groupName}'内没有元素");
        }

        if (result.errors.Count == 0)
            result.isValid = true;

        return result;
    }

    static void ValidatePerformanceClips(
        LevelValidationResult result, PerformanceScript script, HashSet<string> slotIds)
    {
        foreach (var clip in script.clips ?? new List<PerformanceClip>())
        {
            if (clip == null) continue;
            if (clip.startTime < 0f || clip.duration < 0f)
                result.AddError($"Performance script '{script.scriptId}' has a clip with invalid timing");
            if (clip.clipType != PerformanceClipType.MoveCamera &&
                !slotIds.Contains(clip.actorSlotId))
                result.AddError($"Performance script '{script.scriptId}' clip references unknown slot '{clip.actorSlotId}'");
            if ((clip.clipType == PerformanceClipType.MoveActor ||
                 clip.clipType == PerformanceClipType.MoveCamera) &&
                (float.IsNaN(clip.targetPosition.x) || float.IsInfinity(clip.targetPosition.x) ||
                 float.IsNaN(clip.targetPosition.y) || float.IsInfinity(clip.targetPosition.y)))
                result.AddError($"Performance script '{script.scriptId}' has an invalid movement target");
            if (clip.clipType == PerformanceClipType.PlayAnimation &&
                string.IsNullOrWhiteSpace(clip.animationName))
                result.AddError($"Performance script '{script.scriptId}' has an animation clip without a name");
        }
    }

    // ========== JSON 导入/导出（由 Editor 端 LevelDataJsonUtility 实现）==========
    // 运行时通过 ExportToJson / ImportFromJson 为 stub，实际序列化在 Editor 中完成
}
