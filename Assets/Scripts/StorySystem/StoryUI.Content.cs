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
    /// 设置说话者名称（带位置和动画）
    /// </summary>
    /// <param name="name">说话者名称</param>
    /// <param name="isLeft">是否在左侧，默认 true</param>
    /// <param name="animate">是否播放过渡动画</param>
    /// <param name="durationOverride">覆盖动画时长，用于与外部动画同步，-1 表示使用默认值</param>
    public void SetSpeakerName(string name, bool isLeft = true, bool animate = true, float durationOverride = -1f)
    {
        if (speakerNameController != null)
        {
            // 若有预设的同步时长，优先使用并消费
            float dur = durationOverride > 0f ? durationOverride : _pendingNameSyncDuration;
            _pendingNameSyncDuration = -1f;
            speakerNameController.SetName(name, isLeft, animate, dur);
        }
    }

/// <summary>
    /// 设置头像
    /// </summary>
    public void SetAvatar(string position, Sprite sprite, float scale = 1f)
    {
        HideAllAvatars();

        Image targetAvatar = GetAvatarByPosition(position);
        if (targetAvatar != null && sprite != null)
        {
            targetAvatar.gameObject.SetActive(true);
            targetAvatar.sprite = sprite;
            FitAvatarToContainer(targetAvatar);
        }
    }

/// <summary>
    /// 设置图片展示区
    /// </summary>
    public void SetImage(bool show, Sprite sprite = null)
    {
        if (imageDisplayRoot != null)
            imageDisplayRoot.SetActive(show);

        if (displayImage != null && sprite != null)
        {
            displayImage.sprite = sprite;
            displayImage.SetNativeSize();
        }
    }

/// <summary>
    /// 开始打字机效果播放文字
    /// </summary>
    public void StartTypewriter(string text, float charInterval)
    {
        StopTypewriter();
        _fullText = text;
        _typewriterCoroutine = StartCoroutine(TypewriterCoroutine(text, charInterval));
    }

/// <summary>
    /// 立即显示完整文字（跳过打字机效果）
    /// </summary>
    public void CompleteTypewriter()
    {
        StopTypewriter();
        if (contentText != null)
        {
            contentText.text = _fullText ?? "";
            contentText.maxVisibleCharacters = int.MaxValue;
        }
        _isTypewriting = false;
    }

/// <summary>
    /// 停止打字机效果
    /// </summary>
    public void StopTypewriter()
    {
        if (_typewriterCoroutine != null)
        {
            StopCoroutine(_typewriterCoroutine);
            _typewriterCoroutine = null;
        }
        _isTypewriting = false;
    }

IEnumerator TypewriterCoroutine(string text, float charInterval)
    {
        _isTypewriting = true;

        if (contentText != null)
            contentText.text = "";

        // 使用富文本标签感知的打字效果
        int visibleCount = 0;
        // 使用TMP的maxVisibleCharacters实现逐字显示
        if (contentText != null)
        {
            contentText.text = text;
            contentText.maxVisibleCharacters = 0;
            contentText.ForceMeshUpdate();
            int totalChars = contentText.textInfo.characterCount;

            while (visibleCount < totalChars)
            {
                visibleCount++;
                contentText.maxVisibleCharacters = visibleCount;
                yield return new WaitForSecondsRealtime(charInterval);
            }
        }

        _isTypewriting = false;
    }

void HideImage()
    {
        if (imageDisplayRoot != null) imageDisplayRoot.SetActive(false);
    }

void ClearContent()
    {
        if (contentText != null) contentText.text = "";
        if (speakerNameController != null) speakerNameController.HideImmediate();
    }
}
