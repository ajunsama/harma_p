using UnityEngine;
using TMPro;

/// <summary>
/// 文字样式模板 - ScriptableObject，在编辑器中配置
/// 主人公、Boss、小怪各有一套样式模板
/// </summary>
[CreateAssetMenu(fileName = "NewStoryStyle", menuName = "剧情系统/文字样式模板")]
public class StoryStyleTemplate : ScriptableObject
{
    [Tooltip("样式ID，与JSON中的styleId匹配")]
    public string styleId;

    [Tooltip("样式名称（编辑器显示用）")]
    public string displayName;

    [Header("文字设置")]
    [Tooltip("字体大小")]
    public float fontSize = 36f;

    [Tooltip("文字颜色")]
    public Color textColor = Color.white;

    [Tooltip("文字字体（留空使用默认字体）")]
    public TMP_FontAsset font;

    [Header("对话框设置")]
    [Tooltip("对话框背景颜色")]
    public Color dialogueBoxColor = new Color(0f, 0.8f, 1f, 0.9f);

    [Tooltip("对话框背景图片（留空使用纯色）")]
    public Sprite dialogueBoxSprite;

    [Header("说话者名称")]
    [Tooltip("默认说话者名称")]
    public string defaultSpeakerName;

    [Tooltip("名称字体大小")]
    public float speakerNameFontSize = 28f;

    [Header("描边设置")]
    [Tooltip("是否启用描边")]
    public bool enableOutline = true;

    [Tooltip("描边颜色")]
    public Color outlineColor = Color.black;

    [Tooltip("描边宽度")]
    public float outlineWidth = 0.3f;
}
