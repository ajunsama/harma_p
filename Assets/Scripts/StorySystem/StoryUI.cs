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
public partial class StoryUI : MonoBehaviour
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
    /// 应用样式模板到UI
    /// </summary>
    public void ApplyStyle(StoryStyleTemplate style)
    {
        if (style == null) { GameLog.Verbose("[StoryUI] ApplyStyle: style is null, skipping"); return; }
        GameLog.Verbose($"[StoryUI] ApplyStyle: styleId={style.styleId}, boxColor={style.dialogueBoxColor}, hasSprite={style.dialogueBoxSprite != null}");

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
            GameLog.Verbose($"[StoryUI] ApplyStyle: dialogueBoxImage type={dialogueBoxImage.GetType().Name}, instanceID={dialogueBoxImage.GetInstanceID()}");
            if (style.dialogueBoxSprite != null)
            {
                dialogueBoxImage.sprite = style.dialogueBoxSprite;
                dialogueBoxImage.type = Image.Type.Sliced;
            }
            dialogueBoxImage.color = style.dialogueBoxColor;
            GameLog.Verbose($"[StoryUI] ApplyStyle: color set to {dialogueBoxImage.color}");
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


}
