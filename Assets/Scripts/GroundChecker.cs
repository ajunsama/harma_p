using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class GroundChecker : MonoBehaviour
{
    Rigidbody2D rb;

    void Awake() => rb = GetComponent<Rigidbody2D>();

    public void MovePosition(Vector2 pos, float baseY)
    {
        pos.y = baseY - transform.localScale.y / 2;
        rb.MovePosition(pos);
    }
}
