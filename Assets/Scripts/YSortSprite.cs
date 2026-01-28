using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class YSortSprite : MonoBehaviour
{
    [Tooltip("数值越大，整体越靠后（容易被遮挡）")]
    public int baseOrder = 1;   // 如果你想给某些物体永久偏移，就调这个

    Renderer rend;
    PlayerMovement playerMovement;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        playerMovement = GetComponentInParent<PlayerMovement>();
    }

    // 用 LateUpdate 保证所有位移/动画都跑完了再排序
    void LateUpdate()
    {
        float sortingY = transform.position.y;

        // 如果能找到 PlayerMovement，说明是玩家（或类似机制的角色）
        // 使用 BaseY（地面基准高度）来排序，避免跳跃时层级错误
        if (playerMovement != null)
        {
            sortingY = playerMovement.BaseY;
        }

        // 1 像素 = 0.01 单位时，乘 -100 正好 1 单位对应 1 个 order
        int order = Mathf.RoundToInt(-sortingY * 100f) + baseOrder;
        rend.sortingOrder = order;
    }
}