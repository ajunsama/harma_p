using UnityEngine;

public enum ItemType
{
    HealthRecovery,
    Invincible,
    SpeedBoost,
    ScoreBonus,
    Custom
}

public class ItemPickup : MonoBehaviour
{
    public ItemType itemType = ItemType.HealthRecovery;
    public float effectValue = 1f;
    public float effectDuration;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            ApplyEffect(other.gameObject);
            Destroy(gameObject);
        }
    }

    void ApplyEffect(GameObject player)
    {
        switch (itemType)
        {
            case ItemType.HealthRecovery:
                if (player.TryGetComponent<PlayerHP>(out var hp))
                    hp.Heal((int)effectValue);
                break;
            case ItemType.Invincible:
                if (player.TryGetComponent<PlayerHP>(out var hp2))
                    hp2.MakeInvincible(effectDuration);
                break;
        }
    }
}
