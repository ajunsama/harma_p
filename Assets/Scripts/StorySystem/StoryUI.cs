using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using UnityEngine.Rendering;

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

    [Header("说话者标识图")]
    [Tooltip("左侧说话者标识图（放在 AvatarBarLeft 内利用 Mask 裁切，配合 FollowWorldAnchor 保持位置固定）")]
    public Image leftSpeakerIcon;

    [Header("图片展示区")]
    [Tooltip("图片展示区域的根节点")]
    public GameObject imageDisplayRoot;

    [Tooltip("图片展示Image")]
    public Image displayImage;

    [Header("动画设置")]
    [Tooltip("UI出现/消失的过渡时间")]
    public float fadeTime = 0.3f;

    [Header("背景滤镜")]
    [Tooltip("剧情播放时启用的模糊Volume，可选。建议绑定专门的Global Volume，并在Profile里配置模糊后处理")]
    public Volume backgroundBlurVolume;

    [Tooltip("用于承载背景截图的 RawImage。给它挂 UIEffect，并在 Inspector 里把 Sampling Filter 设成 Blur Fast/Medium")]
    public RawImage backgroundBlurCaptureImage;

    [Tooltip("剧情UI显示时的模糊权重")]
    [Range(0f, 1f)]
    public float backgroundBlurWeight = 1f;

    [Tooltip("入场动画前的延迟时间（秒），等待背景模糊等效果先生效")]
    public float entranceDelay = 0.2f;

    [Header("过渡动画")]
    [Tooltip("剧情动画控制器（可选，不设置则使用默认淡入淡出）")]
    public StoryAnimator animator;

    [Tooltip("对话框特效控制器（可选，色块入场/换边特效）")]
    public DialogBoxEffectController effectController;

    [Tooltip("非活跃说话者的暗化遮罩颜色（半透明黑色）")]
    public Color inactiveDimColor = new Color(0f, 0f, 0f, 0.6f);

    [Tooltip("角色背景条控制器（可选，管理头像后方的彩色背景条）")]
    public AvatarBarController avatarBarController;

    private CanvasGroup _canvasGroup;
    private Coroutine _typewriterCoroutine;
    private Coroutine _fadeCoroutine;
    private Coroutine _morphCoroutine;
    private Coroutine _blurCoroutine;
    private Texture2D _capturedBlurTexture;
    private bool _isTypewriting;
    private string _fullText;
    private string _currentSpeakerSide;
    private float _pendingNameSyncDuration = -1f;

    // 头像展示区域的原始尺寸和位置（编辑器中设定的 RectTransform）
    private Vector2 _avatarLeftContainerSize;
    private Vector2 _avatarCenterContainerSize;
    private Vector2 _avatarRightContainerSize;
    private Vector2 _avatarLeftOriginalPos;
    private Vector2 _avatarCenterOriginalPos;
    private Vector2 _avatarRightOriginalPos;

    // 头像暗化遮罩（运行时自动创建的半透明黑色 Image，覆盖在头像上方）
    private Image _avatarLeftOverlay;
    private Image _avatarCenterOverlay;
    private Image _avatarRightOverlay;

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

        // 缓存头像展示区域的初始尺寸和位置，后续用于等比缩放和顶部对齐
        if (avatarLeft != null)
        {
            _avatarLeftContainerSize = avatarLeft.rectTransform.sizeDelta;
            _avatarLeftOriginalPos = avatarLeft.rectTransform.anchoredPosition;
        }
        if (avatarCenter != null)
        {
            _avatarCenterContainerSize = avatarCenter.rectTransform.sizeDelta;
            _avatarCenterOriginalPos = avatarCenter.rectTransform.anchoredPosition;
        }
        if (avatarRight != null)
        {
            _avatarRightContainerSize = avatarRight.rectTransform.sizeDelta;
            _avatarRightOriginalPos = avatarRight.rectTransform.anchoredPosition;
        }

        // 确保头像在对话框后面渲染（sibling index 越小越先渲染，即在后面）
        EnsureAvatarsBehindDialogueBox();

        // 为每个头像创建暗化遮罩子物体
        _avatarLeftOverlay = CreateDimOverlay(avatarLeft, "LeftDimOverlay");
        _avatarCenterOverlay = CreateDimOverlay(avatarCenter, "CenterDimOverlay");
        _avatarRightOverlay = CreateDimOverlay(avatarRight, "RightDimOverlay");

        if (backgroundBlurCaptureImage != null)
        {
            backgroundBlurCaptureImage.texture = null;
            backgroundBlurCaptureImage.gameObject.SetActive(false);
        }

        SetBackgroundBlurImmediate(0f, false);
    }

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

        // 同步背景条颜色
        if (avatarBarController != null)
            avatarBarController.SetColor(style.dialogueBoxColor);
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
        Debug.Log($"[StoryUI] HighlightSpeaker: side={side}, resolved={_currentSpeakerSide}");

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

        // 入场前设置说话者状态：暗化遮罩 + 对话框形状
        _currentSpeakerSide = firstSpeakerIsLeft ? "left" : "right";

        // 入场时头像颜色始终为白色，通过遮罩暗化非活跃方
        Color leftColor = Color.white;
        Color rightColor = Color.white;

        // 入场前先设置暗化遮罩状态
        SetDimOverlay(_avatarLeftOverlay, !firstSpeakerIsLeft);
        SetDimOverlay(_avatarRightOverlay, firstSpeakerIsLeft);

        // 左侧说话者标识图：入场时根据说话者立即设置透明度（不做动画）
        if (leftSpeakerIcon != null)
        {
            var c = leftSpeakerIcon.color;
            leftSpeakerIcon.color = new Color(c.r, c.g, c.b, firstSpeakerIsLeft ? 1f : 0f);
        }

        // 对话框形状立即设置（不播放变形动画）
        var irregularBox = dialogueBoxImage as IrregularDialogBox;
        if (irregularBox != null)
            irregularBox.MirrorProgress = firstSpeakerIsLeft ? 0f : 1f;

        // 背景条：设置初始颜色和角度
        if (avatarBarController != null)
        {
            if (animator != null)
                avatarBarController.SetSyncEase(animator.characterEntranceEase);
            avatarBarController.Setup(entranceColor, firstSpeakerIsLeft);
            // 强制背景条排在最底层（sibling index 最小 = 最先渲染 = 最后面）
            if (avatarBarController.leftBar != null)
                avatarBarController.leftBar.transform.SetAsFirstSibling();
            if (avatarBarController.rightBar != null)
                avatarBarController.rightBar.transform.SetSiblingIndex(1);
        }

        // 等待背景模糊等效果先生效
        if (entranceDelay > 0f)
            yield return new WaitForSecondsRealtime(entranceDelay);

        // 角色入场（不等待完成，与色块动画并行）
        RectTransform leftRT = avatarLeft != null ? avatarLeft.rectTransform : null;
        RectTransform rightRT = avatarRight != null ? avatarRight.rectTransform : null;
        bool hasLeftAvatar = avatarLeft != null && avatarLeft.sprite != null;
        bool hasRightAvatar = avatarRight != null && avatarRight.sprite != null;
        RectTransform leftBarRT = hasLeftAvatar && avatarBarController != null && avatarBarController.leftBar != null
            ? avatarBarController.leftBar.rectTransform : null;
        RectTransform rightBarRT = hasRightAvatar && avatarBarController != null && avatarBarController.rightBar != null
            ? avatarBarController.rightBar.rectTransform : null;
        Coroutine entranceCoroutine = animator.PlayFullEntrance(
            leftRT, rightRT, leftColor, rightColor, leftBarRT, rightBarRT);

        // 色块入场特效与头像入场同时启动
        Sequence entranceSeq = null;
        if (effectController != null)
        {
            float charInterval = speakerNameController != null ? speakerNameController.CharInterval : -1f;
            Debug.Log($"[Entrance] charInterval={charInterval:F3}, speakerName='{speakerName}'");

            // 先以无动画方式设置名称（设置文本/位置/alpha，maxVisibleCharacters=0）
            SetSpeakerName(speakerName, firstSpeakerIsLeft, false);
            // ShowImmediate 内部启动了协程逐字显示，将其终止，改由 Sequence 驱动
            speakerNameController?.CancelCharReveal();

            // 构建 Phase1 callbacks：每个色块出现时，让对应字符可见
            int nameLen = string.IsNullOrEmpty(speakerName) ? 0 : speakerName.Length;
            System.Action[] phase1Callbacks = new System.Action[nameLen];
            for (int ci = 0; ci < nameLen; ci++)
            {
                int captured = ci;
                float expectedTime = charInterval > 0f ? charInterval * captured : effectController.fadeInInterval * captured;
                Debug.Log($"[Entrance] 注册 callback: char[{captured}] 预计触发时刻 t={expectedTime:F3}s");
                phase1Callbacks[captured] = () => speakerNameController?.RevealChar(captured);
            }

            entranceSeq = effectController.PlayEntrance(speakerName, entranceColor, firstSpeakerIsLeft, charInterval, phase1Callbacks);
        }

        // 等待头像入场和色块入场中较长的完成
        if (entranceSeq != null)
            yield return entranceSeq.WaitForCompletion();
        // 确保头像入场也已完成
        if (entranceCoroutine != null)
            yield return entranceCoroutine;
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

        // 0. 立即清空旧文本，防止切换动画期间显示上一句内容
        if (contentText != null)
            contentText.text = "";

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

        // 背景条换边动画（角度旋转 + 颜色变化，与其他动画并行）
        if (avatarBarController != null)
        {
            if (syncDuration > 0f)
                avatarBarController.SetSyncDuration(syncDuration);
            if (animator != null)
                avatarBarController.SetSyncEase(animator.characterEntranceEase);
            avatarBarController.PlaySwitchTransition(toColor, newSide?.ToLower() == "left");
        }

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

    void PrepareAvatarForEntrance(Image avatar, Sprite sprite)
    {
        if (avatar == null) return;

        avatar.gameObject.SetActive(false);
        avatar.sprite = sprite;
        avatar.color = Color.white;

        if (sprite != null)
            FitAvatarToContainer(avatar);
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

        Debug.Log($"[StoryUI] FitAvatar: name={avatar.name}, sprite={sp.name}, " +
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
