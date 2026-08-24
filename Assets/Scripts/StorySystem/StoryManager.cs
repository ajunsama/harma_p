using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 剧情播放管理器 - 单例，控制整个剧情播放流程
/// 负责：暂停游戏主循环 → 播放剧情 → 处理结果 → 恢复游戏
/// </summary>
public partial class StoryManager : MonoBehaviour
{
    public static StoryManager Instance { get; private set; }

    [Header("引用")]
    [Tooltip("剧情UI组件")]
    public StoryUI storyUI;

    [Tooltip("模板库")]
    public StoryTemplateLibrary templateLibrary;

    [Header("JSON数据")]
    [Tooltip("剧情JSON文件（放在Resources或StreamingAssets中）")]
    public TextAsset storyJsonFile;

    [Header("默认设置")]
    [Tooltip("默认文字播放速度（每字符间隔秒数）")]
    public float defaultPlaySpeed = 0.05f;

    [Header("状态")]
    [SerializeField] private bool isPlaying;
    [SerializeField] private int currentDialogueIndex;

    [Header("事件")]
    public UnityEvent OnStoryStart;
    public UnityEvent OnStoryEnd;
    public UnityEvent<StoryDialogue> OnDialogueStart;
    public UnityEvent<StoryDialogue> OnDialogueEnd;

    // 已加载的所有剧情数据
    private StoryDataCollection _loadedData;
    private Dictionary<string, StorySequence> _storyLookup = new Dictionary<string, StorySequence>();

    // 当前播放状态
    private StorySequence _currentSequence;
    private Coroutine _playCoroutine;
    private bool _waitingForInput;
    private float _previousTimeScale;

    // 剧情完成后的回调
    private Action _onComplete;
    private IStoryPerformanceSession _performanceSession;
    private Coroutine _prepareCoroutine;
    private bool _isPreparing;
    private Coroutine _skipCoroutine;
    private bool _isSkipping;
    private CanvasGroup _blackoutCanvasGroup;
    private PlayerMovement _lockedPlayer;
    private int _controlLockToken;
    private const float SafeStateTimeout = 5f;

    // 剧情标志位系统（用于游戏主循环中的flag触发）
    private HashSet<string> _storyFlags = new HashSet<string>();

    // 属性
    public bool IsPlaying => isPlaying || _isPreparing || _isSkipping;
    public StorySequence CurrentSequence => _currentSequence;


    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 初始化模板缓存
        if (templateLibrary != null)
            templateLibrary.BuildCache();

        CreateBlackoutCurtain();
    }

    void Start()
    {
        // 自动加载JSON数据
        if (storyJsonFile != null && _storyLookup.Count == 0)
        {
            LoadStoryData(storyJsonFile.text);
        }

        // 确保UI初始隐藏
        if (storyUI != null)
            storyUI.Hide();
    }


}
