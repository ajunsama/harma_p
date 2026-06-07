using UnityEngine;

public class PunkPKnife : MonoBehaviour
{
    [Header("参数")]
    public float speed = 10f;
    public int damage = 1;
    public float lifetime = 3f;
    public float hitRadius = 1.5f;

    Vector2 moveDirection;
    float timer;
    Transform player;

    public void SetDirection(Vector2 dir)
    {
        moveDirection = dir.normalized;
    }

    void Start()
    {
        var go = GameObject.FindGameObjectWithTag("Player");
        if (go != null) player = go.transform;
    }

    void Update()
    {
        transform.Translate(moveDirection * speed * Time.deltaTime, Space.World);

        if (player != null)
        {
            float dist = Vector2.Distance(transform.position, player.position);
            if (dist < hitRadius)
            {
                PlayerHP playerHP = player.GetComponent<PlayerHP>();
                if (playerHP != null && !playerHP.IsInvincible)
                {
                    playerHP.TakeDamage(damage);

                    PlayerMovement playerMovement = player.GetComponent<PlayerMovement>();
                    if (playerMovement != null)
                    {
                        playerMovement.TriggerHitKnockback(transform.position);
                    }
                }
                Destroy(gameObject);
                return;
            }
        }

        timer += Time.deltaTime;
        if (timer >= lifetime)
        {
            Destroy(gameObject);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerHP playerHP = other.GetComponent<PlayerHP>();
            if (playerHP != null && !playerHP.IsInvincible)
            {
                playerHP.TakeDamage(damage);

                PlayerMovement playerMovement = other.GetComponent<PlayerMovement>();
                if (playerMovement != null)
                {
                    playerMovement.TriggerHitKnockback(transform.position);
                }
            }
            Destroy(gameObject);
        }
    }
}
