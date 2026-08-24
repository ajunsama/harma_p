using System;
using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public partial class StoryUI
{
// ====================
    // 入场与过渡动画
    // ====================

    /// <summary>
    /// 设置两侧头像（用于入场动画前，同时显示左右两侧角色）
    /// </summary>
    public void SetupBothAvatars(Sprite leftSprite, float leftScale, Sprite rightSprite, float rightScale)
    {
        // 每段剧情都显式重建头像状态，避免缺少某一侧角色时复用上一段剧情的 Sprite。
        // 只设置精灵和尺寸，不激活 GameObject；入场动画会在设好起始位置后再激活。
        PrepareAvatarForEntrance(avatarLeft, leftSprite);
        PrepareAvatarForEntrance(avatarRight, rightSprite);
        PrepareAvatarForEntrance(avatarCenter, null);
    }

/// <summary>
    /// 更新指定位置的头像精灵（不影响其他位置的头像）
    /// 用于对话过程中切换表情等场景
    /// </summary>
    public void UpdateAvatar(string position, Sprite sprite, float scale = 1f)
    {
        Image targetAvatar = GetAvatarByPosition(position);
        if (targetAvatar != null && sprite != null)
        {
            targetAvatar.gameObject.SetActive(true);
            targetAvatar.sprite = sprite;
            FitAvatarToContainer(targetAvatar);
        }
    }

/// <summary>
    /// 高亮当前说话者头像，暗化另一侧，并切换对话框形状
    /// </summary>
    public void HighlightSpeaker(string side)
    {
        _currentSpeakerSide = side?.ToLower();
        GameLog.Verbose($"[StoryUI] HighlightSpeaker: side={side}, resolved={_currentSpeakerSide}");

        bool isLeft = _currentSpeakerSide == "left";
        bool isRight = _currentSpeakerSide == "right";
        bool isCenter = _currentSpeakerSide == "center";

        // 头像始终保持原色，通过遮罩控制暗化
        if (avatarLeft != null && avatarLeft.gameObject.activeSelf)
            avatarLeft.color = Color.white;
        if (avatarRight != null && avatarRight.gameObject.activeSelf)
            avatarRight.color = Color.white;
        if (avatarCenter != null && avatarCenter.gameObject.activeSelf)
            avatarCenter.color = Color.white;

        // 非说话者：显示暗化遮罩；说话者：隐藏遮罩
        SetDimOverlay(_avatarLeftOverlay, !isLeft && avatarLeft != null && avatarLeft.gameObject.activeSelf);
        SetDimOverlay(_avatarRightOverlay, !isRight && avatarRight != null && avatarRight.gameObject.activeSelf);
        SetDimOverlay(_avatarCenterOverlay, !isCenter && avatarCenter != null && avatarCenter.gameObject.activeSelf);

        // 左侧说话者标识图：左边说话时渐显，否则渐隐
        if (leftSpeakerIcon != null)
        {
            leftSpeakerIcon.DOKill();
            float targetAlpha = isLeft ? 1f : 0f;
            leftSpeakerIcon.DOFade(targetAlpha, fadeTime).SetUpdate(true);
        }

        // 切换对话框形状：左边说话者=原始形状，右边说话者=镜像形状
        UpdateDialogueBoxShape(isRight);
    }

/// <summary>
    /// 更新对话框不规则形状的镜像状态（带变形动画）
    /// </summary>
    void UpdateDialogueBoxShape(bool mirrored)
    {
        if (dialogueBoxImage == null)
        {
            Debug.LogWarning("[StoryUI] UpdateDialogueBoxShape: dialogueBoxImage is NULL!");
            return;
        }
        GameLog.Verbose($"[StoryUI] UpdateDialogueBoxShape: mirrored={mirrored}, imageType={dialogueBoxImage.GetType().Name}, isIrregular={dialogueBoxImage is IrregularDialogBox}");
        var irregularBox = dialogueBoxImage as IrregularDialogBox;
        if (irregularBox != null)
        {
            float target = mirrored ? 1f : 0f;
            // 如果已经在目标状态，不重复播放
            if (Mathf.Approximately(irregularBox.MirrorProgress, target))
            {
                GameLog.Verbose($"[StoryUI] UpdateDialogueBoxShape: already at target {target}, skip");
                return;
            }

            if (_morphCoroutine != null)
                StopCoroutine(_morphCoroutine);
            _morphCoroutine = StartCoroutine(MorphDialogueBox(irregularBox, target));
        }
        else
        {
            Debug.LogWarning($"[StoryUI] UpdateDialogueBoxShape: dialogueBoxImage is {dialogueBoxImage.GetType().Name}, NOT IrregularDialogBox! 请在DialogBox上替换Image为IrregularDialogBox组件");
        }
    }

/// <summary>
    /// 对话框变形动画协程
    /// </summary>
    IEnumerator MorphDialogueBox(IrregularDialogBox box, float target)
    {
        float from = box.MirrorProgress;
        float duration = box.MorphDuration;
        if (duration <= 0f)
        {
            box.MirrorProgress = target;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);
            // 使用SmoothStep缓动，开头慢结尾慢中间快
            float easedT = Mathf.SmoothStep(0f, 1f, normalizedTime);
            box.MirrorProgress = Mathf.Lerp(from, target, easedT);
            yield return null;
        }
        box.MirrorProgress = target;
        _morphCoroutine = null;
    }

void HideAllAvatars()
    {
        if (avatarLeft != null) avatarLeft.gameObject.SetActive(false);
        if (avatarCenter != null) avatarCenter.gameObject.SetActive(false);
        if (avatarRight != null) avatarRight.gameObject.SetActive(false);
    }

void PrepareAvatarForEntrance(Image avatar, Sprite sprite)
    {
        if (avatar == null) return;

        avatar.gameObject.SetActive(false);
        avatar.sprite = sprite;
        avatar.color = Color.white;

        if (sprite != null)
            FitAvatarToContainer(avatar);
    }

Image GetAvatarByPosition(string position)
    {
        switch (position?.ToLower())
        {
            case "left": return avatarLeft;
            case "center": return avatarCenter;
            case "right": return avatarRight;
            default: return avatarLeft;
        }
    }

/// <summary>
    /// 将头像等比缩放到容器宽度，顶部对齐。
    /// 基于 sprite 所在纹理的完整尺寸（即原始 PNG 尺寸）作为缩放基准，
    /// 确保来自相同尺寸画布的素材获得一致的缩放比例。
    /// </summary>
    void FitAvatarToContainer(Image avatar)
    {
        if (avatar == null || avatar.sprite == null) return;

        Vector2 containerSize = GetAvatarContainerSize(avatar);
        Vector2 originalPos = GetAvatarOriginalPos(avatar);
        if (containerSize.x <= 0f) return;

        Sprite sp = avatar.sprite;
        Texture2D tex = sp.texture;
        if (tex == null) return;

        // 使用纹理的完整尺寸（= 原始 PNG 尺寸）作为缩放基准
        float texW = tex.width;
        float texH = tex.height;
        Rect contentRect = sp.rect; // sprite 在纹理中的内容区域（裁剪后的像素矩形）

        if (texW <= 0f || contentRect.width <= 0f) return;

        // 统一缩放比例：将完整纹理宽度映射到容器宽度
        float scale = containerSize.x / texW;

        float scaledW = contentRect.width * scale;
        float scaledH = contentRect.height * scale;

        avatar.rectTransform.sizeDelta = new Vector2(scaledW, scaledH);

        float pivotX = avatar.rectTransform.pivot.x;
        float pivotY = avatar.rectTransform.pivot.y;

        // 水平定位：内容在纹理中的 X 偏移映射到容器中
        // 容器左边缘 = originalPos.x - containerSize.x * pivotX
        // 内容左边缘 = 容器左边缘 + contentRect.x * scale
        // newPosX = contentLeftEdge + scaledW * pivotX
        float newPosX = originalPos.x + pivotX * (scaledW - containerSize.x) + contentRect.x * scale;

        // 顶部对齐：容器顶部对应纹理顶部
        float containerTop = originalPos.y + containerSize.y * (1f - pivotY);

        // sprite 内容顶部距纹理顶部的距离（缩放后）
        float spriteTopInTex = contentRect.y + contentRect.height;
        float distFromTexTop = (texH - spriteTopInTex) * scale;

        // 定位：使 sprite 内容的顶边位于 (containerTop - distFromTexTop)
        float contentTop = containerTop - distFromTexTop;
        float newPosY = contentTop - scaledH * (1f - pivotY);

        avatar.rectTransform.anchoredPosition = new Vector2(newPosX, newPosY);
        avatar.rectTransform.localScale = Vector3.one;

        GameLog.Verbose($"[StoryUI] FitAvatar: name={avatar.name}, sprite={sp.name}, " +
            $"texSize=({texW}x{texH}), contentRect=({contentRect.x},{contentRect.y},{contentRect.width}x{contentRect.height}), " +
            $"container=({containerSize.x}x{containerSize.y}), scale={scale:F4}, " +
            $"result sizeDelta={avatar.rectTransform.sizeDelta}, pos={avatar.rectTransform.anchoredPosition}");
    }

Vector2 GetAvatarContainerSize(Image avatar)
    {
        if (avatar == avatarLeft) return _avatarLeftContainerSize;
        if (avatar == avatarCenter) return _avatarCenterContainerSize;
        if (avatar == avatarRight) return _avatarRightContainerSize;
        return Vector2.zero;
    }

Vector2 GetAvatarOriginalPos(Image avatar)
    {
        if (avatar == avatarLeft) return _avatarLeftOriginalPos;
        if (avatar == avatarCenter) return _avatarCenterOriginalPos;
        if (avatar == avatarRight) return _avatarRightOriginalPos;
        return Vector2.zero;
    }

/// <summary>
    /// 确保头像在对话框后面渲染（sibling index 更小 = 更早渲染 = 在后面）
    /// 同时确保背景条在头像后面渲染
    /// </summary>
    void EnsureAvatarsBehindDialogueBox()
    {
        if (dialogueBoxImage == null) return;
        Transform dlgParent = dialogueBoxImage.transform.parent;
        if (dlgParent == null) return;

        // 将对话框移到最后，确保在所有头像之上
        // 同时保留 effectController 在对话框之前（如果它也在同一父节点下）
        Image[] avatars = { avatarLeft, avatarCenter, avatarRight };
        foreach (var avatar in avatars)
        {
            if (avatar != null && avatar.transform.parent == dlgParent)
            {
                int avatarIdx = avatar.transform.GetSiblingIndex();
                int dlgIdx = dialogueBoxImage.transform.GetSiblingIndex();
                if (avatarIdx > dlgIdx)
                {
                    avatar.transform.SetSiblingIndex(dlgIdx);
                }
            }
        }

        // 确保背景条在头像后面（sibling index 更小）
        if (avatarBarController != null)
        {
            Image[] bars = { avatarBarController.leftBar, avatarBarController.rightBar };
            foreach (var bar in bars)
            {
                if (bar == null || bar.transform.parent != dlgParent) continue;
                // 找到所有头像中最小的 sibling index，背景条要排在它前面
                int minAvatarIdx = int.MaxValue;
                foreach (var avatar in avatars)
                {
                    if (avatar != null && avatar.transform.parent == dlgParent)
                        minAvatarIdx = Mathf.Min(minAvatarIdx, avatar.transform.GetSiblingIndex());
                }
                if (minAvatarIdx < int.MaxValue && bar.transform.GetSiblingIndex() > minAvatarIdx)
                {
                    bar.transform.SetSiblingIndex(minAvatarIdx);
                }
            }
        }
    }

/// <summary>
    /// 为头像创建暗化遮罩子物体（使用相同 sprite 的半透明黑色 Image，只暗化有像素的区域）
    /// </summary>
    Image CreateDimOverlay(Image avatar, string name)
    {
        if (avatar == null) return null;

        GameObject overlayGO = new GameObject(name, typeof(RectTransform), typeof(Image));
        overlayGO.transform.SetParent(avatar.transform, false);

        RectTransform rt = overlayGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image overlayImg = overlayGO.GetComponent<Image>();
        overlayImg.color = inactiveDimColor;
        overlayImg.raycastTarget = false;

        overlayGO.SetActive(false);
        return overlayImg;
    }

/// <summary>
    /// 设置暗化遮罩的显隐，并同步 sprite 使其与头像形状一致
    /// </summary>
    void SetDimOverlay(Image overlay, bool show)
    {
        if (overlay == null) return;
        overlay.gameObject.SetActive(show);
        if (show)
        {
            // 取父级头像的 sprite，遮罩只在有像素的地方生效
            Image parentAvatar = overlay.transform.parent?.GetComponent<Image>();
            if (parentAvatar != null && parentAvatar.sprite != null)
            {
                overlay.sprite = parentAvatar.sprite;
                overlay.type = parentAvatar.type;
            }
            overlay.color = inactiveDimColor;
        }
    }
}
