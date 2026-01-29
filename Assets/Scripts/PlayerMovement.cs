using System;
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

    [Header("地面检测")]
    [SerializeField] GroundChecker groundChecker; // 拖影子进来

    // [Header("世界边界")] - 已移至 LevelManager
    // 运行时从 LevelManager 获取
    private float leftBound;
    private float rightBound;
    private float bottomBound;
    private float topBound;
    
    // 战斗区域边界（由 BattleWaveManager 提供）
    private float battleLeftBound;
    private float battleRightBound;
    private bool useBattleBounds = false;

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
    
    // 攻击状态引用
    // PlayerAttack playerAttack; // 已移除
    
    // 公共属性供外部访问
    public bool IsJumping => isJumping;
    public bool IsKnockedBack => isKnockedBack;
    public float BaseY => baseY;
    
    // 新 InputSystem 自动生成的回调
    void OnMove(InputValue value) => moveInput = value.Get<Vector2>();

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
        transform.localScale = new Vector3(
            -lastFaceDir * Mathf.Abs(originalScale.x),
            originalScale.y,
            originalScale.z);
        
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
        float effectiveLeftBound = leftBound;
        float effectiveRightBound = rightBound;
        
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
        
        // [修改] 不再使用战斗区域限制玩家，只使用相机视野限制
        /*
        // 如果在战斗中，使用战斗区域边界（可能比屏幕更小）
        if (BattleWaveManager.Instance != null && BattleWaveManager.Instance.IsInBattle)
        {
            effectiveLeftBound = Mathf.Max(effectiveLeftBound, BattleWaveManager.Instance.BattleLeftBound);
            effectiveRightBound = Mathf.Min(effectiveRightBound, BattleWaveManager.Instance.BattleRightBound);
        }
        */

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

    void Jump()
    {
        isJumping = true;
        isBouncing = false;
        // 播放跳跃动画（不循环）
        PlayAnimation(jumpAnimName, false);
        jumpTimer = 0f;
        baseY = rb.position.y; // 记录起跳时的平地位置
        jumpOffset = 0f;
        currentJumpHeight = jumpHeight;
        currentJumpDuration = jumpDuration;
    }
    
    /// <summary>
    /// 触发反弹跳跃（踩踏攻击成功后调用）
    /// </summary>
    public void TriggerBounce()
    {
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
            rb.position = new Vector2(rb.position.x, baseY);
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
        knockbackTargetPos.x = Mathf.Clamp(knockbackTargetPos.x, leftBound, rightBound);
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
            currentPos.x = Mathf.Clamp(currentPos.x, leftBound, rightBound);
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
