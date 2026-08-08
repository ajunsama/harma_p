using System;
using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;

/// <summary>Marker used by the level editor when scanning environment actor prefabs.</summary>
public sealed class EnvironmentActorMarker : MonoBehaviour { }

public sealed class EnvironmentActorContext
{
    public string actorId;
    public GameObject actor;
    public Transform player;
    public LevelVariableManager variables;
    public PlayerGameplaySignalHub playerSignals;
}

public interface IEnvironmentActorContextReceiver
{
    void InitializeEnvironmentActor(EnvironmentActorContext context);
}

public interface IEnvironmentActorSignalReceiver
{
    void ReceiveEnvironmentSignal(string signalId, EnvironmentActorContext context);
}

public sealed class EnvironmentActorController : MonoBehaviour
{
    private EnvironmentActorData data;
    private EnvironmentActorContext context;
    private Animator animator;
    private SkeletonAnimation spine;
    private SpriteRenderer[] renderers = Array.Empty<SpriteRenderer>();
    private Collider2D[] colliders = Array.Empty<Collider2D>();
    private readonly HashSet<int> activeTriggers = new HashSet<int>();
    private readonly HashSet<int> firedTriggers = new HashSet<int>();
    private bool actorActive = true;

    public void Initialize(
        EnvironmentActorData actorData,
        Transform player,
        LevelVariableManager variables,
        PlayerGameplaySignalHub playerSignals)
    {
        data = actorData;
        animator = GetComponentInChildren<Animator>(true);
        spine = GetComponentInChildren<SkeletonAnimation>(true);
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        colliders = GetComponentsInChildren<Collider2D>(true);
        context = new EnvironmentActorContext
        {
            actorId = data?.actorId,
            actor = gameObject,
            player = player,
            variables = variables,
            playerSignals = playerSignals
        };

        if (data != null)
        {
            foreach (var renderer in renderers)
                if (renderer != null) renderer.sortingOrder = data.SortingOrder + renderer.sortingOrder;
        }

        foreach (var behaviour in GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour is IEnvironmentActorContextReceiver receiver)
                receiver.InitializeEnvironmentActor(context);

        if (playerSignals != null)
            playerSignals.SignalPublished += OnPlayerSignal;
        if (variables != null)
            variables.OnVariableChanged("*", RefreshActiveConditions);
        RefreshActiveConditions();
    }

    private void Update()
    {
        if (data == null || context?.player == null || !actorActive) return;
        UpdateContinuousBindings();
        UpdateTriggers();
    }

    private void UpdateContinuousBindings()
    {
        if (animator == null || data.continuousBindings == null) return;
        foreach (var binding in data.continuousBindings)
        {
            if (binding == null || string.IsNullOrWhiteSpace(binding.animatorParameter)) continue;
            float input = ReadContinuousSource(binding.source);
            float denominator = binding.inputMax - binding.inputMin;
            float t = Mathf.Abs(denominator) <= Mathf.Epsilon
                ? 0f
                : (input - binding.inputMin) / denominator;
            if (binding.clamp) t = Mathf.Clamp01(t);
            animator.SetFloat(binding.animatorParameter,
                Mathf.LerpUnclamped(binding.outputMin, binding.outputMax, t));
        }
    }

    private float ReadContinuousSource(EnvironmentContinuousSource source)
    {
        Vector2 playerPosition = context.player.position;
        Vector2 actorPosition = transform.position;
        switch (source)
        {
            case EnvironmentContinuousSource.PlayerY: return playerPosition.y;
            case EnvironmentContinuousSource.HorizontalDistance: return Mathf.Abs(playerPosition.x - actorPosition.x);
            case EnvironmentContinuousSource.Distance: return Vector2.Distance(playerPosition, actorPosition);
            default: return playerPosition.x;
        }
    }

    private void UpdateTriggers()
    {
        if (data.triggers == null) return;
        for (int i = 0; i < data.triggers.Count; i++)
        {
            var trigger = data.triggers[i];
            if (trigger == null || trigger.triggerType == EnvironmentTriggerType.PlayerSignal) continue;
            bool inside = EvaluateTrigger(trigger);
            bool wasInside = activeTriggers.Contains(i);
            if (inside && !wasInside && (!trigger.triggerOnce || !firedTriggers.Contains(i)))
            {
                activeTriggers.Add(i);
                firedTriggers.Add(i);
                ExecuteActions(trigger.onEnterActions);
            }
            else if (!inside && wasInside)
            {
                activeTriggers.Remove(i);
                ExecuteActions(trigger.onExitActions);
            }
        }
    }

    private bool EvaluateTrigger(EnvironmentActorTrigger trigger)
    {
        if (context.variables != null && !context.variables.CheckAllConditions(trigger.conditions))
            return false;
        switch (trigger.triggerType)
        {
            case EnvironmentTriggerType.PlayerXRange:
                return context.player.position.x >= trigger.minValue &&
                       context.player.position.x <= trigger.maxValue;
            case EnvironmentTriggerType.PlayerDistance:
            {
                float distance = Vector2.Distance(context.player.position, transform.position);
                return distance >= trigger.minValue && distance <= trigger.maxValue;
            }
            case EnvironmentTriggerType.LevelConditions:
                return true;
            default:
                return false;
        }
    }

    private void OnPlayerSignal(PlayerGameplaySignal signal)
    {
        if (data?.triggers == null || !actorActive) return;
        for (int i = 0; i < data.triggers.Count; i++)
        {
            var trigger = data.triggers[i];
            if (trigger == null || trigger.triggerType != EnvironmentTriggerType.PlayerSignal ||
                trigger.signalId != signal.id || (trigger.triggerOnce && firedTriggers.Contains(i)))
                continue;
            if (context.variables != null && !context.variables.CheckAllConditions(trigger.conditions))
                continue;
            firedTriggers.Add(i);
            ExecuteActions(trigger.onEnterActions);
        }
    }

    private void ExecuteActions(List<EnvironmentActorAction> actions)
    {
        if (actions == null) return;
        foreach (var action in actions)
        {
            if (action == null) continue;
            switch (action.actionType)
            {
                case EnvironmentActionType.PlayAnimation:
                    PlayAnimation(action);
                    break;
                case EnvironmentActionType.SetAnimatorFloat:
                    if (animator != null) animator.SetFloat(action.name, action.floatValue);
                    break;
                case EnvironmentActionType.SetAnimatorBool:
                    if (animator != null) animator.SetBool(action.name, action.boolValue);
                    break;
                case EnvironmentActionType.SetAnimatorTrigger:
                    if (animator != null) animator.SetTrigger(action.name);
                    break;
                case EnvironmentActionType.SetLevelVariable:
                    context.variables?.ApplySetAction(new VariableSetAction
                    {
                        variableName = action.name,
                        stringValue = action.stringValue
                    });
                    break;
                case EnvironmentActionType.SetVisualActive:
                    SetVisualActive(action.boolValue);
                    break;
                case EnvironmentActionType.EmitActorSignal:
                    EmitActorSignal(action.name);
                    break;
            }
        }
    }

    private void PlayAnimation(EnvironmentActorAction action)
    {
        if (string.IsNullOrWhiteSpace(action.name)) return;
        if (spine != null && spine.AnimationState != null)
        {
            spine.AnimationState.SetAnimation(Mathf.Max(0, action.animationTrack), action.name, action.loop);
            return;
        }
        if (animator != null)
            animator.Play(action.name, Mathf.Clamp(action.animationTrack, 0, Mathf.Max(0, animator.layerCount - 1)));
    }

    private void EmitActorSignal(string signalId)
    {
        if (string.IsNullOrWhiteSpace(signalId)) return;
        foreach (var behaviour in GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour is IEnvironmentActorSignalReceiver receiver)
                receiver.ReceiveEnvironmentSignal(signalId, context);
    }

    private void RefreshActiveConditions()
    {
        bool shouldBeActive = data == null || context?.variables == null ||
                              context.variables.CheckAllConditions(data.activeConditions);
        if (shouldBeActive == actorActive) return;
        actorActive = shouldBeActive;
        SetVisualActive(actorActive);
    }

    private void SetVisualActive(bool value)
    {
        foreach (var renderer in renderers)
            if (renderer != null) renderer.enabled = value;
        foreach (var collider in colliders)
            if (collider != null) collider.enabled = value;
    }

    private void OnDestroy()
    {
        if (context?.playerSignals != null)
            context.playerSignals.SignalPublished -= OnPlayerSignal;
        if (context?.variables != null)
            context.variables.RemoveListener("*", RefreshActiveConditions);
    }
}
