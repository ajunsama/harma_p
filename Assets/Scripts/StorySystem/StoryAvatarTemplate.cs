using UnityEngine;

/// <summary>
/// 头像模板 - ScriptableObject，在编辑器中配置
/// </summary>
[CreateAssetMenu(fileName = "NewStoryAvatar", menuName = "剧情系统/头像模板")]
public class StoryAvatarTemplate : ScriptableObject
{
    [Tooltip("头像代号，与JSON中的avatarId匹配")]
    public string avatarId;

    [Tooltip("头像名称（编辑器显示用）")]
    public string displayName;

    [Tooltip("头像精灵")]
    public Sprite avatarSprite;

    [Tooltip("头像缩放")]
    public float scale = 1f;
}
