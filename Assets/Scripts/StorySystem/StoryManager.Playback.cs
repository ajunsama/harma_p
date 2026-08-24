using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public partial class StoryManager
{
/// <summary>
    /// 播放指定ID的剧情
    /// </summary>
    /// <param name="storyId">剧情ID</param>
    /// <param name="onComplete">播放完成后的回调</param>
    public void PlayStory(string storyId, Action onComplete = null,
        IStoryPerformanceSession performanceSession = null)
    {
        GameLog.Verbose($"[StoryManager] PlayStory被调用, storyId={storyId}, isPlaying={isPlaying}, 已加载剧情数={_storyLookup.Count}");

        if (IsPlaying)
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

        GameLog.Verbose($"[StoryManager] 找到剧情 {storyId}，共 {sequence.dialogues?.Count ?? 0} 条对话，开始播放");
        PlayStory(sequence, onComplete, performanceSession);
    }

/// <summary>
    /// 播放指定的剧情序列
    /// </summary>
    public void PlayStory(StorySequence sequence, Action onComplete = null,
        IStoryPerformanceSession performanceSession = null)
    {
        if (IsPlaying)
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

        _isPreparing = true;
        _onComplete = onComplete;
        _performanceSession = performanceSession;
        _prepareCoroutine = StartCoroutine(PrepareAndPlay(sequence));
    }

IEnumerator PrepareAndPlay(StorySequence sequence)
    {
        var playerObject = GameObject.FindGameObjectWithTag("Player");
        _lockedPlayer = playerObject != null ? playerObject.GetComponent<PlayerMovement>() : null;
        if (_lockedPlayer == null)
        {
            Debug.LogError($"[StoryManager] Cannot start story '{sequence.storyId}': player is missing.");
            AbortPreparation();
            yield break;
        }

        _controlLockToken = _lockedPlayer.AcquireControlLock($"Story:{sequence.storyId}");

        float elapsed = 0f;
        while (true)
        {
            while (!_lockedPlayer.IsSafeForStory)
            {
                elapsed += Time.unscaledDeltaTime;
                if (elapsed >= SafeStateTimeout)
                {
                    Debug.LogError($"[StoryManager] Timed out waiting for a safe state before story '{sequence.storyId}'.");
                    AbortPreparation();
                    yield break;
                }
                yield return null;
            }

            _lockedPlayer.PrepareStandingAnimationForStory();
            yield return new WaitForFixedUpdate();
            yield return null;
            if (_lockedPlayer == null)
            {
                AbortPreparation();
                yield break;
            }
            elapsed += Time.unscaledDeltaTime;
            if (elapsed >= SafeStateTimeout)
            {
                Debug.LogError($"[StoryManager] Timed out waiting for the standing animation before story '{sequence.storyId}'.");
                AbortPreparation();
                yield break;
            }
            if (_lockedPlayer.IsReadyForStory)
                break;
        }

        _isPreparing = false;
        _prepareCoroutine = null;
        _currentSequence = sequence;
        _playCoroutine = StartCoroutine(PlaySequenceCoroutine(sequence));
    }

void AbortPreparation()
    {
        _isPreparing = false;
        _prepareCoroutine = null;
        _performanceSession = null;
        ReleasePlayerControlLock();
        var callback = _onComplete;
        _onComplete = null;
        callback?.Invoke();
    }

/// <summary>
    /// 跳过当前剧情
    /// </summary>
    public void SkipStory()
    {
        if (_isSkipping) return;
        if (_isPreparing)
        {
            if (_prepareCoroutine != null) StopCoroutine(_prepareCoroutine);
            AbortPreparation();
            return;
        }
        if (!isPlaying) return;

        if (_playCoroutine != null)
        {
            StopCoroutine(_playCoroutine);
            _playCoroutine = null;
        }

        _skipCoroutine = StartCoroutine(SkipStoryCoroutine());
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
            storyUI.Show(sequence.maskBackground);

        OnStoryStart?.Invoke();

        GameLog.Verbose($"[StoryManager] 开始播放剧情: {sequence.storyId}，共 {sequence.dialogues.Count} 条对话");

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
            _performanceSession?.BeginDialogue(
                dialogue, StoryPerformanceCueTriggerTiming.DialogueStart);

            string currentSide = dialogue.avatarPosition?.ToLower() ?? "left";
            bool sideChanged = previousSide != null && previousSide != currentSide;
            GameLog.Verbose($"[StoryManager] Dialogue[{i}]: side={currentSide}, prevSide={previousSide}, sideChanged={sideChanged}, hasAnimator={hasAnimator}, styleId={dialogue.styleId}");

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

            // 阻塞型 Cue 完成前不接受进入下一句的输入
            if (_performanceSession != null)
                yield return _performanceSession.WaitForBlockingCue(dialogue.id);

            // 等待玩家点击继续
            yield return WaitForPlayerInput();

            // 配置为“玩家点击下一句后”的 Cue 在本次点击被消费后才启动。
            if (_performanceSession != null)
            {
                _performanceSession.BeginDialogue(
                    dialogue, StoryPerformanceCueTriggerTiming.AfterAdvanceInput);
                yield return _performanceSession.WaitForBlockingCue(dialogue.id);
            }

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
}
