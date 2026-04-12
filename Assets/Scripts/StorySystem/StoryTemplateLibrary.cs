using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 模板库 - ScriptableObject，集中管理所有模板
/// </summary>
[CreateAssetMenu(fileName = "StoryTemplateLibrary", menuName = "剧情系统/模板库")]
public class StoryTemplateLibrary : ScriptableObject
{
    [Header("文字样式模板")]
    public List<StoryStyleTemplate> styleTemplates = new List<StoryStyleTemplate>();

    [Header("头像模板")]
    public List<StoryAvatarTemplate> avatarTemplates = new List<StoryAvatarTemplate>();

    [Header("图片模板")]
    public List<StoryImageTemplate> imageTemplates = new List<StoryImageTemplate>();

    // 运行时缓存
    private Dictionary<string, StoryStyleTemplate> _styleCache;
    private Dictionary<string, StoryAvatarTemplate> _avatarCache;
    private Dictionary<string, StoryImageTemplate> _imageCache;

    /// <summary>
    /// 根据ID获取样式模板
    /// </summary>
    public StoryStyleTemplate GetStyle(string styleId)
    {
        if (_styleCache == null) BuildCache();
        _styleCache.TryGetValue(styleId, out var template);
        if (template == null) Debug.LogWarning($"[StoryTemplateLibrary] 找不到样式: {styleId}");
        return template;
    }

    /// <summary>
    /// 根据ID获取头像模板
    /// </summary>
    public StoryAvatarTemplate GetAvatar(string avatarId)
    {
        if (_avatarCache == null) BuildCache();
        _avatarCache.TryGetValue(avatarId, out var template);
        if (template == null) Debug.LogWarning($"[StoryTemplateLibrary] 找不到头像: {avatarId}");
        return template;
    }

    /// <summary>
    /// 根据ID获取图片模板
    /// </summary>
    public StoryImageTemplate GetImage(string imageId)
    {
        if (_imageCache == null) BuildCache();
        _imageCache.TryGetValue(imageId, out var template);
        if (template == null) Debug.LogWarning($"[StoryTemplateLibrary] 找不到图片: {imageId}");
        return template;
    }

    /// <summary>
    /// 构建运行时缓存
    /// </summary>
    public void BuildCache()
    {
        _styleCache = new Dictionary<string, StoryStyleTemplate>();
        foreach (var s in styleTemplates)
        {
            if (s != null && !string.IsNullOrEmpty(s.styleId))
                _styleCache[s.styleId] = s;
        }

        _avatarCache = new Dictionary<string, StoryAvatarTemplate>();
        foreach (var a in avatarTemplates)
        {
            if (a != null && !string.IsNullOrEmpty(a.avatarId))
                _avatarCache[a.avatarId] = a;
        }

        _imageCache = new Dictionary<string, StoryImageTemplate>();
        foreach (var i in imageTemplates)
        {
            if (i != null && !string.IsNullOrEmpty(i.imageId))
                _imageCache[i.imageId] = i;
        }
    }
}
