using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 不规则对话框形状组件 —— 继承Image，重写网格生成
/// 左下角45°锐角，左上角135°钝角，右上角90°圆角，右下角90°直角
/// mirrorProgress: 0=左说话者形状, 1=右说话者(镜像)形状, 中间值平滑插值
/// </summary>
public class IrregularDialogBox : Image
{
    [Header("不规则形状设置")]
    [Tooltip("锐角端水平偏移量（控制锐角大小，0=自动用高度，保持45°角）")]
    [SerializeField] private float leftBottomOffset = 0f;

    [Tooltip("圆角半径")]
    [SerializeField] private float topRightRadius = 20f;

    [Tooltip("圆角分段数（越多越圆滑）")]
    [Range(2, 32)]
    [SerializeField] private int cornerSegments = 8;

    [Tooltip("镜像进度（0=左说话者, 1=右说话者）")]
    [Range(0f, 1f)]
    [SerializeField] private float mirrorProgress = 0f;

    [Header("变形动画")]
    [Tooltip("变形动画时长（秒）")]
    [SerializeField] private float morphDuration = 0.1f;

    [Tooltip("中间态矩形比原始宽度额外增加的像素（左右各一半）")]
    [SerializeField] private float middleExtraWidth = 200f;

    /// <summary>
    /// 锐角端偏移量
    /// </summary>
    public float LeftBottomOffset
    {
        get => leftBottomOffset;
        set { leftBottomOffset = value; SetVerticesDirty(); }
    }

    /// <summary>
    /// 圆角半径
    /// </summary>
    public float TopRightRadius
    {
        get => topRightRadius;
        set { topRightRadius = value; SetVerticesDirty(); }
    }

    /// <summary>
    /// 圆角分段数
    /// </summary>
    public int CornerSegments
    {
        get => cornerSegments;
        set { cornerSegments = Mathf.Clamp(value, 2, 32); SetVerticesDirty(); }
    }

    /// <summary>
    /// 镜像进度 0~1（0=左说话者, 1=右说话者镜像）
    /// </summary>
    public float MirrorProgress
    {
        get => mirrorProgress;
        set { mirrorProgress = Mathf.Clamp01(value); SetVerticesDirty(); }
    }

    /// <summary>
    /// 变形动画时长
    /// </summary>
    public float MorphDuration
    {
        get => morphDuration;
        set => morphDuration = Mathf.Max(0f, value);
    }

    /// <summary>
    /// 中间态矩形额外宽度
    /// </summary>
    public float MiddleExtraWidth
    {
        get => middleExtraWidth;
        set { middleExtraWidth = Mathf.Max(0f, value); SetVerticesDirty(); }
    }

    /// <summary>
    /// 是否水平镜像（兼容属性，立即切换）
    /// </summary>
    public bool Mirrored
    {
        get => mirrorProgress > 0.5f;
        set { mirrorProgress = value ? 1f : 0f; SetVerticesDirty(); }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = GetPixelAdjustedRect();
        float xMin = rect.xMin;
        float xMax = rect.xMax;
        float yMin = rect.yMin;
        float yMax = rect.yMax;
        float w = xMax - xMin;
        float h = yMax - yMin;

        if (w <= 0 || h <= 0) return;

        float offset = leftBottomOffset > 0f ? leftBottomOffset : h;
        offset = Mathf.Min(offset, w * 0.5f);

        float r = Mathf.Clamp(topRightRadius, 0f, Mathf.Min(w * 0.4f, h * 0.4f));
        int segs = cornerSegments;
        float t = mirrorProgress; // 0=左形状, 0.5=中间态矩形, 1=右形状

        // ======= 生成三套形状，顶点数统一为 segs+5 =======
        // 布局: [底左, 顶左dup, 顶左, 顶边curve(segs+1点), 底右]
        // 对齐后统一: [底左, 顶过渡1, 顶过渡2, 顶部segs+1点, 底右]

        List<Vector2> leftShape = BuildLeftShape(xMin, xMax, yMin, yMax, offset, r, segs);
        List<Vector2> rightShape = BuildRightShape(xMin, xMax, yMin, yMax, offset, r, segs);
        AlignShapeCounts(leftShape, rightShape);

        int count = leftShape.Count;
        List<Vector2> middleShape = BuildMiddleShape(xMin, xMax, yMin, yMax, middleExtraWidth, count);

        // ======= 三段插值 =======
        List<Vector2> verts;
        if (t <= 0.5f)
        {
            // 正常 → 中间态
            float st = t * 2f;
            verts = LerpShapes(leftShape, middleShape, st);
        }
        else
        {
            // 中间态 → 镜像
            float st = (t - 0.5f) * 2f;
            verts = LerpShapes(middleShape, rightShape, st);
        }

        // ======= 生成三角形网格（扇形三角剖分） =======
        Color32 c = color;

        Vector2 centroid = Vector2.zero;
        for (int i = 0; i < verts.Count; i++)
            centroid += verts[i];
        centroid /= verts.Count;

        // 顶点0：中心点
        vh.AddVert(new Vector3(centroid.x, centroid.y), c,
            new Vector2((centroid.x - xMin) / w, (centroid.y - yMin) / h));

        for (int i = 0; i < verts.Count; i++)
        {
            Vector2 p = verts[i];
            vh.AddVert(new Vector3(p.x, p.y), c,
                new Vector2((p.x - xMin) / w, (p.y - yMin) / h));
        }

        int triCount = verts.Count;
        for (int i = 0; i < triCount; i++)
        {
            int cur = i + 1;
            int next = (i + 1) % triCount + 1;
            vh.AddTriangle(0, cur, next);
        }
    }

    /// <summary>
    /// 构建左说话者形状顶点
    /// </summary>
    List<Vector2> BuildLeftShape(float xMin, float xMax, float yMin, float yMax, float offset, float r, int segs)
    {
        var shape = new List<Vector2>();
        shape.Add(new Vector2(xMin, yMin));
        shape.Add(new Vector2(xMin + offset, yMax));
        if (r > 0.01f)
        {
            Vector2 center = new Vector2(xMax - r, yMax - r);
            for (int i = 0; i <= segs; i++)
            {
                float angle = Mathf.PI * 0.5f * (1f - (float)i / segs);
                shape.Add(new Vector2(center.x + Mathf.Cos(angle) * r, center.y + Mathf.Sin(angle) * r));
            }
        }
        else
        {
            for (int i = 0; i <= segs; i++)
                shape.Add(new Vector2(xMax, yMax));
        }
        shape.Add(new Vector2(xMax, yMin));
        return shape;
    }

    /// <summary>
    /// 构建右说话者形状顶点（镜像）
    /// </summary>
    List<Vector2> BuildRightShape(float xMin, float xMax, float yMin, float yMax, float offset, float r, int segs)
    {
        var shape = new List<Vector2>();
        shape.Add(new Vector2(xMin, yMin));
        if (r > 0.01f)
        {
            Vector2 centerR = new Vector2(xMin + r, yMax - r);
            shape.Add(new Vector2(centerR.x + Mathf.Cos(Mathf.PI) * r, centerR.y + Mathf.Sin(Mathf.PI) * r));
            for (int i = 0; i <= segs; i++)
            {
                float angle = Mathf.PI * 0.5f + Mathf.PI * 0.5f * (1f - (float)i / segs);
                shape.Add(new Vector2(centerR.x + Mathf.Cos(angle) * r, centerR.y + Mathf.Sin(angle) * r));
            }
        }
        else
        {
            shape.Add(new Vector2(xMin, yMax));
            for (int i = 0; i <= segs; i++)
                shape.Add(new Vector2(xMin, yMax));
        }
        shape.Add(new Vector2(xMax - offset, yMax));
        shape.Add(new Vector2(xMax, yMin));
        return shape;
    }

    /// <summary>
    /// 对齐两个形状的顶点数（在较少的一方插入重复点）
    /// </summary>
    void AlignShapeCounts(List<Vector2> a, List<Vector2> b)
    {
        while (a.Count < b.Count)
            a.Insert(1, a[1]);
        while (b.Count < a.Count)
            b.Insert(1, b[1]);
    }

    /// <summary>
    /// 构建中间态矩形形状，顶点数与左右形状一致
    /// 矩形比原始rect左右各扩展 extraWidth/2
    /// </summary>
    List<Vector2> BuildMiddleShape(float xMin, float xMax, float yMin, float yMax, float extraWidth, int vertexCount)
    {
        float halfExtra = extraWidth * 0.5f;
        float midXMin = xMin - halfExtra;
        float midXMax = xMax + halfExtra;

        var shape = new List<Vector2>(vertexCount);
        // [0]: 底左
        shape.Add(new Vector2(midXMin, yMin));
        // [1..vertexCount-2]: 沿顶边均匀分布
        int topCount = vertexCount - 2;
        for (int i = 0; i < topCount; i++)
        {
            float fraction = topCount > 1 ? (float)i / (topCount - 1) : 0.5f;
            shape.Add(new Vector2(Mathf.Lerp(midXMin, midXMax, fraction), yMax));
        }
        // [vertexCount-1]: 底右
        shape.Add(new Vector2(midXMax, yMin));
        return shape;
    }

    /// <summary>
    /// 两组形状逐点线性插值
    /// </summary>
    List<Vector2> LerpShapes(List<Vector2> from, List<Vector2> to, float t)
    {
        int count = Mathf.Min(from.Count, to.Count);
        var result = new List<Vector2>(count);
        for (int i = 0; i < count; i++)
            result.Add(Vector2.Lerp(from[i], to[i], t));
        return result;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        SetVerticesDirty();
    }
#endif
}
