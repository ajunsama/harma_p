using System.Collections.Generic;
using System.Linq;

public partial class LevelData
{
    public bool MigrateLegacyBackground()
    {
        if (backgroundSettings == null)
            backgroundSettings = new BackgroundSettings();
        return backgroundSettings.MigrateLegacyData();
    }

    public bool MigrateLegacyStoryTriggers()
    {
        if (storyTriggers == null || storyTriggers.Count == 0)
            return false;
        if (events == null)
            events = new List<LevelFlowData>();

        for (int i = 0; i < storyTriggers.Count; i++)
        {
            StoryTriggerPoint trigger = storyTriggers[i];
            if (trigger == null)
                continue;

            string eventId = $"legacy_story_{i + 1}_{trigger.storyId}";
            if (events.Any(levelEvent => levelEvent != null && levelEvent.flowId == eventId))
                continue;

            var steps = new List<LevelFlowStep>();
            if (trigger.onStoryStartSetVariables != null)
            {
                foreach (VariableSetAction action in trigger.onStoryStartSetVariables)
                {
                    steps.Add(new LevelFlowStep
                    {
                        stepType = LevelFlowStepType.SetVariable,
                        setVariable = CloneSetAction(action)
                    });
                }
            }

            steps.Add(new LevelFlowStep
            {
                stepType = LevelFlowStepType.PlayStory,
                storyId = trigger.storyId
            });

            if (trigger.onStoryCompleteSetVariables != null)
            {
                foreach (VariableSetAction action in trigger.onStoryCompleteSetVariables)
                {
                    steps.Add(new LevelFlowStep
                    {
                        stepType = LevelFlowStepType.SetVariable,
                        setVariable = CloneSetAction(action)
                    });
                }
            }

            events.Add(new LevelFlowData
            {
                flowId = eventId,
                triggerMode = trigger.triggerMode,
                positionX = trigger.positionX,
                triggerFromLeft = trigger.triggerFromLeft,
                triggerOnce = trigger.triggerOnce,
                triggerConditions = trigger.triggerConditions?.Select(condition =>
                    new LevelVariableCondition
                    {
                        variableName = condition.variableName,
                        mode = condition.mode,
                        compareValue = condition.compareValue
                    }).ToList() ?? new List<LevelVariableCondition>(),
                steps = steps
            });
        }

        storyTriggers.Clear();
        return true;
    }

    private static VariableSetAction CloneSetAction(VariableSetAction action)
    {
        return action == null
            ? new VariableSetAction()
            : new VariableSetAction
            {
                variableName = action.variableName,
                stringValue = action.stringValue
            };
    }

    private static IEnumerable<EnvironmentActorAction> EnumerateEnvironmentActions(
        EnvironmentActorData actor)
    {
        if (actor?.triggers == null)
            yield break;

        foreach (EnvironmentActorTrigger trigger in actor.triggers)
        {
            if (trigger == null)
                continue;

            foreach (EnvironmentActorAction action in
                     trigger.onEnterActions ?? new List<EnvironmentActorAction>())
            {
                if (action != null)
                    yield return action;
            }

            foreach (EnvironmentActorAction action in
                     trigger.onExitActions ?? new List<EnvironmentActorAction>())
            {
                if (action != null)
                    yield return action;
            }
        }
    }
}
