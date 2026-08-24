using System;
using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public partial class StoryUI
{
/// <summary>
    /// 显示剧情UI
    /// </summary>
    public void Show()
    {
        Show(true);
    }

public void Show(bool maskBackground)
    {
        // 取消正在进行的淡入淡出，避免和Hide的协程竞态
        StopFade();

        if (storyRoot != null)
            storyRoot.SetActive(true);

        if (maskBackground)
        {
            CaptureBackgroundForBlur();
            PlayBackgroundBlur(backgroundBlurWeight);
        }
        else
        {
            StopBlur();
            ReleaseCapturedBlurTexture();
            SetBackgroundBlurImmediate(0f, false);
        }

        if (_canvasGroup != null)
            _fadeCoroutine = StartCoroutine(FadeCanvasGroup(0f, 1f, fadeTime));

        HideAllAvatars();
        HideImage();
        ClearContent();

        if (avatarBarController != null)
            avatarBarController.HideAll();

        // 重新确保渲染层级：背景条 < 头像 < 对话框
        EnsureAvatarsBehindDialogueBox();
    }

/// <summary>
    /// 隐藏剧情UI
    /// </summary>
    public void Hide()
    {
        StopFade();
        PlayBackgroundBlur(0f);

        if (_canvasGroup != null)
        {
            _fadeCoroutine = StartCoroutine(FadeAndDeactivate());
        }
        else if (storyRoot != null)
        {
            storyRoot.SetActive(false);
        }
    }

void StopFade()
    {
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }
    }

void StopBlur()
    {
        if (_blurCoroutine != null)
        {
            StopCoroutine(_blurCoroutine);
            _blurCoroutine = null;
        }
    }

IEnumerator FadeAndDeactivate()
    {
        yield return FadeCanvasGroup(1f, 0f, fadeTime);
        if (storyRoot != null) storyRoot.SetActive(false);
        _fadeCoroutine = null;
    }

IEnumerator FadeCanvasGroup(float from, float to, float duration)
    {
        if (_canvasGroup == null) yield break;
        float elapsed = 0f;
        _canvasGroup.alpha = from;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        _canvasGroup.alpha = to;
    }

void PlayBackgroundBlur(float targetWeight)
    {
        bool hasVolumeBlur = backgroundBlurVolume != null;
        bool hasCaptureBlur = backgroundBlurCaptureImage != null;
        if (!hasVolumeBlur && !hasCaptureBlur)
            return;

        StopBlur();

        if (targetWeight > 0f)
        {
            if (hasVolumeBlur && !backgroundBlurVolume.gameObject.activeSelf)
                backgroundBlurVolume.gameObject.SetActive(true);

            if (hasCaptureBlur && !backgroundBlurCaptureImage.gameObject.activeSelf)
                backgroundBlurCaptureImage.gameObject.SetActive(true);
        }

        float fromWeight = hasCaptureBlur ? backgroundBlurCaptureImage.color.a : backgroundBlurVolume.weight;
        if (fadeTime <= 0f)
        {
            SetBackgroundBlurImmediate(targetWeight, targetWeight > 0f);
            return;
        }

        _blurCoroutine = StartCoroutine(FadeBackgroundBlur(fromWeight, targetWeight, fadeTime));
    }

void SetBackgroundBlurImmediate(float weight, bool keepActive)
    {
        float clamped = Mathf.Clamp01(weight);

        if (backgroundBlurVolume != null)
        {
            backgroundBlurVolume.weight = clamped;

            if (!keepActive && Mathf.Approximately(backgroundBlurVolume.weight, 0f))
                backgroundBlurVolume.gameObject.SetActive(false);
            else if (keepActive && !backgroundBlurVolume.gameObject.activeSelf)
                backgroundBlurVolume.gameObject.SetActive(true);
        }

        if (backgroundBlurCaptureImage != null)
        {
            Color color = backgroundBlurCaptureImage.color;
            backgroundBlurCaptureImage.color = new Color(color.r, color.g, color.b, clamped);

            if (!keepActive && Mathf.Approximately(clamped, 0f))
                backgroundBlurCaptureImage.gameObject.SetActive(false);
            else if (keepActive && !backgroundBlurCaptureImage.gameObject.activeSelf)
                backgroundBlurCaptureImage.gameObject.SetActive(true);
        }
    }

IEnumerator FadeBackgroundBlur(float from, float to, float duration)
    {
        if (backgroundBlurVolume == null && backgroundBlurCaptureImage == null)
            yield break;

        float elapsed = 0f;
        SetBackgroundBlurImmediate(from, from > 0f);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetBackgroundBlurImmediate(Mathf.Lerp(from, to, elapsed / duration), true);
            yield return null;
        }

        SetBackgroundBlurImmediate(to, to > 0f);

        _blurCoroutine = null;
    }

void CaptureBackgroundForBlur()
    {
        if (backgroundBlurCaptureImage == null)
            return;

        ReleaseCapturedBlurTexture();

        _capturedBlurTexture = ScreenCapture.CaptureScreenshotAsTexture();
        backgroundBlurCaptureImage.texture = _capturedBlurTexture;
        backgroundBlurCaptureImage.uvRect = new Rect(0f, 0f, 1f, 1f);
        backgroundBlurCaptureImage.SetNativeSize();

        RectTransform rectTransform = backgroundBlurCaptureImage.rectTransform;
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        Color color = backgroundBlurCaptureImage.color;
        backgroundBlurCaptureImage.color = new Color(color.r, color.g, color.b, 0f);
        backgroundBlurCaptureImage.gameObject.SetActive(false);
    }

void ReleaseCapturedBlurTexture()
    {
        if (_capturedBlurTexture == null)
            return;

        if (backgroundBlurCaptureImage != null && backgroundBlurCaptureImage.texture == _capturedBlurTexture)
            backgroundBlurCaptureImage.texture = null;

        Destroy(_capturedBlurTexture);
        _capturedBlurTexture = null;
    }

void OnDestroy()
    {
        ReleaseCapturedBlurTexture();
    }
}
