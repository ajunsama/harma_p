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

    public void TakeDamage(float amount)
    {
        if (obstacleType != ObstacleType.Destructible || amount <= 0f)
            return;

        hp -= amount;
        if (hp <= 0f)
        {
            Destroy(gameObject);
        }
    }
}
