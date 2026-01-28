using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 视差滚动背景层
/// </summary>
[System.Serializable]
public class ParallaxLayer
{
    [Tooltip("背景精灵")]
    public SpriteRenderer spriteRenderer;
    
    [Tooltip("视差系数 (0=静止, 1=与相机同步移动)")]
    [Range(0f, 1f)]
    public float parallaxFactor = 0.5f;
    
    [Tooltip("是否水平无限循环")]
    public bool infiniteHorizontal = true;
    
    [Tooltip("是否垂直移动")]
    public bool verticalParallax = false;
    
    [HideInInspector]
    public float spriteWidth;
    
    [HideInInspector]
    public Vector3 startPosition;
}

/// <summary>
/// 视差滚动背景系统 - 用于横版过关游戏的卷轴式背景
/// </summary>
public class ParallaxBackground : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("跟随的相机")]
    public Transform cameraTransform;
    
    [Header("背景层")]
    [Tooltip("所有背景层，从后到前排列")]
    public List<ParallaxLayer> layers = new List<ParallaxLayer>();
    
    [Header("自动生成设置")]
    [Tooltip("是否自动生成无限滚动的副本")]
    public bool autoGenerateClones = true;
    
    private Vector3 lastCameraPosition;
    private Dictionary<SpriteRenderer, SpriteRenderer[]> clonedSprites = new Dictionary<SpriteRenderer, SpriteRenderer[]>();
    
    void Start()
    {
        if (cameraTransform == null)
        {
            cameraTransform = Camera.main?.transform;
        }
        
        if (cameraTransform == null)
        {
            Debug.LogError("[ParallaxBackground] 未找到相机！");
            return;
        }
        
        lastCameraPosition = cameraTransform.position;
        
        // 初始化每个层
        foreach (var layer in layers)
        {
            if (layer.spriteRenderer == null) continue;
            
            layer.startPosition = layer.spriteRenderer.transform.position;
            
            // 计算精灵宽度
            if (layer.spriteRenderer.sprite != null)
            {
                layer.spriteWidth = layer.spriteRenderer.sprite.bounds.size.x * 
                                    layer.spriteRenderer.transform.localScale.x;
            }
            
            // 生成无限滚动的副本
            if (autoGenerateClones && layer.infiniteHorizontal)
            {
                CreateClones(layer);
            }
        }
    }
    
    void CreateClones(ParallaxLayer layer)
    {
        if (layer.spriteRenderer == null || layer.spriteWidth <= 0) return;
        
        // 创建左右两个副本
        SpriteRenderer[] clones = new SpriteRenderer[2];
        
        for (int i = 0; i < 2; i++)
        {
            GameObject clone = new GameObject($"{layer.spriteRenderer.name}_Clone{i}");
            clone.transform.SetParent(layer.spriteRenderer.transform.parent);
            clone.transform.localScale = layer.spriteRenderer.transform.localScale;
            clone.transform.localRotation = layer.spriteRenderer.transform.localRotation;
            
            SpriteRenderer sr = clone.AddComponent<SpriteRenderer>();
            sr.sprite = layer.spriteRenderer.sprite;
            sr.sortingLayerID = layer.spriteRenderer.sortingLayerID;
            sr.sortingOrder = layer.spriteRenderer.sortingOrder;
            sr.color = layer.spriteRenderer.color;
            sr.material = layer.spriteRenderer.material;
            
            // 左边副本
            if (i == 0)
            {
                clone.transform.position = layer.spriteRenderer.transform.position - 
                                           new Vector3(layer.spriteWidth, 0, 0);
            }
            // 右边副本
            else
            {
                clone.transform.position = layer.spriteRenderer.transform.position + 
                                           new Vector3(layer.spriteWidth, 0, 0);
            }
            
            clones[i] = sr;
        }
        
        clonedSprites[layer.spriteRenderer] = clones;
    }
    
    void LateUpdate()
    {
        if (cameraTransform == null) return;
        
        Vector3 deltaMovement = cameraTransform.position - lastCameraPosition;
        
        foreach (var layer in layers)
        {
            if (layer.spriteRenderer == null) continue;
            
            // 计算视差移动
            float parallaxX = deltaMovement.x * layer.parallaxFactor;
            float parallaxY = layer.verticalParallax ? deltaMovement.y * layer.parallaxFactor : 0f;
            
            // 移动背景层（相反方向，因为视差效果）
            Vector3 newPos = layer.spriteRenderer.transform.position;
            newPos.x += deltaMovement.x - parallaxX;
            
            if (layer.verticalParallax)
            {
                newPos.y += deltaMovement.y - parallaxY;
            }
            
            layer.spriteRenderer.transform.position = newPos;
            
            // 处理无限滚动
            if (layer.infiniteHorizontal && autoGenerateClones)
            {
                HandleInfiniteScroll(layer);
            }
        }
        
        lastCameraPosition = cameraTransform.position;
    }
    
    void HandleInfiniteScroll(ParallaxLayer layer)
    {
        if (!clonedSprites.ContainsKey(layer.spriteRenderer)) return;
        
        SpriteRenderer[] clones = clonedSprites[layer.spriteRenderer];
        SpriteRenderer mainSprite = layer.spriteRenderer;
        
        // 更新副本位置
        clones[0].transform.position = mainSprite.transform.position - new Vector3(layer.spriteWidth, 0, 0);
        clones[1].transform.position = mainSprite.transform.position + new Vector3(layer.spriteWidth, 0, 0);
        
        // 检查是否需要重新定位
        float cameraX = cameraTransform.position.x;
        float mainX = mainSprite.transform.position.x;
        
        // 如果相机超出主精灵范围，重新定位
        if (cameraX > mainX + layer.spriteWidth * 0.5f)
        {
            // 相机在右边，将主精灵移到右边
            mainSprite.transform.position += new Vector3(layer.spriteWidth, 0, 0);
        }
        else if (cameraX < mainX - layer.spriteWidth * 0.5f)
        {
            // 相机在左边，将主精灵移到左边
            mainSprite.transform.position -= new Vector3(layer.spriteWidth, 0, 0);
        }
    }
    
    /// <summary>
    /// 添加新的背景层
    /// </summary>
    public void AddLayer(Sprite sprite, float parallaxFactor, int sortingOrder)
    {
        GameObject layerObj = new GameObject($"Background_Layer_{layers.Count}");
        layerObj.transform.SetParent(transform);
        layerObj.transform.localPosition = Vector3.zero;
        
        SpriteRenderer sr = layerObj.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = sortingOrder;
        
        ParallaxLayer newLayer = new ParallaxLayer();
        newLayer.spriteRenderer = sr;
        newLayer.parallaxFactor = parallaxFactor;
        newLayer.infiniteHorizontal = true;
        
        layers.Add(newLayer);
        
        // 如果已经在运行，初始化新层
        if (Application.isPlaying)
        {
            newLayer.startPosition = sr.transform.position;
            newLayer.spriteWidth = sprite.bounds.size.x;
            
            if (autoGenerateClones && newLayer.infiniteHorizontal)
            {
                CreateClones(newLayer);
            }
        }
    }
    
    /// <summary>
    /// 重置所有背景层到初始位置
    /// </summary>
    public void ResetAllLayers()
    {
        foreach (var layer in layers)
        {
            if (layer.spriteRenderer != null)
            {
                layer.spriteRenderer.transform.position = layer.startPosition;
            }
        }
        
        if (cameraTransform != null)
        {
            lastCameraPosition = cameraTransform.position;
        }
    }
    
    void OnDrawGizmosSelected()
    {
        // 在编辑器中可视化背景层
        foreach (var layer in layers)
        {
            if (layer.spriteRenderer == null) continue;
            
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            
            if (layer.spriteRenderer.sprite != null)
            {
                Vector3 center = layer.spriteRenderer.transform.position;
                Vector3 size = layer.spriteRenderer.sprite.bounds.size;
                size.x *= layer.spriteRenderer.transform.localScale.x;
                size.y *= layer.spriteRenderer.transform.localScale.y;
                
                Gizmos.DrawWireCube(center, size);
            }
        }
    }
}
