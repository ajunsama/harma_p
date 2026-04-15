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

    [Header("角色入场 - 右侧")]
    [Tooltip("右侧角色起始位置偏移（相对于最终位置）")]
    public Vector2 rightStartOffset = new Vector2(300f, 0f);



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
        Color leftTargetColor, Color rightTargetColor)
    {
        return StartCoroutine(FullEntranceCoroutine(leftRT, rightRT, leftTargetColor, rightTargetColor));
    }

    IEnumerator FullEntranceCoroutine(RectTransform leftRT, RectTransform rightRT,
        Color leftTargetColor, Color rightTargetColor)
    {
        // 收集需要入场的角色
        Image leftImg = leftRT != null ? leftRT.GetComponent<Image>() : null;
        Image rightImg = rightRT != null ? rightRT.GetComponent<Image>() : null;
        bool hasLeft = leftImg != null && leftImg.sprite != null;
        bool hasRight = rightImg != null && rightImg.sprite != null;

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
                leftStartOffset, leftTargetColor);
        }
        if (hasRight)
        {
            SetupAndStartEntrance(rightRT, rightImg,
                rightStartOffset, rightTargetColor);
        }

        // 4. 等待角色入场动画完成
        yield return new WaitForSecondsRealtime(characterEntranceDuration);
    }

    void SetupAndStartEntrance(RectTransform rt, Image img,
        Vector2 startOffset, Color targetColor)
    {
        Vector2 finalPos = rt.anchoredPosition;

        rt.anchoredPosition = finalPos + startOffset;
        img.color = targetColor;

        rt.DOKill();

        rt.DOAnchorPos(finalPos, characterEntranceDuration)
            .SetEase(characterEntranceEase)
            .SetUpdate(true);
    }

}
