using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单条对话数据 - 对应JSON中的一条记录
/// </summary>
[Serializable]
public class StoryDialogue
{
    [Tooltip("该条对话的唯一ID")]
    public int id;

    [Tooltip("所属章ID")]
    public string chapterId;

    [Tooltip("所属节ID")]
    public string sectionId;

    [Tooltip("正文内容")]
    [TextArea(2, 5)]
    public string content;

    [Tooltip("文字样式ID，对应StoryStyleTemplate")]
    public string styleId;

    [Tooltip("播放速度（每个字符的间隔秒数，越小越快）")]
    public float playSpeed = 0.05f;

    [Tooltip("头像位置: left / center / right")]
    public string avatarPosition = "left";

    [Tooltip("头像代号，对应StoryAvatarTemplate")]
    public string avatarId;

    [Tooltip("是否显示图片")]
    public bool showImage;

    [Tooltip("图片代号，对应StoryImageTemplate")]
    public string imageId;

    // ---- 扩展字段区域 ----
    // 后续需要新增属性时在此追加，JSON反序列化会自动忽略缺失字段

    [Tooltip("说话者名称（可选，留空则从样式模板取默认值）")]
    public string speakerName;

    [Tooltip("附加数据，用于自定义扩展（JSON键值对）")]
    public string extraJson;
}

/// <summary>
/// 一段完整剧情（由多条对话组成）
/// </summary>
[Serializable]
public class StorySequence
{
    [Tooltip("剧情段落唯一ID")]
    public string storyId;

    [Tooltip("所属章ID")]
    public string chapterId;

    [Tooltip("所属节ID")]
    public string sectionId;

    [Tooltip("对话列表")]
    public List<StoryDialogue> dialogues = new List<StoryDialogue>();
}

/// <summary>
/// 剧情数据集合 - JSON根对象
/// </summary>
[Serializable]
public class StoryDataCollection
{
    public List<StorySequence> stories = new List<StorySequence>();
}

/// <summary>
/// 剧情播放完成后的结果处理配置
/// </summary>
[Serializable]
public class StoryResultAction
{
    public enum ActionType
    {
        None,
        SpawnEnemies,       // 生成敌人
        RemoveEnemies,      // 移除敌人
        ChangeEnemyState,   // 改变敌人状态
        GameOver,           // 游戏结束
        LevelComplete,      // 通关
        LoadScene,          // 加载场景
        SetFlag,            // 设置标志位
        Custom              // 自定义（通过UnityEvent处理）
    }

    public ActionType actionType = ActionType.None;

    [Tooltip("参数（如场景名、标志位名等）")]
    public string parameter;

    [Tooltip("数值参数")]
    public int intParameter;
}
