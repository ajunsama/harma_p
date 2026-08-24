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
    [Tooltip("未启用画面边界时，相机中心允许到达的最小 X")]
    public float minX = -20f;
    [Tooltip("未启用画面边界时，相机中心允许到达的最大 X")]
    public float maxX = 100f;
    [SerializeField, Tooltip("让整个相机画面保持在世界边界内，而不只是限制相机中心")]
    private bool constrainViewportToWorldBounds;
    [SerializeField] private float worldBoundsStartX;
    [SerializeField] private float worldBoundsEndX = 50f;

    [Header("战斗锁屏")]
    public bool lockPosition;
    public float lockX;

    public static bool IsLocked { get; private set; }
    public static float LockedLeftBound { get; private set; }
    public static float LockedRightBound { get; private set; }
    private static LevelCameraController lockOwner;

    private Camera _cam;
    private bool _didInitCenter;
    private bool _flowOverride;
    private Vector2 _flowPosition;

    void Start()
    {
        _cam = GetComponent<Camera>();
    }

    void LateUpdate()
    {
        if (_cam == null) return;

        if (_flowOverride)
        {
            _flowPosition.x = ClampCameraX(
                _flowPosition.x,
                _cam.orthographicSize * _cam.aspect);
            transform.position = new Vector3(_flowPosition.x, _flowPosition.y, zOffset);
            return;
        }

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
            initX = ClampCameraX(initX, halfWidth);
            fixedY = initY;
            transform.position = new Vector3(initX, initY, zOffset);
            _didInitCenter = true;
            return;
        }

        if (lockPosition)
        {
            float lockedCameraX = ClampCameraX(lockX, halfWidth);
            transform.position = new Vector3(lockedCameraX, fixedY, zOffset);
            UpdateLockedBounds(lockedCameraX, halfWidth);
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

        float cameraX = ClampCameraX(cameraLeft + halfWidth, halfWidth);
        transform.position = new Vector3(cameraX, fixedY, zOffset);
    }

    public void LockAt(float x)
    {
        _flowOverride = false;
        lockPosition = true;
        lockX = x;
        lockOwner = this;
        IsLocked = true;
        if (TryGetComponent<Camera>(out var cam))
        {
            float halfWidth = cam.orthographicSize * cam.aspect;
            UpdateLockedBounds(ClampCameraX(x, halfWidth), halfWidth);
        }
    }

    public void LockCurrentView()
    {
        LockAt(transform.position.x);
    }

    public void Unlock()
    {
        lockPosition = false;
        if (lockOwner == this)
            ClearSharedLockState();
    }

    void OnDisable()
    {
        if (lockOwner == this)
            ClearSharedLockState();
    }

    void OnDestroy()
    {
        if (lockOwner == this)
            ClearSharedLockState();
    }

    static void ClearSharedLockState()
    {
        lockOwner = null;
        IsLocked = false;
        LockedLeftBound = 0f;
        LockedRightBound = 0f;
    }

    public Vector2 CurrentPosition => new Vector2(transform.position.x, transform.position.y);

    public void SetFlowPosition(Vector2 position)
    {
        _flowOverride = true;
        float halfWidth = GetCameraHalfWidth();
        _flowPosition = new Vector2(
            ClampCameraX(position.x, halfWidth),
            position.y);
        transform.position = new Vector3(_flowPosition.x, _flowPosition.y, zOffset);
    }

    public void ResumeFollowing()
    {
        _flowOverride = false;
    }

    public void SetWorldBounds(float startX, float endX)
    {
        constrainViewportToWorldBounds = true;
        worldBoundsStartX = Mathf.Min(startX, endX);
        worldBoundsEndX = Mathf.Max(startX, endX);
    }

    public void ClearWorldBounds()
    {
        constrainViewportToWorldBounds = false;
    }

    float ClampCameraX(float requestedX, float halfWidth)
    {
        if (!constrainViewportToWorldBounds)
            return Mathf.Clamp(requestedX, minX, maxX);

        float minCenterX = worldBoundsStartX + halfWidth;
        float maxCenterX = worldBoundsEndX - halfWidth;

        // 边界比当前画面窄时不存在能完整容纳画面的坐标，固定在范围中点，
        // 避免相机在反向的最小/最大值之间跳动。
        if (minCenterX > maxCenterX)
            return (worldBoundsStartX + worldBoundsEndX) * 0.5f;

        return Mathf.Clamp(requestedX, minCenterX, maxCenterX);
    }

    void UpdateLockedBounds(float cameraX, float halfWidth)
    {
        LockedLeftBound = cameraX - halfWidth + 0.5f;
        LockedRightBound = cameraX + halfWidth - 0.5f;
    }

    public float GetCameraHalfWidth()
    {
        var cam = GetComponent<Camera>();
        if (cam != null)
            return cam.orthographicSize * cam.aspect;
        return 5f;
    }
}
