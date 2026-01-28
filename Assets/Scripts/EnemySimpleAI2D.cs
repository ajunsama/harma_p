using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class EnemySimpleAI2D : MonoBehaviour
{
    [Header("目标")]
    public Transform player;

    [Header("参数")]
    public float speed = 1.2f;          // 移动速度
    public float stopDistance = 1.5f;   // 离玩家多远就站着不动
    public float minMoveTime = 0.6f;    // 最少走多久
    public float maxMoveTime = 1.8f;    // 最多走多久
    public float minWaitTime = 0.5f;    // 最少停多久
    public float maxWaitTime = 1.5f;    // 最多停多久

    Rigidbody2D rb;
    Animator animator;
    bool isMoving;                      // 当前是否处于移动状态
    public bool isKnockedBack = false;  // 是否被击退中
    
    // 入场相关
    private bool hasEntranceTarget = false;  // 是否有入场目标
    private Vector3 entranceTarget;           // 入场目标位置
    private bool isEntering = false;          // 是否正在入场

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
    }

    void OnEnable() => StartCoroutine(ThinkLoop());
    
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
            // 如果被击退，暂停 AI 控制，交由物理引擎处理
            if (isKnockedBack)
            {
                yield return null;
                continue;
            }

            float dist = Vector2.Distance(transform.position, player.position);

            // 1. 足够近就站着不动
            if (dist <= stopDistance)
            {
                rb.linearVelocity = Vector2.zero;
                if (animator != null)
                    animator.SetFloat("speed", 0f);
                yield return new WaitForSeconds(Random.Range(minWaitTime, maxWaitTime));
                continue;
            }

            // 2. 随机决定“走”还是“停”
            isMoving = Random.value > 0.4f;   // 60% 概率继续走，40% 概率停

            if (isMoving)
            {
                // 2-a. 走一段随机时间
                float moveTimer = Random.Range(minMoveTime, maxMoveTime);
                while (moveTimer > 0f)
                {
                    // 如果被击退，中断移动
                    if (isKnockedBack) break;

                    // 实时朝向玩家
                    Vector2 dir = (player.position - transform.position).normalized;
                    rb.linearVelocity = dir * speed;

                    // 设置动画速度参数
                    if (animator != null)
                        animator.SetFloat("speed", rb.linearVelocity.magnitude);

                    // 每帧减掉已经流逝的时间
                    moveTimer -= Time.deltaTime;
                    yield return null;
                }
            }
            else
            {
                // 2-b. 停一段随机时间
                rb.linearVelocity = Vector2.zero;
                if (animator != null)
                    animator.SetFloat("speed", 0f);
                yield return new WaitForSeconds(Random.Range(minWaitTime, maxWaitTime));
            }
        }
    }

    // 左右翻转朝向
    void Update()
    {
        // 只翻转 X 轴方向，保持原有的 scale 大小
        Vector3 scale = transform.localScale;
        if (player.position.x > transform.position.x)
            scale.x = -Mathf.Abs(scale.x);  // 面向右侧，保持负值
        else
            scale.x = Mathf.Abs(scale.x); // 面向左侧，保持正值
        transform.localScale = scale;
    }
}