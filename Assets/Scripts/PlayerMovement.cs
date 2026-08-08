using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;   // 新 InputSystem 命名空间
using Spine.Unity;                // Spine 动画

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviour
{
    [Header("速度系数")]
    [SerializeField] float horizontalSpeed = 5f;   // 左右
    [SerializeField] float verticalSpeed   = 2.5f; // 上下

    [Header("跳跃")]
    [SerializeField] float jumpHeight = 3.5f;       // 跳跃最大高度
    [SerializeField] float jumpDuration = 0.9f;   // 跳跃持续时间
    [SerializeField] float hangTimeFactor = 0.3f; // 滞空因子，越大滞空越长 (0~0.5)
    
    [Header("反弹跳跃")]
    [SerializeField] float bounceHeight = 1.5f;    // 反弹跳跃高度
    [SerializeField] float bounceDuration = 0.4f; // 反弹跳跃持续时间

    [Header("过场准备")]
    [SerializeField, Min(0f), InspectorName("站立过渡时间（秒）")]
    float storyStandingBlendDuration = 0.15f;
    [SerializeField, Min(0f), InspectorName("站稳延时（秒）")]
    float storyStandingDelay = 0.25f;

    [Header("地面检测")]
    [SerializeField] GroundChecker groundChecker; // 拖影子进来

    // [Header("世界边界")] - 已移至 LevelManager
    // 运行时从 LevelManager 获取
    private float leftBound;
    private float rightBound;
    private float bottomBound;
    private float topBound;
    [SerializeField, Tooltip("用于计算角色左右边缘；未指定时自动使用玩家根对象上的 Collider2D")]
    private Collider2D boundaryCollider;
    private bool useLevelHorizontalBounds;
    private float levelHorizontalStartX;
    private float levelHorizontalEndX;

    [Header("Spine动画")]
    [SerializeField] SkeletonAnimation skeletonAnimation;   // 拖Spine对象进来
    
    // Spine动画名称
    [SpineAnimation] public string idleAnimName = "idle";
    [SpineAnimation] public string runAnimName = "run";
    [SpineAnimation] public string jumpAnimName = "jump";
    
    private Spine.TrackEntry currentTrack;
    
    [Header("受伤击退")]
    [SerializeField] float knockbackDistance = 4f;  // 击退距离
    [SerializeField] float knockbackDuration = 0.5f; // 击退持续时间
    [SerializeField] float knockbackJumpHeight = 2f; // 击退跳跃高度
    
    Rigidbody2D rb;
    Vector2 moveInput;      // 新 InputSystem 传进来的 (-1~1 , -1~1)
    Vector3 originalScale;          // 原始大小
    float lastFaceDir = 1;      // 1 右  -1 左,开局默认朝右
    
    // 跳跃状态
    bool isJumping = false;
    bool isBouncing = false;    // 是否正在反弹跳跃
    float jumpTimer = 0f;       // 跳跃计时器
    float baseY = 0f;           // 基准Y坐标(平地位置,不含跳跃偏移)
    float jumpOffset = 0f;      // 当前跳跃的Y轴偏移量(相对于基准位置)
    float currentJumpHeight;    // 当前跳跃高度（普通跳或反弹）
    float currentJumpDuration;  // 当前跳跃时长
    float bounceStartOffset = 0f; // 反弹跳跃的起始高度偏移
    float bounceGravity;        // 反弹跳跃的重力
    float bounceVelocity;       // 反弹跳跃的初始速度
    
    // 受伤击退状态
    bool isKnockedBack = false;
    float knockbackTimer = 0f;
    Vector2 knockbackStartPos;
    Vector2 knockbackTargetPos;
    
    // 下落攻击冷却
    private bool canJumpAttack = true;
    private float jumpAttackCooldown = 0.3f; // 冷却时间
    private float jumpAttackCooldownTimer = 0f;

    // Gameplay input may be locked by stories and level flows. The simulation
    // (jump landing / knockback) intentionally keeps running while locked.
    private readonly HashSet<int> controlLocks = new HashSet<int>();
    private static int nextControlLockToken = 1;
    private bool isScriptedMoving;
    private int performanceAnimationControlCount;
    private bool storyStandingPosePrepared;
    private float storyStandingPosePreparedAt;
    
    // 攻击状态引用
    // PlayerAttack playerAttack; // 已移除
    
    // 公共属性供外部访问
    public bool IsJumping => isJumping;
    public bool IsKnockedBack => isKnockedBack;
    public float BaseY => baseY;
    public bool IsSafeForStory =>
        !isJumping &&
        !isBouncing &&
        !isKnockedBack &&
        Mathf.Abs(jumpOffset) <= 0.001f;
    public bool IsReadyForStory =>
        IsSafeForStory &&
        storyStandingPosePrepared &&
        IsStandingAnimationActive &&
        Time.unscaledTime - storyStandingPosePreparedAt >=
            Mathf.Max(storyStandingDelay, storyStandingBlendDuration);
    public bool IsGameplayControlEnabled => controlLocks.Count == 0;

    private bool IsStandingAnimationActive =>
        skeletonAnimation == null ||
        string.IsNullOrEmpty(idleAnimName) ||
        IsSpineTrackPlaying(idleAnimName);
    
    // 新 InputSystem 自动生成的回调
    void OnMove(InputValue value)
    {
        if (!IsGameplayControlEnabled) return;
        bool wasMoving = moveInput.sqrMagnitude > 0.0001f;
        moveInput = value.Get<Vector2>();
        bool isMovingNow = moveInput.sqrMagnitude > 0.0001f;
        if (wasMoving != isMovingNow)
            GetComponent<PlayerGameplaySignalHub>()?.Publish(
                isMovingNow ? PlayerGameplaySignals.MoveStarted : PlayerGameplaySignals.MoveStopped,
                moveInput.x,
                gameObject);
    }

    public int AcquireControlLock(string owner)
    {
        int token = nextControlLockToken++;
        if (nextControlLockToken <= 0) nextControlLockToken = 1;
        controlLocks.Add(token);
        moveInput = Vector2.zero;
        return token;
    }

    public void ReleaseControlLock(int token)
    {
        if (token != 0) controlLocks.Remove(token);
        if (controlLocks.Count == 0)
            storyStandingPosePrepared = false;
    }

    public void PrepareStandingAnimationForStory()
    {
        if (!IsSafeForStory) return;
        moveInput = Vector2.zero;
        if (storyStandingPosePrepared) return;

        storyStandingPosePrepared = true;
        storyStandingPosePreparedAt = Time.unscaledTime;
        if (skeletonAnimation == null || string.IsNullOrEmpty(idleAnimName)) return;

        // Blend from the airborne pose into idle while gameplay time is still
        // running. The story readiness delay guarantees this mix completes
        // before StoryManager pauses scaled time.
        currentTrack = skeletonAnimation.AnimationState.SetAnimation(0, idleAnimName, true);
        if (currentTrack != null)
            currentTrack.MixDuration = storyStandingBlendDuration;
    }

    private bool IsSpineTrackPlaying(string animationName)
    {
        if (skeletonAnimation == null) return true;
        var actualTrack = skeletonAnimation.AnimationState?.GetCurrent(0);
        return actualTrack != null && actualTrack.Animation != null &&
               actualTrack.Animation.Name == animationName;
    }

    public bool MoveScriptedTowards(Vector2 target, float speed, float tolerance)
    {
        if (rb == null) return false;

        tolerance = Mathf.Max(0.001f, tolerance);
        speed = Mathf.Max(0.01f, speed);
        target.x = ClampHorizontalPosition(target.x);
        Vector2 current = new Vector2(rb.position.x, baseY);
        Vector2 next = Vector2.MoveTowards(current, target, speed * Time.deltaTime);
        Vector2 delta = next - current;

        if (Mathf.Abs(delta.x) > 0.001f)
            lastFaceDir = Mathf.Sign(delta.x);

        baseY = next.y;
        rb.position = new Vector2(next.x, next.y + jumpOffset);
        isScriptedMoving = true;
        if (!isJumping) PlayAnimation(runAnimName, true);
        ApplyFacing();

        if (Vector2.Distance(next, target) > tolerance) return false;

        baseY = target.y;
        rb.position = new Vector2(target.x, target.y + jumpOffset);
        StopScriptedMovement();
        return true;
    }

    public void StopScriptedMovement()
    {
        isScriptedMoving = false;
        if (!isJumping) PlayAnimation(idleAnimName, true);
    }

    public void SetPerformancePosition(Vector2 position)
    {
        position.x = ClampHorizontalPosition(position.x);
        baseY = position.y;
        jumpOffset = 0f;
        if (rb != null)
            rb.position = position;
        transform.position = new Vector3(position.x, position.y, transform.position.z);
    }

    public void SetPerformanceFacing(bool faceRight)
    {
        lastFaceDir = faceRight ? 1f : -1f;
        ApplyFacing();
    }

    public void SetLevelHorizontalBounds(float startX, float endX)
    {
        useLevelHorizontalBounds = true;
        levelHorizontalStartX = Mathf.Min(startX, endX);
        levelHorizontalEndX = Mathf.Max(startX, endX);
        ClampCurrentHorizontalPosition();
    }

    public void ClearLevelHorizontalBounds()
    {
        useLevelHorizontalBounds = false;
    }

    public void BeginPerformanceAnimationControl()
    {
        performanceAnimationControlCount++;
    }

    public void EndPerformanceAnimationControl()
    {
        performanceAnimationControlCount = Mathf.Max(0, performanceAnimationControlCount - 1);
    }

    public bool PlayPerformanceAnimation(string animName, bool loop, int trackIndex, float mixDuration)
    {
        if (skeletonAnimation == null || string.IsNullOrEmpty(animName))
            return false;

        currentTrack = skeletonAnimation.AnimationState.SetAnimation(
            Mathf.Max(0, trackIndex), animName, loop);
        if (currentTrack != null)
            currentTrack.MixDuration = Mathf.Max(0f, mixDuration);
        return currentTrack != null;
    }

    private void ApplyFacing()
    {
        transform.localScale = new Vector3(
            -lastFaceDir * Mathf.Abs(originalScale.x),
            originalScale.y,
            originalScale.z);
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        originalScale = transform.localScale;   // 开局拍一张"标准照"
        // playerAttack = GetComponent<PlayerAttack>(); // 已移除
    }

    void Start()
    {
        baseY = transform.position.y;

        // 从 LevelManager 获取边界
        if (LevelManager.Instance != null)
        {
            leftBound = LevelManager.Instance.LeftBound;
            rightBound = LevelManager.Instance.RightBound;
            bottomBound = LevelManager.Instance.BottomBound;
            topBound = LevelManager.Instance.TopBound;
        }
        else
        {
            Debug.LogError("场景中缺少 LevelManager！请创建一个空对象挂载 LevelManager 脚本。");
        }
    }

    void Update()                     // 只负责送参数
    {
        // 更新下落攻击冷却
        if (!canJumpAttack)
        {
            jumpAttackCooldownTimer -= Time.deltaTime;
            if (jumpAttackCooldownTimer <= 0f)
            {
                canJumpAttack = true;
            }
        }
        
        // 受伤击退中，不处理任何输入
        if (isKnockedBack)
        {
            // 击退中不播放移动动画，由PlayerHP控制hited动画
            return;
        }

        // PerformanceRunner owns the player's pose while a story performance
        // is active. Story input locking would otherwise force idle here every
        // frame and immediately overwrite the performance's move animation.
        if (performanceAnimationControlCount > 0)
        {
            if (!IsGameplayControlEnabled)
                moveInput = Vector2.zero;
            ApplyFacing();
            return;
        }

        if (!IsGameplayControlEnabled && !isScriptedMoving)
        {
            moveInput = Vector2.zero;
            if (isJumping)
                UpdateJump();
            else
                PlayAnimation(idleAnimName, true);
            ApplyFacing();
            return;
        }
        
        // 检查是否在攻击中，如果在攻击中则不处理移动和跳跃
        // bool canMove = playerAttack == null || !playerAttack.IsAttacking; // 已移除
        bool canMove = true;
        
        if (canMove)
        {
            // 根据移动状态播放对应动画
            if (!isJumping)
            {
                if (moveInput.magnitude > 0.01f)
                {
                    // 播放跑步动画
                    PlayAnimation(runAnimName, true);
                }
                else
                {
                    // 播放待机动画
                    PlayAnimation(idleAnimName, true);
                }
            }

            float h = moveInput.x;

            // 1. 有真实输入时才改朝向
            if (h > 0.01f)
                lastFaceDir = 1;
            else if (h < -0.01f)
                lastFaceDir = -1;

            // 判断跳跃 - 只有在地面上才能跳
            if (InputSystem.actions["Jump"].triggered && !isJumping)
            {
                Jump();
            }
        }
        else
        {
            // 攻击中，播放待机动画（或者由攻击脚本控制动画）
            if (!isJumping)
            {
                PlayAnimation(idleAnimName, true);
            }
        }

        // 2. 无论是否在走，都把 lastFaceDir 应用到缩放
        ApplyFacing();
        
        // 更新跳跃状态
        if (isJumping)
        {
            UpdateJump();
        }
    }

    void FixedUpdate()
    {
        // 受伤击退中，执行击退逻辑
        if (isKnockedBack)
        {
            UpdateKnockback();
            return;
        }

        if (!IsGameplayControlEnabled || isScriptedMoving)
        {
            // A control lock only disables gameplay input. Existing jump/bounce
            // simulation must still be applied to the rigidbody so the player
            // can visibly finish the trajectory before a story starts.
            if (!isScriptedMoving && isJumping)
            {
                Vector2 lockedPos = rb.position;
                lockedPos.x = ClampHorizontalPosition(lockedPos.x);
                lockedPos.y = baseY + jumpOffset;
                rb.MovePosition(lockedPos);
                if (groundChecker != null)
                    groundChecker.MovePosition(lockedPos, baseY - transform.localScale.y / 2 + 0.55f);
            }
            return;
        }
        
        // 检查是否在攻击中，如果在攻击中则不移动
        // bool canMove = playerAttack == null || !playerAttack.IsAttacking; // 已移除
        bool canMove = true;
        
        if (!canMove)
        {
            // 攻击中不移动，保持当前位置
            return;
        }
        
        // 处理上下左右移动，更新基准位置
        Vector2 velocity = new Vector2(
            moveInput.x * horizontalSpeed,
            moveInput.y * verticalSpeed);

        // 获取当前有效的左右边界 - 基于屏幕位置动态计算
        GetEffectiveHorizontalBounds(out float effectiveLeftBound, out float effectiveRightBound);
        
        // 如果有相机，使用屏幕边界
        // Camera mainCam = Camera.main;
        // if (mainCam != null)
        // {
        //     float cameraHalfWidth = mainCam.orthographicSize * mainCam.aspect;
        //     float cameraX = mainCam.transform.position.x;
            
        //     // 默认屏幕边界（留一点边距防止完全贴边）
        //     effectiveLeftBound = cameraX - cameraHalfWidth + 0.5f;
        //     effectiveRightBound = cameraX + cameraHalfWidth - 0.5f;
        // }
        
        // 战斗锁屏时限制玩家在相机视野内

        Vector2 pos = rb.position + velocity * Time.fixedDeltaTime;
        if (isJumping)
        {
            // 跳跃中：更新基准位置，限制在边界内
            baseY += velocity.y * Time.fixedDeltaTime;
            baseY = Mathf.Clamp(baseY, bottomBound, topBound);
            // 实际位置 = 基准位置 + 跳跃偏移
            pos = new Vector2(pos.x, baseY + jumpOffset);

            pos.x = Mathf.Clamp(pos.x, effectiveLeftBound, effectiveRightBound);
            pos.y = Mathf.Max(pos.y, bottomBound); // 只限制下边界
        }
        else
        {
            // 非跳跃：正常移动，左右基于屏幕，上下基于地图
            pos.x = Mathf.Clamp(pos.x, effectiveLeftBound, effectiveRightBound);
            pos.y = Mathf.Clamp(pos.y, bottomBound, topBound);

            // 更新基准位置
            baseY = pos.y;
        }
        rb.MovePosition(pos);
        groundChecker.MovePosition(pos, baseY - transform.localScale.y / 2 + 0.55f);
    }

    void GetEffectiveHorizontalBounds(out float effectiveLeftBound, out float effectiveRightBound)
    {
        float worldLeftBound = useLevelHorizontalBounds ? levelHorizontalStartX : leftBound;
        float worldRightBound = useLevelHorizontalBounds ? levelHorizontalEndX : rightBound;

        GetBoundaryPadding(out float leftPadding, out float rightPadding);
        effectiveLeftBound = worldLeftBound + leftPadding;
        effectiveRightBound = worldRightBound - rightPadding;

        // 范围比玩家碰撞体还窄时，固定在中点，避免 Mathf.Clamp 收到反向边界。
        if (effectiveLeftBound > effectiveRightBound)
        {
            float middle = (worldLeftBound + worldRightBound) * 0.5f;
            effectiveLeftBound = middle;
            effectiveRightBound = middle;
        }

        if (LevelCameraController.IsLocked)
        {
            float levelAllowedLeft = effectiveLeftBound;
            float levelAllowedRight = effectiveRightBound;
            effectiveLeftBound = Mathf.Max(effectiveLeftBound, LevelCameraController.LockedLeftBound);
            effectiveRightBound = Mathf.Min(effectiveRightBound, LevelCameraController.LockedRightBound);
            if (effectiveLeftBound > effectiveRightBound)
            {
                float lockedMiddle =
                    (LevelCameraController.LockedLeftBound + LevelCameraController.LockedRightBound) * 0.5f;
                float fallback = Mathf.Clamp(lockedMiddle, levelAllowedLeft, levelAllowedRight);
                effectiveLeftBound = fallback;
                effectiveRightBound = fallback;
            }
        }
    }

    float ClampHorizontalPosition(float positionX)
    {
        GetEffectiveHorizontalBounds(out float effectiveLeftBound, out float effectiveRightBound);
        if (effectiveLeftBound > effectiveRightBound)
            return (effectiveLeftBound + effectiveRightBound) * 0.5f;
        return Mathf.Clamp(positionX, effectiveLeftBound, effectiveRightBound);
    }

    void ClampCurrentHorizontalPosition()
    {
        if (rb == null) return;
        Vector2 position = rb.position;
        position.x = ClampHorizontalPosition(position.x);
        rb.position = position;
    }

    void GetBoundaryPadding(out float leftPadding, out float rightPadding)
    {
        if (boundaryCollider == null)
            boundaryCollider = GetComponent<Collider2D>();

        if (boundaryCollider == null)
        {
            leftPadding = 0f;
            rightPadding = 0f;
            return;
        }

        Bounds bounds = boundaryCollider.bounds;
        float playerCenterX = transform.position.x;
        leftPadding = Mathf.Max(0f, playerCenterX - bounds.min.x);
        rightPadding = Mathf.Max(0f, bounds.max.x - playerCenterX);
    }

    void Jump()
    {
        storyStandingPosePrepared = false;
        isJumping = true;
        isBouncing = false;
        // 播放跳跃动画（不循环）
        PlayAnimation(jumpAnimName, false);
        jumpTimer = 0f;
        baseY = rb.position.y; // 记录起跳时的平地位置
        jumpOffset = 0f;
        currentJumpHeight = jumpHeight;
        currentJumpDuration = jumpDuration;
        GetComponent<PlayerGameplaySignalHub>()?.Publish(PlayerGameplaySignals.JumpStarted, 1f, gameObject);
    }
    
    /// <summary>
    /// 触发反弹跳跃（踩踏攻击成功后调用）
    /// </summary>
    public void TriggerBounce()
    {
        storyStandingPosePrepared = false;
        // 保存当前的跳跃高度偏移，反弹将从这个高度开始
        bounceStartOffset = jumpOffset;
        
        isJumping = true;
        isBouncing = true;
        
        // 反弹动画暂时空着，等动画做好后再添加
        // ForcePlayAnimation(bounceAnimName, false);
        
        jumpTimer = 0f;
        
        // 重新计算反弹跳跃的物理参数，使其更自然
        // 使用 bounceHeight 和 bounceDuration 计算重力和初速度
        // g = 8 * h / t^2
        bounceGravity = 8f * bounceHeight / (bounceDuration * bounceDuration);
        // v0 = 4 * h / t
        bounceVelocity = 4f * bounceHeight / bounceDuration;
        
        // 计算这次反弹实际需要的时长（因为起始高度 bounceStartOffset 可能不为0）
        // t = (v0 + sqrt(v0^2 + 2gy0)) / g
        float discriminant = bounceVelocity * bounceVelocity + 2f * bounceGravity * bounceStartOffset;
        currentJumpDuration = (bounceVelocity + Mathf.Sqrt(discriminant)) / bounceGravity;
        
        // 反弹跳跃也可以再次攻击
        canJumpAttack = true;
        jumpAttackCooldownTimer = 0f;
    }
    
    void UpdateJump()
    {
        jumpTimer += Time.deltaTime;
        
        // 计算跳跃进度 (0 到 1)
        float progress = jumpTimer / currentJumpDuration;
        
        if (progress >= 1f)
        {
            // 跳跃结束，落地
            isJumping = false;
            isBouncing = false;
            // 落地后根据移动状态播放对应动画
            if (moveInput.magnitude > 0.01f)
            {
                PlayAnimation(runAnimName, true);
            }
            else
            {
                PlayAnimation(idleAnimName, true);
            }
            jumpTimer = 0f;
            jumpOffset = 0f;  // 重置偏移量
            bounceStartOffset = 0f; // 重置反弹起始高度
            
            // 重置下落攻击冷却，允许下次跳跃再次攻击
            canJumpAttack = true;
            
            // 强制修正位置到地面，防止下一帧 FixedUpdate 使用高空坐标
            Vector2 landedPosition = new Vector2(rb.position.x, baseY);
            rb.position = landedPosition;
            if (groundChecker != null)
                groundChecker.MovePosition(landedPosition, baseY - transform.localScale.y / 2 + 0.55f);
            GetComponent<PlayerGameplaySignalHub>()?.Publish(PlayerGameplaySignals.Landed, 1f, gameObject);
        }
        else
        {
            float adjustedProgress = progress;
            
            // 普通跳跃使用滞空优化
            if (!isBouncing && hangTimeFactor > 0)
            {
                // 使用平滑的S曲线调整进度，让中间部分（顶点附近）时间变长
                float midPoint = 0.5f;
                float distFromMid = Mathf.Abs(progress - midPoint);
                
                // 在顶点附近减缓进度变化
                if (distFromMid < hangTimeFactor)
                {
                    float hangProgress = distFromMid / hangTimeFactor;
                    float smoothFactor = Mathf.Sin(hangProgress * Mathf.PI * 0.5f);
                    float offset = (distFromMid - smoothFactor * hangTimeFactor) * 0.5f;
                    
                    if (progress < midPoint)
                        adjustedProgress = progress + offset;
                    else
                        adjustedProgress = progress - offset;
                }
            }
            
            // 计算跳跃高度
            if (isBouncing)
            {
                // 反弹跳跃：使用物理公式 y = y0 + v0*t - 0.5*g*t^2
                // 这样可以保证从任意高度踩踏都能获得自然的向上冲量
                jumpOffset = bounceStartOffset + bounceVelocity * jumpTimer - 0.5f * bounceGravity * jumpTimer * jumpTimer;
            }
            else
            {
                // 普通跳跃：标准抛物线
                jumpOffset = currentJumpHeight * 4f * adjustedProgress * (1f - adjustedProgress);
            }
        }
    }
    
    // 触发受伤击退（由 PlayerHP 调用，或者碰撞检测调用）
    // sourcePos: 伤害来源位置，如果为 null 则默认向后退
    public void TriggerHitKnockback(Vector2? sourcePos = null)
    {
        if (isKnockedBack)
            return; // 已经在击退中，不重复触发

        storyStandingPosePrepared = false;
            
        isKnockedBack = true;
        knockbackTimer = 0f;
        
        // 开始闪烁效果（由PlayerHP控制）
        PlayerHP hp = GetComponent<PlayerHP>();
        if (hp != null && !hp.IsInvincible)
        {
            // HP的无敌协程会处理闪烁
        }
        
        // 记录击退起始位置
        knockbackStartPos = rb.position;
        
        // 计算击退方向
        float knockbackDir;
        if (sourcePos.HasValue)
        {
            // 如果有伤害来源，往反方向飞
            // 如果玩家在来源左边 (x < source.x)，dir = -1 (向左)
            // 如果玩家在来源右边 (x > source.x)，dir = 1 (向右)
            knockbackDir = Mathf.Sign(transform.position.x - sourcePos.Value.x);
        }
        else
        {
            // 默认向后退
            knockbackDir = -lastFaceDir; 
        }

        knockbackTargetPos = knockbackStartPos + new Vector2(knockbackDir * knockbackDistance, 0);
        
        // 限制目标位置在边界内
        GetEffectiveHorizontalBounds(out float effectiveLeftBound, out float effectiveRightBound);
        knockbackTargetPos.x = Mathf.Clamp(knockbackTargetPos.x, effectiveLeftBound, effectiveRightBound);
        knockbackTargetPos.y = Mathf.Clamp(knockbackTargetPos.y, bottomBound, topBound);
        
        // 记录基准Y坐标 (重要：防止瞬移到 0)
        // 如果是在空中被打，baseY 应该保持原来的地面基准，而不是当前的空中Y
        // 但为了简化，我们假设击退是在当前平面上进行的，或者沿用当前的 baseY
        // 注意：FixedUpdate 里会用到这个 baseY
        // 如果当前是跳跃状态，baseY 已经是地面位置了，不需要改
        // 如果当前是走路状态，baseY 是当前 Y
        if (!isJumping)
        {
            baseY = knockbackStartPos.y;
        }
        
        Debug.Log($"触发击退：从 {knockbackStartPos} 到 {knockbackTargetPos}, BaseY: {baseY}");
    }
    
    // 更新击退逻辑
    void UpdateKnockback()
    {
        knockbackTimer += Time.fixedDeltaTime;
        float progress = knockbackTimer / knockbackDuration;
        
        if (progress >= 1f)
        {
            // 击退结束
            isKnockedBack = false;
            knockbackTimer = 0f;
            jumpOffset = 0f;
            
            // 设置最终位置
            Vector2 finalPos = knockbackTargetPos;
            rb.MovePosition(finalPos);
            groundChecker.MovePosition(finalPos, baseY - transform.localScale.y / 2 + 0.55f);
        }
        else
        {
            // 计算当前位置（线性插值）
            Vector2 currentPos = Vector2.Lerp(knockbackStartPos, knockbackTargetPos, progress);
            
            // 使用抛物线公式计算击退跳跃高度，比线性升降更自然
            jumpOffset = knockbackJumpHeight * 4 * progress * (1 - progress);
            
            // 应用跳跃偏移
            currentPos.y = baseY + jumpOffset;
            
            // 限制位置在边界内
            GetEffectiveHorizontalBounds(out float effectiveLeftBound, out float effectiveRightBound);
            currentPos.x = Mathf.Clamp(currentPos.x, effectiveLeftBound, effectiveRightBound);
            currentPos.y = Mathf.Max(currentPos.y, bottomBound);
            
            rb.MovePosition(currentPos);
            groundChecker.MovePosition(currentPos, baseY - transform.localScale.y / 2 + 0.55f);
        }
    }

    // 改用 OnTriggerEnter2D，因为 Kinematic 刚体之间不会触发 OnCollisionEnter2D
    void OnTriggerEnter2D(Collider2D other)
    {
        HandleCollision(other);
    }

    // 持续检测：解决“刚接触时深度不对，之后对齐了却没反应”的问题
    void OnTriggerStay2D(Collider2D other)
    {
        HandleCollision(other);
    }

    void HandleCollision(Collider2D other)
    {
        // 调试日志：打印所有触发对象 (为了避免 Stay 刷屏，可以注释掉或者加限制)
        // Debug.Log($"[PlayerMovement] 触发 Trigger! 对象: {other.gameObject.name}...");

        if (other.CompareTag("Enemy"))
        {
            // 0. 检查敌人是否处于受击状态
            Enemy hitEnemy = other.GetComponent<Enemy>();
            if (hitEnemy != null && hitEnemy.IsHit)
            {
                // 敌人正在受击（闪烁/击退中），不造成伤害，也不受到伤害
                return;
            }

            // 1. 深度检测 (Y轴检测)
            // 这是一个横版动作游戏，Y轴代表深度（前后位置）。只有深度相近才算真正接触。
            // 
            // 关键点：玩家的"深度"是 baseY（地面位置，不受跳跃影响）
            // 敌人的"深度"应该使用其 transform.position.y（敌人没有跳跃，position就是地面位置）
            // 
            // 之前的问题：使用 bounds.min.y（碰撞体底部）作为敌人深度，
            // 这会导致只有玩家接触到敌人碰撞体底部时才判定成功。
            
            // 获取敌人的深度：使用敌人的 transform.position.y
            // 这样无论敌人碰撞体怎么设置，深度比较都是基于角色的实际"脚底位置"
            float enemyDepth = other.transform.position.y;
            
            // 获取玩家的脚底位置（baseY 就是玩家的地面深度）
            float playerDepth = baseY;
            
            float depthDiff = Mathf.Abs(playerDepth - enemyDepth);
            float depthThreshold = 0.6f; // 深度容差：收紧到 0.6 单位，避免隔空判定

            // Debug.Log($"[PlayerMovement] 深度检测: PlayerBaseY={playerDepth:F2}, EnemyPosY={enemyDepth:F2}, Diff={depthDiff:F2}, Threshold={depthThreshold}");

            if (depthDiff > depthThreshold)
            {
                // Debug.Log("[PlayerMovement] 深度差距过大，忽略碰撞");
                return;
            }
            
            // 2. 水平距离检测 (X轴检测)
            // 碰撞体设置得很大（用于触发检测），但实际伤害判定需要更近的距离
            // 使用固定的"视觉接触距离"，而不是碰撞体尺寸
            Collider2D playerCol = GetComponent<Collider2D>();
            Collider2D enemyCol = other;
            
            // 计算两个角色中心点的水平距离
            float centerDistance = Mathf.Abs(transform.position.x - other.transform.position.x);
            
            // 设定一个合理的视觉接触距离（两个角色贴在一起时的中心距离）
            // 根据角色的实际视觉大小调整，通常角色宽度约 0.5~1.0
            float visualTouchDistance = 1.2f; // 两个角色中心距离小于 1.2 才算贴近
            
            // Debug.Log($"[PlayerMovement] 水平检测: 中心距离={centerDistance:F2}, 视觉接触距离={visualTouchDistance:F2}");
            
            if (centerDistance > visualTouchDistance)
            {
                // Debug.Log("[PlayerMovement] 水平距离过远，忽略碰撞");
                return;
            }
            
            // 踩头攻击判定：检测玩家脚底是否高于敌人上半身（放宽判定范围）
            // 玩家脚底位置：使用碰撞体底部，如果没有碰撞体则用transform位置
            float playerFeetY = playerCol != null ? playerCol.bounds.min.y : transform.position.y;
            
            // 敌人判定高度：改为使用敌人碰撞体的中心位置（即上半身都算踩中）
            // 这样斜着跳过来碰到胸部也能触发踩踏
            float enemyStompThreshold = enemyCol.bounds.center.y; 
            
            // 踩中判定：玩家脚底高于敌人判定高度
            bool isStompingHead = playerFeetY >= enemyStompThreshold;

            // 判断是否处于下落阶段
            bool isFalling = false;
            if (isBouncing)
            {
                // 反弹跳跃：顶点时间是 bounceDuration / 2
                isFalling = jumpTimer > (bounceDuration / 2f);
            }
            else
            {
                // 普通跳跃：进度超过 0.5 即为下落
                isFalling = (jumpTimer / currentJumpDuration) > 0.5f;
            }

            // 踩头攻击：只要玩家在跳跃状态且脚底高于敌人头部，并且处于下落阶段
            if (isJumping && isStompingHead && canJumpAttack && isFalling)
            {
                // 踩踏攻击成功
                if (hitEnemy != null)
                {
                    hitEnemy.TakeJumpDamage(transform.position);
                    
                    // 触发冷却
                    canJumpAttack = false;
                    jumpAttackCooldownTimer = jumpAttackCooldown;
                    
                    // 触发反弹跳跃
                    TriggerBounce();
                }
                // 踩踏成功后，直接返回，不执行受伤逻辑
                return;
            }
            

            // 玩家主动碰到敌人不算受伤
            // 玩家受伤逻辑已移至 EnemyAttackCollider，只有敌人主动冲击时才会造成伤害
        }
    }

    // System.Collections.IEnumerator KnockbackCoroutine(Vector2 sourcePos) // 已移除，改用 TriggerHitKnockback
    // {
    //    ...
    // }
    
    /// <summary>
    /// 播放Spine动画
    /// </summary>
    /// <param name="animName">动画名称</param>
    /// <param name="loop">是否循环播放</param>
    /// <param name="trackIndex">轨道索引，默认为0</param>
    public void PlayAnimation(string animName, bool loop, int trackIndex = 0)
    {
        if (skeletonAnimation == null || string.IsNullOrEmpty(animName))
            return;
            
        // 检查当前动画是否已经是目标动画（避免重复设置）
        if (currentTrack != null && currentTrack.Animation != null && 
            currentTrack.Animation.Name == animName && !currentTrack.IsComplete)
        {
            return;
        }
        
        currentTrack = skeletonAnimation.AnimationState.SetAnimation(trackIndex, animName, loop);
    }
    
    /// <summary>
    /// 强制播放Spine动画（不检查是否已在播放，用于需要重新播放的场景如反弹）
    /// </summary>
    public void ForcePlayAnimation(string animName, bool loop, int trackIndex = 0)
    {
        if (skeletonAnimation == null || string.IsNullOrEmpty(animName))
            return;
        
        currentTrack = skeletonAnimation.AnimationState.SetAnimation(trackIndex, animName, loop);
    }
    
    /// <summary>
    /// 获取SkeletonAnimation引用（供外部脚本使用）
    /// </summary>
    public SkeletonAnimation GetSkeletonAnimation()
    {
        return skeletonAnimation;
    }
}
