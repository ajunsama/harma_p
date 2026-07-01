using UnityEngine;

[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(-100)]
public class LevelCameraController : MonoBehaviour
{
    [Header("跟随目标")]
    public Transform target;

    [Header("相机参数")]
    public float fixedY;
    public float zOffset = -10f;
    public bool useCustomInitialPosition;
    public Vector2 initialPosition;

    [Header("死区设置")]
    [Range(0f, 0.45f)]
    [Tooltip("死区从屏幕边缘算起的比例。0.2 = 玩家在 20%~80% 间自由移动，超出时相机零延迟跟随")]
    public float deadZone = 0.2f;

    [Header("边界限制")]
    public float minX = -20f;
    public float maxX = 100f;

    [Header("战斗锁屏")]
    public bool lockPosition;
    public float lockX;

    public static bool IsLocked { get; private set; }
    public static float LockedLeftBound { get; private set; }
    public static float LockedRightBound { get; private set; }

    private Camera _cam;
    private bool _didInitCenter;

    void Start()
    {
        _cam = GetComponent<Camera>();
    }

    void LateUpdate()
    {
        if (_cam == null) return;

        if (target == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                target = player.transform;
        }

        if (target == null) return;

        // 读 transform.position（已经包含了 Rigidbody2D 的 Interpolation）
        // 这样和玩家 Sprite 渲染的坐标一致，不会出现物理步 vs 渲染帧的速度差
        float playerX = lockPosition ? lockX : target.position.x;

        float halfWidth = _cam.orthographicSize * _cam.aspect;

        if (!_didInitCenter)
        {
            float initX = useCustomInitialPosition ? initialPosition.x : playerX;
            float initY = useCustomInitialPosition ? initialPosition.y : fixedY;
            initX = Mathf.Clamp(initX, minX, maxX);
            fixedY = initY;
            transform.position = new Vector3(initX, initY, zOffset);
            _didInitCenter = true;
            return;
        }

        float screenWidth = halfWidth * 2f;
        float cameraLeft = transform.position.x - halfWidth;

        // 玩家在屏幕上的归一化位置
        float playerScreenPercent = (playerX - cameraLeft) / screenWidth;

        if (playerScreenPercent < deadZone)
            cameraLeft = playerX - deadZone * screenWidth;
        else if (playerScreenPercent > (1f - deadZone))
            cameraLeft = playerX - (1f - deadZone) * screenWidth;

        float cameraX = Mathf.Clamp(cameraLeft + halfWidth, minX, maxX);
        transform.position = new Vector3(cameraX, fixedY, zOffset);
    }

    public void LockAt(float x)
    {
        lockPosition = true;
        lockX = x;
        IsLocked = true;
        if (TryGetComponent<Camera>(out var cam))
        {
            float half = cam.orthographicSize * cam.aspect;
            LockedLeftBound = x - half + 0.5f;
            LockedRightBound = x + half - 0.5f;
        }
    }

    public void Unlock()
    {
        lockPosition = false;
        IsLocked = false;
    }

    public float GetCameraHalfWidth()
    {
        var cam = GetComponent<Camera>();
        if (cam != null)
            return cam.orthographicSize * cam.aspect;
        return 5f;
    }
}
