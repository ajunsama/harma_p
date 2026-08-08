using UnityEngine;

[CreateAssetMenu(fileName = "GameFlowConfig", menuName = "Game/Game Flow Config")]
public sealed class GameFlowConfig : ScriptableObject
{
    [Header("Scene Names")]
    [SerializeField] private string startMenuSceneName = "StartGame";
    [SerializeField] private string gameOverSceneName = "GameOver";
    [SerializeField] private string gameClearSceneName = "GameClear";
    [SerializeField] private string firstGameplaySceneName = "NewLevel_test";

    [Header("Timing")]
    [Min(0f)]
    [SerializeField] private float gameOverDelay = 1.2f;
    [Min(0f)]
    [SerializeField] private float gameClearDelay = 0.8f;

    public string StartMenuSceneName => startMenuSceneName;
    public string GameOverSceneName => gameOverSceneName;
    public string GameClearSceneName => gameClearSceneName;
    public string FirstGameplaySceneName => firstGameplaySceneName;
    public float GameOverDelay => Mathf.Max(0f, gameOverDelay);
    public float GameClearDelay => Mathf.Max(0f, gameClearDelay);
}
