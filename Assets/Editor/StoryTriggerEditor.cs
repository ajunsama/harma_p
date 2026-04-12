using UnityEngine;
using UnityEditor;

/// <summary>
/// StoryTrigger自定义Inspector - 根据触发类型显示/隐藏相关字段
/// </summary>
[CustomEditor(typeof(StoryTrigger))]
public class StoryTriggerEditor : Editor
{
    // 通用
    SerializedProperty triggerType;
    SerializedProperty storyId;
    SerializedProperty triggerOnce;

    // 位置触发
    SerializedProperty useTransformPosition;
    SerializedProperty triggerPositionX;
    SerializedProperty triggerFromLeft;

    // 标志位触发
    SerializedProperty flagName;

    // 波次触发
    SerializedProperty waveIndex;

    // 结果与事件
    SerializedProperty resultActions;
    SerializedProperty onBeforeStory;
    SerializedProperty onAfterStory;

    void OnEnable()
    {
        triggerType = serializedObject.FindProperty("triggerType");
        storyId = serializedObject.FindProperty("storyId");
        triggerOnce = serializedObject.FindProperty("triggerOnce");

        useTransformPosition = serializedObject.FindProperty("useTransformPosition");
        triggerPositionX = serializedObject.FindProperty("triggerPositionX");
        triggerFromLeft = serializedObject.FindProperty("triggerFromLeft");

        flagName = serializedObject.FindProperty("flagName");
        waveIndex = serializedObject.FindProperty("waveIndex");

        resultActions = serializedObject.FindProperty("resultActions");
        onBeforeStory = serializedObject.FindProperty("OnBeforeStory");
        onAfterStory = serializedObject.FindProperty("OnAfterStory");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // ---- 通用配置 ----
        EditorGUILayout.LabelField("触发配置", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(triggerType, new GUIContent("触发类型"));
        EditorGUILayout.PropertyField(storyId, new GUIContent("剧情ID"));
        EditorGUILayout.PropertyField(triggerOnce, new GUIContent("只触发一次"));

        EditorGUILayout.Space(8);

        // ---- 根据类型显示对应设置 ----
        var type = (StoryTrigger.TriggerType)triggerType.enumValueIndex;

        switch (type)
        {
            case StoryTrigger.TriggerType.Position:
                EditorGUILayout.LabelField("位置触发设置", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(useTransformPosition,
                    new GUIContent("使用物体位置", "勾选后直接在Scene中拖动本物体来设定触发X坐标"));
                if (!useTransformPosition.boolValue)
                {
                    EditorGUILayout.PropertyField(triggerPositionX, new GUIContent("触发位置X"));
                }
                else
                {
                    StoryTrigger trigger = (StoryTrigger)target;
                    EditorGUILayout.HelpBox(
                        $"当前触发X = {trigger.transform.position.x:F2}\n在Scene视图中拖动物体即可调整",
                        MessageType.Info);
                }
                EditorGUILayout.PropertyField(triggerFromLeft,
                    new GUIContent("从左向右触发", "true=玩家从左往右越过时触发"));
                break;

            case StoryTrigger.TriggerType.Flag:
                EditorGUILayout.LabelField("标志位触发设置", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(flagName, new GUIContent("标志位名称"));
                break;

            case StoryTrigger.TriggerType.WaveComplete:
                EditorGUILayout.LabelField("波次触发设置", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(waveIndex,
                    new GUIContent("波次索引", "-1 = 任意波次完成时触发"));
                break;

            case StoryTrigger.TriggerType.LevelStart:
                EditorGUILayout.HelpBox("关卡开始时自动触发，无需额外设置。", MessageType.Info);
                break;

            case StoryTrigger.TriggerType.AllWavesComplete:
                EditorGUILayout.HelpBox("所有波次完成后自动触发，无需额外设置。", MessageType.Info);
                break;

            case StoryTrigger.TriggerType.Manual:
                EditorGUILayout.HelpBox("需要从其他脚本调用 TriggerManually() 来触发。", MessageType.Info);
                break;
        }

        EditorGUILayout.Space(8);

        // ---- 播放后处理 ----
        EditorGUILayout.LabelField("播放后处理", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(resultActions, new GUIContent("结果动作列表"), true);

        EditorGUILayout.Space(8);

        // ---- 事件 ----
        EditorGUILayout.LabelField("事件", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(onBeforeStory, new GUIContent("剧情播放前"));
        EditorGUILayout.PropertyField(onAfterStory, new GUIContent("剧情播放后"));

        serializedObject.ApplyModifiedProperties();
    }
}
