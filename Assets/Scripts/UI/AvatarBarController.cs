using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// 角色背景条控制器 - 管理左右两根背景条的角度、位置、颜色和动画
/// 
/// 背景条位于角色头像后面，说话者侧偏竖（stand），非说话者侧偏躺（lie）
/// 切换时角度+位置同时做动画，像棍子在墙角从立着滑到躺下
/// 左右两根条颜色相同，都是当前说话者的主颜色
/// 
/// 层级：背景条 < 头像 < 对话框
/// </summary>
public class AvatarBarController : MonoBehaviour
{
    [Header("背景条引用")]
    [Tooltip("左侧角色背景条 Image")]
    public Image leftBar;

    [Tooltip("右侧角色背景条 Image")]
    public Image rightBar;

    [Header("角度设置")]
    [Tooltip("说话者侧背景条与水平方向的夹角（偏竖，如60°）")]
    public float standAngle = 60f;

    [Header("左侧条位置")]
    [Tooltip("左侧条立着（说话中）时的锚点位置")]
    public Vector2 leftStandPos = new Vector2(310f, -14f);

    [Tooltip("左侧条躺下（非说话）时的锚点位置")]
    public Vector2 leftLiePos = new Vector2(363f, -161f);

    [Header("右侧条位置")]
    [Tooltip("右侧条立着（说话中）时的锚点位置")]
    public Vector2 rightStandPos = new Vector2(-310f, -14f);

    [Tooltip("右侧条躺下（非说话）时的锚点位置")]
    public Vector2 rightLiePos = new Vector2(-363f, -161f);

    [Header("透明度")]
    [Tooltip("背景条的透明度（0~1，1为不透明）")]
    [Range(0f, 1f)]
    public float barAlpha = 0.7f;

    [Header("动画设置")]
    [Tooltip("角度/颜色切换动画时长（-1 表示与 DialogBox 变形时长同步）")]
    public float transitionDuration = -1f;

    // 背景条 Image 是竖直长方形（默认朝上），所以：
    // "站立"（偏竖，如60°与水平方向夹角）= 旋转 (90 - standAngle)° 偏离竖直
    // "躺下"（偏横，差90°）= 旋转 -standAngle°，使条倾斜到另一侧（朝上倾斜）
    private float StandRotation => 90f - standAngle;
    private float LieRotation => -standAngle;

    /// <summary>
    /// 应用透明度到颜色
    /// </summary>
    private Color ApplyAlpha(Color c)
    {
        return new Color(c.r, c.g, c.b, barAlpha);
    }

    // 当前记录的同步时长（由外部设置）
    private float _syncDuration = 0.1f;
    private Ease _syncEase = Ease.OutBack;

    /// <summary>
    /// 设置用于动画同步的时长（通常与 DialogBox MorphDuration 一致）
    /// </summary>
    public void SetSyncDuration(float duration)
    {
        _syncDuration = duration;
    }

    /// <summary>
    /// 设置用于动画同步的缓动类型（通常与 StoryAnimator 入场缓动一致）
    /// </summary>
    public void SetSyncEase(Ease ease)
    {
        _syncEase = ease;
    }

    /// <summary>
    /// 获取实际使用的过渡时长
    /// </summary>
    private float GetTransitionDuration()
    {
        return transitionDuration > 0f ? transitionDuration : _syncDuration;
    }

    /// <summary>
    /// 初始化状态：设置颜色、角度和位置，隐藏背景条（入场动画前调用）
    /// </summary>
    /// <param name="color">初始颜色（第一个说话者的主颜色）</param>
    /// <param name="firstSpeakerIsLeft">第一个说话者是否在左侧</param>
    public void Setup(Color color, bool firstSpeakerIsLeft)
    {
        Color barColor = ApplyAlpha(color);

        if (leftBar != null)
        {
            leftBar.color = barColor;
            bool leftIsStand = firstSpeakerIsLeft;
            float leftAngle = leftIsStand ? StandRotation : -LieRotation;
            Vector2 leftPos = leftIsStand ? leftStandPos : leftLiePos;
            leftBar.rectTransform.localEulerAngles = new Vector3(0f, 0f, leftAngle);
            leftBar.rectTransform.anchoredPosition = leftPos;
            leftBar.gameObject.SetActive(false);
        }

        if (rightBar != null)
        {
            rightBar.color = barColor;
            bool rightIsStand = !firstSpeakerIsLeft;
            float rightAngle = rightIsStand ? -StandRotation : LieRotation;
            Vector2 rightPos = rightIsStand ? rightStandPos : rightLiePos;
            rightBar.rectTransform.localEulerAngles = new Vector3(0f, 0f, rightAngle);
            rightBar.rectTransform.anchoredPosition = rightPos;
            rightBar.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 播放换边过渡动画：角度旋转 + 位置滑动 + 颜色变化
    /// </summary>
    /// <param name="newColor">新说话者的主颜色</param>
    /// <param name="newSpeakerIsLeft">新说话者是否在左侧</param>
    /// <returns>DOTween Sequence，可用于等待完成</returns>
    public Sequence PlaySwitchTransition(Color newColor, bool newSpeakerIsLeft)
    {
        float duration = GetTransitionDuration();
        Color targetColor = ApplyAlpha(newColor);
        Sequence seq = DOTween.Sequence();

        // 左侧条
        if (leftBar != null && leftBar.gameObject.activeSelf)
        {
            bool leftToStand = newSpeakerIsLeft;
            float targetLeftAngle = leftToStand ? StandRotation : -LieRotation;
            Vector2 targetLeftPos = leftToStand ? leftStandPos : leftLiePos;

            seq.Join(leftBar.rectTransform
                .DOLocalRotate(new Vector3(0f, 0f, targetLeftAngle), duration)
                .SetEase(_syncEase)
                .SetUpdate(true));
            seq.Join(leftBar.rectTransform
                .DOAnchorPos(targetLeftPos, duration)
                .SetEase(_syncEase)
                .SetUpdate(true));
            seq.Join(leftBar
                .DOColor(targetColor, duration)
                .SetEase(_syncEase)
                .SetUpdate(true));
        }

        // 右侧条
        if (rightBar != null && rightBar.gameObject.activeSelf)
        {
            bool rightToStand = !newSpeakerIsLeft;
            float targetRightAngle = rightToStand ? -StandRotation : LieRotation;
            Vector2 targetRightPos = rightToStand ? rightStandPos : rightLiePos;

            seq.Join(rightBar.rectTransform
                .DOLocalRotate(new Vector3(0f, 0f, targetRightAngle), duration)
                .SetEase(_syncEase)
                .SetUpdate(true));
            seq.Join(rightBar.rectTransform
                .DOAnchorPos(targetRightPos, duration)
                .SetEase(_syncEase)
                .SetUpdate(true));
            seq.Join(rightBar
                .DOColor(targetColor, duration)
                .SetEase(_syncEase)
                .SetUpdate(true));
        }

        seq.SetUpdate(true);
        return seq;
    }

    /// <summary>
    /// 仅更新颜色（同侧对话切换 style 时使用，无角度变化）
    /// </summary>
    public void SetColor(Color color)
    {
        Color barColor = ApplyAlpha(color);
        if (leftBar != null && leftBar.gameObject.activeSelf)
            leftBar.color = barColor;
        if (rightBar != null && rightBar.gameObject.activeSelf)
            rightBar.color = barColor;
    }

    /// <summary>
    /// 隐藏所有背景条
    /// </summary>
    public void HideAll()
    {
        if (leftBar != null) leftBar.gameObject.SetActive(false);
        if (rightBar != null) rightBar.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (leftBar != null) leftBar.rectTransform.DOKill();
        if (rightBar != null) rightBar.rectTransform.DOKill();
        if (leftBar != null) leftBar.DOKill();
        if (rightBar != null) rightBar.DOKill();
    }
}
