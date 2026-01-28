using UnityEngine;
using UnityEngine.InputSystem;
using Spine.Unity;                // Spine 动画

[RequireComponent(typeof(PlayerMovement))]
public class PlayerAttack : MonoBehaviour
{
    [Header("攻击设置")]
    [SerializeField] float attack1Duration = 0.4f; // 第一段攻击持续时间
    [SerializeField] float attack2Duration = 0.45f; // 第二段攻击持续时间
    [SerializeField] float attack3Duration = 0.6f; // 第三段攻击持续时间
    [SerializeField] float comboWindow = 0.5f; // 连击窗口时间(在攻击结束后多久内可以触发下一段)
    
    [Header("Spine动画")]
    [SerializeField] SkeletonAnimation skeletonAnimation;
    // 如果有攻击动画，可以在这里定义
    // [SpineAnimation] public string attack1AnimName = "attack1";
    // [SpineAnimation] public string attack2AnimName = "attack2";
    // [SpineAnimation] public string attack3AnimName = "attack3";
    
    private PlayerMovement playerMovement;
    private bool isAttacking = false;
    private int currentCombo = 0; // 当前连击段数 (0=未攻击, 1=第一段, 2=第二段, 3=第三段)
    private float attackTimer = 0f;
    private float comboTimer = 0f; // 连击窗口计时器
    private bool canCombo = false; // 是否可以进行下一段连击
    private bool comboQueued = false; // 是否有连击输入排队
    private bool hasDamageChecked = false; // 本次攻击是否已经检测过伤害
    
    // 新 InputSystem 自动生成的回调
    void OnAttack(InputValue value)
    {
        return; // 攻击功能已移除
        /*
        // 只有在不跳跃时才能攻击
        if (playerMovement.IsJumping)
            return;
        
        // 如果没在攻击，直接开始第一段攻击
        if (!isAttacking)
        {
            StartAttack(1);
        }
        // 如果在攻击中且可以连击，排队下一段攻击
        else if (canCombo && currentCombo < 3)
        {
            comboQueued = true;
        }
        */
    }
    
    void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
        
        // 如果没有手动设置SkeletonAnimation，尝试从PlayerMovement获取
        if (skeletonAnimation == null && playerMovement != null)
        {
            skeletonAnimation = playerMovement.GetSkeletonAnimation();
        }
    }
    
    void Update()
    {
        if (isAttacking)
        {
            UpdateAttack();
        }
        else if (currentCombo > 0)
        {
            // 攻击结束后的连击窗口计时
            comboTimer += Time.deltaTime;
            if (comboTimer >= comboWindow)
            {
                ResetCombo();
            }
        }
    }
    
    void StartAttack(int comboIndex)
    {
        isAttacking = true;
        currentCombo = comboIndex;
        attackTimer = 0f;
        canCombo = false;
        comboQueued = false;
        comboTimer = 0f;
        hasDamageChecked = false; // 重置伤害检测标志
        
        // 播放Spine攻击动画（如果有的话）
        // if (skeletonAnimation != null)
        // {
        //     string attackAnim = GetAttackAnimName(comboIndex);
        //     if (!string.IsNullOrEmpty(attackAnim))
        //     {
        //         skeletonAnimation.AnimationState.SetAnimation(0, attackAnim, false);
        //     }
        // }
    }
    
    void UpdateAttack()
    {
        attackTimer += Time.deltaTime;
        
        float currentDuration = GetCurrentAttackDuration();
        
        // 在攻击的伤害判定时机执行一次伤害检测（通常在动画播放到30-40%时）
        if (!hasDamageChecked && attackTimer >= currentDuration * 0.3f)
        {
            hasDamageChecked = true;
            // 场景里所有敌人（也可以用 Physics2D.OverlapCircleAll 做优化）
            // foreach (var enemy in UnityEngine.Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            //    enemy.TryTakeDamage(transform);
        }
        
        // 在攻击进行到一定程度后开启连击窗口(通常是动画播放到50-70%时)
        if (!canCombo && attackTimer >= currentDuration * 0.5f)
        {
            canCombo = true;
        }
        
        // 攻击结束
        if (attackTimer >= currentDuration)
        {
            EndAttack();
        }
    }
    
    void EndAttack()
    {
        isAttacking = false;
        // Spine动画会自动完成，不需要设置参数
        
        // 检查是否有排队的连击
        if (comboQueued && currentCombo < 3)
        {
            StartAttack(currentCombo + 1);
        }
        else
        {
            // 没有连击输入，开始连击窗口计时
            comboTimer = 0f;
        }
    }
    
    void ResetCombo()
    {
        currentCombo = 0;
        comboTimer = 0f;
        // Spine动画不需要重置参数
    }
    
    float GetCurrentAttackDuration()
    {
        switch (currentCombo)
        {
            case 1: return attack1Duration;
            case 2: return attack2Duration;
            case 3: return attack3Duration;
            default: return attack1Duration;
        }
    }
    
    public bool IsAttacking => isAttacking;
    public int CurrentCombo => currentCombo;
}
