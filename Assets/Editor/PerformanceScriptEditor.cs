using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PerformanceScript))]
public class PerformanceScriptEditor : Editor
{
    SerializedProperty scriptId;
    SerializedProperty actorSlots;
    SerializedProperty clips;
    SerializedProperty restoreCameraFollow;

    void OnEnable()
    {
        scriptId = serializedObject.FindProperty("scriptId");
        actorSlots = serializedObject.FindProperty("actorSlots");
        clips = serializedObject.FindProperty("clips");
        restoreCameraFollow = serializedObject.FindProperty("restoreCameraFollowOnStoryEnd");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("演出脚本", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(scriptId, new GUIContent("脚本 ID"));
        EditorGUILayout.PropertyField(restoreCameraFollow, new GUIContent("剧情结束恢复镜头跟随"));
        DrawDuplicateIdWarning();

        EditorGUILayout.Space(8);
        DrawActorSlots();
        EditorGUILayout.Space(8);
        DrawClips();

        serializedObject.ApplyModifiedProperties();
    }

    void DrawActorSlots()
    {
        EditorGUILayout.LabelField("角色槽位", EditorStyles.boldLabel);
        for (int i = 0; i < actorSlots.arraySize; i++)
        {
            var slot = actorSlots.GetArrayElementAtIndex(i);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"槽位 {i + 1}", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("删除", GUILayout.Width(44)))
            {
                actorSlots.DeleteArrayElementAtIndex(i);
                break;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.PropertyField(slot.FindPropertyRelative("slotId"), new GUIContent("槽位 ID"));
            EditorGUILayout.PropertyField(slot.FindPropertyRelative("displayName"), new GUIContent("显示名称"));
            EditorGUILayout.PropertyField(slot.FindPropertyRelative("defaultIdleAnimation"), new GUIContent("默认待机动画"));
            EditorGUILayout.PropertyField(slot.FindPropertyRelative("defaultMoveAnimation"), new GUIContent("默认移动动画"));
            EditorGUILayout.EndVertical();
        }
        if (GUILayout.Button("+ 添加角色槽位"))
        {
            int index = actorSlots.arraySize;
            actorSlots.InsertArrayElementAtIndex(actorSlots.arraySize);
            var slot = actorSlots.GetArrayElementAtIndex(index);
            slot.FindPropertyRelative("slotId").stringValue = $"actor_{index + 1}";
            slot.FindPropertyRelative("displayName").stringValue = $"角色 {index + 1}";
            slot.FindPropertyRelative("defaultIdleAnimation").stringValue = "idle";
            slot.FindPropertyRelative("defaultMoveAnimation").stringValue = "run";
        }
    }

    void DrawClips()
    {
        EditorGUILayout.LabelField("动作片段", EditorStyles.boldLabel);
        string[] slotIds = Enumerable.Range(0, actorSlots.arraySize)
            .Select(index => actorSlots.GetArrayElementAtIndex(index)
                .FindPropertyRelative("slotId").stringValue)
            .Where(value => !string.IsNullOrEmpty(value)).ToArray();

        for (int i = 0; i < clips.arraySize; i++)
        {
            var clip = clips.GetArrayElementAtIndex(i);
            var typeProperty = clip.FindPropertyRelative("clipType");
            var type = (PerformanceClipType)typeProperty.enumValueIndex;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"片段 {i + 1}", EditorStyles.miniBoldLabel);
            using (new EditorGUI.DisabledScope(i == 0))
                if (GUILayout.Button("↑", GUILayout.Width(24)))
                    clips.MoveArrayElement(i, i - 1);
            using (new EditorGUI.DisabledScope(i == clips.arraySize - 1))
                if (GUILayout.Button("↓", GUILayout.Width(24)))
                    clips.MoveArrayElement(i, i + 1);
            if (GUILayout.Button("删除", GUILayout.Width(44)))
            {
                clips.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(clip.FindPropertyRelative("clipName"), new GUIContent("片段名称"));
            EditorGUILayout.PropertyField(typeProperty, new GUIContent("动作类型"));
            EditorGUILayout.PropertyField(clip.FindPropertyRelative("startTime"), new GUIContent("开始时间"));
            EditorGUILayout.PropertyField(clip.FindPropertyRelative("duration"), new GUIContent("持续时间"));

            if (type != PerformanceClipType.MoveCamera)
                DrawSlotPopup(clip.FindPropertyRelative("actorSlotId"), slotIds);

            switch (type)
            {
                case PerformanceClipType.MoveActor:
                    DrawMovementFields(clip);
                    EditorGUILayout.PropertyField(clip.FindPropertyRelative("autoFaceMovement"), new GUIContent("自动面向移动方向"));
                    EditorGUILayout.PropertyField(clip.FindPropertyRelative("playMoveAnimation"), new GUIContent("移动时自动播放动画"));
                    if (clip.FindPropertyRelative("playMoveAnimation").boolValue)
                    {
                        EditorGUILayout.PropertyField(clip.FindPropertyRelative("moveAnimationOverride"), new GUIContent("移动动画覆盖"));
                        EditorGUILayout.PropertyField(clip.FindPropertyRelative("moveAnimationTrack"), new GUIContent("动画 Track / Layer"));
                        EditorGUILayout.PropertyField(clip.FindPropertyRelative("moveAnimationMixDuration"), new GUIContent("动画混合时间"));
                        EditorGUILayout.PropertyField(clip.FindPropertyRelative("restoreIdleAfterMove"), new GUIContent("移动结束恢复待机"));
                    }
                    break;
                case PerformanceClipType.PlayAnimation:
                    EditorGUILayout.PropertyField(clip.FindPropertyRelative("animationName"), new GUIContent("动画名称"));
                    EditorGUILayout.PropertyField(clip.FindPropertyRelative("loopAnimation"), new GUIContent("循环"));
                    EditorGUILayout.PropertyField(clip.FindPropertyRelative("animationTrack"), new GUIContent("Spine Track / Animator Layer"));
                    EditorGUILayout.PropertyField(clip.FindPropertyRelative("animationMixDuration"), new GUIContent("混合时间"));
                    EditorGUILayout.PropertyField(clip.FindPropertyRelative("keepAnimationAfterStory"), new GUIContent("剧情结束保留动画"));
                    break;
                case PerformanceClipType.SetFacing:
                    EditorGUILayout.PropertyField(clip.FindPropertyRelative("faceRight"), new GUIContent("朝向右边"));
                    break;
                case PerformanceClipType.MoveCamera:
                    DrawMovementFields(clip);
                    break;
            }
            EditorGUILayout.EndVertical();
        }

        if (GUILayout.Button("+ 添加动作片段"))
        {
            int index = clips.arraySize;
            clips.InsertArrayElementAtIndex(clips.arraySize);
            var clip = clips.GetArrayElementAtIndex(index);
            clip.FindPropertyRelative("clipName").stringValue = $"片段 {index + 1}";
            clip.FindPropertyRelative("clipType").enumValueIndex = 0;
            clip.FindPropertyRelative("startTime").floatValue = 0f;
            clip.FindPropertyRelative("duration").floatValue = 1f;
            clip.FindPropertyRelative("actorSlotId").stringValue = slotIds.FirstOrDefault() ?? "";
            clip.FindPropertyRelative("positionMode").enumValueIndex =
                (int)PerformancePositionMode.RelativeToStart;
            clip.FindPropertyRelative("targetPosition").vector2Value = Vector2.zero;
            clip.FindPropertyRelative("autoFaceMovement").boolValue = true;
            clip.FindPropertyRelative("playMoveAnimation").boolValue = true;
            clip.FindPropertyRelative("moveAnimationOverride").stringValue = "";
            clip.FindPropertyRelative("moveAnimationTrack").intValue = 0;
            clip.FindPropertyRelative("moveAnimationMixDuration").floatValue = 0.1f;
            clip.FindPropertyRelative("restoreIdleAfterMove").boolValue = true;
            clip.FindPropertyRelative("animationName").stringValue = "";
            clip.FindPropertyRelative("loopAnimation").boolValue = false;
            clip.FindPropertyRelative("animationTrack").intValue = 0;
            clip.FindPropertyRelative("animationMixDuration").floatValue = 0.1f;
            clip.FindPropertyRelative("keepAnimationAfterStory").boolValue = false;
            clip.FindPropertyRelative("faceRight").boolValue = true;
        }
    }

    static void DrawMovementFields(SerializedProperty clip)
    {
        var modeProperty = clip.FindPropertyRelative("positionMode");
        EditorGUILayout.PropertyField(modeProperty, new GUIContent("坐标模式"));
        var mode = (PerformancePositionMode)modeProperty.enumValueIndex;
        string positionLabel = mode == PerformancePositionMode.World
            ? "目标世界坐标"
            : "相对起点位移";
        EditorGUILayout.PropertyField(clip.FindPropertyRelative("targetPosition"),
            new GUIContent(positionLabel));
        if (mode == PerformancePositionMode.World)
            EditorGUILayout.HelpBox(
                "World 表示最终世界坐标，不是移动量。例如角色从 (3,-3) 到 (5,2)，实际位移是 X+2、Y+5。",
                MessageType.Info);
        EditorGUILayout.PropertyField(clip.FindPropertyRelative("easing"), new GUIContent("缓动方式"));
    }

    static void DrawSlotPopup(SerializedProperty slotProperty, string[] slotIds)
    {
        if (slotIds.Length == 0)
        {
            EditorGUILayout.PropertyField(slotProperty, new GUIContent("角色槽位"));
            EditorGUILayout.HelpBox("请先添加角色槽位。", MessageType.Warning);
            return;
        }

        int index = Mathf.Max(0, Array.IndexOf(slotIds, slotProperty.stringValue));
        index = EditorGUILayout.Popup("角色槽位", index, slotIds);
        slotProperty.stringValue = slotIds[index];
    }

    void DrawDuplicateIdWarning()
    {
        string id = scriptId.stringValue;
        if (string.IsNullOrWhiteSpace(id))
        {
            EditorGUILayout.HelpBox("脚本 ID 不能为空。", MessageType.Error);
            return;
        }

        int matches = AssetDatabase.FindAssets("t:PerformanceScript")
            .Select(guid => AssetDatabase.LoadAssetAtPath<PerformanceScript>(
                AssetDatabase.GUIDToAssetPath(guid)))
            .Count(asset => asset != null && asset.scriptId == id);
        if (matches > 1)
            EditorGUILayout.HelpBox($"脚本 ID '{id}' 在项目中重复。", MessageType.Error);
    }
}
