using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// 对话框特效控制器 —— 管理 45° 平行四边形色块池
/// 色块构成对话框的【持久背景】，动画结束后不会消失
/// 条纹通过独立的 StripeOverlay Image 层实现，利用 Stencil 仅在色块像素上可见
///   - 色块使用 UI/BlockColor shader：渲染底色 + 写入 stencil bit 1
///   - StripeOverlay 使用 UI/StripeOverlay shader：读取 stencil bit 1，仅在色块像素上渲染条纹
///   - 渲染后 StripeOverlay 清除 stencil bit 1，不影响后续 ContentText 等 UI
/// DialogBox（IrregularDialogBox + Mask，Show Mask Graphic=OFF）仅作为遮罩裁切形状
///
/// 层级结构：
///   DialogBox (IrregularDialogBox + Mask, Show Mask Graphic=OFF)
///     ├ BlockContainer (RectTransform, stretch 铺满, 无 Image)
///     │   ├ Block_0 ~ Block_N (Image, 旋转45°, Material=BlockColor, 默认 inactive)
///     ├ StripeOverlay (Image, stretch 铺满, Material=StripeOverlay Shader, 始终 active)
///     ├ ContentText ...
///     └ ...
/// </summary>
public class DialogBoxEffectController : MonoBehaviour
{
    // ==============================
    // 引用
    // ==============================

    [Header("引用")]
    [Tooltip("色块容器（所有平行四边形色块的父物体）")]
    public RectTransform blockContainer;

    [Tooltip("条纹遮罩层（独立 Image，Stencil 让它仅在色块上可见，保持 active 即可）")]
    public Image stripeOverlay;

    [Tooltip("色块材质（UI/BlockColor Shader，自动赋给所有色块）")]
    public Material blockMaterial;

    [Tooltip("对话框形状组件（用于同步换边动画时长）")]
    public IrregularDialogBox dialogBox;

    // ==============================
    // 色块尺寸（矩形长短边，旋转后呈平行四边形）
    // ==============================

    [Header("色块尺寸")]
    [Tooltip("色块长边（像素）")]
    public float blockWidth = 491f;

    [Tooltip("色块短边 / 厚度（像素），决定条纹视觉粗细和无缝排列间距")]
    public float blockHeight = 131f;

    [Tooltip("色块旋转角度（度）")]
    public float blockRotation = 45f;

    // ==============================
    // 入场动画参数
    // ==============================

    [Header("入场动画 - Phase1: 色块淡入")]
    [Tooltip("每个色块淡入持续时间")]
    public float fadeInDuration = 0f;

    [Tooltip("相邻色块淡入的间隔时间")]
    public float fadeInInterval = 0.06f;

    [Header("入场动画 - Phase1: 对齐偏移与色块宽度")]
    [Tooltip("首个 Phase1 色块的边界对齐 DialogBox 边界后的 X 偏移（像素，正值右移）\nfromLeft = 左边界对齐容器左边界，fromRight = 右边界对齐容器右边界")]
    public float phase1AlignOffsetX = 0f;

    [Tooltip("Phase1 色块高度/厚度（像素）；0 表示与 blockHeight 相同")]
    public float phase1BlockHeight = 0f;

    [Header("入场动画 - Phase2: 色块飞入")]
    [Tooltip("飞入持续时间")]
    public float flyInDuration = 0.35f;

    [Tooltip("飞入缓动类型")]
    public Ease flyInEase = Ease.OutQuad;

    [Tooltip("所有飞入色块的交错总时长（越大越能看到逐个飞入的效果，建议接近或大于 flyInDuration）")]
    public float flyInStaggerTotal = 1.0f;

    [Tooltip("交错时间幂次（<1 = 前疏后密，第一个间隔最大；1 = 匀速；>1 = 前密后疏）")]
    [Range(0.2f, 5f)]
    public float flyInStaggerPower = 0.35f;

    [Tooltip("Phase1 与 Phase2 之间的间隔")]
    public float phase1To2Gap = 0.05f;

    // ==============================
    // 换边过渡参数
    // ==============================

    [Header("换边过渡 - 散开")]
    [Tooltip("散开时相邻色块间的间隙（像素，在原排列间距基础上额外添加）")]
    public float scatterGap = 30f;

    [Tooltip("散开持续时间")]
    public float scatterDuration = 0.08f;

    [Tooltip("散开缓动")]
    public Ease scatterEase = Ease.OutQuad;

    [Header("换边过渡 - 聚合")]
    [Tooltip("聚合持续时间")]
    public float gatherDuration = 0.08f;

    [Tooltip("聚合缓动")]
    public Ease gatherEase = Ease.InQuad;

    [Tooltip("散开与聚合之间的停顿时间")]
    public float scatterGatherGap = 0.04f;

    // ==============================
    // 运行时数据
    // ==============================

    private readonly List<Image> _blockPool = new List<Image>();
    private Color _baseColor = Color.white;
    private Sequence _currentSequence;

    // 当前色块旋转角度（跟随说话方向变化：左=blockRotation，右=180-blockRotation）
    private float _currentRotation;

    // 无缝排列
    private float _tileSpacing;
    private int _visibleTileCount;
    private float[] _tilePositionsX;

    /// <summary>是否正在播放动画</summary>
    public bool IsPlaying => _currentSequence != null && _currentSequence.IsActive() && _currentSequence.IsPlaying();

    void Awake()
    {
        _currentRotation = blockRotation;
        CollectBlockPool();
    }

    void OnDestroy()
    {
        KillCurrentSequence();
    }

    // ==============================
    // 公共方法
    // ==============================

    /// <summary>
    /// 设置色块基础颜色
    /// </summary>
    public void SetBaseColor(Color color)
    {
        _baseColor = color;
    }

    /// <summary>
    /// 立即设置背景（无动画）：所有色块归位显示
    /// 用于无入场动画时直接显示，或动画结束后的收尾
    /// </summary>
    public void SetupBackground(Color color)
    {
        _baseColor = color;
        RecalculateTiling();

        for (int i = 0; i < _blockPool.Count; i++)
        {
            Image block = _blockPool[i];
            RectTransform rt = block.rectTransform;

            if (i < _visibleTileCount)
            {
                rt.DOKill();
                block.DOKill();
                rt.sizeDelta = new Vector2(blockWidth, blockHeight);
                rt.localRotation = Quaternion.Euler(0, 0, _currentRotation);
                rt.anchoredPosition = new Vector2(_tilePositionsX[i], 0f);
                block.color = _baseColor;
                block.gameObject.SetActive(true);
            }
            else
            {
                block.gameObject.SetActive(false);
            }
        }

        // 直接显示时条纹层也立即可见（stencil 自动裁切）
        ActivateStripeOverlay(true);
        // 确保条纹层归位
        if (stripeOverlay != null)
        {
            stripeOverlay.rectTransform.DOKill();
            stripeOverlay.rectTransform.anchoredPosition = Vector2.zero;
        }
    }

    /// <summary>
    /// 入场动画：色块依次淡入 + 飞入，动画结束后色块【保持显示】作为背景
    /// </summary>
    /// <param name="speakerName">说话者名称，长度决定 Phase1 色块数</param>
    /// <param name="baseColor">色块颜色</param>
    /// <param name="fromLeft">第一个说话者在左侧</param>
    /// <param name="intervalOverride">Phase1 色块淡入间隔覆盖值（秒），≤0 则使用 fadeInInterval</param>
    /// <param name="phase1ShowCallbacks">Phase1 每个色块出现时额外触发的 callback（长度可小于 phase1Count），用于同步字符显示</param>
    public Sequence PlayEntrance(string speakerName, Color baseColor, bool fromLeft, float intervalOverride = -1f, System.Action[] phase1ShowCallbacks = null)
    {
        KillCurrentSequence();
        _baseColor = baseColor;
        RecalculateTiling();

        int nameLen = string.IsNullOrEmpty(speakerName) ? 3 : speakerName.Length;
        int phase1Count = Mathf.Clamp(nameLen, 1, _visibleTileCount);
        int phase2Count = _visibleTileCount - phase1Count;

        // ---- Phase1 高度、间距与边界对齐（独立于 Phase2）----
        // p1Height: Phase1 色块高度/厚度，独立于 blockHeight；Phase2 仍用 blockHeight
        float p1Height = phase1BlockHeight > 0f ? phase1BlockHeight : blockHeight;
        float[] p1PositionsX;
        {
            float angleRad = _currentRotation * Mathf.Deg2Rad;
            float sinA = Mathf.Abs(Mathf.Sin(angleRad));
            // Phase1 无缝间距：基于 p1Height，保证旋转后无缝衔接
            float p1Spacing = sinA > 0.01f ? p1Height / sinA : p1Height;
            // 旋转矩形视觉半宽：(W*|cosθ| + H*|sinθ|) / 2
            float halfExtent = (blockWidth * Mathf.Abs(Mathf.Cos(angleRad)) + p1Height * sinA) * 0.5f;
            float containerHalfWidth = (blockContainer != null && blockContainer.rect.width > 0f)
                ? blockContainer.rect.width * 0.5f : 400f;
            // 首个 Phase1 色块中心X：左/右视觉边界对齐容器对应边界 + 偏移
            float p1StartX = fromLeft
                ? (-containerHalfWidth + halfExtent + phase1AlignOffsetX)
                : ( containerHalfWidth - halfExtent + phase1AlignOffsetX);
            // 依次生成 phase1Count 个位置，方向朝容器内侧延伸
            p1PositionsX = new float[phase1Count];
            for (int i = 0; i < phase1Count; i++)
                p1PositionsX[i] = fromLeft ? (p1StartX + i * p1Spacing) : (p1StartX - i * p1Spacing);
        }

        // ---- 平移 _tilePositionsX，让 Phase2 紧邻 Phase1 末尾无缝衔接 ----
        // Phase1 末尾块右/左边界 → Phase2 首个相邻块左/右边界精确对齐
        {
            float sinA2 = Mathf.Abs(Mathf.Sin(_currentRotation * Mathf.Deg2Rad));
            // 过渡间距：半个 p1Height 块 + 半个 blockHeight 块
            float transSpacing = sinA2 > 0.01f
                ? (p1Height + blockHeight) / (2f * sinA2)
                : (p1Height + blockHeight) * 0.5f;
            float lastP1X = p1PositionsX[phase1Count - 1];
            float firstP2DesiredX = fromLeft ? (lastP1X + transSpacing) : (lastP1X - transSpacing);
            // Phase1 占用的是从 phase1Start 延伸 phase1Count 个块
            // 紧邻的 Phase2 块索引：fromLeft = phase1Count+1，fromRight = visibleTileCount-2-phase1Count
            int adjIdx = Mathf.Clamp(
                fromLeft ? (phase1Count + 1) : (_visibleTileCount - 2 - phase1Count),
                0, _visibleTileCount - 1);
            float p2Shift = firstP2DesiredX - _tilePositionsX[adjIdx];
            for (int i = 0; i < _visibleTileCount; i++)
                _tilePositionsX[i] += p2Shift;
        }

        // 先隐藏所有色块，激活条纹层（stencil 保证无色块时条纹不可见）
        HideAllBlocks();
        ActivateStripeOverlay(true);

        Sequence seq = DOTween.Sequence().SetUpdate(true);

        float interval = intervalOverride > 0f ? intervalOverride : fadeInInterval;
        float phase2Start = interval * phase1Count + phase1To2Gap;
        Debug.Log($"[PlayEntrance] fadeInDuration={fadeInDuration:F3}, interval={interval:F3}, phase1Count={phase1Count}, t={Time.unscaledTime:F3}");

        // ---- Phase 1: 从说话者侧依次淡入（色块在最终排列位置，仅 alpha 变化） ----
        // index 0 为左溢出块、index _visibleTileCount-1 为右溢出块，圆心在容器外会被 Mask 裁切
        // fromLeft:  从 index 1 开始向右，跳过左溢出块
        // fromRight: 从 index _visibleTileCount-2 开始向左，跳过右溢出块
        int phase1Start = fromLeft ? 1 : (_visibleTileCount - 2);
        int phase1Step  = fromLeft ? 1 : -1;
        bool[] usedInPhase1 = new bool[_visibleTileCount];

        for (int i = 0; i < phase1Count; i++)
        {
            int blockIdx = Mathf.Clamp(phase1Start + i * phase1Step, 0, _visibleTileCount - 1);
            usedInPhase1[blockIdx] = true;
            Image block = _blockPool[blockIdx];
            RectTransform rt = block.rectTransform;

            rt.sizeDelta = new Vector2(blockWidth, p1Height);
            rt.localRotation = Quaternion.Euler(0, 0, _currentRotation);
            rt.anchoredPosition = new Vector2(p1PositionsX[i], 0f);

            block.gameObject.SetActive(true);
            Color c = _baseColor;
            c.a = 0f;
            block.color = c;

            float delay = interval * i;
            Debug.Log($"[PlayEntrance] Phase1 block[{i}] blockIdx={blockIdx} delay={delay:F3}s interval={interval:F3}");
            var fadeTween = block.DOFade(1f, fadeInDuration).SetEase(Ease.Linear);
            if (i == 0)
            {
                var b0 = block;
                fadeTween
                    .OnStart(() => Debug.Log($"[PlayEntrance] block[0] DOFade OnStart alpha={b0.color.a:F3} t={Time.unscaledTime:F3}"))
                    .OnComplete(() => Debug.Log($"[PlayEntrance] block[0] DOFade OnComplete alpha={b0.color.a:F3} t={Time.unscaledTime:F3}"));
            }
            seq.Insert(delay, fadeTween);

            // 同步触发外部 callback（字符显示）
            if (phase1ShowCallbacks != null && i < phase1ShowCallbacks.Length && phase1ShowCallbacks[i] != null)
            {
                int captured = i;
                System.Action cb = phase1ShowCallbacks[captured];
                seq.InsertCallback(delay, () =>
                {
                    Debug.Log($"[PlayEntrance] callback触发: char[{captured}] 实际时刻 t={Time.unscaledTime:F3}s");
                    cb();
                });
            }
        }

        // ---- Phase 2: 剩余色块（含两个溢出块）从对话框右边界外逐个飞入 ----
        float containerWidth = (blockContainer != null && blockContainer.rect.width > 0f)
            ? blockContainer.rect.width : 800f;
        // 所有飞入色块的起始X = 容器右边界外（containerWidth/2 + 色块宽度留出余量）
        float flyStartX = containerWidth * 0.5f + blockWidth;

        int phase2Idx = 0;
        for (int bi = 0; bi < _visibleTileCount; bi++)
        {
            if (usedInPhase1[bi]) continue;

            int blockIdx = bi;
            Image block = _blockPool[blockIdx];
            RectTransform rt = block.rectTransform;

            float finalX = _tilePositionsX[blockIdx];
            rt.sizeDelta = new Vector2(blockWidth, blockHeight);
            rt.localRotation = Quaternion.Euler(0, 0, _currentRotation);
            rt.anchoredPosition = new Vector2(flyStartX, 0f);
            block.gameObject.SetActive(true);
            block.color = _baseColor;

            // 幂曲线交错：t ∈ [0,1] → t^power，power<1 时前疏后密
            float t = phase2Count > 1 ? (float)phase2Idx / (phase2Count - 1) : 0f;
            float staggerT = Mathf.Pow(t, flyInStaggerPower);
            float delay = phase2Start + staggerT * flyInStaggerTotal;
            seq.Insert(delay, rt.DOAnchorPosX(finalX, flyInDuration).SetEase(flyInEase));

            phase2Idx++;
        }

        // ---- StripeOverlay 从右边界外滑入（与色块飞入同步） ----
        if (stripeOverlay != null)
        {
            RectTransform stripeRt = stripeOverlay.rectTransform;
            stripeRt.anchoredPosition = new Vector2(flyStartX, 0f);
            seq.Insert(phase2Start, stripeRt.DOAnchorPosX(0f, flyInDuration + flyInStaggerTotal).SetEase(flyInEase));
        }

        // ★ 动画期间及结束后：色块保持显示，条纹通过 stencil 自动跟随色块

        _currentSequence = seq;
        return seq;
    }

    /// <summary>
    /// 换边过渡动画：色块散开→换色→聚合，与 IrregularDialogBox 形变两阶段同步
    /// IrregularDialogBox: mirrorProgress 0→0.5 (左→矩形), 0.5→1 (矩形→右), SmoothStep
    /// 色块: 散开+旋转到90° = 前半程, 聚合+旋转到目标 = 后半程
    /// 条纹角度与色块旋转完全锁定
    /// </summary>
    public Sequence PlaySwitchTransition(Color newColor, bool toMirrored)
    {
        KillCurrentSequence();
        RecalculateTiling();

        int count = _visibleTileCount;

        // 与 IrregularDialogBox 形变同步：总时长 = MorphDuration，前后各一半
        float morphDuration = (dialogBox != null) ? dialogBox.MorphDuration : (scatterDuration + scatterGatherGap + gatherDuration);
        float halfDuration = morphDuration * 0.5f;

        Sequence seq = DOTween.Sequence().SetUpdate(true);

        // 目标旋转：左=blockRotation(45), 右=180-blockRotation(135)
        float targetRotation = toMirrored ? (180f - blockRotation) : blockRotation;
        // 中间态 = 90°（与矩形形状对应）
        float midRotation = 90f;

        // ---- 前半程（散开）：与形变 mirrorProgress 0→0.5 / 1→0.5 同步 ----
        float scatterSpacing = _tileSpacing + scatterGap;
        float scatterTotalWidth = (count - 1) * scatterSpacing;
        float scatterStartX = -scatterTotalWidth * 0.5f;

        for (int i = 0; i < count; i++)
        {
            Image block = _blockPool[i];
            RectTransform rt = block.rectTransform;

            block.gameObject.SetActive(true);
            block.color = _baseColor;
            rt.sizeDelta = new Vector2(blockWidth, blockHeight);
            rt.anchoredPosition = new Vector2(_tilePositionsX[i], 0f);
            rt.localRotation = Quaternion.Euler(0, 0, _currentRotation);

            float targetX = scatterStartX + i * scatterSpacing;

            seq.Insert(0f, rt.DOAnchorPosX(targetX, halfDuration).SetEase(Ease.InQuad));
            seq.Insert(0f, rt.DOLocalRotate(new Vector3(0, 0, midRotation), halfDuration).SetEase(Ease.InQuad));
            seq.Insert(0f, block.DOFade(0.3f, halfDuration).SetEase(Ease.InQuad));
        }

        // ---- 条纹角度：前半程同步 ----
        if (stripeOverlay != null && stripeOverlay.material != null)
        {
            Material mat = stripeOverlay.material;
            float stripeAngle = mat.GetFloat("_StripeAngle");
            float midDelta = midRotation - _currentRotation;
            float stripeMid = stripeAngle + midDelta;
            seq.Insert(0f, DOTween.To(() => stripeAngle, x =>
            {
                stripeAngle = x;
                mat.SetFloat("_StripeAngle", x);
            }, stripeMid, halfDuration).SetEase(Ease.InQuad).SetUpdate(true));

            // ---- 条纹角度：后半程同步 ----
            float gatherDelta = targetRotation - midRotation;
            float stripeFinal = stripeMid + gatherDelta;
            seq.Insert(halfDuration, DOTween.To(() => stripeMid, x =>
            {
                stripeMid = x;
                mat.SetFloat("_StripeAngle", x);
            }, stripeFinal, halfDuration).SetEase(Ease.OutQuad).SetUpdate(true));
        }

        // ---- 后半程（聚合）：与形变 mirrorProgress 0.5→1 / 0.5→0 同步 ----
        for (int i = 0; i < count; i++)
        {
            Image block = _blockPool[i];
            RectTransform rt = block.rectTransform;
            int idx = i;

            // 聚合开始时换色
            seq.InsertCallback(halfDuration, () =>
            {
                Color c = newColor;
                c.a = 0.3f;
                _blockPool[idx].color = c;
            });

            float finalX = _tilePositionsX[i];

            seq.Insert(halfDuration, rt.DOAnchorPosX(finalX, halfDuration).SetEase(Ease.OutQuad));
            seq.Insert(halfDuration, rt.DOLocalRotate(new Vector3(0, 0, targetRotation), halfDuration).SetEase(Ease.OutQuad));
            seq.Insert(halfDuration, block.DOFade(1f, halfDuration).SetEase(Ease.OutQuad));
        }

        // 结束后更新状态
        seq.OnComplete(() =>
        {
            _currentRotation = targetRotation;
            _baseColor = newColor;
            SetupBackground(newColor);
        });

        _currentSequence = seq;
        return seq;
    }

    /// <summary>
    /// 停止所有特效并隐藏（不常用，仅在需要完全清除时调用）
    /// </summary>
    public void StopAll()
    {
        KillCurrentSequence();
        HideAllBlocks();
        ActivateStripeOverlay(false);
        if (stripeOverlay != null)
        {
            stripeOverlay.rectTransform.DOKill();
            stripeOverlay.rectTransform.anchoredPosition = Vector2.zero;
        }
    }

    // ==============================
    // 内部方法
    // ==============================

    /// <summary>
    /// 根据色块尺寸和容器宽度计算无缝排列参数
    /// tileSpacing = blockHeight / sin(rotation)，保证旋转后无缝衔接
    /// </summary>
    void RecalculateTiling()
    {
        float angleRad = blockRotation * Mathf.Deg2Rad;
        float sinA = Mathf.Sin(angleRad);
        _tileSpacing = sinA > 0.01f ? blockHeight / sinA : blockHeight;

        float containerWidth = (blockContainer != null && blockContainer.rect.width > 0f)
            ? blockContainer.rect.width
            : 800f;

        _visibleTileCount = Mathf.CeilToInt(containerWidth / _tileSpacing) + 2;
        _visibleTileCount = Mathf.Min(_visibleTileCount, _blockPool.Count);

        _tilePositionsX = new float[_visibleTileCount];
        float totalWidth = (_visibleTileCount - 1) * _tileSpacing;
        float startX = -totalWidth * 0.5f;
        for (int i = 0; i < _visibleTileCount; i++)
        {
            _tilePositionsX[i] = startX + i * _tileSpacing;
        }
    }

    /// <summary>
    /// 从 blockContainer 子物体收集色块池
    /// </summary>
    void CollectBlockPool()
    {
        _blockPool.Clear();
        if (blockContainer == null) return;

        for (int i = 0; i < blockContainer.childCount; i++)
        {
            Image img = blockContainer.GetChild(i).GetComponent<Image>();
            if (img != null)
            {
                if (blockMaterial != null)
                    img.material = blockMaterial;
                _blockPool.Add(img);
                img.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// 激活/停用条纹层（stencil 自动控制可见性，此处仅控制 GameObject active 状态）
    /// </summary>
    void ActivateStripeOverlay(bool active)
    {
        if (stripeOverlay != null)
            stripeOverlay.gameObject.SetActive(active);
    }

    void HideAllBlocks()
    {
        foreach (var block in _blockPool)
        {
            if (block != null)
            {
                block.DOKill();
                block.rectTransform.DOKill();
                block.gameObject.SetActive(false);
            }
        }
    }

    void KillCurrentSequence()
    {
        if (_currentSequence != null && _currentSequence.IsActive())
            _currentSequence.Kill();
        _currentSequence = null;
    }
}
