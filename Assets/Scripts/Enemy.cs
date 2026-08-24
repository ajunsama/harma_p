using Harma.Combat;
using UnityEngine;
using Spine.Unity;
using System.Collections;
using System.Collections.Generic;

public class Enemy : MonoBehaviour, IStompTarget
{
    [Header("----- 基础属性 -----")]
    public int maxHp = 5;

    [Header("----- 踩踏判定 -----")]
    [SerializeField, Tooltip("开启后使用下方手动高度；旧资源默认关闭并沿用重构前的碰撞体中心高度")]
    bool useCustomStompHeight;
    [SerializeField, Min(0f), Tooltip("独立跳跃高度轴上的手动可踩踏高度")]
    float stompHeight = 1f;

    [Header("----- Spine动画 -----")]
    [SerializeField] SkeletonAnimation skeletonAnimation;
    [SpineAnimation] public string hitAnimName = "hit";     // 受击动画名
    [SpineAnimation] public string deathAnimName = "die";   // 死亡动画名
    [SpineAnimation] public string idleAnimName = "idle";   // 待机动画名

    [Header("----- 闪烁参数 -----")]
    [SerializeField] float blinkDuration = 0.6f;
    [SerializeField] float blinkInterval = 0.1f;

    // 运行时变量
    int curHp;
    MeshRenderer meshRenderer;      // Spine使用MeshRenderer
    readonly List<IEnemyHitReactionReceiver> hitReactionReceivers =
        new List<IEnemyHitReactionReceiver>();
    Rigidbody2D rb;
    Collider2D[] colliders;
    float automaticStompHeight = 1f;
    bool isDead;

    public bool IsHit { get; private set; }
    public Transform StompTransform => transform;
    public float StompHeight => useCustomStompHeight
        ? Mathf.Max(0f, stompHeight)
        : automaticStompHeight;
    public bool CanReceiveStomp => !IsHit && !isDead;

    // 死亡事件（传递死亡的敌人实例）
    public static event System.Action<Enemy> OnEnemyDied;

    void Awake()
    {
        curHp = maxHp;
        meshRenderer = GetComponentInChildren<MeshRenderer>();
        foreach (var behaviour in GetComponents<MonoBehaviour>())
        {
            if (behaviour is IEnemyHitReactionReceiver receiver)
                hitReactionReceivers.Add(receiver);
        }
        rb = GetComponent<Rigidbody2D>();
        colliders = GetComponents<Collider2D>();
        CacheAutomaticStompHeight();

        // 如果没有手动指定，尝试自动获取 SkeletonAnimation
        if (skeletonAnimation == null)
            skeletonAnimation = GetComponentInChildren<SkeletonAnimation>();
    }

    void CacheAutomaticStompHeight()
    {
        float highestColliderCenter = 0f;
        foreach (Collider2D enemyCollider in colliders)
        {
            if (enemyCollider == null || !enemyCollider.enabled)
                continue;

            highestColliderCenter = Mathf.Max(
                highestColliderCenter,
                enemyCollider.bounds.center.y - transform.position.y);
        }

        if (highestColliderCenter > 0.001f)
            automaticStompHeight = highestColliderCenter;
    }

    /// <summary>
    /// 受到踩踏伤害
    /// </summary>
    public void TakeJumpDamage(Vector2 sourcePos)
    {
        if (IsHit || isDead) return; // 受击中不重复受伤

        curHp--;
        GameLog.Verbose($"[Enemy] 受到踩踏伤害! 剩余HP: {curHp}");

        bool willDie = curHp <= 0;
        if (willDie)
            isDead = true;

        StartCoroutine(HitReactionCoroutine(sourcePos, willDie));
    }

    /// <summary>
    /// 受击反应协程
    /// </summary>
    IEnumerator HitReactionCoroutine(Vector2 sourcePos, bool isDead)
    {
        IsHit = true;

        // 1. 暂停 AI
        SetAIKnockedBack(true);
        StopMovement();

        if (isDead)
            SetCollidersEnabled(false);

        // 2. 播放受击/死亡动画（不循环）
        Spine.TrackEntry hitTrack = isDead
            ? PlayFirstExistingAnimation(false, deathAnimName, hitAnimName)
            : PlaySpineAnimation(hitAnimName, false);

        // 3. 等待受击动画播放完毕
        if (hitTrack != null)
        {
            while (!hitTrack.IsComplete)
            {
                StopMovement();
                yield return null;
            }
        }

        // 4. 判断死亡或存活
        if (isDead)
        {
            StopMovement();
            GameLog.Verbose("[Enemy] 死亡");
            OnEnemyDied?.Invoke(this);
            Destroy(gameObject);
            yield break;
        }

        // 5. 存活：闪烁效果
        yield return StartCoroutine(BlinkCoroutine());

        // 6. 恢复正常
        SetAIKnockedBack(false);
        PlaySpineAnimation(idleAnimName, true);
        IsHit = false;
    }

    /// <summary>
    /// 闪烁协程
    /// </summary>
    IEnumerator BlinkCoroutine()
    {
        float elapsed = 0f;
        while (elapsed < blinkDuration)
        {
            if (meshRenderer != null)
                meshRenderer.enabled = !meshRenderer.enabled;

            yield return new WaitForSeconds(blinkInterval);
            elapsed += blinkInterval;
        }

        // 确保最终可见
        if (meshRenderer != null)
            meshRenderer.enabled = true;
    }

    /// <summary>
    /// 播放Spine动画
    /// </summary>
    Spine.TrackEntry PlaySpineAnimation(string animName, bool loop, int trackIndex = 0)
    {
        if (skeletonAnimation == null || string.IsNullOrEmpty(animName))
            return null;

        if (!HasSpineAnimation(animName))
        {
            Debug.LogWarning($"[Enemy] Spine动画不存在: {animName} ({name})");
            return null;
        }

        return skeletonAnimation.AnimationState.SetAnimation(trackIndex, animName, loop);
    }

    public void ReceiveStomp(Vector2 sourceGroundPosition)
    {
        TakeJumpDamage(sourceGroundPosition);
    }

    Spine.TrackEntry PlayFirstExistingAnimation(bool loop, params string[] animNames)
    {
        foreach (string animName in animNames)
        {
            Spine.TrackEntry track = PlaySpineAnimation(animName, loop);
            if (track != null)
                return track;
        }

        return null;
    }

    bool HasSpineAnimation(string animName)
    {
        return skeletonAnimation != null
            && skeletonAnimation.Skeleton != null
            && skeletonAnimation.Skeleton.Data != null
            && skeletonAnimation.Skeleton.Data.FindAnimation(animName) != null;
    }

    void StopMovement()
    {
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    void SetCollidersEnabled(bool enabled)
    {
        if (colliders == null)
            return;

        foreach (Collider2D col in colliders)
        {
            if (col != null)
                col.enabled = enabled;
        }
    }

    /// <summary>
    /// 设置AI击退状态
    /// </summary>
    void SetAIKnockedBack(bool value)
    {
        foreach (var receiver in hitReactionReceivers)
            receiver.SetHitReactionActive(value);
    }
}
