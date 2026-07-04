using System.Collections;
using UnityEngine;
using Spine.Unity;

[RequireComponent(typeof(Rigidbody2D))]
public class FatP_AI_Movement : MonoBehaviour
{
    [Header("目标")]
    public Transform player;

    [Header("参数")]
    public float speed = 0.8f;
    public float stopDistance = 2f;
    public float startDelay = 0f;

    public float minMoveTime = 0.8f;
    public float maxMoveTime = 2.0f;
    public float minWaitTime = 0.2f;
    public float maxWaitTime = 0.5f;

    [Header("距离控制")]
    public float minKeepDistance = 4f;
    public float maxKeepDistance = 6f;

    [Header("移动参数")]
    public float minWanderDistance = 1.5f;
    public float maxWanderDistance = 3f;

    private float leftBound = -1000f;
    private float rightBound = 1000f;
    private float bottomBound;
    private float topBound;

    [Header("攻击参数")]
    [Range(0, 100)]
    public float attackDesire = 20f;
    public float attackRange = 6f;
    public float yAxisTolerance = 0.6f;
    public float maxYAxisOffset = 0.6f;
    public float forceAttackTime = 5f;
    public float chargeTime = 0.7f;
    public float dashSpeed = 10f;
    public float playerBodyWidth = 2f;
    public float postAttackDelay = 0.5f;

    [Header("Spine动画")]
    [SerializeField] SkeletonAnimation skeletonAnimation;

    [SpineAnimation] public string idleAnimName = "idle";
    [SpineAnimation] public string walkAnimName = "walk";
    [SpineAnimation] public string attackAnimName = "attack";

    private Spine.TrackEntry currentTrack;

    Rigidbody2D rb;
    bool isAttacking = false;
    bool isDashing = false;

    enum AIState { Wander, Idle, Approach, Attack }
    AIState lastState = AIState.Idle;
    float lastAttackTime;
    bool canIdle = true;
    bool canAttack = true;

    public bool isKnockedBack = false;

    public bool IsAttacking => isDashing;

    public void OnHit()
    {
        isKnockedBack = true;
        isAttacking = false;
        isDashing = false;
        rb.linearVelocity = Vector2.zero;
    }

    private bool hasEntranceTarget = false;
    private Vector3 entranceTarget;
    private bool isEntering = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        lastAttackTime = Time.time;

        if (skeletonAnimation == null)
            skeletonAnimation = GetComponentInChildren<SkeletonAnimation>();
    }

    void Start()
    {
        if (LevelManager.Instance != null)
        {
            bottomBound = LevelManager.Instance.BottomBound;
            topBound = LevelManager.Instance.TopBound;
        }
        else
        {
            Debug.LogWarning("场景中缺少 LevelManager，使用默认Y轴边界。");
            bottomBound = -5f;
            topBound = 5f;
        }
    }

    void OnEnable()
    {
        lastAttackTime = Time.time;
        StartCoroutine(ThinkLoop());
    }

    public void SetEntranceTarget(Vector3 target)
    {
        entranceTarget = target;
        hasEntranceTarget = true;
        isEntering = true;
    }

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
        while (player == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) player = go.transform;
            yield return new WaitForSeconds(0.1f);
        }

        if (startDelay > 0f)
        {
            PlayAnimation(idleAnimName, true);
            yield return new WaitForSeconds(startDelay);
        }

        while (true)
        {
            if (isKnockedBack)
            {
                yield return new WaitForSeconds(0.1f);
                continue;
            }

            if (isEntering && hasEntranceTarget)
            {
                yield return MoveToEntranceTarget();
                continue;
            }

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

            if (timeSinceAttack >= forceAttackTime)
            {
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
                nextState = ChooseNextState(isYAligned, inAttackRange);
            }

            switch (nextState)
            {
                case AIState.Wander:
                    yield return Wander();
                    canIdle = true;
                    canAttack = true;
                    break;

                case AIState.Idle:
                    yield return Idle();
                    canIdle = false;
                    canAttack = true;
                    break;

                case AIState.Approach:
                    yield return Approach();
                    canIdle = true;
                    canAttack = true;
                    break;

                case AIState.Attack:
                    yield return Attack();
                    lastAttackTime = Time.time;
                    canIdle = true;
                    canAttack = false;
                    break;
            }

            lastState = nextState;
        }
    }

    AIState ChooseNextState(bool isYAligned, bool inAttackRange)
    {
        float dist = Vector2.Distance(transform.position, player.position);
        float yDiff = Mathf.Abs(transform.position.y - player.position.y);

        if (yDiff > maxYAxisOffset)
        {
            return AIState.Approach;
        }

        if (isYAligned && inAttackRange && canAttack)
        {
            if (attackDesire <= 0)
            {
            }
            else if (attackDesire >= 100)
            {
                return AIState.Attack;
            }
            else
            {
                float baseChance;
                if (dist <= stopDistance)
                    baseChance = 0.8f;
                else if (dist <= attackRange * 0.5f)
                    baseChance = 0.6f;
                else
                    baseChance = 0.45f;

                float desireMultiplier = attackDesire / 50f;
                float attackChance = Mathf.Clamp01(baseChance * desireMultiplier);

                if (Random.value < attackChance)
                    return AIState.Attack;
            }
        }

        if (dist < minKeepDistance)
        {
            if (isYAligned && inAttackRange && canAttack && attackDesire > 0)
            {
                float closeRangeAttackChance = attackDesire >= 100 ? 1f : (attackDesire / 100f) * 0.8f;
                if (Random.value < closeRangeAttackChance)
                {
                    return AIState.Attack;
                }
            }
            return AIState.Wander;
        }

        if (dist > maxKeepDistance)
        {
            return AIState.Approach;
        }

        if (dist <= stopDistance && !isYAligned)
        {
            return AIState.Approach;
        }

        if (lastState == AIState.Attack && Random.value < 0.4f)
        {
            return AIState.Idle;
        }

        if (!canIdle)
        {
            return Random.value > 0.4f ? AIState.Wander : AIState.Approach;
        }

        float r = Random.value;
        if (r < 0.20f)
            return AIState.Wander;
        else if (r < 0.30f)
            return AIState.Idle;
        else
            return AIState.Approach;
    }

    IEnumerator Wander()
    {
        Vector2 targetPos = CalculateWanderTarget();

        while (Vector2.Distance(transform.position, targetPos) > 0.1f)
        {
            if (isKnockedBack)
            {
                yield break;
            }

            Vector2 dir = (targetPos - (Vector2)transform.position).normalized;
            rb.linearVelocity = dir * speed;

            ClampPositionToBounds();

            UpdateAnimator();
            yield return null;
        }

        StopMovement();
    }

    Vector2 CalculateWanderTarget()
    {
        Vector2 toPlayer = player.position - transform.position;
        float dist = toPlayer.magnitude;
        float yDiff = Mathf.Abs(transform.position.y - player.position.y);
        Vector2 targetPos;

        if (yDiff > maxYAxisOffset)
        {
            float targetY = player.position.y + Random.Range(-maxYAxisOffset * 0.5f, maxYAxisOffset * 0.5f);
            targetY = Mathf.Clamp(targetY, bottomBound, topBound);

            float xOffset = Random.Range(-minWanderDistance, minWanderDistance);
            targetPos = new Vector2(transform.position.x + xOffset, targetY);
        }
        else if (dist < minKeepDistance)
        {
            float retreatDist = Random.Range(minWanderDistance, maxWanderDistance);
            Vector2 retreatDir = -toPlayer.normalized;
            targetPos = (Vector2)transform.position + retreatDir * retreatDist;

            float targetY = Mathf.Clamp(targetPos.y, player.position.y - maxYAxisOffset, player.position.y + maxYAxisOffset);
            targetPos.y = targetY;
        }
        else if (dist > maxKeepDistance)
        {
            float approachDist = Random.Range(minWanderDistance, maxWanderDistance);
            Vector2 approachDir = toPlayer.normalized;
            targetPos = (Vector2)transform.position + approachDir * approachDist;

            float targetY = Mathf.Clamp(targetPos.y, player.position.y - maxYAxisOffset, player.position.y + maxYAxisOffset);
            targetPos.y = targetY;
        }
        else if (dist < stopDistance * 1.5f)
        {
            float retreatDist = Random.Range(minWanderDistance, maxWanderDistance);
            targetPos = (Vector2)transform.position + (-toPlayer.normalized * retreatDist);

            float targetY = Mathf.Clamp(targetPos.y, player.position.y - maxYAxisOffset, player.position.y + maxYAxisOffset);
            targetPos.y = targetY;
        }
        else
        {
            Vector2 perpendicular = new Vector2(-toPlayer.y, toPlayer.x).normalized;
            if (Random.value > 0.5f)
                perpendicular = -perpendicular;

            Vector2 dir = (perpendicular * 0.7f + toPlayer.normalized * 0.3f).normalized;
            float wanderDist = Random.Range(minWanderDistance, maxWanderDistance);
            targetPos = (Vector2)transform.position + dir * wanderDist;

            float targetY = Mathf.Clamp(targetPos.y, player.position.y - maxYAxisOffset, player.position.y + maxYAxisOffset);
            targetPos.y = targetY;
        }

        targetPos.x = Mathf.Clamp(targetPos.x, leftBound, rightBound);
        targetPos.y = Mathf.Clamp(targetPos.y, bottomBound, topBound);

        return targetPos;
    }

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
            rb.linearVelocity = dir * speed * 1.5f;

            UpdateAnimator();
            yield return null;
        }

        isEntering = false;
        StopMovement();
    }

    IEnumerator ApproachPlayerFromOffscreen()
    {
        while (!IsOnScreen())
        {
            if (isKnockedBack)
            {
                yield return null;
                continue;
            }

            Vector2 dir = (player.position - transform.position).normalized;
            rb.linearVelocity = dir * speed * 1.2f;

            ClampPositionToBounds();

            UpdateAnimator();
            yield return null;
        }
    }

    IEnumerator Idle()
    {
        StopMovement();
        yield return new WaitForSeconds(Random.Range(minWaitTime, maxWaitTime));
    }

    IEnumerator Approach()
    {
        Vector2 targetPos = CalculateApproachTarget();

        while (Vector2.Distance(transform.position, targetPos) > 0.1f)
        {
            if (isKnockedBack)
            {
                yield break;
            }

            Vector2 dir = (targetPos - (Vector2)transform.position).normalized;
            rb.linearVelocity = dir * speed;

            ClampPositionToBounds();

            UpdateAnimator();
            yield return null;
        }

        StopMovement();
    }

    Vector2 CalculateApproachTarget()
    {
        float dist = Vector2.Distance(transform.position, player.position);
        float yDiff = Mathf.Abs(transform.position.y - player.position.y);
        Vector2 targetPos;

        if (yDiff > maxYAxisOffset)
        {
            float targetY = Mathf.Lerp(transform.position.y, player.position.y, 0.7f);
            targetY = Mathf.Clamp(targetY, bottomBound, topBound);

            float xMove = 0f;
            if (dist > maxKeepDistance)
            {
                float xDir = Mathf.Sign(player.position.x - transform.position.x);
                xMove = xDir * Random.Range(minWanderDistance, maxWanderDistance);
            }
            else if (dist < minKeepDistance)
            {
                float xDir = -Mathf.Sign(player.position.x - transform.position.x);
                xMove = xDir * Random.Range(minWanderDistance * 0.5f, minWanderDistance);
            }
            else
            {
                xMove = Random.Range(-minWanderDistance * 0.5f, minWanderDistance * 0.5f);
            }

            targetPos = new Vector2(transform.position.x + xMove, targetY);
        }
        else if (dist > maxKeepDistance)
        {
            Vector2 toPlayer = (Vector2)player.position - (Vector2)transform.position;
            float approachDist = Random.Range(minWanderDistance, maxWanderDistance);
            targetPos = (Vector2)transform.position + toPlayer.normalized * approachDist;

            float targetY = Mathf.Clamp(targetPos.y, player.position.y - maxYAxisOffset, player.position.y + maxYAxisOffset);
            targetPos.y = targetY;
        }
        else if (dist <= stopDistance && yDiff > yAxisTolerance)
        {
            if (Random.value < 0.8f)
            {
                targetPos = new Vector2(transform.position.x, player.position.y);

                float xDir = Mathf.Sign(player.position.x - transform.position.x);
                targetPos.x += xDir * minWanderDistance * 0.3f;
            }
            else
            {
                Vector2 awayDir = ((Vector2)transform.position - (Vector2)player.position).normalized;
                float retreatDist = minKeepDistance + Random.Range(0f, 1f);
                targetPos = (Vector2)player.position + awayDir * retreatDist;
            }
        }
        else if (dist <= stopDistance && yDiff <= yAxisTolerance)
        {
            Vector2 perpendicular = new Vector2(-((Vector2)player.position - (Vector2)transform.position).y,
                                               ((Vector2)player.position - (Vector2)transform.position).x).normalized;
            if (Random.value > 0.5f)
                perpendicular = -perpendicular;

            targetPos = (Vector2)transform.position + perpendicular * minWanderDistance;

            float targetY = Mathf.Clamp(targetPos.y, player.position.y - maxYAxisOffset, player.position.y + maxYAxisOffset);
            targetPos.y = targetY;
        }
        else if (yDiff > yAxisTolerance)
        {
            float approachDist = Random.Range(minWanderDistance, maxWanderDistance);
            targetPos = new Vector2(transform.position.x, player.position.y);

            float xDir = Mathf.Sign(player.position.x - transform.position.x);
            float xMove = Mathf.Min(approachDist * 0.5f, dist - minKeepDistance);
            targetPos.x += xDir * xMove;
        }
        else
        {
            Vector2 toPlayer = (Vector2)player.position - (Vector2)transform.position;
            float approachDist = Random.Range(minWanderDistance, maxWanderDistance);
            approachDist = Mathf.Min(approachDist, Mathf.Max(toPlayer.magnitude - minKeepDistance, minWanderDistance * 0.5f));
            targetPos = (Vector2)transform.position + toPlayer.normalized * approachDist;
        }

        targetPos.x = Mathf.Clamp(targetPos.x, leftBound, rightBound);
        targetPos.y = Mathf.Clamp(targetPos.y, bottomBound, topBound);

        return targetPos;
    }

    IEnumerator Attack()
    {
        isAttacking = true;
        StopMovement();

        Vector3 scale = transform.localScale;
        if (player.position.x > transform.position.x)
            scale.x = -Mathf.Abs(scale.x);
        else
            scale.x = Mathf.Abs(scale.x);
        transform.localScale = scale;

        Debug.Log("FatP 蓄力中...");
        ForcePlayAnimation(attackAnimName, false);
        yield return new WaitForSeconds(chargeTime);

        Vector2 dashDirection = (player.position.x > transform.position.x) ? Vector2.right : Vector2.left;

        float distanceToPlayer = Mathf.Abs(player.position.x - transform.position.x);
        float dashDistance = distanceToPlayer + (playerBodyWidth * 2f);

        Vector2 startPos = transform.position;
        float fixedY = startPos.y;
        Vector2 targetPos = new Vector2(startPos.x + dashDirection.x * dashDistance, fixedY);

        targetPos.x = Mathf.Clamp(targetPos.x, leftBound, rightBound);

        Debug.Log($"FatP 发起滚动攻击！方向: {dashDirection}, 距离: {dashDistance}");

        isDashing = true;
        float dashedDistance = 0f;
        while (dashedDistance < dashDistance)
        {
            if (isKnockedBack)
            {
                rb.linearVelocity = Vector2.zero;
                isDashing = false;
                yield break;
            }

            if (rb.bodyType == RigidbodyType2D.Kinematic)
            {
                rb.linearVelocity = new Vector2(dashDirection.x * dashSpeed, 0);
            }

            Vector2 currentPos = rb.position;
            currentPos.y = fixedY;

            currentPos.x = Mathf.Clamp(currentPos.x, leftBound, rightBound);

            if (currentPos.x == leftBound || currentPos.x == rightBound)
            {
                rb.position = currentPos;
                rb.linearVelocity = Vector2.zero;
                Debug.Log("滚动攻击碰到边界，提前结束");
                break;
            }

            rb.position = currentPos;

            dashedDistance = Mathf.Abs(currentPos.x - startPos.x);

            yield return null;
        }

        isDashing = false;
        StopMovement();

        Debug.Log("FatP 攻击结束，暂停中...");
        yield return new WaitForSeconds(postAttackDelay);

        isAttacking = false;
    }

    void StopMovement()
    {
        rb.linearVelocity = Vector2.zero;
        UpdateAnimator();
    }

    void UpdateAnimator()
    {
        if (rb.linearVelocity.magnitude > 0.01f)
        {
            PlayAnimation(walkAnimName, true);
        }
        else
        {
            PlayAnimation(idleAnimName, true);
        }
    }

    void PlayAnimation(string animName, bool loop, int trackIndex = 0)
    {
        if (skeletonAnimation == null || string.IsNullOrEmpty(animName))
            return;

        if (!HasAnimation(animName))
            return;

        if (currentTrack != null && currentTrack.Animation != null &&
            currentTrack.Animation.Name == animName && !currentTrack.IsComplete)
        {
            return;
        }

        currentTrack = skeletonAnimation.AnimationState.SetAnimation(trackIndex, animName, loop);
    }

    void ForcePlayAnimation(string animName, bool loop, int trackIndex = 0)
    {
        if (skeletonAnimation == null || string.IsNullOrEmpty(animName))
            return;

        if (!HasAnimation(animName))
            return;

        currentTrack = skeletonAnimation.AnimationState.SetAnimation(trackIndex, animName, loop);
    }

    bool HasAnimation(string animName)
    {
        return skeletonAnimation.Skeleton != null
            && skeletonAnimation.Skeleton.Data != null
            && skeletonAnimation.Skeleton.Data.FindAnimation(animName) != null;
    }

    void StopMovementForEnemy()
    {
        isAttacking = false;
        isDashing = false;
        if (rb != null)
            rb.linearVelocity = Vector2.zero;
    }

    void ClampPositionToBounds()
    {
        Vector2 pos = rb.position;
        pos.x = Mathf.Clamp(pos.x, leftBound, rightBound);
        pos.y = Mathf.Clamp(pos.y, bottomBound, topBound);

        if (pos != rb.position)
        {
            Vector2 velocity = rb.linearVelocity;

            if (pos.x != rb.position.x)
                velocity.x = 0;

            if (pos.y != rb.position.y)
                velocity.y = 0;

            rb.position = pos;
            rb.linearVelocity = velocity;
        }
    }

    void Update()
    {
        if (isAttacking || isKnockedBack)
            return;

        Vector3 scale = transform.localScale;
        if (player.position.x > transform.position.x)
            scale.x = -Mathf.Abs(scale.x);
        else
            scale.x = Mathf.Abs(scale.x);
        transform.localScale = scale;
    }
}
