using UnityEngine;

/// <summary>
/// 使物体相对主相机完全固定（适用于UI背景、天空盒等不随相机移动的物体）
/// </summary>
[DefaultExecutionOrder(500)] // 确保在相机移动（LevelProgressManager默认是0）之后执行
public class CameraFixedObject : MonoBehaviour
{
    [Tooltip("要跟随的相机，为空则自动查找主相机")]
    public Camera targetCamera;

    [Tooltip("是否在Start时自动计算当前固定的偏移量（保持当前相对位置）")]
    public bool maintainCurrentOffset = true;

    [Tooltip("手动指定的偏移量（如果 maintainCurrentOffset 为 false）")]
    public Vector3 manualOffset = new Vector3(0, 0, 10);
    
    private Vector3 _offset;

    void Start()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera != null)
        {
            if (maintainCurrentOffset)
            {
                // 记录初始相对位置
                _offset = transform.position - targetCamera.transform.position;
            }
            else
            {
                _offset = manualOffset;
            }
        }
    }

    void LateUpdate()
    {
        if (targetCamera == null) return;

        // 每一帧强制将位置设置到相机位置 + 偏移量
        // LateUpdate 确保在相机移动之后更新，防止抖动
        transform.position = targetCamera.transform.position + _offset;
    }
}
