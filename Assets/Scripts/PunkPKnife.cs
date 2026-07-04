using UnityEngine;

public class PunkPKnife : MonoBehaviour
{
    [Header("参数")]
    public float speed = 10f;
    public int damage = 1;
    public float lifetime = 3f;
    public float hitRadius = 0.45f;
    public float yHitTolerance = 0.6f;
    public int sortingBaseOrder = 1;

    Vector2 moveDirection;
    float timer;
    float laneY;
    bool hasLaneY;
    SpriteRenderer spriteRenderer;
    Collider2D playerCollider;
    PlayerHP playerHP;
    PlayerMovement playerMovement;
    bool hasHit;

    public void SetDirection(Vector2 dir)
    {
        moveDirection = dir.normalized;
    }

    public void SetLaneY(float y)
    {
        laneY = y;
        hasLaneY = true;
    }

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        var go = GameObject.FindGameObjectWithTag("Player");
        if (go != null)
        {
            playerCollider = go.GetComponent<Collider2D>();
            if (playerCollider == null)
                playerCollider = go.GetComponentInChildren<Collider2D>();
            playerHP = go.GetComponent<PlayerHP>();
            playerMovement = go.GetComponent<PlayerMovement>();
        }
    }

    void Update()
    {
        transform.Translate(moveDirection * speed * Time.deltaTime, Space.World);
        UpdateSortingOrder();

        if (playerCollider != null && IsPlayerOnKnifeLane(playerMovement, playerCollider.transform.position.y))
        {
            Vector2 closestPoint = playerCollider.ClosestPoint(transform.position);
            float dist = Vector2.Distance(transform.position, closestPoint);
            if (dist <= hitRadius)
                HitPlayer(playerHP, playerMovement);
        }

        timer += Time.deltaTime;
        if (timer >= lifetime)
        {
            Destroy(gameObject);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        PlayerHP targetHP = other.GetComponentInParent<PlayerHP>();
        if (targetHP == null)
            return;

        PlayerMovement targetMovement = targetHP.GetComponent<PlayerMovement>();
        if (!IsPlayerOnKnifeLane(targetMovement, targetHP.transform.position.y))
            return;

        HitPlayer(targetHP, targetMovement);
    }

    bool IsPlayerOnKnifeLane(PlayerMovement targetMovement, float fallbackY)
    {
        float compareY = hasLaneY ? laneY : transform.position.y;
        float playerLaneY = targetMovement != null ? targetMovement.BaseY : fallbackY;
        return Mathf.Abs(playerLaneY - compareY) <= yHitTolerance;
    }

    void UpdateSortingOrder()
    {
        if (spriteRenderer == null)
            return;

        float sortingY = hasLaneY ? laneY : transform.position.y;
        int order = Mathf.RoundToInt(-sortingY * 100f) + sortingBaseOrder;

        if (playerMovement != null)
        {
            float playerY = playerMovement.BaseY;
            if (Mathf.Abs(sortingY - playerY) < 0.01f)
                order = Mathf.RoundToInt(-playerY * 100f) + sortingBaseOrder + 1;
        }

        spriteRenderer.sortingOrder = order;
    }

    void HitPlayer(PlayerHP targetHP, PlayerMovement targetMovement)
    {
        if (hasHit)
            return;

        hasHit = true;

        if (targetHP != null && !targetHP.IsInvincible)
        {
            targetHP.TakeDamage(damage);
            if (targetMovement != null)
                targetMovement.TriggerHitKnockback(transform.position);
        }

        Destroy(gameObject);
    }
}
