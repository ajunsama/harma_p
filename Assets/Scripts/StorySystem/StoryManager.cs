using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// 剧情播放管理器 - 单例，控制整个剧情播放流程
/// 负责：暂停游戏主循环 → 播放剧情 → 处理结果 → 恢复游戏
/// </summary>
public class StoryManager : MonoBehaviour
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

    // 剧情标志位系统（用于游戏主循环中的flag触发）
    private HashSet<string> _storyFlags = new HashSet<string>();

    // 属性
    public bool IsPlaying => isPlaying;
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
    }

    void Start()
    {
        // 自动加载JSON数据
        if (storyJsonFile != null)
        {
            LoadStoryData(storyJsonFile.text);
        }

        // 确保UI初始隐藏
        if (storyUI != null)
            storyUI.Hide();
    }

    /// <summary>
    /// 从JSON字符串加载剧情数据
    /// </summary>
    public void LoadStoryData(string json)
    {
        _loadedData = JsonUtility.FromJson<StoryDataCollection>(json);

        _storyLookup.Clear();
        if (_loadedData?.stories != null)
        {
            foreach (var story in _loadedData.stories)
            {
                if (!string.IsNullOrEmpty(story.storyId))
                    _storyLookup[story.storyId] = story;
            }
        }

        Debug.Log($"[StoryManager] 加载了 {_storyLookup.Count} 段剧情数据");
    }

    /// <summary>
    /// 从TextAsset加载剧情数据
    /// </summary>
    public void LoadStoryData(TextAsset jsonAsset)
    {
        if (jsonAsset != null)
            LoadStoryData(jsonAsset.text);
    }

    /// <summary>
    /// 播放指定ID的剧情
    /// </summary>
    /// <param name="storyId">剧情ID</param>
    /// <param name="onComplete">播放完成后的回调</param>
    public void PlayStory(string storyId, Action onComplete = null)
    {
        Debug.Log($"[StoryManager] PlayStory被调用, storyId={storyId}, isPlaying={isPlaying}, 已加载剧情数={_storyLookup.Count}");

        if (isPlaying)
        {
            Debug.LogWarning($"[StoryManager] 正在播放中，忽略请求: {storyId}");
            return;
        }

        if (!_storyLookup.TryGetValue(storyId, out StorySequence sequence))
        {
            Debug.LogError($"[StoryManager] 找不到剧情: {storyId}，已加载的ID: [{string.Join(", ", _storyLookup.Keys)}]");
            onComplete?.Invoke();
            return;
        }

        Debug.Log($"[StoryManager] 找到剧情 {storyId}，共 {sequence.dialogues?.Count ?? 0} 条对话，开始播放");
        PlayStory(sequence, onComplete);
    }

    /// <summary>
    /// 播放指定的剧情序列
    /// </summary>
    public void PlayStory(StorySequence sequence, Action onComplete = null)
    {
        if (isPlaying)
        {
            Debug.LogWarning("[StoryManager] 正在播放中，忽略请求");
            return;
        }

        if (sequence == null || sequence.dialogues == null || sequence.dialogues.Count == 0)
        {
            Debug.LogWarning("[StoryManager] 剧情数据为空");
            onComplete?.Invoke();
            return;
        }

        _currentSequence = sequence;
        _onComplete = onComplete;
        _playCoroutine = StartCoroutine(PlaySequenceCoroutine(sequence));
    }

    /// <summary>
    /// 跳过当前剧情
    /// </summary>
    public void SkipStory()
    {
        if (!isPlaying) return;

        if (_playCoroutine != null)
        {
            StopCoroutine(_playCoroutine);
            _playCoroutine = null;
        }

        EndStory();
    }

    /// <summary>
    /// 设置剧情标志位
    /// </summary>
    public void SetFlag(string flagName)
    {
        _storyFlags.Add(flagName);
    }

    /// <summary>
    /// 检查剧情标志位
    /// </summary>
    public bool HasFlag(string flagName)
    {
        return _storyFlags.Contains(flagName);
    }

    /// <summary>
    /// 移除剧情标志位
    /// </summary>
    public void RemoveFlag(string flagName)
    {
        _storyFlags.Remove(flagName);
    }

    // ====================
    // 核心播放协程
    // ====================

    IEnumerator PlaySequenceCoroutine(StorySequence sequence)
    {
        // 1. 暂停游戏主循环
        PauseGame();
        isPlaying = true;
        currentDialogueIndex = 0;

        // 2. 显示UI
        if (storyUI != null)
            storyUI.Show();

        OnStoryStart?.Invoke();

        Debug.Log($"[StoryManager] 开始播放剧情: {sequence.storyId}，共 {sequence.dialogues.Count} 条对话");

        // 3. 入场动画（如果配置了动画控制器）
        bool hasAnimator = storyUI != null && storyUI.animator != null;
        string firstSpeakerSide = "left";

        if (hasAnimator && templateLibrary != null)
        {
            ScanAvatarsForEntrance(sequence,
                out string leftAvatarId, out string rightAvatarId, out firstSpeakerSide);

            // 找到第一个说话者的名称，用于入场色块动画
            string firstSpeakerName = null;
            if (sequence.dialogues.Count > 0)
            {
                var firstDialogue = sequence.dialogues[0];
                firstSpeakerName = firstDialogue.speakerName;
                if (string.IsNullOrEmpty(firstSpeakerName) && !string.IsNullOrEmpty(firstDialogue.styleId))
                {
                    var style = templateLibrary.GetStyle(firstDialogue.styleId);
                    if (style != null) firstSpeakerName = style.defaultSpeakerName;
                }
            }

            Sprite leftSprite = null; float leftScale = 1f;
            Sprite rightSprite = null; float rightScale = 1f;

            if (!string.IsNullOrEmpty(leftAvatarId))
            {
                var la = templateLibrary.GetAvatar(leftAvatarId);
                if (la != null) { leftSprite = la.avatarSprite; leftScale = la.scale; }
            }
            if (!string.IsNullOrEmpty(rightAvatarId))
            {
                var ra = templateLibrary.GetAvatar(rightAvatarId);
                if (ra != null) { rightSprite = ra.avatarSprite; rightScale = ra.scale; }
            }

            // 获取第一条对话的颜色用于入场色块动画
            Color entranceColor = Color.white;
            if (sequence.dialogues.Count > 0)
            {
                var firstDlg = sequence.dialogues[0];
                if (!string.IsNullOrEmpty(firstDlg.styleId))
                {
                    var entranceStyle = templateLibrary.GetStyle(firstDlg.styleId);
                    if (entranceStyle != null)
                    {
                        entranceColor = entranceStyle.dialogueBoxColor;
                        // 入场前只设置名称样式（阴影色/字号），不触发 effectController.SetupBackground
                        storyUI.ApplySpeakerNameStyle(entranceStyle.dialogueBoxColor, entranceStyle.speakerNameFontSize);
                    }
                }
            }

            storyUI.SetupBothAvatars(leftSprite, leftScale, rightSprite, rightScale);
            yield return storyUI.PlayEntranceAnimation(firstSpeakerSide == "left", firstSpeakerName, entranceColor);
        }

        // 4. 逐条播放对话
        string previousSide = null;

        for (int i = 0; i < sequence.dialogues.Count; i++)
        {
            currentDialogueIndex = i;
            StoryDialogue dialogue = sequence.dialogues[i];

            OnDialogueStart?.Invoke(dialogue);

            string currentSide = dialogue.avatarPosition?.ToLower() ?? "left";
            bool sideChanged = previousSide != null && previousSide != currentSide;
            Debug.Log($"[StoryManager] Dialogue[{i}]: side={currentSide}, prevSide={previousSide}, sideChanged={sideChanged}, hasAnimator={hasAnimator}, styleId={dialogue.styleId}");

            if (sideChanged && hasAnimator)
            {
                // 获取新旧对话框颜色用于百叶窗过渡
                Color oldColor = storyUI.CurrentDialogueBoxColor;
                Color newColor = oldColor;
                if (!string.IsNullOrEmpty(dialogue.styleId) && templateLibrary != null)
                {
                    var style = templateLibrary.GetStyle(dialogue.styleId);
                    if (style != null) newColor = style.dialogueBoxColor;
                }

                // 百叶窗特效：关闭→切换内容→打开
                yield return storyUI.PlaySideTransition(currentSide, oldColor, newColor, () =>
                {
                    SetupDialogueDisplay(dialogue, true);
                });
            }
            else
            {
                SetupDialogueDisplay(dialogue, hasAnimator);
                if (hasAnimator)
                    storyUI.HighlightSpeaker(currentSide);
            }

            previousSide = currentSide;

            // 播放打字机效果
            float speed = dialogue.playSpeed > 0 ? dialogue.playSpeed : defaultPlaySpeed;
            storyUI.StartTypewriter(dialogue.content, speed);

            // 等待打字完成或玩家点击跳过
            yield return WaitForTypewriterOrSkip();

            // 确保文字完整显示
            storyUI.CompleteTypewriter();

            // 等待玩家点击继续
            yield return WaitForPlayerInput();

            OnDialogueEnd?.Invoke(dialogue);
        }

        // 5. 播放结束
        EndStory();
    }

    /// <summary>
    /// 扫描剧情序列，找出左右两侧的头像信息和第一个说话者方向
    /// 用于入场动画的准备
    /// </summary>
    void ScanAvatarsForEntrance(StorySequence sequence,
        out string leftAvatarId, out string rightAvatarId, out string firstSpeakerSide)
    {
        leftAvatarId = null;
        rightAvatarId = null;
        firstSpeakerSide = "left";

        if (sequence?.dialogues == null) return;

        bool foundFirst = false;
        foreach (var d in sequence.dialogues)
        {
            if (string.IsNullOrEmpty(d.avatarId)) continue;

            string pos = d.avatarPosition?.ToLower() ?? "left";
            if (!foundFirst)
            {
                firstSpeakerSide = pos;
                foundFirst = true;
            }

            if (pos == "left" && leftAvatarId == null)
                leftAvatarId = d.avatarId;
            else if (pos == "right" && rightAvatarId == null)
                rightAvatarId = d.avatarId;

            if (leftAvatarId != null && rightAvatarId != null)
                break;
        }
    }

    /// <summary>
    /// 设置单条对话的UI显示
    /// </summary>
    /// <param name="dialogue">对话数据</param>
    /// <param name="useNewAvatarMode">是否使用新头像模式（更新单侧而不隐藏其他）</param>
    void SetupDialogueDisplay(StoryDialogue dialogue, bool useNewAvatarMode = false)
    {
        if (storyUI == null || templateLibrary == null) return;

        // 应用样式
        if (!string.IsNullOrEmpty(dialogue.styleId))
        {
            var style = templateLibrary.GetStyle(dialogue.styleId);
            if (style != null)
            {
                storyUI.ApplyStyle(style);

                // 设置说话者名称（优先使用对话单独设置的名称）
                string speaker = !string.IsNullOrEmpty(dialogue.speakerName)
                    ? dialogue.speakerName
                    : style.defaultSpeakerName;
                bool isLeft = (dialogue.avatarPosition?.ToLower() ?? "left") != "right";
                storyUI.SetSpeakerName(speaker, isLeft);
            }
        }

        // 设置头像
        if (!string.IsNullOrEmpty(dialogue.avatarId))
        {
            var avatar = templateLibrary.GetAvatar(dialogue.avatarId);
            if (avatar != null)
            {
                if (useNewAvatarMode)
                    storyUI.UpdateAvatar(dialogue.avatarPosition, avatar.avatarSprite, avatar.scale);
                else
                    storyUI.SetAvatar(dialogue.avatarPosition, avatar.avatarSprite, avatar.scale);
            }
        }

        // 设置图片
        if (dialogue.showImage && !string.IsNullOrEmpty(dialogue.imageId))
        {
            var image = templateLibrary.GetImage(dialogue.imageId);
            storyUI.SetImage(true, image?.imageSprite);
        }
        else
        {
            storyUI.SetImage(false);
        }
    }

    /// <summary>
    /// 等待打字机效果完成，或者玩家点击提前完成
    /// </summary>
    IEnumerator WaitForTypewriterOrSkip()
    {
        while (storyUI != null && storyUI.IsTypewriting)
        {
            // 检测点击或按键输入来跳过打字效果
            if (GetConfirmInput())
            {
                storyUI.CompleteTypewriter();
                // 等待玩家松开按键，防止同一次点击被下一步WaitForPlayerInput消费
                yield return WaitForInputRelease();
                yield break;
            }
            yield return null;
        }
    }

    /// <summary>
    /// 等待玩家点击/按键输入以继续下一句
    /// </summary>
    IEnumerator WaitForPlayerInput()
    {
        _waitingForInput = true;

        // 先等一帧，确保不会读到上次的输入
        yield return null;

        while (_waitingForInput)
        {
            if (GetConfirmInput())
            {
                _waitingForInput = false;
            }
            yield return null;
        }

    }

    /// <summary>
    /// 等待所有确认按键都释放（防止一次点击被多步骤连续消费）
    /// </summary>
    IEnumerator WaitForInputRelease()
    {
        while (IsConfirmHeld())
        {
            yield return null;
        }
    }

    /// <summary>
    /// 检测确认键是否正在按住
    /// </summary>
    bool IsConfirmHeld()
    {
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            return true;
        if (Keyboard.current != null &&
            (Keyboard.current.spaceKey.isPressed || Keyboard.current.enterKey.isPressed))
            return true;
        if (Gamepad.current != null && Gamepad.current.buttonSouth.isPressed)
            return true;
        return false;
    }

    /// <summary>
    /// 检测确认输入（鼠标左键点击 / 键盘Space/Enter / 手柄A键）
    /// </summary>
    bool GetConfirmInput()
    {
        // 鼠标左键
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            return true;

        // 键盘
        if (Keyboard.current != null)
        {
            if (Keyboard.current.spaceKey.wasPressedThisFrame ||
                Keyboard.current.enterKey.wasPressedThisFrame)
                return true;
        }

        // 手柄
        if (Gamepad.current != null)
        {
            if (Gamepad.current.buttonSouth.wasPressedThisFrame)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 暂停游戏主循环
    /// </summary>
    void PauseGame()
    {
        _previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        Debug.Log("[StoryManager] 游戏暂停，进入剧情模式");
    }

    /// <summary>
    /// 恢复游戏主循环
    /// </summary>
    void ResumeGame()
    {
        Time.timeScale = _previousTimeScale > 0 ? _previousTimeScale : 1f;
        Debug.Log("[StoryManager] 剧情结束，恢复游戏");
    }

    /// <summary>
    /// 结束剧情播放
    /// </summary>
    void EndStory()
    {
        isPlaying = false;
        _waitingForInput = false;

        // 隐藏UI
        if (storyUI != null)
            storyUI.Hide();

        OnStoryEnd?.Invoke();

        // 恢复游戏
        ResumeGame();

        // 执行完成回调
        var callback = _onComplete;
        _onComplete = null;
        _currentSequence = null;
        callback?.Invoke();
    }

    // ====================
    // 结果处理
    // ====================

    /// <summary>
    /// 处理剧情结果动作
    /// </summary>
    public void ProcessResultAction(StoryResultAction action)
    {
        if (action == null) return;

        switch (action.actionType)
        {
            case StoryResultAction.ActionType.SpawnEnemies:
                // 通过BattleWaveManager生成敌人
                if (BattleWaveManager.Instance != null && action.intParameter >= 0)
                {
                    BattleWaveManager.Instance.StartWave(action.intParameter);
                }
                break;

            case StoryResultAction.ActionType.GameOver:
                if (LevelProgressManager.Instance != null)
                {
                    LevelProgressManager.Instance.OnLevelFailed?.Invoke();
                }
                break;

            case StoryResultAction.ActionType.LevelComplete:
                if (LevelProgressManager.Instance != null)
                {
                    LevelProgressManager.Instance.OnLevelComplete?.Invoke();
                }
                break;

            case StoryResultAction.ActionType.LoadScene:
                if (!string.IsNullOrEmpty(action.parameter))
                {
                    Time.timeScale = 1f;
                    UnityEngine.SceneManagement.SceneManager.LoadScene(action.parameter);
                }
                break;

            case StoryResultAction.ActionType.SetFlag:
                if (!string.IsNullOrEmpty(action.parameter))
                {
                    SetFlag(action.parameter);
                }
                break;

            case StoryResultAction.ActionType.None:
            default:
                break;
        }
    }
}
