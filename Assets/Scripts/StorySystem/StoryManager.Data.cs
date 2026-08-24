using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public partial class StoryManager
{
public bool HasStory(string storyId)
    {
        return !string.IsNullOrEmpty(storyId) && _storyLookup.ContainsKey(storyId);
    }

public StorySequence GetStory(string storyId)
    {
        if (string.IsNullOrEmpty(storyId)) return null;
        _storyLookup.TryGetValue(storyId, out var story);
        return story;
    }

/// <summary>
    /// 从JSON字符串加载剧情数据
    /// </summary>
    public void LoadStoryData(string json)
    {
        _loadedData = JsonUtility.FromJson<StoryDataCollection>(json);

        _storyLookup.Clear();
        if (_loadedData?.stories != null)
        {
            foreach (var story in _loadedData.stories)
            {
                if (!string.IsNullOrEmpty(story.storyId))
                    _storyLookup[story.storyId] = story;
            }
        }

        GameLog.Verbose($"[StoryManager] 加载了 {_storyLookup.Count} 段剧情数据");
    }

/// <summary>
    /// 从TextAsset加载剧情数据
    /// </summary>
    public void LoadStoryData(TextAsset jsonAsset)
    {
        if (jsonAsset != null)
            LoadStoryData(jsonAsset.text);
    }

/// <summary>
    /// 设置剧情标志位
    /// </summary>
    public void SetFlag(string flagName)
    {
        _storyFlags.Add(flagName);
    }

/// <summary>
    /// 检查剧情标志位
    /// </summary>
    public bool HasFlag(string flagName)
    {
        return _storyFlags.Contains(flagName);
    }

/// <summary>
    /// 移除剧情标志位
    /// </summary>
    public void RemoveFlag(string flagName)
    {
        _storyFlags.Remove(flagName);
    }
}
