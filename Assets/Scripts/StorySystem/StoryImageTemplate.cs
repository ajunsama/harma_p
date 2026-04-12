using UnityEngine;

/// <summary>
/// 图片模板 - ScriptableObject，在编辑器中配置
/// </summary>
[CreateAssetMenu(fileName = "NewStoryImage", menuName = "剧情系统/图片模板")]
public class StoryImageTemplate : ScriptableObject
{
    [Tooltip("图片代号，与JSON中的imageId匹配")]
    public string imageId;

    [Tooltip("图片名称（编辑器显示用）")]
    public string displayName;

    [Tooltip("图片精灵")]
    public Sprite imageSprite;
}
