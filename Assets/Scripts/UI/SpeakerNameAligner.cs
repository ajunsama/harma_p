using UnityEngine;

/// <summary>
/// 将 SpeakerName 下的 NameText 位置与 DialogBox 斜边对齐，实现无缝衔接。
/// 根据 IrregularDialogBox 的斜边几何，自动计算 SpeakerNameController 的
/// leftPositionX / rightPositionX，使名称标签的视觉边缘紧贴对话框斜角延长线。
///
/// 对齐原理：
///   - 左说话者：文本左边缘 + TMPLongShadow(45°) 构成的斜边与 DialogBox 左斜边共线
///   - 右说话者：文本右边缘 + TMPLongShadow(135°) 构成的斜边与 DialogBox 右斜边共线
///   - 斜边延伸到 NameText 所在 Y 高度的 X 交点 = 文本的视觉边缘位置
///
/// 使用方式：挂在 SpeakerName 节点上，在 Inspector 中绑定引用。
/// 编辑器下实时预览，运行时在 OnEnable 和 LateUpdate 中持续更新。
/// </summary>
[ExecuteInEditMode]
public class SpeakerNameAligner : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("DialogBox 的 RectTransform")]
    public RectTransform dialogBoxRect;

    [Tooltip("DialogBox 上的 IrregularDialogBox 组件")]
    public IrregularDialogBox dialogBox;

    [Tooltip("SpeakerNameController 组件")]
    public SpeakerNameController nameController;

    [Header("对齐微调")]
    [Tooltip("左侧额外水平偏移（正值=向右移）")]
    public float leftOffset = 0f;

    [Tooltip("右侧额外水平偏移（正值=向左移）")]
    public float rightOffset = 0f;

    void OnEnable()
    {
        Recalculate();
    }

    void LateUpdate()
    {
        Recalculate();
    }

    /// <summary>
    /// 重新计算并更新 NameText_A/B 的对齐位置。
    /// 也可从外部（如 StoryUI）调用以在特定时机强制刷新。
    /// </summary>
    public void Recalculate()
    {
        if (dialogBoxRect == null || dialogBox == null || nameController == null) return;
        if (nameController.nameTextA == null || nameController.nameTextB == null) return;

        RectTransform rtA = nameController.nameTextA.rectTransform;
        RectTransform rtB = nameController.nameTextB.rectTransform;

        // ---- DialogBox 几何 ----
        Rect dbRect = dialogBoxRect.rect;
        float dbH = dbRect.height;
        if (dbH <= 0) return;

        // 斜角偏移量（0 时默认等于高度，即 45°）
        float slantOffset = dialogBox.LeftBottomOffset > 0f ? dialogBox.LeftBottomOffset : dbH;
        slantOffset = Mathf.Min(slantOffset, dbRect.width * 0.5f);

        // ---- 左侧斜边：(xMin, yMin) → (xMin + offset, yMax) ----
        Vector3 leftBot = dialogBoxRect.TransformPoint(new Vector3(dbRect.xMin, dbRect.yMin, 0));
        Vector3 leftTop = dialogBoxRect.TransformPoint(new Vector3(dbRect.xMin + slantOffset, dbRect.yMax, 0));
        float leftDy = leftTop.y - leftBot.y;
        float leftSlope = Mathf.Abs(leftDy) > 0.01f ? (leftTop.x - leftBot.x) / leftDy : 0f;

        // ---- 右侧斜边：(xMax, yMin) → (xMax - offset, yMax) ----
        Vector3 rightBot = dialogBoxRect.TransformPoint(new Vector3(dbRect.xMax, dbRect.yMin, 0));
        Vector3 rightTop = dialogBoxRect.TransformPoint(new Vector3(dbRect.xMax - slantOffset, dbRect.yMax, 0));
        float rightDy = rightTop.y - rightBot.y;
        float rightSlope = Mathf.Abs(rightDy) > 0.01f ? (rightTop.x - rightBot.x) / rightDy : 0f;

        // ---- NameText 纵坐标（世界空间）----
        float nameY = rtA.position.y;

        // ---- 斜边延伸到 NameText 高度处的 X（世界空间）----
        float leftSlantX = leftBot.x + leftSlope * (nameY - leftBot.y) + leftOffset;
        float rightSlantX = rightBot.x + rightSlope * (nameY - rightBot.y) - rightOffset;

        // ---- 考虑文本边缘偏移 ----
        // anchoredPosition 定位的是 pivot 点。为了让「文本左边缘」对齐斜线，
        // 需要从斜线交点 X 减去 pivot 到左边缘的世界空间距离。
        // pivot 到左边缘的偏移（世界空间） = rect.xMin * lossyScale.x（不随位置变化）
        float pivotToLeftA = rtA.rect.xMin * rtA.lossyScale.x;
        float pivotToRightB = rtB.rect.xMax * rtB.lossyScale.x;

        // 期望 pivot 的世界 X = 斜线交点 X - 边缘偏移
        float desiredPivotLeftX = leftSlantX - pivotToLeftA;
        float desiredPivotRightX = rightSlantX - pivotToRightB;

        // ---- 转为 anchoredPosition.x 写入控制器 ----
        nameController.leftPositionX = WorldXToAnchoredX(rtA, desiredPivotLeftX);
        nameController.rightPositionX = WorldXToAnchoredX(rtB, desiredPivotRightX);
    }

    /// <summary>
    /// 世界空间 X 坐标 → 目标 RectTransform 的 anchoredPosition.x
    /// anchorOffset = localPosition - anchoredPosition（由锚点配置决定，常量）
    /// </summary>
    float WorldXToAnchoredX(RectTransform rt, float worldX)
    {
        float anchorOffX = rt.localPosition.x - rt.anchoredPosition.x;
        Vector3 localPos = rt.parent.InverseTransformPoint(
            new Vector3(worldX, rt.position.y, rt.position.z));
        return localPos.x - anchorOffX;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        Recalculate();
    }
#endif
}
