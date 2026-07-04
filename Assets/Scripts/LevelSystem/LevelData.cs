using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
    ParallaxLayers
}

public enum StoryTriggerMode
{
    Position,
    Conditions,
    LevelStart,
    LevelComplete
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
public class BackgroundSettings
{
    public const int DefaultSortingOrder = -10000;

    public BackgroundMode mode = BackgroundMode.SingleInfiniteScroll;

    public Sprite singleBackground;
    public float singleParallaxFactor = 0f;
    public int singleSortingOrder = DefaultSortingOrder;

    public List<ParallaxLayerData> parallaxLayers = new List<ParallaxLayerData>();
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

    public List<LevelVariableCondition> appearConditions = new List<LevelVariableCondition>();
    public List<ElementCustomParameter> customParameters = new List<ElementCustomParameter>();
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

// ============================================================
// 元素组
// ============================================================

[Serializable]
public class ElementGroup
{
    public string groupId;
    public string groupName;
    public float triggerPositionX;
    public bool mustClearToProceed;
    public List<LevelVariableCondition> triggerConditions = new List<LevelVariableCondition>();
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

    [Header("过场动画触发器")]
    public List<StoryTriggerPoint> storyTriggers = new List<StoryTriggerPoint>();

    [Header("元素组")]
    public List<ElementGroup> groups = new List<ElementGroup>();

    [Header("预制体库（编辑器辅助）")]
    public List<ElementPrefabEntry> enemyPrefabLibrary = new List<ElementPrefabEntry>();
    public List<ElementPrefabEntry> itemPrefabLibrary = new List<ElementPrefabEntry>();
    public List<ElementPrefabEntry> obstaclePrefabLibrary = new List<ElementPrefabEntry>();

    // ========== 查找辅助 ==========

    public ElementGroup FindGroup(string groupId)
    {
        return groups.Find(g => g.groupId == groupId);
    }

    public List<LevelElement> GetElementsByGroup(string groupId)
    {
        return elements.Where(e => e.groupId == groupId).ToList();
    }

    public List<LevelElement> GetUngroupedElements()
    {
        return elements.Where(e => string.IsNullOrEmpty(e.groupId)).ToList();
    }

    public LevelVariableDefinition FindVariable(string name)
    {
        return variables.Find(v => v.variableName == name);
    }

    // ========== 校验 ==========

    public LevelValidationResult Validate()
    {
        var result = new LevelValidationResult();

        if (string.IsNullOrEmpty(levelName))
            result.AddError("关卡名称不能为空");

        if (playerPrefab == null)
            result.AddError("玩家预制体不能为空");

        if (levelEndPositionX <= playerSpawnPosition.x)
            result.AddError($"终点位置X({levelEndPositionX})必须大于起点X({playerSpawnPosition.x})");

        for (int i = 0; i < elements.Count; i++)
        {
            var el = elements[i];
            if (el.prefab == null)
                result.AddError($"元素 #{i} '{el.displayName}' 引用的预制体为空");
            if (string.IsNullOrEmpty(el.elementId))
                result.AddError($"元素 #{i} '{el.displayName}' 缺少elementId");
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

        var variableNames = new HashSet<string>(variables.Select(v => v.variableName));
        foreach (var el in elements)
        {
            foreach (var cond in el.appearConditions)
            {
                if (!string.IsNullOrEmpty(cond.variableName) && !variableNames.Contains(cond.variableName))
                    result.AddError($"元素 '{el.displayName}' 引用了未定义的变量'{cond.variableName}'");
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

        // 警告
        if (elements.Count == 0)
            result.AddWarning("关卡没有任何元素");

        if (storyTriggers.Count == 0)
            result.AddWarning("没有配置任何过场动画");

        if (backgroundSettings.singleBackground == null &&
            (backgroundSettings.parallaxLayers == null || backgroundSettings.parallaxLayers.Count == 0))
            result.AddWarning("没有配置背景图");

        foreach (var g in groups)
        {
            int count = elements.Count(e => e.groupId == g.groupId);
            if (count == 0)
                result.AddWarning($"元素组'{g.groupName}'内没有元素");
        }

        if (result.errors.Count == 0)
            result.isValid = true;

        return result;
    }

    // ========== JSON 导入/导出（由 Editor 端 LevelDataJsonUtility 实现）==========
    // 运行时通过 ExportToJson / ImportFromJson 为 stub，实际序列化在 Editor 中完成
}
