using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public partial class StoryManager
{
/// <summary>
    /// 等待打字机效果完成，或者玩家点击提前完成
    /// </summary>
    IEnumerator WaitForTypewriterOrSkip()
    {
        while (storyUI != null && storyUI.IsTypewriting)
        {
            // 检测点击或按键输入来跳过打字效果
            if (GetConfirmInput())
            {
                storyUI.CompleteTypewriter();
                // 等待玩家松开按键，防止同一次点击被下一步WaitForPlayerInput消费
                yield return WaitForInputRelease();
                yield break;
            }
            yield return null;
        }
    }

/// <summary>
    /// 等待玩家点击/按键输入以继续下一句
    /// </summary>
    IEnumerator WaitForPlayerInput()
    {
        _waitingForInput = true;

        // 先等一帧，确保不会读到上次的输入
        yield return null;

        while (_waitingForInput)
        {
            if (GetConfirmInput())
            {
                _waitingForInput = false;
            }
            yield return null;
        }

    }

/// <summary>
    /// 等待所有确认按键都释放（防止一次点击被多步骤连续消费）
    /// </summary>
    IEnumerator WaitForInputRelease()
    {
        while (IsConfirmHeld())
        {
            yield return null;
        }
    }

/// <summary>
    /// 检测确认键是否正在按住
    /// </summary>
    bool IsConfirmHeld()
    {
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            return true;
        if (Keyboard.current != null &&
            (Keyboard.current.spaceKey.isPressed || Keyboard.current.enterKey.isPressed))
            return true;
        if (Gamepad.current != null && Gamepad.current.buttonSouth.isPressed)
            return true;
        return false;
    }

/// <summary>
    /// 检测确认输入（鼠标左键点击 / 键盘Space/Enter / 手柄A键）
    /// </summary>
    bool GetConfirmInput()
    {
        // 鼠标左键
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            return true;

        // 键盘
        if (Keyboard.current != null)
        {
            if (Keyboard.current.spaceKey.wasPressedThisFrame ||
                Keyboard.current.enterKey.wasPressedThisFrame)
                return true;
        }

        // 手柄
        if (Gamepad.current != null)
        {
            if (Gamepad.current.buttonSouth.wasPressedThisFrame)
                return true;
        }

        return false;
    }
}
