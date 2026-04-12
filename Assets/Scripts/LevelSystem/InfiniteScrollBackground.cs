using UnityEngine;

/// <summary>
/// 简单的无限滚动背景 - 单张背景图无限循环
/// 适用于固定视角横版清关游戏（类似《吞食天地》）
/// 背景相对于世界静止，通过复制实现无限延伸
/// </summary>
public class InfiniteScrollBackground : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("背景精灵渲染器")]
    public SpriteRenderer backgroundSprite;
    
    [Tooltip("跟随的相机")]
    public Transform cameraTransform;
    
    [Header("设置")]
    [Tooltip("背景固定Y坐标")]
    public float fixedY = 0f;
    
    [Header("调试")]
    [Tooltip("开启后在关键位置打印详细日志，便于排查为何不能滚动背景")] 
    public bool verboseLogging = false;
    
    // 内部变量
    private float spriteWidth;
    
    // 左右两个副本
    private SpriteRenderer leftClone;
    private SpriteRenderer rightClone;
    
    void Start()
    {
        // 自动获取相机
        if (cameraTransform == null)
        {
            cameraTransform = Camera.main?.transform;
        }
        
        if (cameraTransform == null)
        {
            Debug.LogError("[InfiniteScrollBackground] 未找到相机！");
            return;
        }
        
        // 自动获取背景精灵
        if (backgroundSprite == null)
        {
            backgroundSprite = GetComponent<SpriteRenderer>();
        }
        
        if (backgroundSprite == null || backgroundSprite.sprite == null)
        {
            Debug.LogError("[InfiniteScrollBackground] 未找到背景精灵！");
            return;
        }
        
        // 计算精灵宽度
        spriteWidth = backgroundSprite.sprite.bounds.size.x * backgroundSprite.transform.localScale.x;
        
        if (verboseLogging)
        {
            Debug.Log($"[InfiniteScrollBackground] Found background sprite: {backgroundSprite.sprite.name}");
            Debug.Log($"[InfiniteScrollBackground] Sprite bounds: {backgroundSprite.sprite.bounds.size}, localScale: {backgroundSprite.transform.localScale}");
            Debug.Log($"[InfiniteScrollBackground] Calculated spriteWidth: {spriteWidth}");
        }

        // 创建左右副本实现无缝循环
        CreateClones();

        if (verboseLogging)
        {
            Debug.Log($"[InfiniteScrollBackground] 初始化完成，精灵宽度: {spriteWidth}");
        }
    }
    
    void CreateClones()
    {
        // 创建左副本
        GameObject leftObj = new GameObject("Background_Left");
        leftObj.transform.SetParent(transform.parent);
        leftObj.transform.localScale = backgroundSprite.transform.localScale;
        leftObj.transform.position = new Vector3(
            backgroundSprite.transform.position.x - spriteWidth,
            backgroundSprite.transform.position.y,
            backgroundSprite.transform.position.z
        );
        
        leftClone = leftObj.AddComponent<SpriteRenderer>();
        CopySpriteRenderer(backgroundSprite, leftClone);
        if (verboseLogging)
        {
            Debug.Log($"[InfiniteScrollBackground] Created left clone at {leftObj.transform.position}");
        }
        
        // 创建右副本
        GameObject rightObj = new GameObject("Background_Right");
        rightObj.transform.SetParent(transform.parent);
        rightObj.transform.localScale = backgroundSprite.transform.localScale;
        rightObj.transform.position = new Vector3(
            backgroundSprite.transform.position.x + spriteWidth,
            backgroundSprite.transform.position.y,
            backgroundSprite.transform.position.z
        );
        
        rightClone = rightObj.AddComponent<SpriteRenderer>();
        CopySpriteRenderer(backgroundSprite, rightClone);
        if (verboseLogging)
        {
            Debug.Log($"[InfiniteScrollBackground] Created right clone at {rightObj.transform.position}");
        }
    }
    
    void CopySpriteRenderer(SpriteRenderer source, SpriteRenderer target)
    {
        target.sprite = source.sprite;
        target.sortingLayerID = source.sortingLayerID;
        target.sortingOrder = source.sortingOrder;
        target.color = source.color;
        target.material = source.material;
        target.flipX = source.flipX;
        target.flipY = source.flipY;
    }
    
    void LateUpdate()
    {
        if (cameraTransform == null || backgroundSprite == null) return;
        if (verboseLogging && Time.frameCount % 120 == 0)
        {
            Debug.Log($"[InfiniteScrollBackground] LateUpdate status: cameraPos={cameraTransform.position}, bgPos={backgroundSprite.transform.position}, spriteWidth={spriteWidth}, fixedY={fixedY}");
        }
        
        // 背景不移动！背景相对于世界是静止的
        // 只需要根据相机位置，把三块背景（左、中、右）摆放到正确位置
        // 确保相机视野内总是有背景覆盖
        
        float cameraX = cameraTransform.position.x;
        float bgX = backgroundSprite.transform.position.x;
        
        // 计算相机相对于当前主背景中心的偏移
        // 如果相机超出当前主背景的覆盖范围，就把主背景整体平移
        if (cameraX > bgX + spriteWidth * 0.5f)
        {
            if (verboseLogging) Debug.Log($"[InfiniteScrollBackground] Camera beyond right threshold (cameraX={cameraX}, bgX={bgX}). Moving background right.");
            // 相机在主背景右侧太远，主背景整体右移一个宽度
            Vector3 pos = backgroundSprite.transform.position;
            pos.x += spriteWidth;
            backgroundSprite.transform.position = pos;
        }
        else if (cameraX < bgX - spriteWidth * 0.5f)
        {
            if (verboseLogging) Debug.Log($"[InfiniteScrollBackground] Camera beyond left threshold (cameraX={cameraX}, bgX={bgX}). Moving background left.");
            // 相机在主背景左侧太远，主背景整体左移一个宽度
            Vector3 pos = backgroundSprite.transform.position;
            pos.x -= spriteWidth;
            backgroundSprite.transform.position = pos;
        }
        else
        {
            if (verboseLogging && Time.frameCount % 120 == 0)
            {
                Debug.Log($"[InfiniteScrollBackground] Camera within center. No background movement needed. cameraX={cameraX}, bgX={bgX}");
            }
        }
        
        // 更新副本位置（始终在主背景左右两侧）
        if (leftClone != null)
        {
            leftClone.transform.position = new Vector3(
                backgroundSprite.transform.position.x - spriteWidth,
                fixedY,
                backgroundSprite.transform.position.z
            );
            if (verboseLogging && Time.frameCount % 120 == 0)
            {
                Debug.Log($"[InfiniteScrollBackground] Updated left clone position to {leftClone.transform.position}");
            }
        }
        
        if (rightClone != null)
        {
            rightClone.transform.position = new Vector3(
                backgroundSprite.transform.position.x + spriteWidth,
                fixedY,
                backgroundSprite.transform.position.z
            );
            if (verboseLogging && Time.frameCount % 120 == 0)
            {
                Debug.Log($"[InfiniteScrollBackground] Updated right clone position to {rightClone.transform.position}");
            }
        }
        
        if (verboseLogging)
        {
            if (leftClone == null) Debug.LogWarning("[InfiniteScrollBackground] leftClone is null");
            if (rightClone == null) Debug.LogWarning("[InfiniteScrollBackground] rightClone is null");
        }
    }
    
    void OnDrawGizmosSelected()
    {
        if (backgroundSprite == null || backgroundSprite.sprite == null) return;
        
        // 显示背景范围
        float width = backgroundSprite.sprite.bounds.size.x * backgroundSprite.transform.localScale.x;
        float height = backgroundSprite.sprite.bounds.size.y * backgroundSprite.transform.localScale.y;
        
        Gizmos.color = new Color(0, 1, 0, 0.3f);
        Gizmos.DrawWireCube(backgroundSprite.transform.position, new Vector3(width, height, 0));
        
        // 显示副本范围
        Gizmos.color = new Color(1, 1, 0, 0.2f);
        Gizmos.DrawWireCube(backgroundSprite.transform.position + new Vector3(-width, 0, 0), new Vector3(width, height, 0));
        Gizmos.DrawWireCube(backgroundSprite.transform.position + new Vector3(width, 0, 0), new Vector3(width, height, 0));
    }
}
