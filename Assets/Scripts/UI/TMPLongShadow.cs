using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

[ExecuteInEditMode]
public class TMPLongShadow : BaseMeshEffect
{
    public Color shadowColor = Color.black;
    public int shadowLength = 50; // 阴影长度
    public float angle = 45f;     // 阴影角度

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive()) return;

        List<UIVertex> verts = new List<UIVertex>();
        vh.GetUIVertexStream(verts);

        int initialCount = verts.Count;
        Vector3 dir = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad), 0);

        List<UIVertex> shadowVerts = new List<UIVertex>();

        // 核心逻辑：从后往前绘制阴影层（先添加阴影，再添加原始文字，确保文字在最上层）
        for (int i = shadowLength; i > 0; i--)
        {
            for (int j = 0; j < initialCount; j++)
            {
                UIVertex v = verts[j];
                Vector3 offset = dir * i; // 偏移量
                v.position += offset;
                v.color = shadowColor;
                shadowVerts.Add(v);
            }
        }

        // 阴影在前（先绘制），原始文字在后（后绘制，显示在上层）
        shadowVerts.AddRange(verts);

        vh.Clear();
        vh.AddUIVertexTriangleStream(shadowVerts);
    }

    /// <summary>
    /// 运行时修改参数后调用，强制重新生成阴影网格
    /// </summary>
    public void Refresh()
    {
        if (graphic != null)
        {
            // TextMeshProUGUI 重写了 UpdateGeometry() 为空操作，
            // 单纯 SetVerticesDirty() 不会触发 ModifyMesh，
            // 必须通过 ForceMeshUpdate 强制走 TMP 网格生成管线。
            var tmp = graphic as TextMeshProUGUI;
            if (tmp != null)
                tmp.ForceMeshUpdate(true);
            else
                graphic.SetVerticesDirty();
        }
    }
}