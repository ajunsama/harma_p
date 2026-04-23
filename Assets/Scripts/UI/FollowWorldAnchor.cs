using UnityEngine;

/// <summary>
/// 将物体的世界坐标位置和旋转锁定到指定锚点，
/// 使其不受父节点变换（旋转/位移）影响。
/// 用于 Mask 内需要固定位置的子物体。
/// </summary>
public class FollowWorldAnchor : MonoBehaviour
{
    [Tooltip("世界坐标锚点，本物体每帧对齐到该锚点的位置和旋转")]
    public Transform anchor;

    void LateUpdate()
    {
        if (anchor != null)
        {
            transform.position = anchor.position;
            transform.rotation = anchor.rotation;
        }
    }
}
