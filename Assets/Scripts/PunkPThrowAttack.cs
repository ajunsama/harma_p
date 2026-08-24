using System.Collections;
using Harma.Combat;
using Spine.Unity;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Enemy))]
public class PunkPThrowAttack : MonoBehaviour, IPlayerTargetReceiver, IEnemyHitReactionReceiver
{
    [Header("目标")]
    public Transform player;

    [Header("移动参数")]
    public float speed = 1.8f;
    public float minKeepDistance = 4f;
    public float maxKeepDistance = 8f;
    public float minWanderDistance = 2f;
    public float maxWanderDistance = 5f;
    public float minIdleTime = 0.2f;
    public float maxIdleTime = 0.6f;

    [Header("近身退避")]
    public float closeRetreatDistance = 4f;
    [Range(0f, 100f)] public float closeRetreatPriority = 80f;
    [Min(0.01f)] public float retreatSpeedMultiplier = 1.35f;

    [Header("Y轴对齐")]
    public float yAxisTolerance = 0.5f;
    public float maxYAxisOffset = 0.8f;

    [Header("普通攻击")]
    public GameObject knifePrefab;
    [Min(0f)] public float throwCooldown = 0.8f;
    [Min(0f)] public float throwWindupTime = 0.3f;
    [Min(0f)] public float doubleThrowInterval = 0.25f;
    [Min(0f)] public float postThrowRecoveryTime = 0.3f;
    public float knifeSpawnOffsetX = 0.8f;
    public float knifeSpawnOffsetY = 2f;
    [Range(0f, 100f)] public float attackDesire = 100f;
    [Min(0f)] public float forceAttackTime = 1.2f;

    [Header("特殊攻击")]
    [Range(0f, 100f)] public float specialAttackChance = 40f;
    [Min(4f)] public float specialAttackCooldown = 4f;
    [Min(0.01f)] public float bodyWidth = 2f;
    [Min(0.01f)] public float specialRetreatBodyLengths = 4f;
    [Min(0.01f)] public float specialRetreatDuration = 0.6f;

    [Header("Spine动画")]
    [SerializeField] SkeletonAnimation skeletonAnimation;
    [SpineAnimation] public string idleAnimName = "idle";
    [SpineAnimation] public string walkAnimName = "walk";
    [SpineAnimation] public string attackAnimName = "attack";

    Rigidbody2D rb;
    Enemy enemy;
    Collider2D bodyCollider;
    Spine.TrackEntry currentTrack;

    float leftBound = -20f;
    float rightBound = 20f;
    float bottomBound = -5f;
    float topBound = 5f;
    float colliderHalfWidth;
    float lastAttackTime;
    float lastSpecialAttackTime;

    public bool isKnockedBack;
    bool isAttacking;
    bool canIdle = true;

    enum AIState { Wander, Idle, Retreat, ThrowKnife, SpecialAttack }

    bool hasEntranceTarget;
    Vector3 entranceTarget;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        enemy = GetComponent<Enemy>();
        bodyCollider = GetComponent<Collider2D>();

        if (skeletonAnimation == null)
            skeletonAnimation = GetComponentInChildren<SkeletonAnimation>();

        CacheLevelBounds();
    }

    void Start()
    {
        CacheLevelBounds();
    }

    void OnEnable()
    {
        lastAttackTime = Time.time;
        lastSpecialAttackTime = Time.time;
        StartCoroutine(ThinkLoop());
    }

    void OnDisable()
    {
        StopAllCoroutines();
        isAttacking = false;
        StopMovementImmediate();
    }

    public void SetPlayerTarget(Transform target)
    {
        player = target;
    }

    public void SetHitReactionActive(bool active)
    {
        isKnockedBack = active;
        if (active)
        {
            isAttacking = false;
            StopMovementImmediate();
        }
        else
        {
            UpdateAnimator();
        }
    }

    public void SetEntranceTarget(Vector3 target)
    {
        entranceTarget = target;
        hasEntranceTarget = true;
    }

    void FixedUpdate()
    {
        if (!isKnockedBack)
            ClampPositionToBounds();

        UpdateFacing();
    }

    void CacheLevelBounds()
    {
        leftBound = -20f;
        rightBound = 20f;
        bottomBound = -5f;
        topBound = 5f;

        if (LevelManager.Instance != null)
        {
            leftBound = LevelManager.Instance.LeftBound;
            rightBound = LevelManager.Instance.RightBound;
            bottomBound = LevelManager.Instance.BottomBound;
            topBound = LevelManager.Instance.TopBound;
        }

        colliderHalfWidth = bodyCollider != null ? bodyCollider.bounds.extents.x : 0f;
        leftBound += colliderHalfWidth;
        rightBound -= colliderHalfWidth;

        if (leftBound > rightBound)
        {
            float middle = (leftBound + rightBound) * 0.5f;
            leftBound = middle;
            rightBound = middle;
        }
    }

    void ClampPositionToBounds()
    {
        if (rb == null)
            return;

        Vector2 pos = rb.position;
        pos.x = Mathf.Clamp(pos.x, leftBound, rightBound);
        pos.y = Mathf.Clamp(pos.y, bottomBound, topBound);

        if (pos == rb.position)
            return;

        Vector2 velocity = rb.linearVelocity;
        if (!Mathf.Approximately(pos.x, rb.position.x)) velocity.x = 0f;
        if (!Mathf.Approximately(pos.y, rb.position.y)) velocity.y = 0f;
        rb.position = pos;
        rb.linearVelocity = velocity;
    }

    void UpdateFacing()
    {
        if (isKnockedBack || isAttacking || player == null)
            return;

        FaceDirection(GetHorizontalDirectionTowardPlayer());
    }

    void FaceDirection(float horizontalDirection)
    {
        Vector3 scale = transform.localScale;
        scale.x = horizontalDirection > 0f ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
        transform.localScale = scale;
    }

    float GetHorizontalDirectionTowardPlayer()
    {
        if (player == null)
            return transform.localScale.x < 0f ? 1f : -1f;

        float delta = player.position.x - transform.position.x;
        if (Mathf.Abs(delta) > 0.001f)
            return Mathf.Sign(delta);

        return transform.localScale.x < 0f ? 1f : -1f;
    }

    Vector2 GetAwayDirection()
    {
        Vector2 away = (Vector2)transform.position - (Vector2)player.position;
        if (away.sqrMagnitude > 0.0001f)
            return away.normalized;

        return new Vector2(-GetHorizontalDirectionTowardPlayer(), 0f);
    }

    bool IsOnScreen()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return true;

        float halfWidth = cam.orthographicSize * cam.aspect;
        float cameraX = cam.transform.position.x;
        return transform.position.x > cameraX - halfWidth - 1f
            && transform.position.x < cameraX + halfWidth + 1f;
    }

    IEnumerator ThinkLoop()
    {
        while (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
                player = playerObject.transform;
            yield return new WaitForSeconds(0.1f);
        }

        while (true)
        {
            if (IsActionInterrupted())
            {
                yield return new WaitForSeconds(0.1f);
                continue;
            }

            if (hasEntranceTarget)
            {
                yield return MoveToTarget(entranceTarget, speed * 1.5f);
                if (!IsActionInterrupted())
                    hasEntranceTarget = false;
                continue;
            }

            if (!IsOnScreen())
            {
                yield return ApproachPlayerFromOffscreen();
                continue;
            }

            float distance = Vector2.Distance(transform.position, player.position);
            float yDifference = Mathf.Abs(transform.position.y - player.position.y);
            bool isYAligned = yDifference <= yAxisTolerance;
            AIState nextState = ChooseNextState(isYAligned, yDifference, distance);

            switch (nextState)
            {
                case AIState.Retreat:
                    yield return Retreat();
                    canIdle = true;
                    break;
                case AIState.ThrowKnife:
                    yield return DoNormalVolley();
                    canIdle = true;
                    break;
                case AIState.SpecialAttack:
                    yield return DoSpecialAttack();
                    canIdle = true;
                    break;
                case AIState.Idle:
                    yield return Idle();
                    canIdle = false;
                    break;
                default:
                    yield return Wander();
                    canIdle = true;
                    break;
            }
        }
    }

    AIState ChooseNextState(bool isYAligned, float yDifference, float distance)
    {
        bool isClose = distance <= closeRetreatDistance;

        if (isClose && IsSpecialAttackReady() && RollPercentage(specialAttackChance))
            return AIState.SpecialAttack;

        if (isClose && RollPercentage(closeRetreatPriority))
            return AIState.Retreat;

        if (yDifference <= maxYAxisOffset && CanThrowAtPlayer(isYAligned, distance) && IsNormalAttackReady())
        {
            if (Time.time - lastAttackTime >= forceAttackTime || RollPercentage(attackDesire))
                return AIState.ThrowKnife;
        }

        if (!canIdle)
            return AIState.Wander;

        float roll = Random.value;
        if (roll < 0.75f) return AIState.Wander;
        if (roll < 0.85f) return AIState.Idle;
        return AIState.Wander;
    }

    bool RollPercentage(float percentage)
    {
        if (percentage <= 0f) return false;
        if (percentage >= 100f) return true;
        return Random.value < percentage / 100f;
    }

    bool IsNormalAttackReady()
    {
        return Time.time - lastAttackTime >= throwCooldown;
    }

    bool IsSpecialAttackReady()
    {
        return Time.time - lastSpecialAttackTime >= Mathf.Max(4f, specialAttackCooldown);
    }

    bool CanThrowAtPlayer(bool isYAligned, float distance)
    {
        return isYAligned && distance <= maxKeepDistance;
    }

    bool IsActionInterrupted()
    {
        return isKnockedBack || enemy == null || enemy.IsHit || !isActiveAndEnabled;
    }

    IEnumerator Retreat()
    {
        float targetDistance = Random.Range(minWanderDistance, maxWanderDistance);
        float travelled = 0f;
        Vector2 previousPosition = rb.position;

        while (travelled < targetDistance)
        {
            if (IsActionInterrupted())
                yield break;

            Vector2 direction = GetAwayDirection();
            rb.linearVelocity = direction * speed * retreatSpeedMultiplier;
            ClampPositionToBounds();
            UpdateAnimator();
            yield return null;

            float stepDistance = Vector2.Distance(previousPosition, rb.position);
            travelled += stepDistance;
            previousPosition = rb.position;

            if (stepDistance < 0.0001f && IsPushingAgainstBoundary(direction))
                break;
        }

        StopMovement();
    }

    bool IsPushingAgainstBoundary(Vector2 direction)
    {
        Vector2 position = rb.position;
        return direction.x < 0f && position.x <= leftBound + 0.001f
            || direction.x > 0f && position.x >= rightBound - 0.001f
            || direction.y < 0f && position.y <= bottomBound + 0.001f
            || direction.y > 0f && position.y >= topBound - 0.001f;
    }

    IEnumerator Wander()
    {
        Vector2 targetPosition = CalculateWanderTarget();

        while (Vector2.Distance(transform.position, targetPosition) > 0.2f)
        {
            if (IsActionInterrupted())
                yield break;

            Vector2 direction = (targetPosition - (Vector2)transform.position).normalized;
            rb.linearVelocity = direction * speed;
            ClampPositionToBounds();
            UpdateAnimator();
            yield return null;
        }

        StopMovement();
    }

    Vector2 CalculateWanderTarget()
    {
        Vector2 toPlayer = player.position - transform.position;
        float distance = toPlayer.magnitude;
        float yDifference = Mathf.Abs(transform.position.y - player.position.y);
        Vector2 targetPosition;

        if (yDifference > maxYAxisOffset || distance > maxKeepDistance)
        {
            float desiredDistance = Random.Range(minKeepDistance + 0.4f, maxKeepDistance - 0.4f);
            float side = transform.position.x <= player.position.x ? -1f : 1f;
            targetPosition = new Vector2(player.position.x + side * desiredDistance, player.position.y);
        }
        else if (distance < minKeepDistance)
        {
            targetPosition = (Vector2)transform.position
                + GetAwayDirection() * Random.Range(minWanderDistance, maxWanderDistance);
        }
        else
        {
            Vector2 perpendicular = new Vector2(-toPlayer.y, toPlayer.x).normalized;
            if (Random.value > 0.5f)
                perpendicular = -perpendicular;
            Vector2 direction = (perpendicular * 0.7f + toPlayer.normalized * 0.3f).normalized;
            targetPosition = (Vector2)transform.position
                + direction * Random.Range(minWanderDistance, maxWanderDistance);
        }

        targetPosition.y = Mathf.Lerp(targetPosition.y, player.position.y, 0.6f);
        targetPosition.x = Mathf.Clamp(targetPosition.x, leftBound, rightBound);
        targetPosition.y = Mathf.Clamp(targetPosition.y, bottomBound, topBound);
        return targetPosition;
    }

    IEnumerator ApproachPlayerFromOffscreen()
    {
        while (!IsOnScreen())
        {
            if (IsActionInterrupted())
                yield break;

            Vector2 direction = (player.position - transform.position).normalized;
            rb.linearVelocity = direction * speed * 1.2f;
            ClampPositionToBounds();
            UpdateAnimator();
            yield return null;
        }

        StopMovement();
    }

    IEnumerator Idle()
    {
        StopMovement();
        float waitTime = Random.Range(minIdleTime, maxIdleTime);
        float elapsed = 0f;
        while (elapsed < waitTime)
        {
            if (IsActionInterrupted())
                yield break;
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    IEnumerator MoveToTarget(Vector3 target, float moveSpeed)
    {
        while (Vector2.Distance(transform.position, target) > 0.3f)
        {
            if (IsActionInterrupted())
                yield break;

            Vector2 direction = (target - transform.position).normalized;
            rb.linearVelocity = direction * moveSpeed;
            ClampPositionToBounds();
            UpdateAnimator();
            yield return null;
        }

        StopMovement();
    }

    IEnumerator DoNormalVolley()
    {
        isAttacking = true;
        StopMovementImmediate();

        float lockedDirection = GetHorizontalDirectionTowardPlayer();
        FaceDirection(lockedDirection);
        ForcePlayAnimation(attackAnimName, false);

        yield return WaitInterruptibly(throwWindupTime);
        if (AbortAttackIfInterrupted() || knifePrefab == null)
            yield break;

        SpawnKnife(new Vector2(lockedDirection, 0f));

        yield return WaitInterruptibly(doubleThrowInterval);
        if (AbortAttackIfInterrupted())
            yield break;

        SpawnKnife(new Vector2(lockedDirection, 0f));

        yield return WaitForAttackRecovery();
        if (AbortAttackIfInterrupted())
            yield break;

        lastAttackTime = Time.time;
        isAttacking = false;
        UpdateAnimator();
    }

    IEnumerator DoSpecialAttack()
    {
        isAttacking = true;
        StopMovementImmediate();

        float retreatDirection = -GetHorizontalDirectionTowardPlayer();
        FaceDirection(-retreatDirection);
        ForcePlayAnimation(attackAnimName, false);

        Vector2 startPosition = rb.position;
        float retreatDistance = bodyWidth * specialRetreatBodyLengths;
        float targetX = Mathf.Clamp(startPosition.x + retreatDirection * retreatDistance, leftBound, rightBound);
        float duration = Mathf.Max(0.01f, specialRetreatDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (AbortAttackIfInterrupted())
                yield break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);
            rb.position = new Vector2(Mathf.Lerp(startPosition.x, targetX, eased), startPosition.y);
            rb.linearVelocity = Vector2.zero;
            yield return null;
        }

        rb.position = new Vector2(targetX, startPosition.y);
        StopMovementImmediate();

        float volleyDirection = GetHorizontalDirectionTowardPlayer();
        FaceDirection(volleyDirection);
        ForcePlayAnimation(attackAnimName, false);

        if (AbortAttackIfInterrupted() || knifePrefab == null)
            yield break;

        SpawnKnife(RotateDirection(new Vector2(volleyDirection, 0f), 0f));
        SpawnKnife(RotateDirection(new Vector2(volleyDirection, 0f), 30f));
        SpawnKnife(RotateDirection(new Vector2(volleyDirection, 0f), -30f));

        yield return WaitInterruptibly(postThrowRecoveryTime);
        if (AbortAttackIfInterrupted())
            yield break;

        lastSpecialAttackTime = Time.time;
        lastAttackTime = Time.time;
        isAttacking = false;
        UpdateAnimator();
    }

    static Vector2 RotateDirection(Vector2 direction, float degrees)
    {
        return (Quaternion.Euler(0f, 0f, degrees) * direction).normalized;
    }

    void SpawnKnife(Vector2 direction)
    {
        if (knifePrefab == null)
            return;

        float horizontalDirection = Mathf.Sign(direction.x);
        if (Mathf.Approximately(horizontalDirection, 0f))
            horizontalDirection = GetHorizontalDirectionTowardPlayer();

        Vector3 spawnPosition = transform.position
            + new Vector3(horizontalDirection * knifeSpawnOffsetX, knifeSpawnOffsetY, 0f);
        GameObject knife = Instantiate(knifePrefab, spawnPosition, Quaternion.identity);
        PunkPKnife knifeScript = knife.GetComponent<PunkPKnife>();
        if (knifeScript == null)
            return;

        knifeScript.SetDirection(direction);
        knifeScript.SetLaneY(transform.position.y);
    }

    IEnumerator WaitInterruptibly(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (IsActionInterrupted())
                yield break;
            StopMovementImmediate();
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    IEnumerator WaitForAttackRecovery()
    {
        float elapsed = 0f;
        while (elapsed < postThrowRecoveryTime || currentTrack != null && !currentTrack.IsComplete)
        {
            if (IsActionInterrupted())
                yield break;
            StopMovementImmediate();
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    bool AbortAttackIfInterrupted()
    {
        if (!IsActionInterrupted())
            return false;

        isAttacking = false;
        StopMovementImmediate();
        return true;
    }

    void StopMovement()
    {
        StopMovementImmediate();
        UpdateAnimator();
    }

    void StopMovementImmediate()
    {
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    void UpdateAnimator()
    {
        if (isAttacking)
            return;

        if (rb != null && rb.linearVelocity.magnitude > 0.01f)
            PlayAnimation(walkAnimName, true);
        else
            PlayAnimation(idleAnimName, true);
    }

    void PlayAnimation(string animationName, bool loop, int trackIndex = 0)
    {
        if (skeletonAnimation == null || string.IsNullOrEmpty(animationName) || !HasAnimation(animationName))
            return;

        if (currentTrack != null && currentTrack.Animation != null
            && currentTrack.Animation.Name == animationName && !currentTrack.IsComplete)
        {
            return;
        }

        currentTrack = skeletonAnimation.AnimationState.SetAnimation(trackIndex, animationName, loop);
    }

    void ForcePlayAnimation(string animationName, bool loop, int trackIndex = 0)
    {
        currentTrack = null;
        if (skeletonAnimation == null || string.IsNullOrEmpty(animationName) || !HasAnimation(animationName))
            return;

        currentTrack = skeletonAnimation.AnimationState.SetAnimation(trackIndex, animationName, loop);
    }

    bool HasAnimation(string animationName)
    {
        return skeletonAnimation != null
            && skeletonAnimation.Skeleton != null
            && skeletonAnimation.Skeleton.Data != null
            && skeletonAnimation.Skeleton.Data.FindAnimation(animationName) != null;
    }
}
