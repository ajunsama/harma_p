using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public partial class StoryManager
{
// ====================
    // 结果处理
    // ====================

    /// <summary>
    /// 处理剧情结果动作
    /// </summary>
    public void ProcessResultAction(StoryResultAction action)
    {
        if (action == null) return;

        switch (action.actionType)
        {
            case StoryResultAction.ActionType.SpawnEnemies:
                // TODO: Phase 3 — 通过 LevelSceneBuilder 生成敌人
                break;

            case StoryResultAction.ActionType.GameOver:
                var levelBuilder = FindObjectOfType<LevelSceneBuilder>();
                if (levelBuilder != null)
                    levelBuilder.FailLevel();
                else
                    GameFlowService.LoadGameOver();
                break;

            case StoryResultAction.ActionType.LevelComplete:
                var completionBuilder = FindObjectOfType<LevelSceneBuilder>();
                if (completionBuilder != null)
                    completionBuilder.CompleteLevel();
                else
                    GameFlowService.LoadGameClear();
                break;

            case StoryResultAction.ActionType.LoadScene:
                if (!string.IsNullOrEmpty(action.parameter))
                {
                    Time.timeScale = 1f;
                    UnityEngine.SceneManagement.SceneManager.LoadScene(action.parameter);
                }
                break;

            case StoryResultAction.ActionType.SetFlag:
                if (!string.IsNullOrEmpty(action.parameter))
                {
                    SetFlag(action.parameter);
                }
                break;

            case StoryResultAction.ActionType.None:
            default:
                break;
        }
    }
}
