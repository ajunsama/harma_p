using UnityEngine;
using UnityEngine.UI;

public class Enemy : MonoBehaviour
{
    [Header("----- 血条 -----")]
    // public Slider hpBar;        // 把做好的 Slider 拖进来
    public int maxHp = 5;       // 主角打5下就死

    [Header("----- 受击范围 -----")]
    public float checkRadius = 0.8f;   // 主角得在敌人前方 0.8 米内
    public float yTolerance = 0.3f;    // Y 轴允许误差

    Animator anim;      // 新增
    int hitHash;        // 新增
    int curHp;
    Collider2D col;     // 用于获取脚底位置
    
    public bool IsHit { get; private set; } // 是否处于受击状态

    SpriteRenderer sr;
    EnemySimpleAI2D ai;
    MuscleP_AI_Movement muscleAi; // 新增：支持 MuscleP AI
    Rigidbody2D rb;

    // 定义死亡事件
    public static event System.Action OnEnemyDied;

    void Awake()
    {
        curHp = maxHp;
        // hpBar.maxValue = maxHp;
        // hpBar.value = maxHp;
        anim = GetComponent<Animator>();   // 新增
        hitHash = Animator.StringToHash("hit"); // 新增
        col = GetComponent<Collider2D>(); // 获取碰撞体
        
        sr = GetComponent<SpriteRenderer>();
        ai = GetComponent<EnemySimpleAI2D>();
        muscleAi = GetComponent<MuscleP_AI_Movement>(); // 获取 MuscleP AI
        rb = GetComponent<Rigidbody2D>();
    }

    // 踩踏伤害
    public void TakeJumpDamage(Vector2 sourcePos)
    {
        Debug.Log($"[Enemy] 受到踩踏伤害! 当前HP: {curHp}");
        curHp--;
        
        if (curHp <= 0)
        {
            Debug.Log("[Enemy] HP归零，死亡");
            // 触发死亡事件
            OnEnemyDied?.Invoke();
            Destroy(gameObject);
            return;
        }

        StartCoroutine(HitReactionCoroutine(sourcePos));
    }

    System.Collections.IEnumerator HitReactionCoroutine(Vector2 sourcePos)
    {
        IsHit = true;
        Debug.Log("[Enemy] 开始受伤反应 (击退+闪烁)");
        
        // 1. 暂停 AI
        if (ai != null) ai.isKnockedBack = true;
        if (muscleAi != null) muscleAi.isKnockedBack = true; 
        
        // 2. 击退：立即施加力
        float dirX = Mathf.Sign(transform.position.x - sourcePos.x);
        if (rb != null) 
        {
            rb.linearVelocity = new Vector2(dirX * 12f, 6f); 
        }

        // 3. 并行闪烁 & 击飞计时
        float totalDuration = 1.0f; // 总受击时间 (0.4s 击飞 + 0.6s 硬直)
        float flyTime = 0.4f;       // 击飞时间
        float elapsed = 0f;
        
        while (elapsed < totalDuration)
        {
            // 闪烁逻辑
            if (sr != null) sr.enabled = !sr.enabled;
            
            // 击飞结束逻辑：超过飞行时间后停止移动
            if (elapsed >= flyTime)
            {
                if (rb != null)
                {
                    rb.linearVelocity = Vector2.zero;
                }
            }
            
            yield return new WaitForSeconds(0.1f); // 闪烁频率
            elapsed += 0.1f;
        }
        
        // 结束状态：确保显示出来，且速度归零
        if (sr != null) sr.enabled = true;
        if (rb != null) rb.linearVelocity = Vector2.zero;

        // 4. 恢复 AI
        if (ai != null) ai.isKnockedBack = false;
        if (muscleAi != null) muscleAi.isKnockedBack = false; 
        IsHit = false;
        Debug.Log("[Enemy] 受伤反应结束，恢复AI");
    }

    // 由主角调用：当主角挥拳时把敌人自己传进来
    // public void TryTakeDamage(Transform player) // 已移除
    // {
    //     // ...
    // }

    // 获取角色脚底的Y坐标

    // 获取角色脚底的Y坐标
    float GetBottomY(Transform target)
    {
        Collider2D targetCol = target.GetComponent<Collider2D>();
        if (targetCol != null)
        {
            return targetCol.bounds.min.y;  // 使用碰撞体底部
        }
        return target.position.y;  // 如果没有碰撞体，使用中心点
    }
}