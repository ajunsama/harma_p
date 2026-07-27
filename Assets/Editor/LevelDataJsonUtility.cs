using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

public static class LevelDataJsonUtility
{
    public static string Export(LevelData ld)
    {
        var dto = LevelDataDto.FromLevelData(ld);
        return JsonUtility.ToJson(dto, true);
    }

    public static void Import(LevelData ld, string json)
    {
        var dto = JsonUtility.FromJson<LevelDataDto>(json);
        if (dto != null)
            dto.ToLevelData(ld);
        EditorUtility.SetDirty(ld);
    }
}

[Serializable]
public class LevelDataDto
{
    public string levelName;
    public int difficulty;
    public float levelLength;
    public string playerPrefabGuid;
    public float playerSpawnX;
    public float playerSpawnY;
    public bool playerFaceRight;
    public bool useCustomInitialCameraPosition;
    public float initialCameraX;
    public float initialCameraY;
    public float cameraDeadZone;
    public bool hasCameraSettings;
    public float levelEndPositionX;
    public string storyCollectionGuid;
    public string backgroundMode;
    public string backgroundSpriteGuid;
    public float backgroundParallax;
    public int backgroundSortingOrder;
    public bool hasBackgroundCameraBounds;
    public bool constrainCameraToBounds;
    public float backgroundCameraBoundsStartX;
    public float backgroundCameraBoundsEndX;
    public float backgroundSequenceStartX;
    public float backgroundSequenceCenterY;
    public int backgroundSequenceSortingOrder;
    public List<BackgroundSequenceEntryDto> backgroundSequence = new List<BackgroundSequenceEntryDto>();

    public List<VariableDto> variables = new List<VariableDto>();
    public List<ElementDto> elements = new List<ElementDto>();
    public List<StoryTriggerDto> storyTriggers = new List<StoryTriggerDto>();
    public List<GroupDto> groups = new List<GroupDto>();
    public List<FlowDto> events = new List<FlowDto>();
    public List<FlowDto> flows = new List<FlowDto>();

    [Serializable]
    public class VariableDto
    {
        public string name;
        public string type;
        public string defaultValue;
        public string description;
    }

    [Serializable]
    public class BackgroundSequenceEntryDto
    {
        public string spriteGuid;
        public int repeatCount;
    }

    [Serializable]
    public class ElementDto
    {
        public string id;
        public string name;
        public string type;
        public string prefabGuid;
        public float posX, posY;
        public bool faceRight;
        public float delay;
        public string groupId;
        public List<string> groupIds = new List<string>();
        public List<ConditionDto> conditions = new List<ConditionDto>();
    }

    [Serializable]
    public class ConditionDto
    {
        public string variable;
        public string mode;
        public string value;
    }

    [Serializable]
    public class StoryTriggerDto
    {
        public string triggerMode;
        public float posX;
        public string storyId;
        public bool triggerOnce;
        public bool triggerFromLeft;
        public List<ConditionDto> conditions = new List<ConditionDto>();
        public List<SetActionDto> onStartSetVariables = new List<SetActionDto>();
        public List<SetActionDto> onCompleteSetVariables = new List<SetActionDto>();
    }

    [Serializable]
    public class SetActionDto
    {
        public string variable;
        public string value;
    }

    [Serializable]
    public class GroupDto
    {
        public string id;
        public string name;
        public string triggerMode;
        public float triggerX;
        public bool mustClear;
        public List<ConditionDto> conditions = new List<ConditionDto>();
        public List<SetActionDto> onClearedSetVariables = new List<SetActionDto>();
    }

    [Serializable]
    public class FlowDto
    {
        public string id;
        public string triggerMode;
        public float posX;
        public bool triggerFromLeft;
        public bool triggerOnce;
        public List<ConditionDto> conditions = new List<ConditionDto>();
        public List<FlowStepDto> steps = new List<FlowStepDto>();
    }

    [Serializable]
    public class FlowStepDto
    {
        public string type;
        public float duration;
        public float targetX;
        public float targetY;
        public float speed;
        public float tolerance;
        public string easing;
        public SetActionDto setVariable;
        public string storyId;
        public List<string> performanceScriptIds = new List<string>();
        public List<PerformanceActorBindingDto> performanceBindings =
            new List<PerformanceActorBindingDto>();

        // 旧版格式：Cue 曾直接存放在关卡 PlayStory 步骤中，仅保留导入兼容。
        public List<StoryPerformanceCueDto> performanceCues = new List<StoryPerformanceCueDto>();
    }

    [Serializable]
    public class StoryPerformanceCueDto
    {
        public int dialogueId;
        public float delay;
        public string scriptId;
        public bool blockDialogueAdvance;
        public string triggerTiming;
        public List<PerformanceActorBindingDto> actorBindings = new List<PerformanceActorBindingDto>();
    }

    [Serializable]
    public class PerformanceActorBindingDto
    {
        public string scriptId;
        public string slotId;
        public string targetType;
        public string elementId;
        public string idleAnimationOverride;
        public string moveAnimationOverride;
    }

    public static LevelDataDto FromLevelData(LevelData ld)
    {
        ld.MigrateLegacyStoryTriggers();
        var d = new LevelDataDto
        {
            levelName = ld.levelName,
            difficulty = ld.difficulty,
            levelLength = ld.levelLength,
            playerPrefabGuid = ld.playerPrefab != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(ld.playerPrefab)) : "",
            playerSpawnX = ld.playerSpawnPosition.x,
            playerSpawnY = ld.playerSpawnPosition.y,
            playerFaceRight = ld.playerFaceRight,
            useCustomInitialCameraPosition = ld.useCustomInitialCameraPosition,
            initialCameraX = ld.initialCameraPosition.x,
            initialCameraY = ld.initialCameraPosition.y,
            cameraDeadZone = ld.cameraDeadZone,
            hasCameraSettings = true,
            levelEndPositionX = ld.levelEndPositionX,
            storyCollectionGuid = ld.storyCollectionJson != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(ld.storyCollectionJson)) : "",
            backgroundMode = ld.backgroundSettings.mode.ToString(),
            backgroundSpriteGuid = ld.backgroundSettings.singleBackground != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(ld.backgroundSettings.singleBackground)) : "",
            backgroundParallax = ld.backgroundSettings.singleParallaxFactor,
            backgroundSortingOrder = ld.backgroundSettings.singleSortingOrder,
            hasBackgroundCameraBounds = true,
            constrainCameraToBounds = ld.backgroundSettings.constrainCameraToBounds,
            backgroundCameraBoundsStartX = ld.backgroundSettings.cameraBoundsStartX,
            backgroundCameraBoundsEndX = ld.backgroundSettings.cameraBoundsEndX,
            backgroundSequenceStartX = ld.backgroundSettings.sequenceStartX,
            backgroundSequenceCenterY = ld.backgroundSettings.sequenceCenterY,
            backgroundSequenceSortingOrder = ld.backgroundSettings.sequenceSortingOrder
        };

        if (ld.backgroundSettings.sequence != null)
        {
            foreach (var entry in ld.backgroundSettings.sequence)
            {
                d.backgroundSequence.Add(new BackgroundSequenceEntryDto
                {
                    spriteGuid = entry?.sprite != null
                        ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(entry.sprite))
                        : "",
                    repeatCount = entry?.repeatCount ?? 1
                });
            }
        }

        foreach (var v in ld.variables)
            d.variables.Add(new VariableDto { name = v.variableName, type = v.type.ToString(), defaultValue = v.defaultValue, description = v.description });

        foreach (var el in ld.elements)
        {
            string guid = el.prefab != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(el.prefab)) : "";
            var conds = new List<ConditionDto>();
            if (el.appearConditions != null)
                foreach (var c in el.appearConditions)
                    conds.Add(new ConditionDto { variable = c.variableName, mode = c.mode.ToString(), value = c.compareValue });

            d.elements.Add(new ElementDto
            {
                id = el.elementId, name = el.displayName, type = el.elementType.ToString(),
                prefabGuid = guid, posX = el.position.x, posY = el.position.y,
                faceRight = el.faceRight, delay = el.appearDelay, groupId = el.groupId,
                groupIds = el.groupIds != null ? new List<string>(el.groupIds) : new List<string>(),
                conditions = conds
            });
        }

        foreach (var st in ld.storyTriggers)
        {
            var dto = new StoryTriggerDto { triggerMode = st.triggerMode.ToString(), posX = st.positionX, storyId = st.storyId, triggerOnce = st.triggerOnce, triggerFromLeft = st.triggerFromLeft };
            if (st.triggerConditions != null)
                foreach (var c in st.triggerConditions)
                    dto.conditions.Add(new ConditionDto { variable = c.variableName, mode = c.mode.ToString(), value = c.compareValue });
            if (st.onStoryStartSetVariables != null)
                foreach (var a in st.onStoryStartSetVariables)
                    dto.onStartSetVariables.Add(new SetActionDto { variable = a.variableName, value = a.stringValue });
            if (st.onStoryCompleteSetVariables != null)
                foreach (var a in st.onStoryCompleteSetVariables)
                    dto.onCompleteSetVariables.Add(new SetActionDto { variable = a.variableName, value = a.stringValue });
            d.storyTriggers.Add(dto);
        }

        foreach (var g in ld.groups)
        {
            var dto = new GroupDto { id = g.groupId, name = g.groupName, triggerMode = g.triggerMode.ToString(), triggerX = g.triggerPositionX, mustClear = g.mustClearToProceed };
            if (g.triggerConditions != null)
                foreach (var c in g.triggerConditions)
                    dto.conditions.Add(new ConditionDto { variable = c.variableName, mode = c.mode.ToString(), value = c.compareValue });
            if (g.onAllEnemiesClearedSetVariables != null)
                foreach (var a in g.onAllEnemiesClearedSetVariables)
                    dto.onClearedSetVariables.Add(new SetActionDto { variable = a.variableName, value = a.stringValue });
            d.groups.Add(dto);
        }

        if (ld.events != null)
        {
            foreach (var flow in ld.events)
            {
                if (flow == null) continue;
                var dto = new FlowDto
                {
                    id = flow.flowId,
                    triggerMode = flow.triggerMode.ToString(),
                    posX = flow.positionX,
                    triggerFromLeft = flow.triggerFromLeft,
                    triggerOnce = flow.triggerOnce
                };
                if (flow.triggerConditions != null)
                    foreach (var c in flow.triggerConditions)
                        dto.conditions.Add(new ConditionDto { variable = c.variableName, mode = c.mode.ToString(), value = c.compareValue });
                if (flow.steps != null)
                    foreach (var step in flow.steps)
                    {
                        if (step == null) continue;
                        var stepDto = new FlowStepDto
                        {
                            type = step.stepType.ToString(), duration = step.duration,
                            targetX = step.targetPosition.x, targetY = step.targetPosition.y,
                            speed = step.speed, tolerance = step.tolerance, easing = step.easing.ToString(),
                            setVariable = step.setVariable == null ? null : new SetActionDto { variable = step.setVariable.variableName, value = step.setVariable.stringValue },
                            storyId = step.storyId
                        };
                        if (step.storyPerformanceScripts != null)
                            foreach (var script in step.storyPerformanceScripts)
                                if (script != null && !string.IsNullOrWhiteSpace(script.scriptId) &&
                                    !stepDto.performanceScriptIds.Contains(script.scriptId))
                                    stepDto.performanceScriptIds.Add(script.scriptId);
                        if (step.storyCastBindings != null)
                            foreach (var binding in step.storyCastBindings)
                            {
                                if (binding == null) continue;
                                stepDto.performanceBindings.Add(new PerformanceActorBindingDto
                                {
                                    scriptId = binding.scriptId,
                                    slotId = binding.slotId,
                                    targetType = binding.targetType.ToString(),
                                    elementId = binding.elementId,
                                    idleAnimationOverride = binding.idleAnimationOverride,
                                    moveAnimationOverride = binding.moveAnimationOverride
                                });
                            }

                        // 没有新格式数据时才继续导出旧 Cue，保证已有未迁移关卡可往返。
                        if (stepDto.performanceScriptIds.Count == 0 &&
                            stepDto.performanceBindings.Count == 0 &&
                            step.storyPerformanceCues != null)
                            foreach (var cue in step.storyPerformanceCues)
                            {
                                if (cue == null) continue;
                                var cueDto = new StoryPerformanceCueDto
                                {
                                    dialogueId = cue.dialogueId,
                                    delay = cue.delay,
                                    scriptId = cue.performanceScript != null ? cue.performanceScript.scriptId : "",
                                    blockDialogueAdvance = cue.blockDialogueAdvance,
                                    triggerTiming = cue.triggerTiming.ToString()
                                };
                                if (cue.actorBindings != null)
                                    foreach (var binding in cue.actorBindings)
                                    {
                                        if (binding == null) continue;
                                        cueDto.actorBindings.Add(new PerformanceActorBindingDto
                                        {
                                            scriptId = binding.scriptId,
                                            slotId = binding.slotId,
                                            targetType = binding.targetType.ToString(),
                                            elementId = binding.elementId,
                                            idleAnimationOverride = binding.idleAnimationOverride,
                                            moveAnimationOverride = binding.moveAnimationOverride
                                        });
                                    }
                                stepDto.performanceCues.Add(cueDto);
                            }
                        dto.steps.Add(stepDto);
                    }
                d.events.Add(dto);
            }
        }

        return d;
    }

    public void ToLevelData(LevelData ld)
    {
        ld.levelName = levelName;
        ld.difficulty = difficulty;
        ld.levelLength = levelLength;
        ld.playerPrefab = !string.IsNullOrEmpty(playerPrefabGuid) ? AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(playerPrefabGuid)) : null;
        ld.playerSpawnPosition = new Vector2(playerSpawnX, playerSpawnY);
        ld.playerFaceRight = playerFaceRight;
        ld.useCustomInitialCameraPosition = useCustomInitialCameraPosition;
        ld.initialCameraPosition = new Vector2(initialCameraX, initialCameraY);
        ld.cameraDeadZone = hasCameraSettings ? Mathf.Clamp(cameraDeadZone, 0f, 0.45f) : 0.2f;
        ld.levelEndPositionX = levelEndPositionX;
        ld.storyCollectionJson = !string.IsNullOrEmpty(storyCollectionGuid) ? AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(storyCollectionGuid)) : null;

        if (ld.backgroundSettings == null)
            ld.backgroundSettings = new BackgroundSettings();
        if (!string.IsNullOrEmpty(backgroundMode))
            ld.backgroundSettings.mode = (BackgroundMode)Enum.Parse(typeof(BackgroundMode), backgroundMode);
        ld.backgroundSettings.singleBackground = !string.IsNullOrEmpty(backgroundSpriteGuid) ? AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(backgroundSpriteGuid)) : null;
        ld.backgroundSettings.singleParallaxFactor = backgroundParallax;
        ld.backgroundSettings.singleSortingOrder = backgroundSortingOrder;
        ld.backgroundSettings.constrainCameraToBounds =
            hasBackgroundCameraBounds && constrainCameraToBounds;
        ld.backgroundSettings.cameraBoundsStartX = hasBackgroundCameraBounds
            ? backgroundCameraBoundsStartX
            : Mathf.Min(0f, ld.playerSpawnPosition.x);
        ld.backgroundSettings.cameraBoundsEndX = hasBackgroundCameraBounds
            ? backgroundCameraBoundsEndX
            : Mathf.Max(ld.levelLength, ld.levelEndPositionX);
        ld.backgroundSettings.sequenceStartX = backgroundSequenceStartX;
        ld.backgroundSettings.sequenceCenterY = backgroundSequenceCenterY;
        ld.backgroundSettings.sequenceSortingOrder = backgroundSequenceSortingOrder;
        ld.backgroundSettings.sequence = new List<BackgroundSequenceEntry>();
        if (backgroundSequence != null)
        {
            foreach (var entry in backgroundSequence)
            {
                if (entry == null) continue;
                ld.backgroundSettings.sequence.Add(new BackgroundSequenceEntry
                {
                    sprite = !string.IsNullOrEmpty(entry.spriteGuid)
                        ? AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(entry.spriteGuid))
                        : null,
                    repeatCount = Mathf.Max(1, entry.repeatCount)
                });
            }
        }

        ld.variables.Clear();
        if (variables != null)
            foreach (var v in variables)
                ld.variables.Add(new LevelVariableDefinition { variableName = v.name, type = (LevelVariableType)Enum.Parse(typeof(LevelVariableType), v.type), defaultValue = v.defaultValue, description = v.description });

        ld.elements.Clear();
        if (elements != null)
            foreach (var e in elements)
            {
                var conds = new List<LevelVariableCondition>();
                if (e.conditions != null)
                    foreach (var c in e.conditions)
                        conds.Add(new LevelVariableCondition { variableName = c.variable, mode = (LevelVariableCondition.CompareMode)Enum.Parse(typeof(LevelVariableCondition.CompareMode), c.mode), compareValue = c.value });

                ld.elements.Add(new LevelElement
                {
                    elementId = e.id, displayName = e.name, elementType = (ElementType)Enum.Parse(typeof(ElementType), e.type),
                    prefab = !string.IsNullOrEmpty(e.prefabGuid) ? AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(e.prefabGuid)) : null,
                    position = new Vector2(e.posX, e.posY), faceRight = e.faceRight,
                    appearDelay = e.delay, groupId = e.groupId,
                    groupIds = e.groupIds != null ? new List<string>(e.groupIds) : new List<string>(),
                    appearConditions = conds
                });
            }

        ld.storyTriggers.Clear();
        if (storyTriggers != null)
            foreach (var s in storyTriggers)
            {
                var conditions = new List<LevelVariableCondition>();
                if (s.conditions != null)
                    foreach (var c in s.conditions)
                        conditions.Add(new LevelVariableCondition { variableName = c.variable, mode = (LevelVariableCondition.CompareMode)Enum.Parse(typeof(LevelVariableCondition.CompareMode), c.mode), compareValue = c.value });

                var startActions = new List<VariableSetAction>();
                if (s.onStartSetVariables != null)
                    foreach (var a in s.onStartSetVariables)
                        startActions.Add(new VariableSetAction { variableName = a.variable, stringValue = a.value });

                var completeActions = new List<VariableSetAction>();
                if (s.onCompleteSetVariables != null)
                    foreach (var a in s.onCompleteSetVariables)
                        completeActions.Add(new VariableSetAction { variableName = a.variable, stringValue = a.value });

                ld.storyTriggers.Add(new StoryTriggerPoint
                {
                    triggerMode = string.IsNullOrEmpty(s.triggerMode) ? StoryTriggerMode.Position : (StoryTriggerMode)Enum.Parse(typeof(StoryTriggerMode), s.triggerMode),
                    positionX = s.posX,
                    storyId = s.storyId,
                    triggerOnce = s.triggerOnce,
                    triggerFromLeft = s.triggerFromLeft,
                    triggerConditions = conditions,
                    onStoryStartSetVariables = startActions,
                    onStoryCompleteSetVariables = completeActions
                });
            }

        ld.groups.Clear();
        if (groups != null)
            foreach (var g in groups)
            {
                var conditions = new List<LevelVariableCondition>();
                if (g.conditions != null)
                    foreach (var c in g.conditions)
                        conditions.Add(new LevelVariableCondition { variableName = c.variable, mode = (LevelVariableCondition.CompareMode)Enum.Parse(typeof(LevelVariableCondition.CompareMode), c.mode), compareValue = c.value });

                var clearedActions = new List<VariableSetAction>();
                if (g.onClearedSetVariables != null)
                    foreach (var a in g.onClearedSetVariables)
                        clearedActions.Add(new VariableSetAction { variableName = a.variable, stringValue = a.value });

                ld.groups.Add(new ElementGroup
                {
                    groupId = g.id,
                    groupName = g.name,
                    triggerMode = string.IsNullOrEmpty(g.triggerMode) ? ElementGroupTriggerMode.Position : (ElementGroupTriggerMode)Enum.Parse(typeof(ElementGroupTriggerMode), g.triggerMode),
                    triggerPositionX = g.triggerX,
                    mustClearToProceed = g.mustClear,
                    triggerConditions = conditions,
                    onAllEnemiesClearedSetVariables = clearedActions
                });
            }

        if (ld.events == null) ld.events = new List<LevelFlowData>();
        ld.events.Clear();
        var importedEvents = events != null && events.Count > 0 ? events : flows;
        if (importedEvents != null)
            foreach (var f in importedEvents)
            {
                var flow = new LevelFlowData
                {
                    flowId = f.id,
                    triggerMode = string.IsNullOrEmpty(f.triggerMode) ? StoryTriggerMode.Conditions : (StoryTriggerMode)Enum.Parse(typeof(StoryTriggerMode), f.triggerMode),
                    positionX = f.posX,
                    triggerFromLeft = f.triggerFromLeft,
                    triggerOnce = f.triggerOnce,
                    triggerConditions = new List<LevelVariableCondition>(),
                    steps = new List<LevelFlowStep>()
                };
                if (f.conditions != null)
                    foreach (var c in f.conditions)
                        flow.triggerConditions.Add(new LevelVariableCondition { variableName = c.variable, mode = (LevelVariableCondition.CompareMode)Enum.Parse(typeof(LevelVariableCondition.CompareMode), c.mode), compareValue = c.value });
                if (f.steps != null)
                    foreach (var s in f.steps)
                    {
                        var step = new LevelFlowStep
                        {
                            stepType = string.IsNullOrEmpty(s.type) ? LevelFlowStepType.WaitForPlayerSafe : (LevelFlowStepType)Enum.Parse(typeof(LevelFlowStepType), s.type),
                            duration = s.duration,
                            targetPosition = new Vector2(s.targetX, s.targetY),
                            speed = s.speed > 0f ? s.speed : 3f,
                            tolerance = s.tolerance > 0f ? s.tolerance : 0.05f,
                            easing = string.IsNullOrEmpty(s.easing) ? LevelFlowEasing.SmoothStep : (LevelFlowEasing)Enum.Parse(typeof(LevelFlowEasing), s.easing),
                            setVariable = s.setVariable == null ? new VariableSetAction() : new VariableSetAction { variableName = s.setVariable.variable, stringValue = s.setVariable.value },
                            storyId = s.storyId
                        };
                        if (s.performanceScriptIds != null)
                            foreach (var scriptId in s.performanceScriptIds)
                            {
                                var script = FindPerformanceScript(scriptId);
                                if (script != null && !step.storyPerformanceScripts.Contains(script))
                                    step.storyPerformanceScripts.Add(script);
                            }
                        if (s.performanceBindings != null)
                            foreach (var bindingDto in s.performanceBindings)
                            {
                                step.storyCastBindings.Add(new PerformanceActorBinding
                                {
                                    scriptId = bindingDto.scriptId,
                                    slotId = bindingDto.slotId,
                                    targetType = string.IsNullOrEmpty(bindingDto.targetType)
                                        ? PerformanceActorTargetType.Player
                                        : (PerformanceActorTargetType)Enum.Parse(
                                            typeof(PerformanceActorTargetType), bindingDto.targetType),
                                    elementId = bindingDto.elementId,
                                    idleAnimationOverride = bindingDto.idleAnimationOverride,
                                    moveAnimationOverride = bindingDto.moveAnimationOverride
                                });
                            }
                        if (s.performanceCues != null)
                            foreach (var cueDto in s.performanceCues)
                            {
                                var cue = new StoryPerformanceCue
                                {
                                    dialogueId = cueDto.dialogueId,
                                    delay = cueDto.delay,
                                    performanceScript = FindPerformanceScript(cueDto.scriptId),
                                    blockDialogueAdvance = cueDto.blockDialogueAdvance,
                                    triggerTiming = string.IsNullOrEmpty(cueDto.triggerTiming)
                                        ? StoryPerformanceCueTriggerTiming.DialogueStart
                                        : (StoryPerformanceCueTriggerTiming)Enum.Parse(
                                            typeof(StoryPerformanceCueTriggerTiming), cueDto.triggerTiming),
                                    actorBindings = new List<PerformanceActorBinding>()
                                };
                                if (cueDto.actorBindings != null)
                                    foreach (var bindingDto in cueDto.actorBindings)
                                    {
                                        cue.actorBindings.Add(new PerformanceActorBinding
                                        {
                                            scriptId = bindingDto.scriptId,
                                            slotId = bindingDto.slotId,
                                            targetType = string.IsNullOrEmpty(bindingDto.targetType)
                                                ? PerformanceActorTargetType.Player
                                                : (PerformanceActorTargetType)Enum.Parse(typeof(PerformanceActorTargetType), bindingDto.targetType),
                                            elementId = bindingDto.elementId,
                                            idleAnimationOverride = bindingDto.idleAnimationOverride,
                                            moveAnimationOverride = bindingDto.moveAnimationOverride
                                        });
                                    }
                                step.storyPerformanceCues.Add(cue);
                            }
                        flow.steps.Add(step);
                    }
                ld.events.Add(flow);
            }

        ld.MigrateLegacyStoryTriggers();
    }

    static PerformanceScript FindPerformanceScript(string scriptId)
    {
        if (string.IsNullOrWhiteSpace(scriptId)) return null;
        PerformanceScript found = null;
        foreach (string guid in AssetDatabase.FindAssets("t:PerformanceScript"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<PerformanceScript>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset == null || asset.scriptId != scriptId) continue;
            if (found != null && found != asset)
            {
                Debug.LogError($"[LevelDataJsonUtility] 演出脚本 ID 重复: {scriptId}");
                return found;
            }
            found = asset;
        }
        if (found == null)
            Debug.LogError($"[LevelDataJsonUtility] 找不到演出脚本: {scriptId}");
        return found;
    }
}
