using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Spine.Unity;
using UnityEngine;

public class PerformanceRunner : MonoBehaviour
{
    private sealed class ActorRuntime
    {
        public GameObject gameObject;
        public Transform transform;
        public Rigidbody2D rigidbody;
        public PlayerMovement player;
        public SkeletonAnimation spine;
        public Animator animator;
        public AnimatorUpdateMode animatorUpdateMode;
        public bool animatorModeCaptured;
        public string idleAnimation;
        public string moveAnimation;
    }

    private sealed class ActionHandle
    {
        public bool complete;
        public bool cancelled;
        public Coroutine coroutine;
    }

    private sealed class CueHandle
    {
        public StoryPerformanceCue cue;
        public bool complete;
        public bool cancelled;
        public Coroutine coroutine;
        public readonly List<ActionHandle> actions = new List<ActionHandle>();
        public readonly Dictionary<string, Vector2> actorStarts = new Dictionary<string, Vector2>();
        public Vector2 cameraStart;
        public bool originsCaptured;
    }

    private sealed class AnimationEndState
    {
        public bool keep;
        public string animationName;
        public bool loop;
        public int track;
        public float mixDuration;
    }

    private LevelSceneBuilder sceneBuilder;
    private LevelCameraController cameraController;
    private int nextOwnerToken = 1;
    private int cameraOwnerToken;
    private readonly Dictionary<Transform, int> movementOwners = new Dictionary<Transform, int>();
    private readonly HashSet<SkeletonAnimation> manuallyUpdatedSpines = new HashSet<SkeletonAnimation>();
    private readonly List<SkeletonAnimation> spineUpdateBuffer = new List<SkeletonAnimation>();

    public void Initialize(LevelSceneBuilder builder, LevelCameraController camera)
    {
        sceneBuilder = builder;
        cameraController = camera;
    }

    public IStoryPerformanceSession CreateSession(List<StoryPerformanceCue> cues)
    {
        if (cues == null || !cues.Any(cue => cue != null && cue.performanceScript != null))
            return null;
        return new StoryPerformanceSession(this, cues);
    }

    public IStoryPerformanceSession CreateSession(
        List<StoryPerformanceCueDefinition> cueDefinitions,
        List<PerformanceActorBinding> castBindings,
        List<PerformanceScript> performanceScripts)
    {
        if (cueDefinitions == null || cueDefinitions.Count == 0)
            return null;

        var scriptsById = new Dictionary<string, PerformanceScript>();
        foreach (var script in performanceScripts ?? new List<PerformanceScript>())
        {
            if (script == null || string.IsNullOrWhiteSpace(script.scriptId)) continue;
            if (scriptsById.ContainsKey(script.scriptId))
            {
                Debug.LogWarning($"[PerformanceRunner] 演出脚本 ID 重复: {script.scriptId}");
                continue;
            }
            scriptsById[script.scriptId] = script;
        }

        var runtimeCues = new List<StoryPerformanceCue>();
        foreach (var definition in cueDefinitions)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.scriptId))
                continue;
            if (!scriptsById.TryGetValue(definition.scriptId, out var script) || script == null)
            {
                Debug.LogWarning($"[PerformanceRunner] 找不到剧情 Cue 引用的演出脚本: {definition.scriptId}");
                continue;
            }

            runtimeCues.Add(new StoryPerformanceCue
            {
                dialogueId = definition.dialogueId,
                delay = Mathf.Max(0f, definition.delay),
                performanceScript = script,
                blockDialogueAdvance = definition.blockDialogueAdvance,
                triggerTiming = definition.triggerTiming,
                actorBindings = castBindings ?? new List<PerformanceActorBinding>()
            });
        }

        return runtimeCues.Count > 0 ? new StoryPerformanceSession(this, runtimeCues) : null;
    }

    void Update()
    {
        if (Time.timeScale > 0f || manuallyUpdatedSpines.Count == 0) return;

        spineUpdateBuffer.Clear();
        spineUpdateBuffer.AddRange(manuallyUpdatedSpines);
        foreach (var spine in spineUpdateBuffer)
        {
            if (spine == null)
            {
                manuallyUpdatedSpines.Remove(spine);
                continue;
            }
            spine.Update(Time.unscaledDeltaTime);
        }
    }

    private sealed class StoryPerformanceSession : IStoryPerformanceSession
    {
        private readonly PerformanceRunner runner;
        private readonly List<StoryPerformanceCue> cues;
        private readonly Dictionary<int, List<CueHandle>> activeByDialogue = new Dictionary<int, List<CueHandle>>();
        private readonly List<CueHandle> activeHandles = new List<CueHandle>();
        private readonly Dictionary<StoryPerformanceCue, CueHandle> handlesByCue =
            new Dictionary<StoryPerformanceCue, CueHandle>();
        private readonly Dictionary<GameObject, ActorRuntime> actors = new Dictionary<GameObject, ActorRuntime>();
        private bool completed;

        public StoryPerformanceSession(PerformanceRunner owner, List<StoryPerformanceCue> source)
        {
            runner = owner;
            cues = source.Where(cue => cue != null && cue.performanceScript != null).ToList();
        }

        public void BeginDialogue(StoryDialogue dialogue,
            StoryPerformanceCueTriggerTiming timing = StoryPerformanceCueTriggerTiming.DialogueStart)
        {
            if (completed || dialogue == null) return;

            foreach (var cue in cues)
            {
                if (cue.dialogueId != dialogue.id || cue.triggerTiming != timing) continue;
                var handle = new CueHandle { cue = cue };
                activeHandles.Add(handle);
                if (!activeByDialogue.TryGetValue(dialogue.id, out var handles))
                {
                    handles = new List<CueHandle>();
                    activeByDialogue[dialogue.id] = handles;
                }
                handles.Add(handle);
                handlesByCue[cue] = handle;
                handle.coroutine = runner.StartCoroutine(runner.RunCue(this, handle));
            }
        }

        public IEnumerator WaitForBlockingCue(int dialogueId)
        {
            if (!activeByDialogue.TryGetValue(dialogueId, out var handles))
                yield break;

            while (handles.Any(handle =>
                       handle != null && handle.cue.blockDialogueAdvance && !handle.complete))
                yield return null;
        }

        public void Complete(bool skipped)
        {
            if (completed) return;
            completed = true;

            foreach (var handle in activeHandles)
            {
                if (handle == null) continue;
                handle.cancelled = true;
                if (handle.coroutine != null)
                    runner.StopCoroutine(handle.coroutine);
                foreach (var action in handle.actions)
                {
                    if (action == null) continue;
                    action.cancelled = true;
                    action.complete = true;
                    if (action.coroutine != null)
                        runner.StopCoroutine(action.coroutine);
                }
                handle.complete = true;
            }

            runner.ApplyFinalState(this, cues);
            runner.ReleaseActors(actors.Values);
            activeHandles.Clear();
            activeByDialogue.Clear();
            handlesByCue.Clear();
        }

        public ActorRuntime ResolveActor(StoryPerformanceCue cue, string slotId)
        {
            if (cue?.performanceScript == null || string.IsNullOrEmpty(slotId))
                return null;

            string scriptId = cue.performanceScript.scriptId;
            var binding = cue.actorBindings?.Find(item =>
                item != null && item.slotId == slotId && item.scriptId == scriptId);
            binding ??= cue.actorBindings?.Find(item =>
                item != null && item.slotId == slotId && string.IsNullOrEmpty(item.scriptId));
            if (binding == null || !runner.sceneBuilder.TryResolvePerformanceActor(binding, out var go))
            {
                Debug.LogWarning($"[PerformanceRunner] 无法解析角色槽位 '{slotId}'");
                return null;
            }

            if (actors.TryGetValue(go, out var cached) && cached != null)
                return cached;

            var slot = cue.performanceScript.FindSlot(slotId);
            string idle = !string.IsNullOrEmpty(binding.idleAnimationOverride)
                ? binding.idleAnimationOverride
                : slot?.defaultIdleAnimation;
            string move = !string.IsNullOrEmpty(binding.moveAnimationOverride)
                ? binding.moveAnimationOverride
                : slot?.defaultMoveAnimation;
            var actor = runner.CreateActorRuntime(go, idle, move);
            actors[go] = actor;
            return actor;
        }

        public bool TryGetActor(GameObject go, out ActorRuntime actor)
        {
            return actors.TryGetValue(go, out actor);
        }

        public IEnumerable<ActorRuntime> GetActors()
        {
            return actors.Values;
        }

        public bool TryGetCueOrigins(StoryPerformanceCue cue,
            out Dictionary<string, Vector2> actorStarts, out Vector2 cameraStart)
        {
            if (cue != null && handlesByCue.TryGetValue(cue, out var handle) &&
                handle != null && handle.originsCaptured)
            {
                actorStarts = handle.actorStarts;
                cameraStart = handle.cameraStart;
                return true;
            }

            actorStarts = null;
            cameraStart = default;
            return false;
        }
    }

    private IEnumerator RunCue(StoryPerformanceSession session, CueHandle handle)
    {
        float delay = Mathf.Max(0f, handle.cue.delay);
        float waited = 0f;
        while (!handle.cancelled && waited < delay)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        if (handle.cancelled)
        {
            handle.complete = true;
            yield break;
        }

        yield return RunScript(session, handle);
        handle.complete = true;
    }

    private IEnumerator RunScript(StoryPerformanceSession session, CueHandle cueHandle)
    {
        var script = cueHandle.cue.performanceScript;
        var clips = (script.clips ?? new List<PerformanceClip>())
            .Where(clip => clip != null)
            .OrderBy(clip => Mathf.Max(0f, clip.startTime))
            .ToList();

        cueHandle.actorStarts.Clear();
        foreach (var slot in script.actorSlots ?? new List<PerformanceActorSlot>())
        {
            if (slot == null || string.IsNullOrEmpty(slot.slotId)) continue;
            var actor = session.ResolveActor(cueHandle.cue, slot.slotId);
            if (actor != null)
                cueHandle.actorStarts[slot.slotId] = actor.transform.position;
        }
        cueHandle.cameraStart = cameraController != null ? cameraController.CurrentPosition : Vector2.zero;
        cueHandle.originsCaptured = true;

        int nextClip = 0;
        float elapsed = 0f;
        float duration = script.Duration;
        while (!cueHandle.cancelled)
        {
            while (nextClip < clips.Count && Mathf.Max(0f, clips[nextClip].startTime) <= elapsed)
            {
                StartClip(session, cueHandle, clips[nextClip],
                    cueHandle.actorStarts, cueHandle.cameraStart);
                nextClip++;
            }

            cueHandle.actions.RemoveAll(action => action == null || action.complete);
            if (nextClip >= clips.Count && elapsed >= duration && cueHandle.actions.Count == 0)
                break;

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void StartClip(StoryPerformanceSession session, CueHandle cueHandle, PerformanceClip clip,
        Dictionary<string, Vector2> actorStarts, Vector2 cameraStart)
    {
        switch (clip.clipType)
        {
            case PerformanceClipType.MoveActor:
            {
                var actor = session.ResolveActor(cueHandle.cue, clip.actorSlotId);
                if (actor == null) return;
                Vector2 start = actor.transform.position;
                Vector2 origin = actorStarts.TryGetValue(clip.actorSlotId, out var value) ? value : start;
                Vector2 target = clip.positionMode == PerformancePositionMode.World
                    ? clip.targetPosition
                    : origin + clip.targetPosition;
                var action = new ActionHandle();
                cueHandle.actions.Add(action);
                action.coroutine = StartCoroutine(RunActorMove(actor, start, target, clip, action));
                break;
            }
            case PerformanceClipType.PlayAnimation:
            {
                var actor = session.ResolveActor(cueHandle.cue, clip.actorSlotId);
                if (actor != null)
                    PlayAnimation(actor, clip.animationName, clip.loopAnimation,
                        clip.animationTrack, clip.animationMixDuration);
                break;
            }
            case PerformanceClipType.SetFacing:
            {
                var actor = session.ResolveActor(cueHandle.cue, clip.actorSlotId);
                if (actor != null) SetFacing(actor, clip.faceRight);
                break;
            }
            case PerformanceClipType.MoveCamera:
            {
                if (cameraController == null)
                {
                    Debug.LogWarning("[PerformanceRunner] 演出脚本请求移动镜头，但关卡没有 LevelCameraController");
                    return;
                }
                Vector2 start = cameraController.CurrentPosition;
                Vector2 target = clip.positionMode == PerformancePositionMode.World
                    ? clip.targetPosition
                    : cameraStart + clip.targetPosition;
                var action = new ActionHandle();
                cueHandle.actions.Add(action);
                action.coroutine = StartCoroutine(RunCameraMove(start, target, clip, action));
                break;
            }
        }
    }

    private IEnumerator RunActorMove(ActorRuntime actor, Vector2 start, Vector2 target,
        PerformanceClip clip, ActionHandle handle)
    {
        int owner = AllocateOwnerToken();
        movementOwners[actor.transform] = owner;
        float duration = Mathf.Max(0f, clip.duration);

        if (clip.autoFaceMovement && Mathf.Abs(target.x - start.x) > 0.001f)
            SetFacing(actor, target.x > start.x);

        string moveAnimation = !string.IsNullOrEmpty(clip.moveAnimationOverride)
            ? clip.moveAnimationOverride
            : actor.moveAnimation;
        int moveTrack = Mathf.Max(0, clip.moveAnimationTrack);
        bool playedMoveAnimation = clip.playMoveAnimation && duration > 0f &&
                                   !string.IsNullOrEmpty(moveAnimation);
        if (playedMoveAnimation)
            PlayAnimation(actor, moveAnimation, true, moveTrack, clip.moveAnimationMixDuration);

        if (duration <= 0f)
        {
            SetPosition(actor, target);
            handle.complete = true;
            yield break;
        }

        float elapsed = 0f;
        while (!handle.cancelled && actor.gameObject != null && elapsed < duration)
        {
            if (!movementOwners.TryGetValue(actor.transform, out int currentOwner) || currentOwner != owner)
                break;
            elapsed += Time.unscaledDeltaTime;
            float t = ApplyEasing(Mathf.Clamp01(elapsed / duration), clip.easing);
            SetPosition(actor, Vector2.LerpUnclamped(start, target, t));
            yield return null;
        }

        if (!handle.cancelled && actor.gameObject != null &&
            movementOwners.TryGetValue(actor.transform, out int finalOwner) && finalOwner == owner)
        {
            SetPosition(actor, target);
            movementOwners.Remove(actor.transform);
            if (playedMoveAnimation && clip.restoreIdleAfterMove &&
                IsPlayingAnimation(actor, moveAnimation, moveTrack))
                PlayAnimation(actor, actor.idleAnimation, true, moveTrack,
                    clip.moveAnimationMixDuration);
        }
        handle.complete = true;
    }

    private IEnumerator RunCameraMove(Vector2 start, Vector2 target, PerformanceClip clip, ActionHandle handle)
    {
        int owner = AllocateOwnerToken();
        cameraOwnerToken = owner;
        float duration = Mathf.Max(0f, clip.duration);
        if (duration <= 0f)
        {
            cameraController.SetFlowPosition(target);
            handle.complete = true;
            yield break;
        }

        float elapsed = 0f;
        while (!handle.cancelled && cameraController != null && elapsed < duration && cameraOwnerToken == owner)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = ApplyEasing(Mathf.Clamp01(elapsed / duration), clip.easing);
            cameraController.SetFlowPosition(Vector2.LerpUnclamped(start, target, t));
            yield return null;
        }
        if (!handle.cancelled && cameraController != null && cameraOwnerToken == owner)
            cameraController.SetFlowPosition(target);
        handle.complete = true;
    }

    private void ApplyFinalState(StoryPerformanceSession session, List<StoryPerformanceCue> cues)
    {
        var animationStates = new Dictionary<GameObject, AnimationEndState>();
        bool restoreCamera = true;

        foreach (var cue in cues)
        {
            var script = cue?.performanceScript;
            if (script == null) continue;
            restoreCamera &= script.restoreCameraFollowOnStoryEnd;

            Dictionary<string, Vector2> actorStarts;
            Vector2 cameraStart;
            if (!session.TryGetCueOrigins(cue, out actorStarts, out cameraStart))
            {
                actorStarts = new Dictionary<string, Vector2>();
                foreach (var slot in script.actorSlots ?? new List<PerformanceActorSlot>())
                {
                    if (slot == null || string.IsNullOrEmpty(slot.slotId)) continue;
                    var actor = session.ResolveActor(cue, slot.slotId);
                    if (actor != null)
                        actorStarts[slot.slotId] = actor.transform.position;
                }
                cameraStart = cameraController != null ? cameraController.CurrentPosition : Vector2.zero;
            }

            foreach (var clip in (script.clips ?? new List<PerformanceClip>())
                         .Where(item => item != null)
                         .OrderBy(item => Mathf.Max(0f, item.startTime)))
            {
                if (clip.clipType == PerformanceClipType.MoveCamera)
                {
                    if (cameraController != null)
                    {
                        Vector2 cameraTarget = clip.positionMode == PerformancePositionMode.World
                            ? clip.targetPosition
                            : cameraStart + clip.targetPosition;
                        cameraController.SetFlowPosition(cameraTarget);
                    }
                    continue;
                }

                var actor = session.ResolveActor(cue, clip.actorSlotId);
                if (actor == null) continue;

                switch (clip.clipType)
                {
                    case PerformanceClipType.MoveActor:
                        Vector2 origin = actorStarts.TryGetValue(clip.actorSlotId, out var start)
                            ? start
                            : (Vector2)actor.transform.position;
                        Vector2 target = clip.positionMode == PerformancePositionMode.World
                            ? clip.targetPosition
                            : origin + clip.targetPosition;
                        if (clip.autoFaceMovement && Mathf.Abs(target.x - actor.transform.position.x) > 0.001f)
                            SetFacing(actor, target.x > actor.transform.position.x);
                        SetPosition(actor, target);
                        break;
                    case PerformanceClipType.SetFacing:
                        SetFacing(actor, clip.faceRight);
                        break;
                    case PerformanceClipType.PlayAnimation:
                        animationStates[actor.gameObject] = new AnimationEndState
                        {
                            keep = clip.keepAnimationAfterStory,
                            animationName = clip.animationName,
                            loop = clip.loopAnimation,
                            track = clip.animationTrack,
                            mixDuration = clip.animationMixDuration
                        };
                        break;
                }
            }
        }

        foreach (var pair in animationStates)
        {
            if (pair.Key == null || !session.TryGetActor(pair.Key, out var actor)) continue;
            var state = pair.Value;
            if (state.keep && !string.IsNullOrEmpty(state.animationName))
                PlayAnimation(actor, state.animationName, state.loop, state.track, state.mixDuration);
            else
                PlayAnimation(actor, actor.idleAnimation, true, 0, 0.1f);
        }

        foreach (var actor in session.GetActors())
        {
            if (actor?.gameObject == null || animationStates.ContainsKey(actor.gameObject)) continue;
            PlayAnimation(actor, actor.idleAnimation, true, 0, 0.1f);
        }

        if (restoreCamera && cameraController != null)
            cameraController.ResumeFollowing();
    }

    private ActorRuntime CreateActorRuntime(GameObject go, string idleAnimation, string moveAnimation)
    {
        var actor = new ActorRuntime
        {
            gameObject = go,
            transform = go.transform,
            rigidbody = go.GetComponent<Rigidbody2D>(),
            player = go.GetComponent<PlayerMovement>(),
            spine = go.GetComponentInChildren<SkeletonAnimation>(true),
            animator = go.GetComponentInChildren<Animator>(true),
            idleAnimation = string.IsNullOrEmpty(idleAnimation) ? "idle" : idleAnimation,
            moveAnimation = string.IsNullOrEmpty(moveAnimation) ? "run" : moveAnimation
        };

        if (actor.spine != null)
            manuallyUpdatedSpines.Add(actor.spine);
        if (actor.animator != null)
        {
            actor.animatorUpdateMode = actor.animator.updateMode;
            actor.animatorModeCaptured = true;
            actor.animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        }
        if (actor.player != null)
            actor.player.BeginPerformanceAnimationControl();
        return actor;
    }

    private void ReleaseActors(IEnumerable<ActorRuntime> actors)
    {
        foreach (var actor in actors)
        {
            if (actor == null) continue;
            if (actor.spine != null)
                manuallyUpdatedSpines.Remove(actor.spine);
            if (actor.animator != null && actor.animatorModeCaptured)
                actor.animator.updateMode = actor.animatorUpdateMode;
            if (actor.player != null)
                actor.player.EndPerformanceAnimationControl();
            if (actor.transform != null)
                movementOwners.Remove(actor.transform);
        }
    }

    private static void SetPosition(ActorRuntime actor, Vector2 position)
    {
        if (actor?.gameObject == null) return;
        if (actor.player != null)
        {
            actor.player.SetPerformancePosition(position);
            return;
        }
        if (actor.rigidbody != null)
            actor.rigidbody.position = position;
        actor.transform.position = new Vector3(position.x, position.y, actor.transform.position.z);
    }

    private static void SetFacing(ActorRuntime actor, bool faceRight)
    {
        if (actor?.gameObject == null) return;
        if (actor.player != null)
        {
            actor.player.SetPerformanceFacing(faceRight);
            return;
        }

        Vector3 scale = actor.transform.localScale;
        scale.x = (faceRight ? 1f : -1f) * Mathf.Abs(scale.x);
        actor.transform.localScale = scale;
    }

    private static void PlayAnimation(ActorRuntime actor, string animationName,
        bool loop, int track, float mixDuration)
    {
        if (actor?.gameObject == null || string.IsNullOrEmpty(animationName)) return;

        if (actor.player != null &&
            actor.player.PlayPerformanceAnimation(animationName, loop, track, mixDuration))
            return;

        if (actor.spine != null && actor.spine.AnimationState != null)
        {
            var entry = actor.spine.AnimationState.SetAnimation(Mathf.Max(0, track), animationName, loop);
            if (entry != null) entry.MixDuration = Mathf.Max(0f, mixDuration);
            return;
        }

        if (actor.animator != null)
        {
            int layer = Mathf.Clamp(track, 0, Mathf.Max(0, actor.animator.layerCount - 1));
            int stateHash = Animator.StringToHash(animationName);
            if (!actor.animator.HasState(layer, stateHash))
            {
                Debug.LogWarning($"[PerformanceRunner] Animator 状态不存在: {animationName} ({actor.gameObject.name})");
                return;
            }
            if (mixDuration > 0f)
                actor.animator.CrossFade(stateHash, mixDuration, layer);
            else
                actor.animator.Play(stateHash, layer, 0f);
        }
    }

    private static bool IsPlayingAnimation(ActorRuntime actor, string animationName, int track)
    {
        if (actor?.gameObject == null || string.IsNullOrEmpty(animationName)) return false;
        if (actor.spine != null && actor.spine.AnimationState != null)
        {
            var current = actor.spine.AnimationState.GetCurrent(Mathf.Max(0, track));
            return current?.Animation != null && current.Animation.Name == animationName;
        }

        if (actor.animator != null)
        {
            int layer = Mathf.Clamp(track, 0, Mathf.Max(0, actor.animator.layerCount - 1));
            return actor.animator.GetCurrentAnimatorStateInfo(layer)
                .shortNameHash == Animator.StringToHash(animationName);
        }
        return false;
    }

    private int AllocateOwnerToken()
    {
        int result = nextOwnerToken++;
        if (nextOwnerToken <= 0) nextOwnerToken = 1;
        return result;
    }

    private static float ApplyEasing(float t, LevelFlowEasing easing)
    {
        return easing == LevelFlowEasing.SmoothStep ? t * t * (3f - 2f * t) : t;
    }
}
