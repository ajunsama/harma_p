using UnityEngine;

public enum ObstacleType
{
    StaticBlock,
    Destructible,
    MovingPlatform,
    Custom
}

public class Obstacle : MonoBehaviour
{
    public ObstacleType obstacleType = ObstacleType.StaticBlock;
    public float hp = 1f;
    public bool blocksPlayer = true;
    public bool blocksEnemy;

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (obstacleType != ObstacleType.Destructible) return;

        if (collision.gameObject.TryGetComponent<PlayerAttack>(out _))
        {
            hp -= 1f;
            if (hp <= 0f)
            {
                Destroy(gameObject);
            }
        }
    }
}
