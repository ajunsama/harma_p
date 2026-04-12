using UnityEngine;
using Spine.Unity;
using System.Collections;

public class Enemy : MonoBehaviour
{
    [Header("----- 基础属性 -----")]
    public int maxHp = 5;

    [Header("----- Spine动画 -----")]
    [SerializeField] SkeletonAnimation skeletonAnimation;
    [SpineAnimation] public string hitAnimName = "hit";     // 受击动画名
    [SpineAnimation] public string idleAnimName = "idle";   // 待机动画名

    [Header("----- 闪烁参数 -----")]
    [SerializeField] float blinkDuration = 0.6f;
    [SerializeField] float blinkInterval = 0.1f;

    // 运行时变量
    int curHp;
    MeshRenderer meshRenderer;      // Spine使用MeshRenderer
    EnemySimpleAI2D ai;
    MuscleP_AI_Movement muscleAi;

    public bool IsHit { get; private set; }

    // 死亡事件（传递死亡的敌人实例）
    public static event System.Action<Enemy> OnEnemyDied;

    void Awake()
    {
        curHp = maxHp;
        meshRenderer = GetComponentInChildren<MeshRenderer>();
        ai = GetComponent<EnemySimpleAI2D>();
        muscleAi = GetComponent<MuscleP_AI_Movement>();

        // 如果没有手动指定，尝试自动获取 SkeletonAnimation
        if (skeletonAnimation == null)
            skeletonAnimation = GetComponentInChildren<SkeletonAnimation>();
    }

    /// <summary>
    /// 受到踩踏伤害
    /// </summary>
    public void TakeJumpDamage(Vector2 sourcePos)
    {
        if (IsHit) return; // 受击中不重复受伤

        curHp--;
        Debug.Log($"[Enemy] 受到踩踏伤害! 剩余HP: {curHp}");

        bool isDead = curHp <= 0;
        StartCoroutine(HitReactionCoroutine(sourcePos, isDead));
    }

    /// <summary>
    /// 受击反应协程
    /// </summary>
    IEnumerator HitReactionCoroutine(Vector2 sourcePos, bool isDead)
    {
        IsHit = true;

        // 1. 暂停 AI
        SetAIKnockedBack(true);

        // 2. 播放受击动画（不循环）
        Spine.TrackEntry hitTrack = PlaySpineAnimation(hitAnimName, false);

        // 3. 等待受击动画播放完毕
        if (hitTrack != null)
        {
            while (!hitTrack.IsComplete)
                yield return null;
        }

        // 4. 判断死亡或存活
        if (isDead)
        {
            Debug.Log("[Enemy] 死亡");
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

        return skeletonAnimation.AnimationState.SetAnimation(trackIndex, animName, loop);
    }

    /// <summary>
    /// 设置AI击退状态
    /// </summary>
    void SetAIKnockedBack(bool value)
    {
        if (ai != null) ai.isKnockedBack = value;
        if (muscleAi != null) muscleAi.isKnockedBack = value;
    }
}