using System.Collections;
using UnityEngine;
using Spine.Unity;

/// <summary>
/// 控制Spine对象按顺序播放舞蹈动画
/// 播放顺序：dance03 -> dance to dance02 -> dance02（循环）
/// </summary>
public class SpineDanceSequence : MonoBehaviour
{
    [Header("Spine组件")]
    [SerializeField] private SkeletonAnimation skeletonAnimation;

    [Header("动画名称")]
    [SerializeField] private string dance03Animation = "dance03";
    [SerializeField] private string transitionAnimation = "dance to dance02";
    [SerializeField] private string dance02Animation = "dance02";

    [Header("设置")]
    [SerializeField] private float startDelay = 5f;
    [SerializeField] private int trackIndex = 0;

    private void Start()
    {
        // 自动获取SkeletonAnimation组件
        if (skeletonAnimation == null)
        {
            skeletonAnimation = GetComponent<SkeletonAnimation>();
        }

        if (skeletonAnimation == null)
        {
            Debug.LogError("SpineDanceSequence: 未找到SkeletonAnimation组件！");
            return;
        }

        // 延时开始播放动画序列
        StartCoroutine(PlayDanceSequence());
    }

    private IEnumerator PlayDanceSequence()
    {
        // 等待指定延时
        yield return new WaitForSeconds(startDelay);

        // 播放 dance03
        var entry = skeletonAnimation.AnimationState.SetAnimation(trackIndex, dance03Animation, false);
        if (entry != null)
        {
            Debug.Log($"开始播放: {dance03Animation}");
            // 等待动画播放完成
            yield return new WaitForSpineAnimationComplete(entry);
        }
        else
        {
            Debug.LogWarning($"动画 '{dance03Animation}' 未找到！");
        }

        // 播放过渡动画 dance to dance02
        entry = skeletonAnimation.AnimationState.SetAnimation(trackIndex, transitionAnimation, false);
        if (entry != null)
        {
            Debug.Log($"开始播放: {transitionAnimation}");
            // 等待动画播放完成
            yield return new WaitForSpineAnimationComplete(entry);
        }
        else
        {
            Debug.LogWarning($"动画 '{transitionAnimation}' 未找到！");
        }

        // 播放 dance02 无限循环
        entry = skeletonAnimation.AnimationState.SetAnimation(trackIndex, dance02Animation, true);
        if (entry != null)
        {
            Debug.Log($"开始循环播放: {dance02Animation}");
        }
        else
        {
            Debug.LogWarning($"动画 '{dance02Animation}' 未找到！");
        }
    }
}

/// <summary>
/// 自定义等待Spine动画完成的Coroutine
/// </summary>
public class WaitForSpineAnimationComplete : CustomYieldInstruction
{
    private Spine.TrackEntry trackEntry;
    private bool isComplete = false;

    public override bool keepWaiting => !isComplete;

    public WaitForSpineAnimationComplete(Spine.TrackEntry entry)
    {
        trackEntry = entry;
        if (trackEntry != null)
        {
            trackEntry.Complete += OnComplete;
        }
        else
        {
            isComplete = true;
        }
    }

    private void OnComplete(Spine.TrackEntry entry)
    {
        isComplete = true;
        if (trackEntry != null)
        {
            trackEntry.Complete -= OnComplete;
        }
    }
}
