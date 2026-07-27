using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum PerformanceClipType
{
    MoveActor,
    PlayAnimation,
    SetFacing,
    MoveCamera
}

public enum PerformancePositionMode
{
    World,
    RelativeToStart
}

public enum PerformanceActorTargetType
{
    Player,
    LevelElement
}

[Serializable]
public class PerformanceActorSlot
{
    public string slotId;
    public string displayName;
    public string defaultIdleAnimation = "idle";
    public string defaultMoveAnimation = "run";
}

[Serializable]
public class PerformanceClip
{
    public string clipName;
    public PerformanceClipType clipType;
    [Min(0f)] public float startTime;
    [Min(0f)] public float duration;

    public string actorSlotId;
    public PerformancePositionMode positionMode;
    public Vector2 targetPosition;
    public LevelFlowEasing easing = LevelFlowEasing.SmoothStep;
    public bool autoFaceMovement = true;
    public bool playMoveAnimation = true;
    public string moveAnimationOverride;
    [Min(0)] public int moveAnimationTrack;
    [Min(0f)] public float moveAnimationMixDuration = 0.1f;
    public bool restoreIdleAfterMove = true;

    public string animationName;
    public bool loopAnimation;
    [Min(0)] public int animationTrack;
    [Min(0f)] public float animationMixDuration = 0.1f;
    public bool keepAnimationAfterStory;

    public bool faceRight = true;
}

[CreateAssetMenu(fileName = "NewPerformanceScript", menuName = "Game/Performance Script", order = 20)]
public class PerformanceScript : ScriptableObject
{
    public string scriptId;
    public List<PerformanceActorSlot> actorSlots = new List<PerformanceActorSlot>();
    public List<PerformanceClip> clips = new List<PerformanceClip>();
    public bool restoreCameraFollowOnStoryEnd = true;

    public float Duration
    {
        get
        {
            float result = 0f;
            if (clips == null) return result;
            foreach (var clip in clips)
            {
                if (clip == null) continue;
                result = Mathf.Max(result, Mathf.Max(0f, clip.startTime) + Mathf.Max(0f, clip.duration));
            }
            return result;
        }
    }

    public PerformanceActorSlot FindSlot(string slotId)
    {
        if (actorSlots == null || string.IsNullOrEmpty(slotId)) return null;
        return actorSlots.Find(slot => slot != null && slot.slotId == slotId);
    }
}

[Serializable]
public class PerformanceActorBinding
{
    [Tooltip("所属演出脚本 ID；旧数据为空时按槽位 ID 兼容匹配")]
    public string scriptId;
    public string slotId;
    public PerformanceActorTargetType targetType;
    public string elementId;
    public string idleAnimationOverride;
    public string moveAnimationOverride;
}

[Serializable]
public class StoryPerformanceCue
{
    public int dialogueId = 1;
    [Min(0f)] public float delay;
    public PerformanceScript performanceScript;
    public bool blockDialogueAdvance;
    public StoryPerformanceCueTriggerTiming triggerTiming =
        StoryPerformanceCueTriggerTiming.DialogueStart;
    public List<PerformanceActorBinding> actorBindings = new List<PerformanceActorBinding>();
}

public interface IStoryPerformanceSession
{
    void BeginDialogue(StoryDialogue dialogue,
        StoryPerformanceCueTriggerTiming timing = StoryPerformanceCueTriggerTiming.DialogueStart);
    IEnumerator WaitForBlockingCue(int dialogueId);
    void Complete(bool skipped);
}
