using Harma.Combat;
using UnityEngine;

/// <summary>
/// 挂载在敌人身上，检测敌人攻击时是否碰撞到玩家
/// 需要敌人有 Collider2D 并设置为 Trigger
/// </summary>
public class EnemyAttackCollider : MonoBehaviour
{
    [SerializeField] private int damageAmount = 1; // 伤害值
    [SerializeField] private LayerMask playerLayer; // 玩家所在层
    
    private IEnemyAttackState attackState;
    
    void Awake()
    {
        foreach (var behaviour in GetComponents<MonoBehaviour>())
        {
            if (behaviour is IEnemyAttackState state)
            {
                attackState = state;
                break;
            }
        }
    }
    
    void OnTriggerEnter2D(Collider2D other)
    {
        // 如果自身处于受击状态，则无法造成伤害
        Enemy enemy = GetComponent<Enemy>();
        if (enemy != null && enemy.IsHit)
            return;

        // 检查是否碰撞到玩家（支持 Layer 或 Tag 检测）
        bool isPlayerLayer = ((1 << other.gameObject.layer) & playerLayer) != 0;
        bool isPlayerTag = other.CompareTag("Player");
        bool isPlayer = isPlayerLayer || isPlayerTag;
        
        if (isPlayer)
        {
            // 检查敌人是否处于攻击状态（正在冲刺）
            if (IsAttackActive())
            {
                // 增加Y轴判定：只有在Y轴高度相近时才算击中
                // 这是一个2D横版过关游戏，虽然Collider重叠了，但如果Y轴（深度）差距太大，不应该算作击中
                PlayerMovement playerMovement = other.GetComponentInParent<PlayerMovement>();
                float playerGroundY = playerMovement != null
                    ? playerMovement.GroundY
                    : other.transform.position.y;
                float yDiff = Mathf.Abs(transform.position.y - playerGroundY);
                if (yDiff > GetYAxisTolerance())
                    return;

                // 对玩家造成伤害
                PlayerHP playerHP = other.GetComponentInParent<PlayerHP>();
                if (playerHP != null && !playerHP.IsInvincible)
                {
                    playerHP.TakeDamage(damageAmount);
                    
                    // 触发玩家击退
                    if (playerMovement != null)
                    {
                        playerMovement.TriggerHitKnockback(transform.position);
                    }
                }
            }
        }
    }

    bool IsAttackActive()
    {
        return attackState != null && attackState.IsAttackActive;
    }

    float GetYAxisTolerance()
    {
        return attackState != null ? attackState.AttackLaneTolerance : 0.5f;
    }
}
