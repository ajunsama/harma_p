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

public enum BackgroundScrollDirection
{
    None,
    RightToLeft,
    LeftToRight
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
    public BackgroundScrollDirection autoScrollDirection = BackgroundScrollDirection.None;
    [Min(0f)] public float autoScrollSpeed;
    [HideInInspector] public float horizontalScrollSpeed;

    public float MotionMultiplierX => depthBand == BackgroundDepthBand.Far
        ? 0f
        : depthBand == BackgroundDepthBand.Mid ? 1f : Mathf.Clamp(nearMotionMultiplier, 0f, 2f);

    public float MotionMultiplierY => enableVerticalMotion
        ? (depthBand == BackgroundDepthBand.Far
            ? 0f
            : depthBand == BackgroundDepthBand.Mid ? 1f : Mathf.Clamp(verticalMotionMultiplier, 0f, 2f))
        : 1f;

    public int SortingOrder => BackgroundSettings.GetBandSortingBase(depthBand) + sortingOffset;

    public float AutoScrollVelocityX => autoScrollDirection == BackgroundScrollDirection.RightToLeft
        ? -Mathf.Max(0f, autoScrollSpeed)
        : autoScrollDirection == BackgroundScrollDirection.LeftToRight
            ? Mathf.Max(0f, autoScrollSpeed)
            : 0f;

    public float CalculateAutoScrollOffset(float elapsedTime)
    {
        return AutoScrollVelocityX * Mathf.Max(0f, elapsedTime);
    }

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
    public const int CurrentDataVersion = 3;

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

        if (dataVersion < 3)
        {
            foreach (var layer in layers)
            {
                if (layer == null || Mathf.Approximately(layer.horizontalScrollSpeed, 0f)) continue;
                layer.autoScrollDirection = layer.horizontalScrollSpeed < 0f
                    ? BackgroundScrollDirection.RightToLeft
                    : BackgroundScrollDirection.LeftToRight;
                layer.autoScrollSpeed = Mathf.Abs(layer.horizontalScrollSpeed);
                layer.horizontalScrollSpeed = 0f;
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

