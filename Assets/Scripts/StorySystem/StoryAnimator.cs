using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// 剧情动画控制器 - 使用 DOTween 驱动所有过渡动画
/// 负责：角色入场、对话框入场
/// 
/// 依赖：DOTween（需通过 Asset Store 导入）
/// 所有动画使用 SetUpdate(true) 使其不受 Time.timeScale 影响
/// </summary>
public class StoryAnimator : MonoBehaviour
{
    // ==============================
    // 角色入场
    // ==============================
    [Header("角色入场动画")]
    [Tooltip("角色入场时长")]
    public float characterEntranceDuration = 0.5f;

    [Tooltip("角色入场缓动类型")]
    public Ease characterEntranceEase = Ease.OutBack;

    [Header("角色入场 - 左侧")]
    [Tooltip("左侧角色起始位置偏移（相对于最终位置）")]
    public Vector2 leftStartOffset = new Vector2(-300f, 0f);

    [Tooltip("左侧角色起始旋转角度（Z轴）")]
    public float leftStartRotation = -30f;

    [Tooltip("左侧角色起始缩放（相对于最终缩放的比例，0.5表示从一半大小开始）")]
    public float leftStartScaleRatio = 0.5f;

    [Header("角色入场 - 右侧")]
    [Tooltip("右侧角色起始位置偏移（相对于最终位置）")]
    public Vector2 rightStartOffset = new Vector2(300f, 0f);

    [Tooltip("右侧角色起始旋转角度（Z轴）")]
    public float rightStartRotation = 30f;

    [Tooltip("右侧角色起始缩放（相对于最终缩放的比例）")]
    public float rightStartScaleRatio = 0.5f;

    // ==============================
    // 对话框入场
    // ==============================
    [Header("对话框入场动画")]
    [Tooltip("对话框入场时长")]
    public float dialogueBoxEntranceDuration = 0.4f;

    [Tooltip("对话框入场缓动类型")]
    public Ease dialogueBoxEntranceEase = Ease.OutBack;

    [Tooltip("对话框左侧入场起始偏移")]
    public Vector2 dialogueBoxLeftOffset = new Vector2(-400f, 0f);

    [Tooltip("对话框左侧入场起始旋转")]
    public float dialogueBoxLeftRotation = -15f;

    [Tooltip("对话框右侧入场起始偏移")]
    public Vector2 dialogueBoxRightOffset = new Vector2(400f, 0f);

    [Tooltip("对话框右侧入场起始旋转")]
    public float dialogueBoxRightRotation = 15f;

    [Tooltip("对话框入场起始缩放")]
    public float dialogueBoxStartScale = 0.6f;

    void OnDestroy()
    {
        DOTween.Kill(this);
    }

    // ==============================
    // 角色入场动画
    // ==============================

    /// <summary>
    /// 播放完整入场动画：角色 + 对话框同时入场
    /// 所有 RT 由调用方传入
    /// </summary>
    public Coroutine PlayFullEntrance(RectTransform leftRT, RectTransform rightRT,
        RectTransform dialogueBoxRT, bool firstSpeakerIsLeft)
    {
        return StartCoroutine(FullEntranceCoroutine(leftRT, rightRT, dialogueBoxRT, firstSpeakerIsLeft));
    }

    IEnumerator FullEntranceCoroutine(RectTransform leftRT, RectTransform rightRT,
        RectTransform dialogueBoxRT, bool firstSpeakerIsLeft)
    {
        // 收集需要入场的角色
        Image leftImg = leftRT != null ? leftRT.GetComponent<Image>() : null;
        Image rightImg = rightRT != null ? rightRT.GetComponent<Image>() : null;
        bool hasLeft = leftImg != null && leftImg.sprite != null;
        bool hasRight = rightImg != null && rightImg.sprite != null;

        // 获取对话框 CanvasGroup（用于隐藏/显示，不影响 Image.color）
        CanvasGroup dlgCG = null;
        if (dialogueBoxRT != null)
        {
            dlgCG = dialogueBoxRT.GetComponent<CanvasGroup>();
            if (dlgCG == null)
                dlgCG = dialogueBoxRT.gameObject.AddComponent<CanvasGroup>();
            dlgCG.alpha = 0f;
        }

        // 1. 激活角色，但先设为透明（避免在布局位置闪现一帧）
        if (hasLeft)
        {
            leftRT.gameObject.SetActive(true);
            leftImg.color = new Color(leftImg.color.r, leftImg.color.g, leftImg.color.b, 0f);
        }
        if (hasRight)
        {
            rightRT.gameObject.SetActive(true);
            rightImg.color = new Color(rightImg.color.r, rightImg.color.g, rightImg.color.b, 0f);
        }

        // 2. 等一帧，让 Canvas 布局系统完成重建
        yield return null;

        // 3. 同时启动角色入场和对话框入场动画
        if (hasLeft)
        {
            SetupAndStartEntrance(leftRT, leftImg,
                leftStartOffset, leftStartRotation, leftStartScaleRatio);
        }
        if (hasRight)
        {
            SetupAndStartEntrance(rightRT, rightImg,
                rightStartOffset, rightStartRotation, rightStartScaleRatio);
        }

        if (dialogueBoxRT != null)
        {
            if (dlgCG != null) dlgCG.alpha = 1f;
            StartDialogueBoxEntrance(dialogueBoxRT, firstSpeakerIsLeft);
        }

        // 4. 等待最长的动画完成
        float maxDuration = Mathf.Max(characterEntranceDuration, dialogueBoxEntranceDuration);
        yield return new WaitForSecondsRealtime(maxDuration);
    }

    void SetupAndStartEntrance(RectTransform rt, Image img,
        Vector2 startOffset, float startRot, float startScaleRatio)
    {
        Vector2 finalPos = rt.anchoredPosition;
        Vector3 finalScale = rt.localScale;
        Color finalColor = new Color(img.color.r, img.color.g, img.color.b, 1f);

        rt.anchoredPosition = finalPos + startOffset;
        rt.localRotation = Quaternion.Euler(0, 0, startRot);
        rt.localScale = finalScale * startScaleRatio;
        img.color = finalColor;

        rt.DOKill();

        rt.DOAnchorPos(finalPos, characterEntranceDuration)
            .SetEase(characterEntranceEase)
            .SetUpdate(true);

        rt.DOLocalRotate(Vector3.zero, characterEntranceDuration)
            .SetEase(characterEntranceEase)
            .SetUpdate(true);

        rt.DOScale(finalScale, characterEntranceDuration)
            .SetEase(characterEntranceEase)
            .SetUpdate(true);
    }

    // ==============================
    // 对话框入场动画
    // ==============================

    void StartDialogueBoxEntrance(RectTransform rt, bool fromLeft)
    {
        Vector2 finalPos = rt.anchoredPosition;
        Vector3 finalScale = rt.localScale;

        Vector2 startOffset = fromLeft ? dialogueBoxLeftOffset : dialogueBoxRightOffset;
        float startRot = fromLeft ? dialogueBoxLeftRotation : dialogueBoxRightRotation;

        rt.anchoredPosition = finalPos + startOffset;
        rt.localRotation = Quaternion.Euler(0, 0, startRot);
        rt.localScale = finalScale * dialogueBoxStartScale;

        rt.DOKill();

        rt.DOAnchorPos(finalPos, dialogueBoxEntranceDuration)
            .SetEase(dialogueBoxEntranceEase)
            .SetUpdate(true);

        rt.DOLocalRotate(Vector3.zero, dialogueBoxEntranceDuration)
            .SetEase(dialogueBoxEntranceEase)
            .SetUpdate(true);

        rt.DOScale(finalScale, dialogueBoxEntranceDuration)
            .SetEase(dialogueBoxEntranceEase)
            .SetUpdate(true);
    }
}
