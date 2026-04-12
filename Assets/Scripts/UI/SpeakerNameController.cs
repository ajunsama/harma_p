using UnityEngine;
using TMPro;
using DG.Tweening;
using System.Collections;

/// <summary>
/// 说话者名称控制器 —— A/B 双缓冲，逐字瞬时出现 + 换人交叉过渡
/// 挂在 SpeakerName 节点上（带 RectMask2D），子节点包含两个对等的 TMP
///
/// 层级结构：
///   SpeakerName (RectMask2D + SpeakerNameController)
///     ├── NameText_A (TextMeshProUGUI + TMPLongShadow)
///     └── NameText_B (TextMeshProUGUI + TMPLongShadow)
///
/// 过渡时长统一由 DialogBox 通过 durationOverride 同步，不再有独立的 slideDuration/fadeDuration
/// </summary>
public class SpeakerNameController : MonoBehaviour
{
    [Header("引用")]
    public TextMeshProUGUI nameTextA;
    public TextMeshProUGUI nameTextB;

    [Header("位置")]
    [Tooltip("左侧锚点X")]
    public float leftPositionX = -200f;
    [Tooltip("右侧锚点X")]
    public float rightPositionX = 200f;

    [Header("逐字显示")]
    [Tooltip("每个字符出现的间隔（秒）")]
    public float charInterval = 0.05f;

    [Header("过渡动画")]
    [Tooltip("Incoming 滑入时的起始偏移量（像素）")]
    public float slideOffset = 60f;
    [Tooltip("位移缓动")]
    public Ease slideEase = Ease.OutQuad;

    [Header("阴影角度")]
    public float leftAngle = 45f;
    public float rightAngle = 135f;

    // 未同步时的后备时长
    private const float DEFAULT_DURATION = 0.5f;

    private bool _currentIsA = true;
    private string _currentName;
    private bool _isLeft = true;
    private Tweener _transitionTween;
    private Coroutine _charRevealCoroutine;

    private CanvasGroup _cgA, _cgB;
    private TMPLongShadow _shadowA, _shadowB;
    private Color _pendingShadowColor = Color.white;

    private TextMeshProUGUI CurrentText => _currentIsA ? nameTextA : nameTextB;
    private TextMeshProUGUI NextText    => _currentIsA ? nameTextB : nameTextA;
    private CanvasGroup CurrentCG       => _currentIsA ? _cgA : _cgB;
    private CanvasGroup NextCG          => _currentIsA ? _cgB : _cgA;
    private TMPLongShadow CurrentShadow => _currentIsA ? _shadowA : _shadowB;
    private TMPLongShadow NextShadow    => _currentIsA ? _shadowB : _shadowA;

    void Awake()
    {
        _cgA = EnsureCanvasGroup(nameTextA);
        _cgB = EnsureCanvasGroup(nameTextB);
        _shadowA = nameTextA != null ? nameTextA.GetComponent<TMPLongShadow>() : null;
        _shadowB = nameTextB != null ? nameTextB.GetComponent<TMPLongShadow>() : null;
        if (_cgA != null) _cgA.alpha = 0f;
        if (_cgB != null) _cgB.alpha = 0f;
    }

    static CanvasGroup EnsureCanvasGroup(TextMeshProUGUI tmp)
    {
        if (tmp == null) return null;
        var cg = tmp.GetComponent<CanvasGroup>();
        if (cg == null) cg = tmp.gameObject.AddComponent<CanvasGroup>();
        return cg;
    }

    // ====================
    // 公开接口
    // ====================

    public void SetName(string name, bool isLeft, bool animate = true, float durationOverride = -1f)
    {
        if (string.IsNullOrEmpty(name))
        {
            HideImmediate();
            _currentName = null;
            return;
        }

        bool sameName = name == _currentName;
        bool sameSide = isLeft == _isLeft;
        if (sameName && sameSide) return;

        if (!animate || string.IsNullOrEmpty(_currentName))
            ShowImmediate(name, isLeft);
        else if (sameName && !sameSide)
            PlaySlideOnly(isLeft, durationOverride);
        else
            PlayCrossTransition(name, isLeft, durationOverride);

        _currentName = name;
        _isLeft = isLeft;
    }

    public void SyncCharInterval(float interval) { charInterval = interval; }

    public void ApplyStyle(Color shadowColor, float fontSize)
    {
        _pendingShadowColor = shadowColor;
        if (nameTextA != null) { nameTextA.color = Color.white; nameTextA.fontSize = fontSize; }
        if (nameTextB != null) { nameTextB.color = Color.white; nameTextB.fontSize = fontSize; }
    }

    public void HideImmediate()
    {
        KillAll();
        if (nameTextA != null) nameTextA.text = "";
        if (nameTextB != null) nameTextB.text = "";
        if (_cgA != null) _cgA.alpha = 0f;
        if (_cgB != null) _cgB.alpha = 0f;
        gameObject.SetActive(false);
    }

    // ====================
    // 内部实现
    // ====================

    static void ApplyToSlot(TextMeshProUGUI tmp, TMPLongShadow shadow, Color shadowColor, float fontSize)
    {
        if (tmp != null) { tmp.color = Color.white; tmp.fontSize = fontSize; }
        if (shadow != null) { shadow.shadowColor = shadowColor; shadow.Refresh(); }
    }

    /// <summary>
    /// 无动画直接显示 + 逐字出现
    /// </summary>
    void ShowImmediate(string name, bool isLeft)
    {
        KillAll();
        gameObject.SetActive(true);

        var current = CurrentText;
        current.text = name;
        current.color = Color.white;
        current.maxVisibleCharacters = 0;
        current.ForceMeshUpdate();

        if (CurrentCG != null) CurrentCG.alpha = 1f;
        if (NextCG != null) NextCG.alpha = 0f;
        if (NextText != null) NextText.text = "";

        float targetX = isLeft ? leftPositionX : rightPositionX;
        current.rectTransform.anchoredPosition = new Vector2(targetX, current.rectTransform.anchoredPosition.y);

        if (CurrentShadow != null)
        {
            CurrentShadow.angle = isLeft ? leftAngle : rightAngle;
            CurrentShadow.shadowColor = _pendingShadowColor;
            CurrentShadow.Refresh();
        }

        _charRevealCoroutine = StartCoroutine(CharRevealCoroutine(current));
    }

    /// <summary>
    /// 同名换边：平移 + 阴影角度（DOVirtual.Float 驱动）
    /// </summary>
    void PlaySlideOnly(bool toLeft, float durationOverride = -1f)
    {
        KillAll();
        float dur = durationOverride > 0f ? durationOverride : DEFAULT_DURATION;
        var current = CurrentText;
        var currentShadow = CurrentShadow;

        float startX = current.rectTransform.anchoredPosition.x;
        float targetX = toLeft ? leftPositionX : rightPositionX;
        float startAngle = currentShadow != null ? currentShadow.angle : 0f;
        float targetAngle = toLeft ? leftAngle : rightAngle;
        float y = current.rectTransform.anchoredPosition.y;

        _transitionTween = DOVirtual.Float(0f, 1f, dur, rawT =>
        {
            float eased = DOVirtual.EasedValue(0f, 1f, rawT, slideEase);

            current.rectTransform.anchoredPosition = new Vector2(
                Mathf.LerpUnclamped(startX, targetX, eased), y);

            if (currentShadow != null)
            {
                currentShadow.angle = Mathf.LerpUnclamped(startAngle, targetAngle, eased);
                currentShadow.Refresh();
            }
        }).SetEase(Ease.Linear).SetUpdate(true).OnComplete(() =>
        {
            current.rectTransform.anchoredPosition = new Vector2(targetX, y);
            if (currentShadow != null) { currentShadow.angle = targetAngle; currentShadow.Refresh(); }
        });
    }

    /// <summary>
    /// 换名交叉过渡（DOVirtual.Float 统一驱动所有动画）：
    /// Outgoing — 滑动 + CanvasGroup 淡出 + 阴影角度动画
    /// Incoming — 滑入 + 逐字出现
    /// </summary>
    void PlayCrossTransition(string newName, bool isLeft, float durationOverride = -1f)
    {
        KillAll();
        gameObject.SetActive(true);

        float dur = durationOverride > 0f ? durationOverride : DEFAULT_DURATION;

        var outgoing = CurrentText;
        var outgoingCG = CurrentCG;
        var outgoingShadow = CurrentShadow;
        var incoming = NextText;
        var incomingCG = NextCG;
        var incomingShadow = NextShadow;

        // --- 准备 incoming ---
        incoming.text = newName;
        incoming.color = Color.white;
        incoming.maxVisibleCharacters = int.MaxValue;
        incoming.ForceMeshUpdate();
        outgoing.ForceMeshUpdate();

        float targetX = isLeft ? leftPositionX : rightPositionX;
        float inY = incoming.rectTransform.anchoredPosition.y;

        // --- outgoing 起始状态 ---
        if (outgoingCG != null) outgoingCG.alpha = 1f;
        float outStartX = outgoing.rectTransform.anchoredPosition.x;
        float outY = outgoing.rectTransform.anchoredPosition.y;

        // --- 重叠移动：通过文字视觉中心让 A/B 尽可能重叠 ---
        // anchoredPosition 与 localPosition 的差值（因锚点不同而不同，但对同一元素是常量）
        float outAnchorToLocal = outgoing.rectTransform.localPosition.x - outStartX;
        float inAnchorToLocal  = incoming.rectTransform.localPosition.x - incoming.rectTransform.anchoredPosition.x;

        float outTextCenterLocal = outgoing.textBounds.center.x;
        float inTextCenterLocal  = incoming.textBounds.center.x;

        // 在父节点本地坐标系中计算视觉中心
        float startVisualCenter = (outStartX + outAnchorToLocal) + outTextCenterLocal;
        float endVisualCenter   = (targetX   + inAnchorToLocal)  + inTextCenterLocal;

        float inStartX = (startVisualCenter - inTextCenterLocal) - inAnchorToLocal;
        float outEndX  = (endVisualCenter - outTextCenterLocal)  - outAnchorToLocal;
        incoming.rectTransform.anchoredPosition = new Vector2(inStartX, inY);
        if (incomingCG != null) incomingCG.alpha = 1f;

        // bounds 计算完毕，隐藏字符准备逐字出现
        incoming.maxVisibleCharacters = 0;

        // --- 阴影角度 ---
        float angleFrom = isLeft ? rightAngle : leftAngle;
        float angleTo   = isLeft ? leftAngle  : rightAngle;
        if (outgoingShadow != null) { outgoingShadow.angle = angleFrom; outgoingShadow.Refresh(); }
        if (incomingShadow != null) { incomingShadow.angle = angleFrom; incomingShadow.Refresh(); }

        // --- 阴影颜色 ---
        Color colorFrom = outgoingShadow != null ? outgoingShadow.shadowColor : _pendingShadowColor;
        Color colorTo = _pendingShadowColor;
        if (incomingShadow != null) { incomingShadow.shadowColor = colorFrom; incomingShadow.Refresh(); }

        // --- 逐字出现 ---
        _charRevealCoroutine = StartCoroutine(CharRevealCoroutine(incoming));

        // --- DOVirtual.Float 统一驱动（Linear 0→1，内部手动应用缓动） ---
        _transitionTween = DOVirtual.Float(0f, 1f, dur, rawT =>
        {
            float eased = DOVirtual.EasedValue(0f, 1f, rawT, slideEase);

            // 共享视觉中心驱动位移（两段文字重叠移动）
            float sharedCenter = Mathf.LerpUnclamped(startVisualCenter, endVisualCenter, eased);

            // Incoming: 跟随共享中心（转回 anchoredPosition）
            incoming.rectTransform.anchoredPosition = new Vector2(
                sharedCenter - inTextCenterLocal - inAnchorToLocal, inY);

            // Outgoing: 跟随共享中心（转回 anchoredPosition）
            outgoing.rectTransform.anchoredPosition = new Vector2(
                sharedCenter - outTextCenterLocal - outAnchorToLocal, outY);

            // Outgoing: 淡出（线性，用 rawT）
            if (outgoingCG != null)
                outgoingCG.alpha = 1f - rawT;

            // 阴影角度 + 颜色（两个同步动画）
            Color lerpedColor = Color.Lerp(colorFrom, colorTo, eased);
            if (outgoingShadow != null)
            {
                outgoingShadow.angle = Mathf.LerpUnclamped(angleFrom, angleTo, eased);
                outgoingShadow.shadowColor = lerpedColor;
                outgoingShadow.Refresh();
            }
            if (incomingShadow != null)
            {
                incomingShadow.angle = Mathf.LerpUnclamped(angleFrom, angleTo, eased);
                incomingShadow.shadowColor = lerpedColor;
                incomingShadow.Refresh();
            }
        }).SetEase(Ease.Linear).SetUpdate(true).OnComplete(() =>
        {
            // 最终值
            incoming.rectTransform.anchoredPosition = new Vector2(targetX, inY);
            outgoing.rectTransform.anchoredPosition = new Vector2(outEndX, outY);
            outgoing.text = "";
            if (outgoingCG != null) outgoingCG.alpha = 0f;
            if (outgoingShadow != null) { outgoingShadow.angle = angleTo; outgoingShadow.shadowColor = colorTo; outgoingShadow.Refresh(); }
            if (incomingShadow != null) { incomingShadow.angle = angleTo; incomingShadow.shadowColor = colorTo; incomingShadow.Refresh(); }
            _currentIsA = !_currentIsA;
        });
    }

    IEnumerator CharRevealCoroutine(TextMeshProUGUI tmp)
    {
        tmp.ForceMeshUpdate();
        int totalChars = tmp.textInfo.characterCount;
        tmp.maxVisibleCharacters = 0;

        for (int i = 0; i < totalChars; i++)
        {
            tmp.maxVisibleCharacters = i + 1;
            yield return new WaitForSecondsRealtime(charInterval);
        }
        _charRevealCoroutine = null;
    }

    void KillAll()
    {
        if (_transitionTween != null && _transitionTween.IsActive())
        {
            _transitionTween.Kill();
            _transitionTween = null;
        }
        if (_charRevealCoroutine != null)
        {
            StopCoroutine(_charRevealCoroutine);
            _charRevealCoroutine = null;
        }
    }

    void OnDestroy() { KillAll(); }
}
