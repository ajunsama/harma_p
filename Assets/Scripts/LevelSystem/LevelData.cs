using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "NewLevel", menuName = "Game/Level Data", order = 1)]
public partial class LevelData : ScriptableObject
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
    // ========== JSON 导入/导出（由 Editor 端 LevelDataJsonUtility 实现）==========
    // 运行时通过 ExportToJson / ImportFromJson 为 stub，实际序列化在 Editor 中完成
}
