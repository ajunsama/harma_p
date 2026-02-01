using System.Collections;
using UnityEngine;
using Spine.Unity;

/// <summary>
/// 敌人冲刺攻击脚本
/// 行为流程：延迟 -> 向左走一段距离 -> 攻击前摇 -> 向左冲刺直到屏幕外
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class EnemyChargeAttack : MonoBehaviour
{
    [Header("触发设置")]
    [Tooltip("触发模式：延时触发或位置触发")]
    public TriggerMode triggerMode = TriggerMode.Delay;
    
    [Tooltip("开始行动前的延迟时间（秒）- 仅延时模式有效")]
    public float startDelay = 2f;
    
    [Tooltip("玩家Transform引用 - 仅位置模式有效")]
    public Transform player;
    
    [Tooltip("触发的X坐标 - 当玩家X坐标超过此值时触发")]
    public float triggerXPosition = 0f;
    
    [Tooltip("触发方向：玩家从左往右经过还是从右往左经过")]
    public TriggerDirection triggerDirection = TriggerDirection.LeftToRight;
    
    // 触发模式枚举
    public enum TriggerMode
    {
        Delay,          // 延时触发
        Position        // 位置触发
    }
    
    // 触发方向枚举
    public enum TriggerDirection
    {
        LeftToRight,    // 玩家从左往右经过触发点
        RightToLeft     // 玩家从右往左经过触发点
    }

    [Header("移动设置")]
    [Tooltip("向左走的距离")]
    public float walkDistance = 3f;
    [Tooltip("走路速度")]
    public float walkSpeed = 2f;

    [Header("攻击设置")]
    [Tooltip("攻击前摇时间（秒）")]
    public float attackWindupTime = 0.5f;
    [Tooltip("冲刺速度")]
    public float chargeSpeed = 15f;
    [Tooltip("冲刺超出屏幕的额外距离")]
    public float offscreenBuffer = 2f;
    [Tooltip("冲刺时固定动画的时间点（秒），设为-1则使用前摇结束时的帧")]
    public float chargeAnimationFreezeTime = -1f;

    [Header("Spine动画")]
    [SerializeField] private SkeletonAnimation skeletonAnimation;
    
    [SpineAnimation] public string idleAnimName = "idle";
    [SpineAnimation] public string walkAnimName = "walk";
    [SpineAnimation] public string attackAnimName = "attack";

    private Rigidbody2D rb;
    private Spine.TrackEntry currentTrack;
    private bool isExecuting = false;

    // 状态枚举
    public enum State
    {
        Waiting,        // 等待延迟
        Walking,        // 向左走
        Windup,         // 攻击前摇
        Charging,       // 冲刺中
        Finished        // 完成
    }
    private State currentState = State.Waiting;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void Start()
    {
        // 自动获取SkeletonAnimation组件
        if (skeletonAnimation == null)
        {
            skeletonAnimation = GetComponent<SkeletonAnimation>();
        }

        if (skeletonAnimation == null)
        {
            Debug.LogError("EnemyChargeAttack: 未找到SkeletonAnimation组件！");
            return;
        }

        // 根据触发模式启动
        if (triggerMode == TriggerMode.Position)
        {
            // 位置触发模式：等待玩家到达指定位置
            if (player == null)
            {
                // 尝试自动查找玩家
                GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
                if (playerObj != null)
                {
                    player = playerObj.transform;
                }
                else
                {
                    Debug.LogError("EnemyChargeAttack: 位置触发模式需要设置玩家引用！");
                    return;
                }
            }
            StartCoroutine(WaitForPlayerPosition());
        }
        else
        {
            // 延时触发模式：直接开始行为序列
            StartCoroutine(ExecuteBehaviorSequence());
        }
    }
    
    /// <summary>
    /// 等待玩家到达指定X坐标
    /// </summary>
    private IEnumerator WaitForPlayerPosition()
    {
        // 播放待机动画
        PlayAnimation(idleAnimName, true);
        Debug.Log($"EnemyChargeAttack: 等待玩家到达 X={triggerXPosition}...");
        
        // 根据触发方向等待玩家到达
        if (triggerDirection == TriggerDirection.LeftToRight)
        {
            // 玩家从左往右，等待玩家X坐标 >= 触发点
            while (player != null && player.position.x < triggerXPosition)
            {
                yield return null;
            }
        }
        else
        {
            // 玩家从右往左，等待玩家X坐标 <= 触发点
            while (player != null && player.position.x > triggerXPosition)
            {
                yield return null;
            }
        }
        
        Debug.Log("EnemyChargeAttack: 玩家到达触发位置，开始行动！");
        
        // 触发后执行行为序列（跳过延迟阶段）
        StartCoroutine(ExecuteBehaviorSequenceNoDelay());
    }
    
    /// <summary>
    /// 执行行为序列（无延迟版本，用于位置触发）
    /// </summary>
    private IEnumerator ExecuteBehaviorSequenceNoDelay()
    {
        isExecuting = true;
        currentState = State.Walking;

        // 1. 向左走阶段
        yield return WalkLeft();

        // 2. 攻击前摇阶段
        currentState = State.Windup;
        yield return AttackWindup();

        // 3. 冲刺阶段
        currentState = State.Charging;
        yield return ChargeLeft();

        // 4. 完成
        currentState = State.Finished;
        isExecuting = false;
        Debug.Log("EnemyChargeAttack: 行为序列完成");
    }

    /// <summary>
    /// 执行完整的行为序列
    /// </summary>
    private IEnumerator ExecuteBehaviorSequence()
    {
        isExecuting = true;

        // 1. 延迟阶段
        currentState = State.Waiting;
        PlayAnimation(idleAnimName, true);
        Debug.Log($"EnemyChargeAttack: 等待 {startDelay} 秒...");
        yield return new WaitForSeconds(startDelay);

        // 2. 向左走阶段
        currentState = State.Walking;
        yield return WalkLeft();

        // 3. 攻击前摇阶段
        currentState = State.Windup;
        yield return AttackWindup();

        // 4. 冲刺阶段
        currentState = State.Charging;
        yield return ChargeLeft();

        // 5. 完成
        currentState = State.Finished;
        isExecuting = false;
        Debug.Log("EnemyChargeAttack: 行为序列完成");
        
        // 可选：冲出屏幕后销毁或禁用对象
        // gameObject.SetActive(false);
        // Destroy(gameObject);
    }

    /// <summary>
    /// 向左走指定距离
    /// </summary>
    private IEnumerator WalkLeft()
    {
        Debug.Log($"EnemyChargeAttack: 开始向左走 {walkDistance} 单位");
        
        // 确保面向左边
        FaceLeft();
        
        // 播放走路动画
        PlayAnimation(walkAnimName, true);

        Vector2 startPos = transform.position;
        float targetX = startPos.x - walkDistance;
        
        while (transform.position.x > targetX)
        {
            // 向左移动
            rb.linearVelocity = Vector2.left * walkSpeed;
            yield return null;
        }

        // 停止移动
        rb.linearVelocity = Vector2.zero;
        Debug.Log("EnemyChargeAttack: 走路完成");
    }

    /// <summary>
    /// 攻击前摇
    /// </summary>
    private IEnumerator AttackWindup()
    {
        Debug.Log($"EnemyChargeAttack: 攻击前摇 {attackWindupTime} 秒");
        
        // 停止移动
        rb.linearVelocity = Vector2.zero;
        
        // 确保面向左边
        FaceLeft();
        
        // 播放攻击动画（前摇）
        ForcePlayAnimation(attackAnimName, false);
        
        yield return new WaitForSeconds(attackWindupTime);
        
        Debug.Log("EnemyChargeAttack: 前摇完成，准备冲刺");
    }

    /// <summary>
    /// 向左冲刺直到屏幕外
    /// </summary>
    private IEnumerator ChargeLeft()
    {
        Debug.Log($"EnemyChargeAttack: 开始冲刺，速度 {chargeSpeed}");
        
        // 播放攻击动画并固定在某一帧
        if (skeletonAnimation != null)
        {
            var track = skeletonAnimation.AnimationState.SetAnimation(0, attackAnimName, false);
            if (track != null)
            {
                // 设置动画时间到指定帧
                if (chargeAnimationFreezeTime >= 0)
                {
                    track.TrackTime = chargeAnimationFreezeTime;
                }
                else
                {
                    // 使用前摇时间作为固定帧
                    track.TrackTime = attackWindupTime;
                }
                // 将时间缩放设为0，冻结动画
                track.TimeScale = 0f;
            }
        }
        
        // 确保面向左边
        FaceLeft();
        
        // 计算屏幕左边界
        float screenLeftEdge = GetScreenLeftEdge();
        float targetX = screenLeftEdge - offscreenBuffer;
        
        // 记录固定的Y坐标（冲刺时保持Y轴不变）
        float fixedY = transform.position.y;
        
        while (transform.position.x > targetX)
        {
            // 向左冲刺
            rb.linearVelocity = new Vector2(-chargeSpeed, 0);
            
            // 保持Y轴位置不变
            Vector2 pos = rb.position;
            pos.y = fixedY;
            rb.position = pos;
            
            yield return null;
        }

        // 停止移动
        rb.linearVelocity = Vector2.zero;
        Debug.Log("EnemyChargeAttack: 冲刺完成，已到达屏幕外");
    }

    /// <summary>
    /// 获取屏幕左边界的世界坐标
    /// </summary>
    private float GetScreenLeftEdge()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("EnemyChargeAttack: 未找到主摄像机，使用默认值");
            return -20f;
        }

        float cameraHalfWidth = cam.orthographicSize * cam.aspect;
        float screenLeft = cam.transform.position.x - cameraHalfWidth;
        return screenLeft;
    }

    /// <summary>
    /// 让角色面向左边
    /// </summary>
    private void FaceLeft()
    {
        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x); // 正值表示面向左边（根据MuscleP_AI_Movement的逻辑）
        transform.localScale = scale;
    }

    /// <summary>
    /// 播放Spine动画
    /// </summary>
    private void PlayAnimation(string animName, bool loop, int trackIndex = 0)
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
    /// 强制播放Spine动画（不检查是否已在播放）
    /// </summary>
    private void ForcePlayAnimation(string animName, bool loop, int trackIndex = 0)
    {
        if (skeletonAnimation == null || string.IsNullOrEmpty(animName))
            return;

        currentTrack = skeletonAnimation.AnimationState.SetAnimation(trackIndex, animName, loop);
    }

    /// <summary>
    /// 获取当前状态（供外部查询）
    /// </summary>
    public State GetCurrentState()
    {
        return currentState;
    }

    /// <summary>
    /// 是否正在执行行为序列
    /// </summary>
    public bool IsExecuting()
    {
        return isExecuting;
    }

    /// <summary>
    /// 是否正在冲刺（用于碰撞检测）
    /// </summary>
    public bool IsCharging()
    {
        return currentState == State.Charging;
    }

    /// <summary>
    /// 手动触发行为序列（如果需要外部控制启动时机）
    /// </summary>
    public void TriggerBehavior()
    {
        if (!isExecuting)
        {
            StartCoroutine(ExecuteBehaviorSequence());
        }
    }

    /// <summary>
    /// 重置并重新开始行为序列
    /// </summary>
    public void ResetAndRestart()
    {
        StopAllCoroutines();
        rb.linearVelocity = Vector2.zero;
        currentState = State.Waiting;
        isExecuting = false;
        StartCoroutine(ExecuteBehaviorSequence());
    }
}
