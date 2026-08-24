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
    /// 暂停游戏主循环
    /// </summary>
    void PauseGame()
    {
        _previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        GameLog.Verbose("[StoryManager] 游戏暂停，进入剧情模式");
    }

/// <summary>
    /// 恢复游戏主循环
    /// </summary>
    void ResumeGame()
    {
        Time.timeScale = _previousTimeScale > 0 ? _previousTimeScale : 1f;
        GameLog.Verbose("[StoryManager] 剧情结束，恢复游戏");
    }

/// <summary>
    /// 结束剧情播放
    /// </summary>
    void EndStory(bool deferFinalize = false, bool skipped = false)
    {
        isPlaying = false;
        _waitingForInput = false;
        _playCoroutine = null;

        _performanceSession?.Complete(skipped);
        _performanceSession = null;

        // 隐藏UI
        if (storyUI != null)
            storyUI.Hide();

        OnStoryEnd?.Invoke();

        _currentSequence = null;
        if (!deferFinalize)
            FinalizeStoryEnd();
    }

void FinalizeStoryEnd()
    {
        ResumeGame();
        ReleasePlayerControlLock();

        var callback = _onComplete;
        _onComplete = null;
        callback?.Invoke();
    }

IEnumerator SkipStoryCoroutine()
    {
        _isSkipping = true;
        storyUI?.StopTypewriter();

        yield return FadeBlackout(1f, 0.12f);
        EndStory(true, true);
        yield return null;
        yield return FadeBlackout(0f, 0.12f);

        FinalizeStoryEnd();
        _isSkipping = false;
        _skipCoroutine = null;
    }

void ReleasePlayerControlLock()
    {
        if (_lockedPlayer != null && _controlLockToken != 0)
            _lockedPlayer.ReleaseControlLock(_controlLockToken);
        _lockedPlayer = null;
        _controlLockToken = 0;
    }

void OnDestroy()
    {
        if ((isPlaying || _isPreparing || _isSkipping) && Time.timeScale == 0f)
            Time.timeScale = _previousTimeScale > 0f ? _previousTimeScale : 1f;
        ReleasePlayerControlLock();
        if (Instance == this) Instance = null;
    }

void CreateBlackoutCurtain()
    {
        var go = new GameObject("StorySkipCurtain",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(CanvasGroup), typeof(Image));
        go.transform.SetParent(transform, false);

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = go.GetComponent<Image>();
        image.color = Color.black;

        _blackoutCanvasGroup = go.GetComponent<CanvasGroup>();
        _blackoutCanvasGroup.alpha = 0f;
        _blackoutCanvasGroup.interactable = false;
        _blackoutCanvasGroup.blocksRaycasts = false;
    }

IEnumerator FadeBlackout(float targetAlpha, float duration)
    {
        if (_blackoutCanvasGroup == null) yield break;

        float from = _blackoutCanvasGroup.alpha;
        _blackoutCanvasGroup.blocksRaycasts = true;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            _blackoutCanvasGroup.alpha = Mathf.Lerp(from, targetAlpha,
                duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f);
            yield return null;
        }
        _blackoutCanvasGroup.alpha = targetAlpha;
        _blackoutCanvasGroup.blocksRaycasts = targetAlpha > 0f;
    }
}
