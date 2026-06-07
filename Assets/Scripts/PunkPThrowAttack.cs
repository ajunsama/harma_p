using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Enemy))]
public class PunkPThrowAttack : MonoBehaviour
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

    [Header("Y轴对齐")]
    public float yAxisTolerance = 0.5f;
    public float maxYAxisOffset = 0.8f;

    [Header("攻击参数")]
    public GameObject knifePrefab;
    public float throwCooldown = 1.2f;
    public float throwWindupTime = 0.25f;
    public float knifeSpawnOffsetX = 4f;
    public float knifeSpawnOffsetY = 1f;
    [Range(0, 100)]
    public float attackDesire = 85f;
    public float forceAttackTime = 3f;

    [Header("边界")]
    private float leftBound = -1000f;
    private float rightBound = 1000f;
    private float bottomBound;
    private float topBound;

    Rigidbody2D rb;
    Enemy enemy;

    public bool isKnockedBack = false;
    bool isAttacking = false;
    float lastAttackTime;

    enum AIState { Wander, Idle, ThrowKnife }
    AIState lastState = AIState.Idle;
    bool canIdle = true;

    bool hasEntranceTarget = false;
    Vector3 entranceTarget;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        enemy = GetComponent<Enemy>();
    }

    void Start()
    {
        lastAttackTime = Time.time;

        if (LevelManager.Instance != null)
        {
            bottomBound = LevelManager.Instance.BottomBound;
            topBound = LevelManager.Instance.TopBound;
        }
        else
        {
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
    }

    void FixedUpdate()
    {
        if (!isKnockedBack)
            ClampPositionToBounds();

        UpdateFacing();
    }

    void ClampPositionToBounds()
    {
        Vector2 pos = rb.position;
        pos.x = Mathf.Clamp(pos.x, leftBound, rightBound);
        pos.y = Mathf.Clamp(pos.y, bottomBound, topBound);

        if (pos != rb.position)
        {
            Vector2 vel = rb.linearVelocity;
            if (pos.x != rb.position.x) vel.x = 0;
            if (pos.y != rb.position.y) vel.y = 0;
            rb.position = pos;
            rb.linearVelocity = vel;
        }
    }

    void UpdateFacing()
    {
        if (isKnockedBack || isAttacking || player == null) return;
        Vector3 scale = transform.localScale;
        if (player.position.x > transform.position.x)
            scale.x = Mathf.Abs(scale.x);
        else
            scale.x = -Mathf.Abs(scale.x);
        transform.localScale = scale;
    }

    bool IsOnScreen()
    {
        Camera cam = Camera.main;
        if (cam == null) return true;
        float halfW = cam.orthographicSize * cam.aspect;
        float cx = cam.transform.position.x;
        float sl = cx - halfW;
        float sr = cx + halfW;
        return transform.position.x > sl - 1f && transform.position.x < sr + 1f;
    }

    IEnumerator ThinkLoop()
    {
        while (player == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) player = go.transform;
            yield return new WaitForSeconds(0.1f);
        }

        if (hasEntranceTarget)
        {
            yield return MoveToTarget(entranceTarget, speed * 1.5f);
            hasEntranceTarget = false;
        }

        while (true)
        {
            if (isKnockedBack)
            {
                yield return new WaitForSeconds(0.1f);
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
            float yDiff = Mathf.Abs(transform.position.y - player.position.y);

            AIState nextState;

            if (timeSinceAttack >= forceAttackTime)
            {
                nextState = AIState.ThrowKnife;
            }
            else
            {
                nextState = ChooseNextState(isYAligned, yDiff, dist);
            }

            switch (nextState)
            {
                case AIState.Wander:
                    yield return Wander();
                    canIdle = true;
                    break;
                case AIState.Idle:
                    yield return Idle();
                    canIdle = false;
                    break;
                case AIState.ThrowKnife:
                    yield return DoThrow();
                    lastAttackTime = Time.time;
                    canIdle = true;
                    break;
            }

            lastState = nextState;
        }
    }

    AIState ChooseNextState(bool isYAligned, float yDiff, float dist)
    {
        if (yDiff > maxYAxisOffset)
            return AIState.Wander;

        if (!canIdle)
            return AIState.Wander;

        if (isYAligned && dist <= maxKeepDistance)
        {
            float chance = attackDesire >= 100 ? 1f : (attackDesire / 100f);
            if (Random.value < chance)
                return AIState.ThrowKnife;
        }

        float r = Random.value;
        if (r < 0.75f) return AIState.Wander;
        if (r < 0.85f) return AIState.Idle;
        return AIState.ThrowKnife;
    }

    IEnumerator Wander()
    {
        Vector2 targetPos = CalculateWanderTarget();

        while (Vector2.Distance(transform.position, targetPos) > 0.2f)
        {
            if (isKnockedBack) yield break;

            Vector2 dir = (targetPos - (Vector2)transform.position).normalized;
            rb.linearVelocity = dir * speed;

            ClampPositionToBounds();
            yield return null;
        }

        StopMovement();
    }

    Vector2 CalculateWanderTarget()
    {
        Vector2 toPlayer = player.position - transform.position;
        float dist = toPlayer.magnitude;
        Vector2 targetPos;

        if (dist < minKeepDistance)
        {
            Vector2 retreatDir = -toPlayer.normalized;
            float retreatDist = Random.Range(minWanderDistance, maxWanderDistance);
            targetPos = (Vector2)transform.position + retreatDir * retreatDist;
        }
        else if (dist > maxKeepDistance)
        {
            float approachDist = Random.Range(minWanderDistance, maxWanderDistance);
            targetPos = (Vector2)transform.position + toPlayer.normalized * approachDist;
        }
        else
        {
            Vector2 perp = new Vector2(-toPlayer.y, toPlayer.x).normalized;
            if (Random.value > 0.5f) perp = -perp;
            Vector2 dir = (perp * 0.7f + toPlayer.normalized * 0.3f).normalized;
            float wanderDist = Random.Range(minWanderDistance, maxWanderDistance);
            targetPos = (Vector2)transform.position + dir * wanderDist;
        }

        float targetY = Mathf.Lerp(targetPos.y, player.position.y, 0.6f);
        targetY = Mathf.Clamp(targetY, bottomBound, topBound);
        targetPos.y = targetY;

        targetPos.x = Mathf.Clamp(targetPos.x, leftBound, rightBound);
        targetPos.y = Mathf.Clamp(targetPos.y, bottomBound, topBound);

        return targetPos;
    }

    IEnumerator ApproachPlayerFromOffscreen()
    {
        while (!IsOnScreen())
        {
            if (isKnockedBack) { yield return null; continue; }

            Vector2 dir = (player.position - transform.position).normalized;
            rb.linearVelocity = dir * speed * 1.2f;
            ClampPositionToBounds();
            yield return null;
        }
    }

    IEnumerator Idle()
    {
        StopMovement();
        yield return new WaitForSeconds(Random.Range(minIdleTime, maxIdleTime));
    }

    IEnumerator MoveToTarget(Vector3 target, float moveSpeed)
    {
        while (Vector2.Distance(transform.position, target) > 0.3f)
        {
            if (isKnockedBack) { yield return null; continue; }

            Vector2 dir = (target - transform.position).normalized;
            rb.linearVelocity = dir * moveSpeed;
            yield return null;
        }
        StopMovement();
    }

    IEnumerator DoThrow()
    {
        isAttacking = true;
        isKnockedBack = true;
        StopMovement();

        yield return new WaitForSeconds(throwWindupTime);

        isKnockedBack = false;

        if (knifePrefab == null || player == null || enemy.IsHit)
        {
            isAttacking = false;
            yield break;
        }

        float faceDir = player.position.x > transform.position.x ? 1f : -1f;
        Vector3 spawnPos = transform.position + new Vector3(faceDir * knifeSpawnOffsetX, knifeSpawnOffsetY, 0);
        Vector2 throwDir = new Vector2(faceDir, 0);
        GameObject knife = Instantiate(knifePrefab, spawnPos, Quaternion.identity);
        PunkPKnife knifeScript = knife.GetComponent<PunkPKnife>();
        if (knifeScript != null)
        {
            knifeScript.SetDirection(throwDir);
        }

        yield return new WaitForSeconds(0.3f);

        isAttacking = false;
    }

    void StopMovement()
    {
        rb.linearVelocity = Vector2.zero;
    }
}
