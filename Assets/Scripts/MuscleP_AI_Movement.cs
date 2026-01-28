using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class MuscleP_AI_Movement : MonoBehaviour
{
    [Header("目标")]
    public Transform player;

    [Header("参数")]
    public float speed = 1.2f;          // 移动速度
    public float stopDistance = 1.5f;   // 离玩家多远就站着不动

    public float minMoveTime = 0.8f;    // 最少走多久
    public float maxMoveTime = 2.0f;    // 最多走多久
    public float minWaitTime = 0.2f;    // 最少停多久
    public float maxWaitTime = 0.5f;    // 最多停多久
    
    [Header("距离控制")]
    public float minKeepDistance = 3f;  // 最小保持距离（太近就后退）
    public float maxKeepDistance = 8f;  // 最大保持距离（太远就靠近）
    
    [Header("移动参数")]
    public float minWanderDistance = 2f;  // 徘徊最小距离
    public float maxWanderDistance = 4f;  // 徘徊最大距离
    
    // [Header("世界边界")] - Y轴从 LevelManager 获取，X轴不限制
    // X轴不限制敌人移动范围，让敌人可以自由追踪玩家
    private float leftBound = -1000f;   // 不限制X轴
    private float rightBound = 1000f;   // 不限制X轴
    private float bottomBound;
    private float topBound;

    [Header("攻击参数")]
    [Range(0, 100)]
    public float attackDesire = 50f;   // 攻击欲望 (0=永不攻击, 100=每步都攻击)
    public float attackRange = 12f;     // 攻击距离（增加以便更远距离发起攻击）
    public float yAxisTolerance = 0.5f; // y轴对齐容差
    public float maxYAxisOffset = 0.5f;   // y轴最大偏移（超过此距离必须调整y轴）
    public float forceAttackTime = 2.5f; // 强制攻击时间（缩短以便更频繁攻击）
    public float chargeTime = 0.4f;     // 蓄力时间（稍微缩短）
    public float dashSpeed = 12f;       // 冲刺速度（增加以便更快冲击）
    public float playerBodyWidth = 2f;  // 玩家身位宽度（增加冲刺距离）
    public float postAttackDelay = 0.3f; // 攻击后延迟（缩短以便更快恢复）

    Rigidbody2D rb;
    Animator animator;
    bool isAttacking = false;           // 是否正在攻击（用于禁止转头）
    bool isDashing = false;             // 是否正在冲刺（用于碰撞检测）

    // AI 状态
    enum AIState { Wander, Idle, Approach, Attack }
    AIState lastState = AIState.Idle;
    float lastAttackTime;               // 上次攻击时间
    bool canIdle = true;                // 是否可以进入静止状态
    bool canAttack = true;              // 是否可以攻击（攻击后必须移动才能再次攻击）
    
    // 受击状态（由Enemy脚本设置）
    public bool isKnockedBack = false;
    
    // 公共属性供外部访问（用于碰撞检测）
    public bool IsAttacking => isDashing;
    
    // 入场相关
    private bool hasEntranceTarget = false;  // 是否有入场目标
    private Vector3 entranceTarget;           // 入场目标位置
    private bool isEntering = false;          // 是否正在入场

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        lastAttackTime = Time.time;
    }

    void Start()
    {
        // 从 LevelManager 获取Y轴边界，X轴不限制（让敌人可以自由追踪玩家）
        if (LevelManager.Instance != null)
        {
            // X轴不限制，保持默认的大范围值
            // leftBound 和 rightBound 保持 -1000/1000 的默认值
            bottomBound = LevelManager.Instance.BottomBound;
            topBound = LevelManager.Instance.TopBound;
        }
        else
        {
            Debug.LogWarning("场景中缺少 LevelManager，使用默认Y轴边界。");
            // 默认值，防止报错（X轴已在声明时设置为不限制）
            bottomBound = -5f;
            topBound = 5f;
        }
    }

    void OnEnable()
    {
        lastAttackTime = Time.time; // 初始化为当前时间，让AI先徘徊一段时间再攻击
        StartCoroutine(ThinkLoop());
    }
    
    /// <summary>
    /// 设置入场目标位置（由BattleWaveManager调用）
    /// </summary>
    public void SetEntranceTarget(Vector3 target)
    {
        entranceTarget = target;
        hasEntranceTarget = true;
        isEntering = true;
    }
    
    /// <summary>
    /// 检查敌人是否在屏幕内
    /// </summary>
    bool IsOnScreen()
    {
        Camera cam = Camera.main;
        if (cam == null) return true;
        
        float cameraHalfWidth = cam.orthographicSize * cam.aspect;
        float cameraX = cam.transform.position.x;
        float screenLeft = cameraX - cameraHalfWidth;
        float screenRight = cameraX + cameraHalfWidth;
        
        return transform.position.x > screenLeft - 1f && transform.position.x < screenRight + 1f;
    }

    IEnumerator ThinkLoop()
    {
        // 等待 player 被设置
        while (player == null)
        {
            yield return new WaitForSeconds(0.1f);
        }
        
        while (true)
        {
            // 受击期间暂停AI
            if (isKnockedBack)
            {
                yield return new WaitForSeconds(0.1f);
                continue;
            }
            
            // 如果正在入场，优先移动到入场目标位置
            if (isEntering && hasEntranceTarget)
            {
                yield return MoveToEntranceTarget();
                continue;
            }
            
            // 如果在屏幕外，主动向玩家靠近（不徘徊，不攻击）
            if (!IsOnScreen())
            {
                yield return ApproachPlayerFromOffscreen();
                continue;
            }
            
            float dist = Vector2.Distance(transform.position, player.position);
            float timeSinceAttack = Time.time - lastAttackTime;
            bool isYAligned = Mathf.Abs(transform.position.y - player.position.y) <= yAxisTolerance;
            bool inAttackRange = dist <= attackRange;

            AIState nextState;

            // 强制攻击逻辑：5秒未攻击则必须走近并攻击
            if (timeSinceAttack >= forceAttackTime)
            {
                // 需要先对齐y轴并走到攻击范围内
                if (!isYAligned || !inAttackRange)
                {
                    nextState = AIState.Approach;
                }
                else
                {
                    nextState = AIState.Attack;
                }
            }
            else
            {
                // 正常行为选择
                nextState = ChooseNextState(isYAligned, inAttackRange);
            }

            // 执行选中的状态
            switch (nextState)
            {
                case AIState.Wander:
                    yield return Wander();
                    canIdle = true; // 徘徊后可以静止
                    canAttack = true; // 移动后可以攻击
                    break;

                case AIState.Idle:
                    yield return Idle();
                    canIdle = false; // 静止后不能连续静止
                    canAttack = true; // 静止后也可以攻击
                    break;

                case AIState.Approach:
                    yield return Approach();
                    canIdle = true; // 走近后可以静止
                    canAttack = true; // 移动后可以攻击
                    break;

                case AIState.Attack:
                    yield return Attack();
                    lastAttackTime = Time.time;
                    canIdle = true; // 攻击后可以静止
                    canAttack = false; // 攻击后短暂不能连续攻击（需要先做其他动作）
                    break;
            }

            lastState = nextState;
        }
    }

    // 选择下一个状态
    AIState ChooseNextState(bool isYAligned, bool inAttackRange)
    {
        float dist = Vector2.Distance(transform.position, player.position);
        float yDiff = Mathf.Abs(transform.position.y - player.position.y);
        
        // Y轴偏移控制：如果Y轴偏移太大，必须优先调整Y轴
        if (yDiff > maxYAxisOffset)
        {
            return AIState.Approach; // 使用Approach来调整Y轴位置
        }

        // 如果可以攻击(y轴对齐且在范围内)，优先考虑攻击
        if (isYAligned && inAttackRange && canAttack)
        {
            // 攻击欲望为0时永不攻击，为100时必定攻击
            if (attackDesire <= 0)
            {
                // 不攻击，继续后面的逻辑
            }
            else if (attackDesire >= 100)
            {
                return AIState.Attack; // 必定攻击
            }
            else
            {
                // 基于攻击欲望计算攻击概率
                // 基础概率根据距离调整，然后乘以攻击欲望系数
                float baseChance;
                if (dist <= stopDistance)
                    baseChance = 0.8f;  // 非常近时基础80%
                else if (dist <= attackRange * 0.5f)
                    baseChance = 0.6f;  // 中等距离基础60%
                else
                    baseChance = 0.45f; // 较远距离基础45%
                
                // 攻击欲望影响：将0-100映射到0.0-2.0的系数
                // 50时系数为1.0（保持原有概率），100时系数为2.0（概率翻倍但不超过1）
                float desireMultiplier = attackDesire / 50f;
                float attackChance = Mathf.Clamp01(baseChance * desireMultiplier);
                
                if (Random.value < attackChance)
                    return AIState.Attack;
            }
        }

        // 距离控制：太近就必须后退（除非准备攻击）
        if (dist < minKeepDistance)
        {
            // 如果Y轴对齐且可以攻击，根据攻击欲望决定是否攻击
            if (isYAligned && inAttackRange && canAttack && attackDesire > 0)
            {
                // 攻击欲望越高，近距离时越倾向于攻击而不是后退
                float closeRangeAttackChance = attackDesire >= 100 ? 1f : (attackDesire / 100f) * 0.8f;
                if (Random.value < closeRangeAttackChance)
                {
                    return AIState.Attack;
                }
            }
            // 否则必须后退
            return AIState.Wander;
        }

        // 距离控制：太远就必须靠近
        if (dist > maxKeepDistance)
        {
            return AIState.Approach;
        }

        // 如果离玩家很近但Y轴不对齐，优先选择走近（对齐Y轴）
        if (dist <= stopDistance && !isYAligned)
        {
            return AIState.Approach;
        }

        // 上一个状态是攻击后，有概率短暂停顿
        if (lastState == AIState.Attack && Random.value < 0.4f)
        {
            return AIState.Idle;
        }

        // 静止不能连续触发
        if (!canIdle)
        {
            // 选择徘徊或走近
            return Random.value > 0.4f ? AIState.Wander : AIState.Approach;
        }

        // 正常概率分配：更积极的行为
        float r = Random.value;
        if (r < 0.20f)      // 20% 徘徊
            return AIState.Wander;
        else if (r < 0.30f) // 10% 静止（减少静止时间）
            return AIState.Idle;
        else                // 70% 走近玩家（准备攻击）
            return AIState.Approach;
    }

    // 徘徊行为：确定一个目标点，直线走过去
    IEnumerator Wander()
    {
        // 计算目标位置
        Vector2 targetPos = CalculateWanderTarget();
        
        // 朝目标点移动
        while (Vector2.Distance(transform.position, targetPos) > 0.1f)
        {
            // 受击时立即停止移动逻辑，但不清空速度（交由Enemy脚本控制击退）
            if (isKnockedBack)
            {
                // StopMovement(); // 不要清空速度！
                yield break;
            }

            Vector2 dir = (targetPos - (Vector2)transform.position).normalized;
            rb.linearVelocity = dir * speed;
            
            // 实时限制位置在边界内
            ClampPositionToBounds();
            
            UpdateAnimator();
            yield return null;
        }

        StopMovement();
    }
    
    // 计算徘徊目标点：绕着玩家走,如果太近就后退
    Vector2 CalculateWanderTarget()
    {
        Vector2 toPlayer = player.position - transform.position;
        float dist = toPlayer.magnitude;
        float yDiff = Mathf.Abs(transform.position.y - player.position.y);
        Vector2 targetPos;

        // 如果Y轴偏移太大，优先调整Y轴（移动到玩家Y轴附近）
        if (yDiff > maxYAxisOffset)
        {
            // 目标Y轴位置靠近玩家，但保持一定随机性
            float targetY = player.position.y + Random.Range(-maxYAxisOffset * 0.5f, maxYAxisOffset * 0.5f);
            targetY = Mathf.Clamp(targetY, bottomBound, topBound);
            
            // X轴方向：保持当前距离或稍微调整
            float xOffset = Random.Range(-minWanderDistance, minWanderDistance);
            targetPos = new Vector2(transform.position.x + xOffset, targetY);
        }
        // 如果距离小于最小保持距离，必须后退
        else if (dist < minKeepDistance)
        {
            // 向后退到安全距离，但限制Y轴偏移
            float retreatDist = Random.Range(minWanderDistance, maxWanderDistance);
            Vector2 retreatDir = -toPlayer.normalized;
            targetPos = (Vector2)transform.position + retreatDir * retreatDist;
            
            // 限制Y轴不要偏离太多
            float targetY = Mathf.Clamp(targetPos.y, player.position.y - maxYAxisOffset, player.position.y + maxYAxisOffset);
            targetPos.y = targetY;
        }
        // 如果距离大于最大保持距离，需要靠近一些
        else if (dist > maxKeepDistance)
        {
            // 朝玩家方向移动，但限制Y轴偏移
            float approachDist = Random.Range(minWanderDistance, maxWanderDistance);
            Vector2 approachDir = toPlayer.normalized;
            targetPos = (Vector2)transform.position + approachDir * approachDist;
            
            // 限制Y轴不要偏离太多
            float targetY = Mathf.Clamp(targetPos.y, player.position.y - maxYAxisOffset, player.position.y + maxYAxisOffset);
            targetPos.y = targetY;
        }
        // 在合适的距离范围内，绕圈走
        else if (dist < stopDistance * 1.5f)
        {
            // 稍微后退一些
            float retreatDist = Random.Range(minWanderDistance, maxWanderDistance);
            targetPos = (Vector2)transform.position + (-toPlayer.normalized * retreatDist);
            
            // 限制Y轴不要偏离太多
            float targetY = Mathf.Clamp(targetPos.y, player.position.y - maxYAxisOffset, player.position.y + maxYAxisOffset);
            targetPos.y = targetY;
        }
        else
        {
            // 绕圈走：朝向玩家方向的垂直方向
            Vector2 perpendicular = new Vector2(-toPlayer.y, toPlayer.x).normalized;
            // 随机左右绕圈
            if (Random.value > 0.5f)
                perpendicular = -perpendicular;
            
            // 混合一些朝向玩家的方向
            Vector2 dir = (perpendicular * 0.7f + toPlayer.normalized * 0.3f).normalized;
            float wanderDist = Random.Range(minWanderDistance, maxWanderDistance);
            targetPos = (Vector2)transform.position + dir * wanderDist;
            
            // 限制Y轴不要偏离太多
            float targetY = Mathf.Clamp(targetPos.y, player.position.y - maxYAxisOffset, player.position.y + maxYAxisOffset);
            targetPos.y = targetY;
        }

        // 限制目标点在边界内
        targetPos.x = Mathf.Clamp(targetPos.x, leftBound, rightBound);
        targetPos.y = Mathf.Clamp(targetPos.y, bottomBound, topBound);

        return targetPos;
    }
    
    /// <summary>
    /// 移动到入场目标位置
    /// </summary>
    IEnumerator MoveToEntranceTarget()
    {
        while (Vector2.Distance(transform.position, entranceTarget) > 0.5f)
        {
            if (isKnockedBack)
            {
                yield return null;
                continue;
            }
            
            Vector2 dir = (entranceTarget - transform.position).normalized;
            rb.linearVelocity = dir * speed * 1.5f; // 入场时速度稍快
            
            UpdateAnimator();
            yield return null;
        }
        
        // 入场完成
        isEntering = false;
        StopMovement();
    }
    
    /// <summary>
    /// 屏幕外时主动向玩家靠近
    /// </summary>
    IEnumerator ApproachPlayerFromOffscreen()
    {
        // 持续向玩家靠近，直到进入屏幕
        while (!IsOnScreen())
        {
            if (isKnockedBack)
            {
                yield return null;
                continue;
            }
            
            Vector2 dir = (player.position - transform.position).normalized;
            rb.linearVelocity = dir * speed * 1.2f; // 屏幕外时速度稍快
            
            // 限制Y轴在边界内
            ClampPositionToBounds();
            
            UpdateAnimator();
            yield return null;
        }
    }

    // 静止行为
    IEnumerator Idle()
    {
        StopMovement();
        yield return new WaitForSeconds(Random.Range(minWaitTime, maxWaitTime));
    }

    // 走近玩家：确定目标位置，直线走过去
    IEnumerator Approach()
    {
        // 计算目标位置：优先对齐y轴
        Vector2 targetPos = CalculateApproachTarget();
        
        // 朝目标点移动
        while (Vector2.Distance(transform.position, targetPos) > 0.1f)
        {
            // 受击时立即停止移动逻辑，但不清空速度（交由Enemy脚本控制击退）
            if (isKnockedBack)
            {
                // StopMovement(); // 不要清空速度！
                yield break;
            }

            Vector2 dir = (targetPos - (Vector2)transform.position).normalized;
            rb.linearVelocity = dir * speed;
            
            // 实时限制位置在边界内
            ClampPositionToBounds();
            
            UpdateAnimator();
            yield return null;
        }

        StopMovement();
    }
    
    // 计算走近目标点
    Vector2 CalculateApproachTarget()
    {
        float dist = Vector2.Distance(transform.position, player.position);
        float yDiff = Mathf.Abs(transform.position.y - player.position.y);
        Vector2 targetPos;
        
        // 如果Y轴偏移太大，优先调整Y轴
        if (yDiff > maxYAxisOffset)
        {
            // 主要目标是靠近玩家的Y轴
            float targetY = Mathf.Lerp(transform.position.y, player.position.y, 0.7f);
            targetY = Mathf.Clamp(targetY, bottomBound, topBound);
            
            // X轴方向：根据距离决定是靠近还是保持
            float xMove = 0f;
            if (dist > maxKeepDistance)
            {
                // 太远了，同时靠近
                float xDir = Mathf.Sign(player.position.x - transform.position.x);
                xMove = xDir * Random.Range(minWanderDistance, maxWanderDistance);
            }
            else if (dist < minKeepDistance)
            {
                // 太近了，同时后退
                float xDir = -Mathf.Sign(player.position.x - transform.position.x);
                xMove = xDir * Random.Range(minWanderDistance * 0.5f, minWanderDistance);
            }
            else
            {
                // 距离合适，X轴稍微调整
                xMove = Random.Range(-minWanderDistance * 0.5f, minWanderDistance * 0.5f);
            }
            
            targetPos = new Vector2(transform.position.x + xMove, targetY);
        }
        // 如果距离太远，直接朝玩家方向靠近
        else if (dist > maxKeepDistance)
        {
            Vector2 toPlayer = (Vector2)player.position - (Vector2)transform.position;
            float approachDist = Random.Range(minWanderDistance, maxWanderDistance);
            targetPos = (Vector2)transform.position + toPlayer.normalized * approachDist;
            
            // 限制Y轴偏移
            float targetY = Mathf.Clamp(targetPos.y, player.position.y - maxYAxisOffset, player.position.y + maxYAxisOffset);
            targetPos.y = targetY;
        }
        // 如果已经很近但Y轴不对齐
        else if (dist <= stopDistance && yDiff > yAxisTolerance)
        {
            // 随机选择：80%概率对齐Y轴准备攻击，20%概率拉开距离
            if (Random.value < 0.8f)
            {
                // 对齐Y轴，向玩家靠近准备攻击
                targetPos = new Vector2(transform.position.x, player.position.y);
                
                // 稍微朝X方向移动一点
                float xDir = Mathf.Sign(player.position.x - transform.position.x);
                targetPos.x += xDir * minWanderDistance * 0.3f;
            }
            else
            {
                // 拉开距离到最小保持距离
                Vector2 awayDir = ((Vector2)transform.position - (Vector2)player.position).normalized;
                float retreatDist = minKeepDistance + Random.Range(0f, 1f);
                targetPos = (Vector2)player.position + awayDir * retreatDist;
            }
        }
        // 如果已经很近且Y轴对齐
        else if (dist <= stopDistance && yDiff <= yAxisTolerance)
        {
            // 已经在攻击位置，稍微调整位置即可
            Vector2 perpendicular = new Vector2(-((Vector2)player.position - (Vector2)transform.position).y, 
                                               ((Vector2)player.position - (Vector2)transform.position).x).normalized;
            if (Random.value > 0.5f)
                perpendicular = -perpendicular;
            
            targetPos = (Vector2)transform.position + perpendicular * minWanderDistance;
            
            // 限制Y轴偏移
            float targetY = Mathf.Clamp(targetPos.y, player.position.y - maxYAxisOffset, player.position.y + maxYAxisOffset);
            targetPos.y = targetY;
        }
        // 正常走近逻辑
        else if (yDiff > yAxisTolerance)
        {
            // 需要对齐y轴，目标点在同一y轴上
            float approachDist = Random.Range(minWanderDistance, maxWanderDistance);
            targetPos = new Vector2(transform.position.x, player.position.y);
            
            // 同时稍微靠近x方向，但不要太近
            float xDir = Mathf.Sign(player.position.x - transform.position.x);
            float xMove = Mathf.Min(approachDist * 0.5f, dist - minKeepDistance);
            targetPos.x += xDir * xMove;
        }
        else
        {
            // y轴已对齐，直接朝玩家方向走一段距离
            Vector2 toPlayer = (Vector2)player.position - (Vector2)transform.position;
            float approachDist = Random.Range(minWanderDistance, maxWanderDistance);
            // 确保不会走得太近
            approachDist = Mathf.Min(approachDist, Mathf.Max(toPlayer.magnitude - minKeepDistance, minWanderDistance * 0.5f));
            targetPos = (Vector2)transform.position + toPlayer.normalized * approachDist;
        }

        // 限制目标点在边界内
        targetPos.x = Mathf.Clamp(targetPos.x, leftBound, rightBound);
        targetPos.y = Mathf.Clamp(targetPos.y, bottomBound, topBound);

        return targetPos;
    }

    // 攻击行为
    IEnumerator Attack()
    {
        isAttacking = true; // 开始攻击，禁止转头
        StopMovement();
        
        // 1. 蓄力阶段：面向玩家
        Vector3 scale = transform.localScale;
        if (player.position.x > transform.position.x)
            scale.x = Mathf.Abs(scale.x);  // 面向右侧
        else
            scale.x = -Mathf.Abs(scale.x); // 面向左侧
        transform.localScale = scale;
        
        Debug.Log("MuscleP 蓄力中...");
        // animator.SetTrigger("Charge"); // 可选：播放蓄力动画
        yield return new WaitForSeconds(chargeTime);
        
        // 2. 冲刺阶段：计算冲刺方向和距离
        Vector2 dashDirection = (player.position.x > transform.position.x) ? Vector2.right : Vector2.left;
        
        // 计算冲刺距离：到玩家的距离 + 2个身位
        float distanceToPlayer = Mathf.Abs(player.position.x - transform.position.x);
        float dashDistance = distanceToPlayer + (playerBodyWidth * 2);
        
        // 记录起始位置和固定的Y坐标
        Vector2 startPos = transform.position;
        float fixedY = startPos.y;
        Vector2 targetPos = new Vector2(startPos.x + dashDirection.x * dashDistance, fixedY);
        
        // 限制目标位置在边界内
        targetPos.x = Mathf.Clamp(targetPos.x, leftBound, rightBound);
        
        Debug.Log($"MuscleP 发起攻击冲刺！方向: {dashDirection}, 距离: {dashDistance}");
        // animator.SetTrigger("Dash"); // 可选：播放冲刺动画
        
        // 执行冲刺
        isDashing = true; // 标记为正在冲刺（用于碰撞检测）
        float dashedDistance = 0f;
        while (dashedDistance < dashDistance)
        {
            // 受击时立即停止冲刺
            if (isKnockedBack)
            {
                rb.linearVelocity = Vector2.zero;
                isDashing = false;
                yield break;
            }
            
            // 检查刚体类型，只有Kinematic时才能设置速度
            if (rb.bodyType == RigidbodyType2D.Kinematic)
            {
                // 设置冲刺速度（只在X轴上移动）
                rb.linearVelocity = new Vector2(dashDirection.x * dashSpeed, 0);
            }
            
            // 强制Y轴位置不变
            Vector2 currentPos = rb.position;
            currentPos.y = fixedY;
            
            // 检查是否碰到边界
            currentPos.x = Mathf.Clamp(currentPos.x, leftBound, rightBound);
            
            // 如果碰到边界就停止冲刺
            if (currentPos.x == leftBound || currentPos.x == rightBound)
            {
                rb.position = currentPos;
                rb.linearVelocity = Vector2.zero;
                Debug.Log("冲刺碰到边界，提前结束");
                break;
            }
            
            rb.position = currentPos;
            
            // 计算已冲刺距离
            dashedDistance = Mathf.Abs(currentPos.x - startPos.x);
            
            yield return null;
        }
        
        // 3. 攻击结束：停止移动
        isDashing = false; // 冲刺结束
        StopMovement();
        
        // 4. 攻击后延迟
        Debug.Log("MuscleP 攻击结束，暂停中...");
        yield return new WaitForSeconds(postAttackDelay);
        
        isAttacking = false; // 攻击结束，允许转头
    }

    // 停止移动
    void StopMovement()
    {
        rb.linearVelocity = Vector2.zero;
        UpdateAnimator();
    }

    // 更新动画参数
    void UpdateAnimator()
    {
        if (animator != null)
            animator.SetFloat("speed", rb.linearVelocity.magnitude);
    }
    
    // 限制位置在边界内
    void ClampPositionToBounds()
    {
        Vector2 pos = rb.position;
        pos.x = Mathf.Clamp(pos.x, leftBound, rightBound);
        pos.y = Mathf.Clamp(pos.y, bottomBound, topBound);
        
        // 如果位置被限制了，更新rigidbody位置并停止对应方向的速度
        if (pos != rb.position)
        {
            Vector2 velocity = rb.linearVelocity;
            
            // 如果碰到左右边界，停止x方向速度
            if (pos.x != rb.position.x)
                velocity.x = 0;
            
            // 如果碰到上下边界，停止y方向速度
            if (pos.y != rb.position.y)
                velocity.y = 0;
            
            rb.position = pos;
            rb.linearVelocity = velocity;
        }
    }

    // 左右翻转朝向
    void Update()
    {
        // 攻击过程中或受击时不允许转头
        if (isAttacking || isKnockedBack)
            return;
            
        // 只翻转 X 轴方向，保持原有的 scale 大小
        Vector3 scale = transform.localScale;
        if (player.position.x > transform.position.x)
            scale.x = -Mathf.Abs(scale.x);  // 面向右侧，保持负值
        else
            scale.x = Mathf.Abs(scale.x); // 面向左侧，保持正值
        transform.localScale = scale;
    }
}
