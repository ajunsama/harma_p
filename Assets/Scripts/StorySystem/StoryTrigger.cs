using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 剧情触发器 - 挂载到场景中，用于在特定条件下触发剧情播放
/// 支持多种触发方式：位置触发、标志位触发、手动触发
/// </summary>
public class StoryTrigger : MonoBehaviour
{
    public enum TriggerType
    {
        [Tooltip("玩家到达指定位置时触发")]
        Position,
        [Tooltip("游戏标志位被设置时触发")]
        Flag,
        [Tooltip("关卡开始时触发")]
        LevelStart,
        [Tooltip("波次完成后触发")]
        WaveComplete,
        [Tooltip("所有波次完成后触发（Boss战后等）")]
        AllWavesComplete,
        [Tooltip("手动调用触发")]
        Manual
    }

    [Header("触发配置")]
    [Tooltip("触发类型")]
    public TriggerType triggerType = TriggerType.Position;

    [Tooltip("要播放的剧情ID")]
    public string storyId;

    [Tooltip("是否只触发一次")]
    public bool triggerOnce = true;

    [Header("位置触发设置")]
    [Tooltip("直接在Scene中拖动本物体来设置触发X坐标")]
    public bool useTransformPosition = true;

    [Tooltip("手动指定触发位置X（仅在useTransformPosition关闭时生效）")]
    public float triggerPositionX;

    [Tooltip("触发方向：true=玩家从左向右越过时触发")]
    public bool triggerFromLeft = true;

    /// <summary>
    /// 获取实际触发X坐标
    /// </summary>
    public float GetTriggerX()
    {
        return useTransformPosition ? transform.position.x : triggerPositionX;
    }

    [Header("标志位触发设置")]
    [Tooltip("监听的标志位名称")]
    public string flagName;

    [Header("波次触发设置")]
    [Tooltip("指定波次索引（-1表示任意波次）")]
    public int waveIndex = -1;

    [Header("播放后处理")]
    [Tooltip("剧情播放后执行的结果动作列表")]
    public List<StoryResultAction> resultActions = new List<StoryResultAction>();

    [Header("事件")]
    [Tooltip("剧情播放前的自定义事件")]
    public UnityEvent OnBeforeStory;

    [Tooltip("剧情播放后的自定义事件")]
    public UnityEvent OnAfterStory;

    // 内部状态
    private bool _hasTriggered;
    private Transform _playerTransform;
    private bool _subscribedToEvents;

    void Awake()
    {
        Debug.Log($"[StoryTrigger] Awake: {gameObject.name}, 类型={triggerType}, 剧情ID={storyId}");
    }

    void Start()
    {
        // 获取玩家引用
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            _playerTransform = player.transform;
        else
            Debug.LogWarning($"[StoryTrigger] {gameObject.name}: 找不到Player标签的对象");

        // 订阅对应事件
        SubscribeEvents();

        // 关键修复：如果是LevelStart类型，检查关卡是否已经在运行
        // （解决Start执行顺序导致OnLevelStart事件已经触发过的问题）
        if (triggerType == TriggerType.LevelStart)
        {
            if (LevelProgressManager.Instance != null &&
                LevelProgressManager.Instance.CurrentState == LevelProgressManager.LevelState.Playing)
            {
                Debug.Log($"[StoryTrigger] {gameObject.name}: 关卡已在运行中，补偿触发LevelStart剧情");
                OnLevelStarted();
            }
            else
            {
                Debug.Log($"[StoryTrigger] {gameObject.name}: 关卡尚未开始，等待OnLevelStart事件");
            }
        }
    }

    void SubscribeEvents()
    {
        if (_subscribedToEvents) return;
        _subscribedToEvents = true;

        switch (triggerType)
        {
            case TriggerType.LevelStart:
                if (LevelProgressManager.Instance != null)
                {
                    LevelProgressManager.Instance.OnLevelStart.AddListener(OnLevelStarted);
                    Debug.Log($"[StoryTrigger] {gameObject.name}: 已订阅OnLevelStart事件");
                }
                else
                {
                    Debug.LogWarning($"[StoryTrigger] {gameObject.name}: LevelProgressManager.Instance为空，无法订阅!");
                }
                break;

            case TriggerType.WaveComplete:
                if (BattleWaveManager.Instance != null)
                {
                    BattleWaveManager.Instance.OnWaveComplete.AddListener(OnWaveCompleted);
                    Debug.Log($"[StoryTrigger] {gameObject.name}: 已订阅OnWaveComplete事件");
                }
                else
                {
                    Debug.LogWarning($"[StoryTrigger] {gameObject.name}: BattleWaveManager.Instance为空，无法订阅!");
                }
                break;

            case TriggerType.AllWavesComplete:
                if (BattleWaveManager.Instance != null)
                {
                    BattleWaveManager.Instance.OnAllWavesComplete.AddListener(OnAllWavesCompleted);
                    Debug.Log($"[StoryTrigger] {gameObject.name}: 已订阅OnAllWavesComplete事件");
                }
                else
                {
                    Debug.LogWarning($"[StoryTrigger] {gameObject.name}: BattleWaveManager.Instance为空，无法订阅!");
                }
                break;
        }
    }

    void OnDestroy()
    {
        // 取消订阅
        if (_subscribedToEvents)
        {
            if (LevelProgressManager.Instance != null)
                LevelProgressManager.Instance.OnLevelStart.RemoveListener(OnLevelStarted);

            if (BattleWaveManager.Instance != null)
            {
                BattleWaveManager.Instance.OnWaveComplete.RemoveListener(OnWaveCompleted);
                BattleWaveManager.Instance.OnAllWavesComplete.RemoveListener(OnAllWavesCompleted);
            }
        }
    }

    void Update()
    {
        if (_hasTriggered && triggerOnce) return;
        if (StoryManager.Instance != null && StoryManager.Instance.IsPlaying) return;

        switch (triggerType)
        {
            case TriggerType.Position:
                CheckPositionTrigger();
                break;

            case TriggerType.Flag:
                CheckFlagTrigger();
                break;
        }
    }

    /// <summary>
    /// 检测位置触发
    /// </summary>
    void CheckPositionTrigger()
    {
        if (_playerTransform == null) return;

        float playerX = _playerTransform.position.x;
        float triggerX = GetTriggerX();

        if (triggerFromLeft && playerX >= triggerX)
        {
            TriggerStory();
        }
        else if (!triggerFromLeft && playerX <= triggerX)
        {
            TriggerStory();
        }
    }

    /// <summary>
    /// 检测标志位触发
    /// </summary>
    void CheckFlagTrigger()
    {
        if (string.IsNullOrEmpty(flagName)) return;

        if (StoryManager.Instance != null && StoryManager.Instance.HasFlag(flagName))
        {
            // 消耗标志位，防止重复触发
            StoryManager.Instance.RemoveFlag(flagName);
            TriggerStory();
        }
    }

    /// <summary>
    /// 关卡开始时触发
    /// </summary>
    void OnLevelStarted()
    {
        Debug.Log($"[StoryTrigger] {gameObject.name}: OnLevelStarted收到! _hasTriggered={_hasTriggered}, triggerOnce={triggerOnce}");
        if (_hasTriggered && triggerOnce) return;
        TriggerStory();
    }

    /// <summary>
    /// 波次完成时触发
    /// </summary>
    void OnWaveCompleted(int completedWaveIndex)
    {
        if (_hasTriggered && triggerOnce) return;
        if (waveIndex >= 0 && completedWaveIndex != waveIndex) return;
        TriggerStory();
    }

    /// <summary>
    /// 所有波次完成时触发
    /// </summary>
    void OnAllWavesCompleted()
    {
        if (_hasTriggered && triggerOnce) return;
        TriggerStory();
    }

    /// <summary>
    /// 手动触发接口（可从其他脚本调用）
    /// </summary>
    public void TriggerManually()
    {
        if (triggerType != TriggerType.Manual)
        {
            Debug.LogWarning($"[StoryTrigger] {gameObject.name} 不是手动触发类型");
        }
        TriggerStory();
    }

    /// <summary>
    /// 执行触发
    /// </summary>
    void TriggerStory()
    {
        Debug.Log($"[StoryTrigger] {gameObject.name}: TriggerStory调用, _hasTriggered={_hasTriggered}, triggerOnce={triggerOnce}");

        if (_hasTriggered && triggerOnce)
        {
            Debug.Log($"[StoryTrigger] {gameObject.name}: 已触发过且triggerOnce=true，跳过");
            return;
        }

        if (StoryManager.Instance == null)
        {
            Debug.LogError($"[StoryTrigger] {gameObject.name}: StoryManager.Instance为空！无法播放剧情");
            return;
        }

        if (StoryManager.Instance.IsPlaying)
        {
            Debug.LogWarning($"[StoryTrigger] {gameObject.name}: StoryManager正在播放中，跳过");
            return;
        }

        _hasTriggered = true;

        Debug.Log($"[StoryTrigger] {gameObject.name}: >>> 开始触发剧情: {storyId} (类型: {triggerType})");

        OnBeforeStory?.Invoke();

        StoryManager.Instance.PlayStory(storyId, OnStoryComplete);
    }

    /// <summary>
    /// 剧情播放完成后的回调
    /// </summary>
    void OnStoryComplete()
    {
        // 处理结果动作
        if (resultActions != null)
        {
            foreach (var action in resultActions)
            {
                StoryManager.Instance.ProcessResultAction(action);
            }
        }

        OnAfterStory?.Invoke();
    }

    // ====================
    // 编辑器辅助：在Scene视图中绘制触发位置
    // ====================
    void OnDrawGizmos()
    {
        if (triggerType != TriggerType.Position) return;

        float drawX = GetTriggerX();
        Gizmos.color = _hasTriggered ? Color.gray : Color.cyan;

        // 画一条竖线表示触发位置
        Vector3 top = new Vector3(drawX, 5f, 0f);
        Vector3 bottom = new Vector3(drawX, -5f, 0f);
        Gizmos.DrawLine(top, bottom);

        // 画一个小三角表示方向
        float dir = triggerFromLeft ? 0.3f : -0.3f;
        Vector3 center = new Vector3(drawX, 0f, 0f);
        Vector3 arrow = new Vector3(drawX - dir, 0.3f, 0f);
        Vector3 arrowB = new Vector3(drawX - dir, -0.3f, 0f);
        Gizmos.DrawLine(center, arrow);
        Gizmos.DrawLine(center, arrowB);

        // 标签
        #if UNITY_EDITOR
        UnityEditor.Handles.Label(
            new Vector3(drawX, 5.5f, 0f),
            $"剧情: {storyId}\nX={drawX:F1}"
        );
        #endif
    }
}
