using System;
using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public partial class StoryUI
{
/// <summary>
    /// 播放入场动画序列：角色从两侧进入 + 对话框从说话者方向进入
    /// 动画的具体表现（位移/旋转/缩放/缓动）全部在 StoryAnimator Inspector 中配置
    /// </summary>
    /// <param name="firstSpeakerIsLeft">第一个说话者在左侧</param>
    /// <param name="speakerName">第一个说话者名称（用于色块入场动画）</param>
    /// <param name="entranceColor">入场色块颜色</param>
    public IEnumerator PlayEntranceAnimation(bool firstSpeakerIsLeft, string speakerName, Color entranceColor)
    {
        if (animator == null) yield break;

        // 入场动画自身就是"显示"过程，直接将 alpha 设为1并停止淡入协程
        // 否则角色动画会被 CanvasGroup alpha=0 的渐变遮盖
        StopFade();
        if (_canvasGroup != null)
            _canvasGroup.alpha = 1f;

        // 入场前设置说话者状态：暗化遮罩 + 对话框形状
        _currentSpeakerSide = firstSpeakerIsLeft ? "left" : "right";

        // 入场时头像颜色始终为白色，通过遮罩暗化非活跃方
        Color leftColor = Color.white;
        Color rightColor = Color.white;

        // 入场前先设置暗化遮罩状态
        SetDimOverlay(_avatarLeftOverlay, !firstSpeakerIsLeft);
        SetDimOverlay(_avatarRightOverlay, firstSpeakerIsLeft);

        // 左侧说话者标识图：入场时根据说话者立即设置透明度（不做动画）
        if (leftSpeakerIcon != null)
        {
            var c = leftSpeakerIcon.color;
            leftSpeakerIcon.color = new Color(c.r, c.g, c.b, firstSpeakerIsLeft ? 1f : 0f);
        }

        // 对话框形状立即设置（不播放变形动画）
        var irregularBox = dialogueBoxImage as IrregularDialogBox;
        if (irregularBox != null)
            irregularBox.MirrorProgress = firstSpeakerIsLeft ? 0f : 1f;

        // 背景条：设置初始颜色和角度
        if (avatarBarController != null)
        {
            if (animator != null)
                avatarBarController.SetSyncEase(animator.characterEntranceEase);
            avatarBarController.Setup(entranceColor, firstSpeakerIsLeft);
            // 强制背景条排在最底层（sibling index 最小 = 最先渲染 = 最后面）
            if (avatarBarController.leftBar != null)
                avatarBarController.leftBar.transform.SetAsFirstSibling();
            if (avatarBarController.rightBar != null)
                avatarBarController.rightBar.transform.SetSiblingIndex(1);
        }

        // 等待背景模糊等效果先生效
        if (entranceDelay > 0f)
            yield return new WaitForSecondsRealtime(entranceDelay);

        // 角色入场（不等待完成，与色块动画并行）
        RectTransform leftRT = avatarLeft != null ? avatarLeft.rectTransform : null;
        RectTransform rightRT = avatarRight != null ? avatarRight.rectTransform : null;
        bool hasLeftAvatar = avatarLeft != null && avatarLeft.sprite != null;
        bool hasRightAvatar = avatarRight != null && avatarRight.sprite != null;
        RectTransform leftBarRT = hasLeftAvatar && avatarBarController != null && avatarBarController.leftBar != null
            ? avatarBarController.leftBar.rectTransform : null;
        RectTransform rightBarRT = hasRightAvatar && avatarBarController != null && avatarBarController.rightBar != null
            ? avatarBarController.rightBar.rectTransform : null;
        Coroutine entranceCoroutine = animator.PlayFullEntrance(
            leftRT, rightRT, leftColor, rightColor, leftBarRT, rightBarRT);

        // 色块入场特效与头像入场同时启动
        Sequence entranceSeq = null;
        if (effectController != null)
        {
            float charInterval = speakerNameController != null ? speakerNameController.CharInterval : -1f;
            GameLog.Verbose($"[Entrance] charInterval={charInterval:F3}, speakerName='{speakerName}'");

            // 先以无动画方式设置名称（设置文本/位置/alpha，maxVisibleCharacters=0）
            SetSpeakerName(speakerName, firstSpeakerIsLeft, false);
            // ShowImmediate 内部启动了协程逐字显示，将其终止，改由 Sequence 驱动
            speakerNameController?.CancelCharReveal();

            // 构建 Phase1 callbacks：每个色块出现时，让对应字符可见
            int nameLen = string.IsNullOrEmpty(speakerName) ? 0 : speakerName.Length;
            System.Action[] phase1Callbacks = new System.Action[nameLen];
            for (int ci = 0; ci < nameLen; ci++)
            {
                int captured = ci;
                float expectedTime = charInterval > 0f ? charInterval * captured : effectController.fadeInInterval * captured;
                GameLog.Verbose($"[Entrance] 注册 callback: char[{captured}] 预计触发时刻 t={expectedTime:F3}s");
                phase1Callbacks[captured] = () => speakerNameController?.RevealChar(captured);
            }

            entranceSeq = effectController.PlayEntrance(speakerName, entranceColor, firstSpeakerIsLeft, charInterval, phase1Callbacks);
        }

        // 等待头像入场和色块入场中较长的完成
        if (entranceSeq != null)
            yield return entranceSeq.WaitForCompletion();
        // 确保头像入场也已完成
        if (entranceCoroutine != null)
            yield return entranceCoroutine;
    }

/// <summary>
    /// 播放换边过渡：色块散开 + DialogBox变形 + 条纹角度旋转 同步执行
    /// </summary>
    public IEnumerator PlaySideTransition(string newSide, Color fromColor, Color toColor, Action onMidpoint)
    {
        bool toMirrored = newSide?.ToLower() == "right";

        // 计算同步时长：使用 DialogBox 变形时长作为统一时长
        float syncDuration = -1f;
        if (effectController != null && effectController.dialogBox != null)
            syncDuration = effectController.dialogBox.MorphDuration;

        // 预设同步时长，这样 onMidpoint 内的 SetSpeakerName 会自动拾取并使用
        _pendingNameSyncDuration = syncDuration;

        // 0. 立即清空旧文本，防止切换动画期间显示上一句内容
        if (contentText != null)
            contentText.text = "";

        // 1. 立即切换内容（在动画开始时就切换文本/头像数据）
        //    SetupDialogueDisplay 会调用 SetSpeakerName，后者会消费 _pendingNameSyncDuration
        onMidpoint?.Invoke();

        // 2. 同步启动：色块换边特效 + DialogBox形状变形 + 头像高亮
        Sequence switchSeq = null;
        if (effectController != null)
        {
            effectController.SetBaseColor(fromColor);
            switchSeq = effectController.PlaySwitchTransition(toColor, toMirrored);
        }

        // 启动形状变形（与色块动画同步）
        HighlightSpeaker(newSide);

        // 背景条换边动画（角度旋转 + 颜色变化，与其他动画并行）
        if (avatarBarController != null)
        {
            if (syncDuration > 0f)
                avatarBarController.SetSyncDuration(syncDuration);
            if (animator != null)
                avatarBarController.SetSyncEase(animator.characterEntranceEase);
            avatarBarController.PlaySwitchTransition(toColor, newSide?.ToLower() == "left");
        }

        // 等待色块动画完成（变形协程独立运行，会自然结束）
        if (switchSeq != null)
            yield return switchSeq.WaitForCompletion();
    }
}
