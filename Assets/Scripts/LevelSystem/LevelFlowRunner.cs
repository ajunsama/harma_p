using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LevelFlowRunner : MonoBehaviour
{
    private const float SafeStateTimeout = 5f;

    private LevelData levelData;
    private LevelVariableManager variables;
    private PlayerMovement player;
    private LevelCameraController cameraController;
    private PerformanceRunner performanceRunner;

    private readonly Queue<LevelFlowData> queue = new Queue<LevelFlowData>();
    private readonly HashSet<LevelFlowData> queued = new HashSet<LevelFlowData>();
    private readonly HashSet<LevelFlowData> completed = new HashSet<LevelFlowData>();
    private readonly Dictionary<LevelFlowData, int> lockTokens = new Dictionary<LevelFlowData, int>();
    private Coroutine runner;

    public void Initialize(LevelData data, LevelVariableManager variableManager,
        PlayerMovement playerMovement, LevelCameraController camera, PerformanceRunner performance)
    {
        levelData = data;
        variables = variableManager;
        player = playerMovement;
        cameraController = camera;
        performanceRunner = performance;
    }

    public void Tick(float playerX)
    {
        if (levelData?.events == null) return;

        foreach (var flow in levelData.events)
        {
            if (flow == null || flow.triggerMode != StoryTriggerMode.Position) continue;
            bool crossed = flow.triggerFromLeft ? playerX >= flow.positionX : playerX <= flow.positionX;
            if (crossed && variables.CheckAllConditions(flow.triggerConditions))
                Queue(flow);
        }
    }

    public void NotifyConditionsChanged()
    {
        TriggerMode(StoryTriggerMode.Conditions);
    }

    public void TriggerMode(StoryTriggerMode mode)
    {
        if (levelData?.events == null) return;

        foreach (var flow in levelData.events)
        {
            if (flow != null && flow.triggerMode == mode &&
                variables.CheckAllConditions(flow.triggerConditions))
                Queue(flow);
        }
    }

    private void Queue(LevelFlowData flow)
    {
        if (flow == null || queued.Contains(flow) || (flow.triggerOnce && completed.Contains(flow)))
            return;

        if (player == null)
        {
            Debug.LogError($"[LevelFlowRunner] Cannot queue flow '{flow.flowId}': player is missing.");
            return;
        }

        // Lock immediately when a trigger becomes true. This is deliberately
        // earlier than dequeuing so a stomp bounce cannot accept more input.
        lockTokens[flow] = player.AcquireControlLock($"LevelFlow:{flow.flowId}");
        queued.Add(flow);
        queue.Enqueue(flow);

        if (runner == null)
            runner = StartCoroutine(RunQueue());
    }

    private IEnumerator RunQueue()
    {
        while (queue.Count > 0)
        {
            LevelFlowData flow = queue.Dequeue();
            bool success = true;

            yield return WaitForPlayerSafe(result => success = result);
            if (success && flow.steps != null)
            {
                foreach (var step in flow.steps)
                {
                    if (step == null) continue;
                    yield return ExecuteStep(flow, step, result => success = result);
                    if (!success) break;
                }
            }

            if (success)
                completed.Add(flow);
            else
            {
                Debug.LogError($"[LevelFlowRunner] Flow '{flow.flowId}' aborted.");
                player?.StopScriptedMovement();
                cameraController?.ResumeFollowing();
            }

            ReleaseFlowLock(flow);
            queued.Remove(flow);
        }

        runner = null;
    }

    private IEnumerator ExecuteStep(LevelFlowData flow, LevelFlowStep step, Action<bool> complete)
    {
        switch (step.stepType)
        {
            case LevelFlowStepType.WaitForPlayerSafe:
                yield return WaitForPlayerSafe(complete);
                yield break;

            case LevelFlowStepType.Wait:
                if (step.duration > 0f)
                    yield return new WaitForSeconds(step.duration);
                complete(true);
                yield break;

            case LevelFlowStepType.SetVariable:
                if (step.setVariable == null)
                {
                    Debug.LogError($"[LevelFlowRunner] Flow '{flow.flowId}' has an empty set-variable step.");
                    complete(false);
                }
                else
                {
                    variables.ApplySetAction(step.setVariable);
                    complete(true);
                }
                yield break;

            case LevelFlowStepType.MovePlayer:
                if (player == null || step.speed <= 0f)
                {
                    complete(false);
                    yield break;
                }
                while (player != null && !player.MoveScriptedTowards(step.targetPosition, step.speed, step.tolerance))
                    yield return null;
                if (player == null)
                {
                    complete(false);
                    yield break;
                }
                complete(true);
                yield break;

            case LevelFlowStepType.MoveCamera:
                if (cameraController == null || step.duration < 0f)
                {
                    Debug.LogError($"[LevelFlowRunner] Flow '{flow.flowId}' cannot move the camera.");
                    complete(false);
                    yield break;
                }
                Vector2 start = cameraController.CurrentPosition;
                if (step.duration <= 0f)
                {
                    cameraController.SetFlowPosition(step.targetPosition);
                }
                else
                {
                    float elapsed = 0f;
                    while (elapsed < step.duration)
                    {
                        if (cameraController == null)
                        {
                            complete(false);
                            yield break;
                        }
                        elapsed += Time.deltaTime;
                        float t = Mathf.Clamp01(elapsed / step.duration);
                        if (step.easing == LevelFlowEasing.SmoothStep)
                            t = t * t * (3f - 2f * t);
                        cameraController.SetFlowPosition(Vector2.LerpUnclamped(start, step.targetPosition, t));
                        yield return null;
                    }
                    cameraController.SetFlowPosition(step.targetPosition);
                }
                complete(true);
                yield break;

            case LevelFlowStepType.ResumeCameraFollow:
                if (cameraController == null)
                {
                    complete(false);
                    yield break;
                }
                cameraController.ResumeFollowing();
                complete(true);
                yield break;

            case LevelFlowStepType.PlayStory:
                StoryManager story = StoryManager.Instance;
                if (story == null || !story.HasStory(step.storyId))
                {
                    Debug.LogError($"[LevelFlowRunner] Flow '{flow.flowId}' references missing story '{step.storyId}'.");
                    complete(false);
                    yield break;
                }

                while (story.IsPlaying)
                    yield return null;

                bool storyFinished = false;
                IStoryPerformanceSession performanceSession = null;
                if (performanceRunner != null)
                {
                    var sequence = story.GetStory(step.storyId);
                    if (sequence?.performanceCues != null && sequence.performanceCues.Count > 0)
                    {
                        performanceSession = performanceRunner.CreateSession(
                            sequence.performanceCues,
                            step.storyCastBindings,
                            step.storyPerformanceScripts);
                    }
                    else
                    {
                        // 兼容旧关卡：旧版 Cue 直接保存在 PlayStory 步骤中。
                        performanceSession = performanceRunner.CreateSession(step.storyPerformanceCues);
                    }
                }
                story.PlayStory(step.storyId, () => storyFinished = true, performanceSession);
                while (!storyFinished)
                    yield return null;
                complete(true);
                yield break;

            default:
                complete(false);
                yield break;
        }
    }

    private IEnumerator WaitForPlayerSafe(Action<bool> complete)
    {
        if (player == null)
        {
            complete(false);
            yield break;
        }

        float elapsed = 0f;
        while (true)
        {
            while (!player.IsSafeForStory)
            {
                elapsed += Time.unscaledDeltaTime;
                if (elapsed >= SafeStateTimeout)
                {
                    Debug.LogError("[LevelFlowRunner] Timed out waiting for the player to reach a safe state.");
                    complete(false);
                    yield break;
                }
                yield return null;
            }

            player.PrepareStandingAnimationForStory();

            // Let Rigidbody2D settle in a physics step, then allow one complete
            // rendered frame for Spine to apply the standing pose before the
            // story pauses scaled time.
            yield return new WaitForFixedUpdate();
            yield return null;
            if (player == null)
            {
                complete(false);
                yield break;
            }
            elapsed += Time.unscaledDeltaTime;
            if (elapsed >= SafeStateTimeout)
            {
                Debug.LogError("[LevelFlowRunner] Timed out waiting for the player's standing animation.");
                complete(false);
                yield break;
            }
            if (player.IsReadyForStory)
                break;
        }

        complete(true);
    }

    private void ReleaseFlowLock(LevelFlowData flow)
    {
        if (player != null && lockTokens.TryGetValue(flow, out int token))
            player.ReleaseControlLock(token);
        lockTokens.Remove(flow);
    }

    private void OnDestroy()
    {
        if (player != null)
            foreach (int token in lockTokens.Values)
                player.ReleaseControlLock(token);
        lockTokens.Clear();
    }
}
