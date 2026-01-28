using UnityEngine;

public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    [Header("世界边界设置")]
    [SerializeField] BoxCollider2D floorCollider; // 拖入场景中的 Floor 对象

    // 公开的边界属性
    public float LeftBound { get; private set; }
    public float RightBound { get; private set; }
    public float BottomBound { get; private set; }
    public float TopBound { get; private set; }

    void Awake()
    {
        // 单例模式设置
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        CalculateBounds();
    }

    void CalculateBounds()
    {
        if (floorCollider != null)
        {
            Bounds bounds = floorCollider.bounds;
            LeftBound = bounds.min.x;
            RightBound = bounds.max.x;
            BottomBound = bounds.min.y;
            TopBound = bounds.max.y;
        }
        else
        {
            Debug.LogError("LevelManager: 未设置 Floor Collider！请在 Inspector 中拖入 Floor 对象。使用默认安全边界。");
            // 默认值，防止报错
            LeftBound = -20f;
            RightBound = 20f;
            BottomBound = -10f;
            TopBound = 10f;
        }
    }
}
