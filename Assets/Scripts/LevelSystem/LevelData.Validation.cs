using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class LevelData
{
    public LevelValidationResult Validate()
    {
        MigrateLegacyStoryTriggers();
        MigrateLegacyBackground();
        var result = new LevelValidationResult();

        if (string.IsNullOrEmpty(levelName))
            result.AddError("关卡名称不能为空");

        if (playerPrefab == null)
            result.AddError("玩家预制体不能为空");

        if (levelEndPositionX <= playerSpawnPosition.x)
            result.AddError($"终点位置X({levelEndPositionX})必须大于起点X({playerSpawnPosition.x})");

        if (backgroundSettings != null && backgroundSettings.constrainCameraToBounds &&
            backgroundSettings.cameraBoundsEndX <= backgroundSettings.cameraBoundsStartX)
            result.AddError(
                $"摄像机边界终点 X({backgroundSettings.cameraBoundsEndX})必须大于起点 X({backgroundSettings.cameraBoundsStartX})");

        var elementIds = new HashSet<string>();
        for (int i = 0; i < elements.Count; i++)
        {
            var el = elements[i];
            if (el.prefab == null)
                result.AddError($"元素 #{i} '{el.displayName}' 引用的预制体为空");
            if (string.IsNullOrEmpty(el.elementId))
                result.AddError($"元素 #{i} '{el.displayName}' 缺少elementId");
            else if (!elementIds.Add(el.elementId))
                result.AddError($"重复的元素 ID: {el.elementId}");
        }

        for (int i = 0; i < storyTriggers.Count; i++)
        {
            var st = storyTriggers[i];
            if (string.IsNullOrEmpty(st.storyId))
                result.AddError($"过场触发器 #{i} 的storyId为空");
        }

        for (int i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            if (g.triggerPositionX < 0 || g.triggerPositionX > levelLength)
                result.AddError($"元素组 '{g.groupName}' 的触发位置X({g.triggerPositionX})超出关卡范围(0~{levelLength})");
        }

        var environmentActorIds = new HashSet<string>();
        foreach (var actor in environmentActors ?? new List<EnvironmentActorData>())
        {
            if (actor == null) continue;
            if (actor.prefab == null)
                result.AddError($"Environment actor '{actor.displayName}' has no prefab");
            if (string.IsNullOrWhiteSpace(actor.actorId))
                result.AddError($"Environment actor '{actor.displayName}' has no actor ID");
            else if (!environmentActorIds.Add(actor.actorId))
                result.AddError($"Duplicate environment actor ID: {actor.actorId}");
            if (actor.SortingOrder < short.MinValue || actor.SortingOrder > short.MaxValue)
                result.AddError($"Environment actor '{actor.displayName}' has an out-of-range sorting order");
            foreach (var binding in actor.continuousBindings ?? new List<EnvironmentContinuousBinding>())
                if (binding != null && string.IsNullOrWhiteSpace(binding.animatorParameter))
                    result.AddError($"Environment actor '{actor.displayName}' has an empty Animator parameter");
            foreach (var trigger in actor.triggers ?? new List<EnvironmentActorTrigger>())
            {
                if (trigger == null) continue;
                if (trigger.maxValue < trigger.minValue)
                    result.AddError($"Environment actor '{actor.displayName}' has an inverted trigger range");
                if (trigger.triggerType == EnvironmentTriggerType.PlayerSignal &&
                    string.IsNullOrWhiteSpace(trigger.signalId))
                    result.AddError($"Environment actor '{actor.displayName}' has an empty player signal");
            }
        }

        var variableNames = new HashSet<string>(variables.Select(v => v.variableName));
        foreach (var el in elements)
        {
            foreach (var cond in el.appearConditions)
            {
                if (!string.IsNullOrEmpty(cond.variableName) && !variableNames.Contains(cond.variableName))
                    result.AddError($"元素 '{el.displayName}' 引用了未定义的变量'{cond.variableName}'");
            }
        }
        foreach (var actor in environmentActors ?? new List<EnvironmentActorData>())
        {
            if (actor == null) continue;
            var conditions = new List<LevelVariableCondition>();
            if (actor.activeConditions != null) conditions.AddRange(actor.activeConditions);
            foreach (var trigger in actor.triggers ?? new List<EnvironmentActorTrigger>())
                if (trigger?.conditions != null) conditions.AddRange(trigger.conditions);
            foreach (var cond in conditions)
                if (cond != null && !string.IsNullOrEmpty(cond.variableName) && !variableNames.Contains(cond.variableName))
                    result.AddError($"Environment actor '{actor.displayName}' references undefined variable '{cond.variableName}'");

            Animator animator = actor.prefab != null
                ? actor.prefab.GetComponentInChildren<Animator>(true)
                : null;
            var animatorParameters = animator != null
                ? animator.parameters.ToDictionary(parameter => parameter.name, parameter => parameter.type)
                : new Dictionary<string, AnimatorControllerParameterType>();

            foreach (var binding in actor.continuousBindings ?? new List<EnvironmentContinuousBinding>())
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.animatorParameter)) continue;
                if (!animatorParameters.TryGetValue(binding.animatorParameter, out var parameterType) ||
                    parameterType != AnimatorControllerParameterType.Float)
                    result.AddError(
                        $"Environment actor '{actor.displayName}' references missing/non-float Animator parameter '{binding.animatorParameter}'");
            }

            foreach (var action in EnumerateEnvironmentActions(actor))
            {
                if (action.actionType == EnvironmentActionType.SetLevelVariable &&
                    !string.IsNullOrWhiteSpace(action.name) && !variableNames.Contains(action.name))
                    result.AddError(
                        $"Environment actor '{actor.displayName}' action references undefined variable '{action.name}'");

                AnimatorControllerParameterType? expectedType = null;
                switch (action.actionType)
                {
                    case EnvironmentActionType.SetAnimatorFloat:
                        expectedType = AnimatorControllerParameterType.Float;
                        break;
                    case EnvironmentActionType.SetAnimatorBool:
                        expectedType = AnimatorControllerParameterType.Bool;
                        break;
                    case EnvironmentActionType.SetAnimatorTrigger:
                        expectedType = AnimatorControllerParameterType.Trigger;
                        break;
                }
                if (expectedType.HasValue &&
                    (!animatorParameters.TryGetValue(action.name ?? "", out var actualType) ||
                     actualType != expectedType.Value))
                    result.AddError(
                        $"Environment actor '{actor.displayName}' references missing/wrong-type Animator parameter '{action.name}'");
            }
        }
        foreach (var cond in endConditions)
        {
            if (!string.IsNullOrEmpty(cond.variableName) && !variableNames.Contains(cond.variableName))
                result.AddError($"结束条件引用了未定义的变量'{cond.variableName}'");
        }
        foreach (var st in storyTriggers)
        {
            foreach (var cond in st.triggerConditions)
            {
                if (!string.IsNullOrEmpty(cond.variableName) && !variableNames.Contains(cond.variableName))
                    result.AddError($"过场触发器(storyId={st.storyId})引用了未定义的变量'{cond.variableName}'");
            }
        }
        foreach (var g in groups)
        {
            foreach (var cond in g.triggerConditions)
            {
                if (!string.IsNullOrEmpty(cond.variableName) && !variableNames.Contains(cond.variableName))
                    result.AddError($"元素组'{g.groupName}'引用了未定义的变量'{cond.variableName}'");
            }
        }


        var flowIds = new HashSet<string>();
        HashSet<string> storyIds = null;
        var storiesById = new Dictionary<string, StorySequence>();
        var dialogueIdsByStory = new Dictionary<string, HashSet<int>>();
        if (storyCollectionJson != null)
        {
            try
            {
                var storyData = JsonUtility.FromJson<StoryDataCollection>(storyCollectionJson.text);
                storyIds = new HashSet<string>((storyData?.stories ?? new List<StorySequence>())
                    .Where(s => s != null && !string.IsNullOrEmpty(s.storyId))
                    .Select(s => s.storyId));
                foreach (var story in storyData?.stories ?? new List<StorySequence>())
                {
                    if (story == null || string.IsNullOrEmpty(story.storyId)) continue;
                    var dialogueIds = new HashSet<int>();
                    foreach (var dialogue in story.dialogues ?? new List<StoryDialogue>())
                    {
                        if (dialogue == null || dialogue.id <= 0 || !dialogueIds.Add(dialogue.id))
                            result.AddError($"Story '{story.storyId}' contains an invalid or duplicate dialogue ID");
                    }
                    dialogueIdsByStory[story.storyId] = dialogueIds;
                    storiesById[story.storyId] = story;
                }
            }
            catch (Exception e)
            {
                result.AddError($"Story collection JSON is invalid: {e.Message}");
            }
        }
        foreach (var flow in events ?? new List<LevelFlowData>())
        {
            if (flow == null || string.IsNullOrWhiteSpace(flow.flowId))
            {
                result.AddError("Level flow ID cannot be empty");
                continue;
            }
            if (!flowIds.Add(flow.flowId))
                result.AddError($"Duplicate level flow ID: {flow.flowId}");
            if (flow.steps == null || flow.steps.Count == 0)
                result.AddError($"Level flow '{flow.flowId}' has no steps");

            foreach (var condition in flow.triggerConditions)
                if (!string.IsNullOrEmpty(condition.variableName) && !variableNames.Contains(condition.variableName))
                    result.AddError($"Level flow '{flow.flowId}' references undefined variable '{condition.variableName}'");

            if (flow.steps == null) continue;
            foreach (var step in flow.steps)
            {
                if (step == null) continue;
                if (step.stepType == LevelFlowStepType.MovePlayer && step.speed <= 0f)
                    result.AddError($"Level flow '{flow.flowId}' has a player move step with invalid speed");
                if ((step.stepType == LevelFlowStepType.MovePlayer || step.stepType == LevelFlowStepType.MoveCamera) &&
                    (float.IsNaN(step.targetPosition.x) || float.IsInfinity(step.targetPosition.x) ||
                     float.IsNaN(step.targetPosition.y) || float.IsInfinity(step.targetPosition.y)))
                    result.AddError($"Level flow '{flow.flowId}' has an invalid target position");
                if (step.stepType == LevelFlowStepType.MoveCamera && step.duration < 0f)
                    result.AddError($"Level flow '{flow.flowId}' has a camera move step with invalid duration");
                if (step.stepType == LevelFlowStepType.PlayStory && string.IsNullOrWhiteSpace(step.storyId))
                    result.AddError($"Level flow '{flow.flowId}' has an empty story ID");
                else if (step.stepType == LevelFlowStepType.PlayStory && storyIds != null && !storyIds.Contains(step.storyId))
                    result.AddError($"Level flow '{flow.flowId}' references unknown story '{step.storyId}'");
                storiesById.TryGetValue(step.storyId ?? "", out var selectedStory);
                bool usesStoryCues = selectedStory?.performanceCues != null &&
                                     selectedStory.performanceCues.Count > 0;
                if (step.stepType == LevelFlowStepType.PlayStory && usesStoryCues)
                {
                    var scriptsById = new Dictionary<string, PerformanceScript>();
                    foreach (var script in step.storyPerformanceScripts ?? new List<PerformanceScript>())
                    {
                        if (script == null || string.IsNullOrWhiteSpace(script.scriptId)) continue;
                        if (!scriptsById.TryAdd(script.scriptId, script))
                            result.AddError($"Duplicate performance script ID: {script.scriptId}");
                    }

                    var requiredSlots = new HashSet<(string scriptId, string slotId)>();
                    foreach (var cue in selectedStory.performanceCues)
                    {
                        if (cue == null || cue.delay < 0f)
                        {
                            result.AddError($"Story '{selectedStory.storyId}' contains an invalid performance cue");
                            continue;
                        }
                        if (!dialogueIdsByStory.TryGetValue(selectedStory.storyId, out var dialogueIds) ||
                            !dialogueIds.Contains(cue.dialogueId))
                            result.AddError($"Story '{selectedStory.storyId}' cue references unknown dialogue ID {cue.dialogueId}");
                        if (string.IsNullOrWhiteSpace(cue.scriptId) ||
                            !scriptsById.TryGetValue(cue.scriptId, out var script) || script == null)
                        {
                            result.AddError($"Story '{selectedStory.storyId}' references missing performance script '{cue.scriptId}'");
                            continue;
                        }

                        var slotIds = new HashSet<string>();
                        foreach (var slot in script.actorSlots ?? new List<PerformanceActorSlot>())
                        {
                            if (slot == null || string.IsNullOrWhiteSpace(slot.slotId) || !slotIds.Add(slot.slotId))
                                result.AddError($"Performance script '{script.scriptId}' has an invalid or duplicate actor slot");
                            else
                                requiredSlots.Add((script.scriptId, slot.slotId));
                        }
                        ValidatePerformanceClips(result, script, slotIds);
                    }

                    foreach (var requiredSlot in requiredSlots)
                    {
                        var binding = step.storyCastBindings?.FirstOrDefault(item =>
                            item != null && item.scriptId == requiredSlot.scriptId &&
                            item.slotId == requiredSlot.slotId);
                        binding ??= step.storyCastBindings?.FirstOrDefault(item =>
                            item != null && string.IsNullOrEmpty(item.scriptId) &&
                            item.slotId == requiredSlot.slotId);
                        if (binding == null)
                            result.AddError(
                                $"Story '{selectedStory.storyId}' does not bind cast slot " +
                                $"'{requiredSlot.scriptId}/{requiredSlot.slotId}'");
                        else if (binding.targetType == PerformanceActorTargetType.LevelElement &&
                                 !elementIds.Contains(binding.elementId))
                            result.AddError(
                                $"Story cast slot '{requiredSlot.scriptId}/{requiredSlot.slotId}' " +
                                $"references unknown element '{binding.elementId}'");
                        else if (binding.targetType == PerformanceActorTargetType.EnvironmentActor &&
                                 !environmentActorIds.Contains(binding.environmentActorId))
                            result.AddError(
                                $"Story cast slot '{requiredSlot.scriptId}/{requiredSlot.slotId}' " +
                                $"references unknown environment actor '{binding.environmentActorId}'");
                    }
                }
                else if (step.stepType == LevelFlowStepType.PlayStory && step.storyPerformanceCues != null)
                {
                    var referencedScripts = new Dictionary<string, PerformanceScript>();
                    foreach (var cue in step.storyPerformanceCues)
                    {
                        if (cue == null || cue.performanceScript == null)
                        {
                            result.AddError($"Level flow '{flow.flowId}' has a story performance cue without a script");
                            continue;
                        }
                        if (dialogueIdsByStory.TryGetValue(step.storyId ?? "", out var dialogueIds) &&
                            !dialogueIds.Contains(cue.dialogueId))
                            result.AddError($"Level flow '{flow.flowId}' cue references unknown dialogue ID {cue.dialogueId}");

                        var script = cue.performanceScript;
                        if (string.IsNullOrWhiteSpace(script.scriptId))
                            result.AddError($"Performance script '{script.name}' has an empty script ID");
                        else if (referencedScripts.TryGetValue(script.scriptId, out var duplicate) && duplicate != script)
                            result.AddError($"Duplicate performance script ID: {script.scriptId}");
                        else
                            referencedScripts[script.scriptId] = script;

                        var slotIds = new HashSet<string>();
                        foreach (var slot in script.actorSlots ?? new List<PerformanceActorSlot>())
                        {
                            if (slot == null || string.IsNullOrWhiteSpace(slot.slotId) || !slotIds.Add(slot.slotId))
                                result.AddError($"Performance script '{script.scriptId}' has an invalid or duplicate actor slot");
                        }
                        foreach (var slotId in slotIds)
                        {
                            var binding = cue.actorBindings?.FirstOrDefault(item => item != null && item.slotId == slotId);
                            if (binding == null)
                                result.AddError($"Performance cue for '{script.scriptId}' does not bind slot '{slotId}'");
                            else if (binding.targetType == PerformanceActorTargetType.LevelElement &&
                                     !elementIds.Contains(binding.elementId))
                                result.AddError($"Performance cue slot '{slotId}' references unknown element '{binding.elementId}'");
                            else if (binding.targetType == PerformanceActorTargetType.EnvironmentActor &&
                                     !environmentActorIds.Contains(binding.environmentActorId))
                                result.AddError($"Performance cue slot '{slotId}' references unknown environment actor '{binding.environmentActorId}'");
                        }
                        ValidatePerformanceClips(result, script, slotIds);
                    }
                }
                if (step.stepType == LevelFlowStepType.SetVariable &&
                    (step.setVariable == null || !variableNames.Contains(step.setVariable.variableName)))
                    result.AddError($"Level flow '{flow.flowId}' has an invalid set-variable step");
            }
        }

        // 警告
        if (elements.Count == 0)
            result.AddWarning("关卡没有任何元素");

        if (events == null || events.Count == 0)
            result.AddWarning("没有配置任何演出事件");

        if (backgroundSettings == null)
        {
            result.AddWarning("没有配置背景图");
        }
        else if (backgroundSettings.layers != null)
        {
            if (backgroundSettings.layers.Count == 0)
                result.AddWarning("No background layers are configured");

            var layerIds = new HashSet<string>();
            foreach (var layer in backgroundSettings.layers)
            {
                if (layer == null) continue;
                if (string.IsNullOrWhiteSpace(layer.layerId) || !layerIds.Add(layer.layerId))
                    result.AddError("Background layer IDs must be non-empty and unique");
                if (layer.scale.x == 0f || layer.scale.y == 0f)
                    result.AddError($"Background layer '{layer.displayName}' has a zero scale");
                if (layer.SortingOrder < short.MinValue || layer.SortingOrder > short.MaxValue)
                    result.AddError($"Background layer '{layer.displayName}' has an out-of-range sorting order");
                if (layer.contentType == BackgroundLayerContentType.SequentialTiles)
                {
                    if (layer.sequence == null || layer.sequence.Count == 0)
                        result.AddError($"Background layer '{layer.displayName}' has no sequence tiles");
                    else if (layer.sequence.Any(entry => entry == null || entry.sprite == null || entry.repeatCount < 1))
                        result.AddError($"Background layer '{layer.displayName}' has an invalid sequence tile");
                }
                else if (layer.sprite == null)
                {
                    result.AddError($"Background layer '{layer.displayName}' has no sprite");
                }
                else if (layer.contentType == BackgroundLayerContentType.RepeatedSprite &&
                         layer.sprite.bounds.size.x * Mathf.Abs(layer.scale.x) <= Mathf.Epsilon)
                {
                    result.AddError($"Repeated background layer '{layer.displayName}' has an invalid repeat width");
                }

                if (layer.contentType != BackgroundLayerContentType.RepeatedSprite &&
                    layer.autoScrollDirection != BackgroundScrollDirection.None &&
                    layer.autoScrollSpeed > Mathf.Epsilon)
                    result.AddError($"Scrolling background layer '{layer.displayName}' must use repeated content");
            }
        }
        else if (backgroundSettings.mode == BackgroundMode.SingleInfiniteScroll)
        {
            if (backgroundSettings.singleBackground == null)
                result.AddWarning("没有配置背景图");
        }
        else if (backgroundSettings.mode == BackgroundMode.SequentialTiles)
        {
            if (backgroundSettings.sequence == null || backgroundSettings.sequence.Count == 0)
            {
                result.AddWarning("顺序背景列表为空");
            }
            else
            {
                for (int i = 0; i < backgroundSettings.sequence.Count; i++)
                {
                    var entry = backgroundSettings.sequence[i];
                    if (entry == null || entry.sprite == null)
                        result.AddError($"顺序背景 #{i + 1} 没有配置图片");
                    else if (entry.repeatCount < 1)
                        result.AddError($"顺序背景 #{i + 1} 的重复次数必须大于等于 1");
                }

                float sequenceEndX = backgroundSettings.sequenceStartX + backgroundSettings.CalculateSequenceWidth();
                float playableStartX = Mathf.Min(0f, playerSpawnPosition.x);
                float playableEndX = Mathf.Max(levelLength, levelEndPositionX);
                if (backgroundSettings.sequenceStartX > playableStartX)
                    result.AddWarning($"顺序背景起点 X({backgroundSettings.sequenceStartX})晚于可玩区域起点 X({playableStartX})");
                if (sequenceEndX < playableEndX)
                    result.AddWarning($"顺序背景终点 X({sequenceEndX})未覆盖可玩区域终点 X({playableEndX})");
            }
        }
        else if (backgroundSettings.parallaxLayers == null || backgroundSettings.parallaxLayers.Count == 0)
        {
            result.AddWarning("没有配置背景图");
        }

        foreach (var g in groups)
        {
            int count = elements.Count(e => e.IsInGroup(g.groupId));
            if (count == 0)
                result.AddWarning($"元素组'{g.groupName}'内没有元素");
        }

        if (result.errors.Count == 0)
            result.isValid = true;

        return result;
    }

static void ValidatePerformanceClips(
        LevelValidationResult result, PerformanceScript script, HashSet<string> slotIds)
    {
        foreach (var clip in script.clips ?? new List<PerformanceClip>())
        {
            if (clip == null) continue;
            if (clip.startTime < 0f || clip.duration < 0f)
                result.AddError($"Performance script '{script.scriptId}' has a clip with invalid timing");
            if (clip.clipType != PerformanceClipType.MoveCamera &&
                !slotIds.Contains(clip.actorSlotId))
                result.AddError($"Performance script '{script.scriptId}' clip references unknown slot '{clip.actorSlotId}'");
            if ((clip.clipType == PerformanceClipType.MoveActor ||
                 clip.clipType == PerformanceClipType.MoveCamera) &&
                (float.IsNaN(clip.targetPosition.x) || float.IsInfinity(clip.targetPosition.x) ||
                 float.IsNaN(clip.targetPosition.y) || float.IsInfinity(clip.targetPosition.y)))
                result.AddError($"Performance script '{script.scriptId}' has an invalid movement target");
            if (clip.clipType == PerformanceClipType.PlayAnimation &&
                string.IsNullOrWhiteSpace(clip.animationName))
                result.AddError($"Performance script '{script.scriptId}' has an animation clip without a name");
        }
    }

}
