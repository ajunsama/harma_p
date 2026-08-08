using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Spine.Unity;                // Spine 动画

public class PlayerHP : MonoBehaviour
{
    public event Action<PlayerHP> Died;

    [SerializeField] private Slider hpSlider;   // 把 HPBar 拖进来

    [SerializeField] private int maxHP = 3;
    
    [SerializeField] private SpriteRenderer spriteRenderer; // 角色的 SpriteRenderer（可选，用于闪烁）
    
    [Header("无敌设置")]
    [SerializeField] private float invincibleDuration = 2f; // 无敌时间
    [SerializeField] private float blinkInterval = 0.1f;    // 闪烁间隔
    
    [Header("Spine动画")]
    [SerializeField] private SkeletonAnimation skeletonAnimation; // Spine动画组件
    [SpineAnimation] public string hitedAnimName = "hited";       // 受伤动画名称
    [SpineAnimation] public string dieAnimName = "die";           // 死亡动画名称    
    private int currentHP;
    private bool isInvincible = false; // 是否处于无敌状态
    private bool isDead = false;       // 是否已死亡
    private PlayerMovement playerMovement; // 玩家移动组件引用
    private Coroutine _invincibleCoroutine; // 当前无敌协程引用
    
    // 公共属性供外部访问
    public bool IsInvincible => isInvincible;
    public bool IsDead => isDead;

    void Start()
    {
        currentHP = maxHP;

        // hpSlider 可选：如果场景中未分配则不更新 UI，避免 NullReferenceException
        if (hpSlider != null)
        {
            hpSlider.maxValue = maxHP;
            hpSlider.value = currentHP;
        }
        
        // 自动获取 SpriteRenderer（可选）
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
            
        // 获取 PlayerMovement 引用
        playerMovement = GetComponent<PlayerMovement>();
        
        // 如果没有手动设置SkeletonAnimation，尝试从PlayerMovement获取
        if (skeletonAnimation == null && playerMovement != null)
        {
            skeletonAnimation = playerMovement.GetSkeletonAnimation();
        }
    }

    // 对外接口：受伤
    public void TakeDamage(int amount = 1)
    {
        // 无敌状态下或已死亡不受伤
        if (isInvincible || isDead)
            return;
            
        currentHP = Mathf.Max(currentHP - amount, 0);
        GetComponentInParent<PlayerGameplaySignalHub>()?.Publish(
            PlayerGameplaySignals.Damaged,
            amount,
            gameObject);
        if (hpSlider != null)
            hpSlider.value = currentHP;

        if (currentHP <= 0)
        {
            Die();
        }
        else
        {
            // 播放受伤动画
            PlayHitAnimation();
            // 触发受伤逻辑：无敌时间和闪烁
            StartInvincible(invincibleDuration);
        }
    }

    // 回血
    public void Heal(int amount = 1)
    {
        currentHP = Mathf.Min(currentHP + amount, maxHP);
        if (hpSlider != null)
            hpSlider.value = currentHP;
    }

    public void MakeInvincible(float duration)
    {
        StartInvincible(duration);
    }

    void StartInvincible(float duration)
    {
        if (_invincibleCoroutine != null)
            StopCoroutine(_invincibleCoroutine);
        _invincibleCoroutine = StartCoroutine(InvincibleCoroutine(duration));
    }

    IEnumerator InvincibleCoroutine(float duration)
    {
        isInvincible = true;
        float elapsed = 0f;
        bool useSpineColor = skeletonAnimation != null;
        while (elapsed < duration)
        {
            if (useSpineColor)
                skeletonAnimation.Skeleton.A = skeletonAnimation.Skeleton.A > 0.5f ? 0.3f : 1f;
            else if (spriteRenderer != null)
                spriteRenderer.enabled = !spriteRenderer.enabled;
            yield return new WaitForSeconds(blinkInterval);
            elapsed += blinkInterval;
        }
        if (useSpineColor) skeletonAnimation.Skeleton.A = 1f;
        else if (spriteRenderer != null) spriteRenderer.enabled = true;
        isInvincible = false;
        _invincibleCoroutine = null;
    }
    
    // 播放受伤动画
    void PlayHitAnimation()
    {
        if (skeletonAnimation == null || string.IsNullOrEmpty(hitedAnimName))
            return;
            
        // 在轨道1播放受伤动画（一次性），不影响轨道0的移动动画
        // 或者直接在轨道0播放，打断当前动画
        skeletonAnimation.AnimationState.SetAnimation(0, hitedAnimName, false);
    }

    void Die()
    {
        if (isDead) return;
        
        isDead = true;
        GetComponentInParent<PlayerGameplaySignalHub>()?.Publish(
            PlayerGameplaySignals.Died,
            1f,
            gameObject);
        Died?.Invoke(this);
        Debug.Log("主角死亡");

        // 禁用移动输入和逻辑
        if (playerMovement != null)
        {
            // 如果在空中，强制落地，防止死在半空中
            if (playerMovement.IsJumping)
            {
                Vector3 pos = transform.position;
                pos.y = playerMovement.BaseY;
                transform.position = pos;
            }
            
            playerMovement.enabled = false;
        }

        // 禁用攻击输入和逻辑
        var playerAttack = GetComponent<PlayerAttack>();
        if (playerAttack != null)
        {
            playerAttack.enabled = false;
        }

        // 停止刚体运动
        var rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }
        
        // 播放死亡动画
        if (skeletonAnimation != null && !string.IsNullOrEmpty(dieAnimName))
        {
            skeletonAnimation.AnimationState.SetAnimation(0, dieAnimName, false);
        }
        
        // 这里写死亡逻辑，比如禁用移动、显示游戏结束界面等
    }
}
