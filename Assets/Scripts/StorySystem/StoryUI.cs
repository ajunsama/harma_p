using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// 剧情UI管理器 - 控制对话框、头像、图片的显示
/// 对应附件效果图：上方图片区域 + 下方对话区域（左头像 + 文字框 + 右头像）
/// </summary>
public class StoryUI : MonoBehaviour
{
    [Header("根画布")]
    [Tooltip("整个剧情UI的根节点，播放时显示，结束时隐藏")]
    public GameObject storyRoot;

    [Header("对话框")]
    [Tooltip("对话框背景Image")]
    public Image dialogueBoxImage;

    [Tooltip("正文TextMeshPro")]
    public TextMeshProUGUI contentText;

    [Tooltip("说话者名称控制器（新版A/B双缓冲逐字动画）")]
    public SpeakerNameController speakerNameController;

    [Header("头像")]
    [Tooltip("左侧头像Image")]
    public Image avatarLeft;

    [Tooltip("中间头像Image")]
    public Image avatarCenter;

    [Tooltip("右侧头像Image")]
    public Image avatarRight;

    [Header("图片展示区")]
    [Tooltip("图片展示区域的根节点")]
    public GameObject imageDisplayRoot;

    [Tooltip("图片展示Image")]
    public Image displayImage;

    [Header("提示")]
    [Tooltip("'点击继续'提示标记")]
    public GameObject continueIndicator;

    [Header("动画设置")]
    [Tooltip("UI出现/消失的过渡时间")]
    public float fadeTime = 0.3f;

    [Header("过渡动画")]
    [Tooltip("剧情动画控制器（可选，不设置则使用默认淡入淡出）")]
    public StoryAnimator animator;

    [Tooltip("对话框特效控制器（可选，色块入场/换边特效）")]
    public DialogBoxEffectController effectController;

    [Tooltip("非活跃说话者的暗化颜色")]
    public Color inactiveSpeakerColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);

    private CanvasGroup _canvasGroup;
    private Coroutine _typewriterCoroutine;
    private Coroutine _fadeCoroutine;
    private Coroutine _morphCoroutine;
    private bool _isTypewriting;
    private string _fullText;
    private string _currentSpeakerSide;
    private float _pendingNameSyncDuration = -1f;

    /// <summary>
    /// 文字是否正在打字中
    /// </summary>
    public bool IsTypewriting => _isTypewriting;

    /// <summary>
    /// 当前说话者方向
    /// </summary>
    public string CurrentSpeakerSide => _currentSpeakerSide;

    /// <summary>
    /// 当前对话框颜色
    /// </summary>
    public Color CurrentDialogueBoxColor => dialogueBoxImage != null ? dialogueBoxImage.color : Color.white;

    void Awake()
    {
        _canvasGroup = storyRoot?.GetComponent<CanvasGroup>();
        if (_canvasGroup == null && storyRoot != null)
        {
            _canvasGroup = storyRoot.AddComponent<CanvasGroup>();
        }
    }

    /// <summary>
    /// 显示剧情UI
    /// </summary>
    public void Show()
    {
        // 取消正在进行的淡入淡出，避免和Hide的协程竞态
        StopFade();

        if (storyRoot != null)
            storyRoot.SetActive(true);

        if (_canvasGroup != null)
            _fadeCoroutine = StartCoroutine(FadeCanvasGroup(0f, 1f, fadeTime));

        HideAllAvatars();
        HideImage();
        HideContinueIndicator();
        ClearContent();
    }

    /// <summary>
    /// 隐藏剧情UI
    /// </summary>
    public void Hide()
    {
        StopFade();

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

    /// <summary>
    /// 应用样式模板到UI
    /// </summary>
    public void ApplyStyle(StoryStyleTemplate style)
    {
        if (style == null) { Debug.Log("[StoryUI] ApplyStyle: style is null, skipping"); return; }
        Debug.Log($"[StoryUI] ApplyStyle: styleId={style.styleId}, boxColor={style.dialogueBoxColor}, hasSprite={style.dialogueBoxSprite != null}");

        if (contentText != null)
        {
            contentText.fontSize = style.fontSize;
            contentText.color = style.textColor;
            if (style.font != null) contentText.font = style.font;

            if (style.enableOutline)
            {
                contentText.outlineColor = style.outlineColor;
                contentText.outlineWidth = style.outlineWidth;
            }
            else
            {
                contentText.outlineWidth = 0;
            }
        }

        if (dialogueBoxImage != null)
        {
            Debug.Log($"[StoryUI] ApplyStyle: dialogueBoxImage type={dialogueBoxImage.GetType().Name}, instanceID={dialogueBoxImage.GetInstanceID()}");
            if (style.dialogueBoxSprite != null)
            {
                dialogueBoxImage.sprite = style.dialogueBoxSprite;
                dialogueBoxImage.type = Image.Type.Sliced;
            }
            dialogueBoxImage.color = style.dialogueBoxColor;
            Debug.Log($"[StoryUI] ApplyStyle: color set to {dialogueBoxImage.color}");
        }
        else
        {
            Debug.LogWarning("[StoryUI] ApplyStyle: dialogueBoxImage is NULL!");
        }

        if (speakerNameController != null)
        {
            speakerNameController.ApplyStyle(style.dialogueBoxColor, style.speakerNameFontSize);
        }

        // 同步色块背景颜色（仅在没有动画播放时直接更新）
        if (effectController != null && !effectController.IsPlaying)
            effectController.SetupBackground(style.dialogueBoxColor);
    }

    /// <summary>
    /// 仅应用说话者名称样式（不触发对话框/色块更新）
    /// 用于入场动画前设置名称颜色，避免 SetupBackground 提前显示色块
    /// </summary>
    public void ApplySpeakerNameStyle(Color shadowColor, float fontSize)
    {
        if (speakerNameController != null)
            speakerNameController.ApplyStyle(shadowColor, fontSize);
    }

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
            targetAvatar.SetNativeSize();
            targetAvatar.rectTransform.localScale = Vector3.one * scale;
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
        HideContinueIndicator();

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
        ShowContinueIndicator();
    }

    public void ShowContinueIndicator()
    {
        if (continueIndicator != null)
            continueIndicator.SetActive(true);
    }

    public void HideContinueIndicator()
    {
        if (continueIndicator != null)
            continueIndicator.SetActive(false);
    }

    // ====================
    // 入场与过渡动画
    // ====================

    /// <summary>
    /// 设置两侧头像（用于入场动画前，同时显示左右两侧角色）
    /// </summary>
    public void SetupBothAvatars(Sprite leftSprite, float leftScale, Sprite rightSprite, float rightScale)
    {
        // 只设置精灵和尺寸，不激活 GameObject
        // 入场动画会在设好起始位置后再激活，避免在最终位置闪现一帧
        if (avatarLeft != null)
        {
            if (leftSprite != null)
            {
                avatarLeft.sprite = leftSprite;
                avatarLeft.SetNativeSize();
                avatarLeft.rectTransform.localScale = Vector3.one * leftScale;
            }
        }

        if (avatarRight != null)
        {
            if (rightSprite != null)
            {
                avatarRight.sprite = rightSprite;
                avatarRight.SetNativeSize();
                avatarRight.rectTransform.localScale = Vector3.one * rightScale;
            }
        }
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
            targetAvatar.SetNativeSize();
            targetAvatar.rectTransform.localScale = Vector3.one * scale;
        }
    }

    /// <summary>
    /// 高亮当前说话者头像，暗化另一侧，并切换对话框形状
    /// </summary>
    public void HighlightSpeaker(string side)
    {
        _currentSpeakerSide = side?.ToLower();
        Debug.Log($"[StoryUI] HighlightSpeaker: side={side}, resolved={_currentSpeakerSide}");

        bool isLeft = _currentSpeakerSide == "left";
        bool isRight = _currentSpeakerSide == "right";
        bool isCenter = _currentSpeakerSide == "center";

        if (avatarLeft != null && avatarLeft.gameObject.activeSelf)
            avatarLeft.color = isLeft ? Color.white : inactiveSpeakerColor;
        if (avatarRight != null && avatarRight.gameObject.activeSelf)
            avatarRight.color = isRight ? Color.white : inactiveSpeakerColor;
        if (avatarCenter != null && avatarCenter.gameObject.activeSelf)
            avatarCenter.color = isCenter ? Color.white : inactiveSpeakerColor;

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
        Debug.Log($"[StoryUI] UpdateDialogueBoxShape: mirrored={mirrored}, imageType={dialogueBoxImage.GetType().Name}, isIrregular={dialogueBoxImage is IrregularDialogBox}");
        var irregularBox = dialogueBoxImage as IrregularDialogBox;
        if (irregularBox != null)
        {
            float target = mirrored ? 1f : 0f;
            // 如果已经在目标状态，不重复播放
            if (Mathf.Approximately(irregularBox.MirrorProgress, target))
            {
                Debug.Log($"[StoryUI] UpdateDialogueBoxShape: already at target {target}, skip");
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

    /// <summary>
    /// 播放入场动画序列：角色从两侧进入 + 对话框从说话者方向进入
    /// 动画的具体表现（位移/旋转/缩放/缓动）全部在 StoryAnimator Inspector 中配置
    /// </summary>
    /// <param name="firstSpeakerIsLeft">第一个说话者在左侧</param>
    /// <param name="speakerName">第一个说话者名称（用于色块入场动画）</param>
    /// <param name="entranceColor">入场色块颜色</param>
    public IEnumerator PlayEntranceAnimation(bool firstSpeakerIsLeft, string speakerName, Color entranceColor)
    {
        if (animator == null) yield break;

        // 入场动画自身就是"显示"过程，直接将 alpha 设为1并停止淡入协程
        // 否则角色动画会被 CanvasGroup alpha=0 的渐变遮盖
        StopFade();
        if (_canvasGroup != null)
            _canvasGroup.alpha = 1f;

        // 角色 + 对话框同时入场
        RectTransform leftRT = avatarLeft != null ? avatarLeft.rectTransform : null;
        RectTransform rightRT = avatarRight != null ? avatarRight.rectTransform : null;
        RectTransform dlgRT = dialogueBoxImage != null ? dialogueBoxImage.rectTransform : null;
        yield return animator.PlayFullEntrance(leftRT, rightRT, dlgRT, firstSpeakerIsLeft);

        // 色块入场特效，同步说话者名称逐字间隔
        if (effectController != null)
        {
            // 将名称逐字出现的间隔同步为色块淡入间隔
            if (speakerNameController != null)
                speakerNameController.SyncCharInterval(effectController.fadeInInterval);

            Sequence entranceSeq = effectController.PlayEntrance(speakerName, entranceColor, firstSpeakerIsLeft);

            // 在色块入场的同时启动名称逐字出现（并行而非串行）
            SetSpeakerName(speakerName, firstSpeakerIsLeft, false);

            if (entranceSeq != null)
                yield return entranceSeq.WaitForCompletion();
        }

        // 高亮第一个说话者
        HighlightSpeaker(firstSpeakerIsLeft ? "left" : "right");
    }

    /// <summary>
    /// 播放换边过渡：色块散开 + DialogBox变形 + 条纹角度旋转 同步执行
    /// </summary>
    public IEnumerator PlaySideTransition(string newSide, Color fromColor, Color toColor, Action onMidpoint)
    {
        bool toMirrored = newSide?.ToLower() == "right";

        // 计算同步时长：使用 DialogBox 变形时长作为统一时长
        float syncDuration = -1f;
        if (effectController != null && effectController.dialogBox != null)
            syncDuration = effectController.dialogBox.MorphDuration;

        // 预设同步时长，这样 onMidpoint 内的 SetSpeakerName 会自动拾取并使用
        _pendingNameSyncDuration = syncDuration;

        // 1. 立即切换内容（在动画开始时就切换文本/头像数据）
        //    SetupDialogueDisplay 会调用 SetSpeakerName，后者会消费 _pendingNameSyncDuration
        onMidpoint?.Invoke();

        // 2. 同步启动：色块换边特效 + DialogBox形状变形 + 头像高亮
        Sequence switchSeq = null;
        if (effectController != null)
        {
            effectController.SetBaseColor(fromColor);
            switchSeq = effectController.PlaySwitchTransition(toColor, toMirrored);
        }

        // 启动形状变形（与色块动画同步）
        HighlightSpeaker(newSide);

        // 等待色块动画完成（变形协程独立运行，会自然结束）
        if (switchSeq != null)
            yield return switchSeq.WaitForCompletion();
    }

    void HideAllAvatars()
    {
        if (avatarLeft != null) avatarLeft.gameObject.SetActive(false);
        if (avatarCenter != null) avatarCenter.gameObject.SetActive(false);
        if (avatarRight != null) avatarRight.gameObject.SetActive(false);
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
}
