using UnityEngine;
using UnityEngine.SceneManagement;

public static class GameFlowService
{
    private const string ConfigResourceName = "GameFlowConfig";

    private static GameFlowConfig _config;
    private static bool _isLoadingScene;
    private static bool _missingConfigLogged;

    public static string LastGameplaySceneName { get; private set; }

    public static GameFlowConfig Config
    {
        get
        {
            if (_config != null)
                return _config;

            _config = Resources.Load<GameFlowConfig>(ConfigResourceName);
            if (_config == null && !_missingConfigLogged)
            {
                _missingConfigLogged = true;
                Debug.LogError(
                    $"[GameFlow] Missing Resources/{ConfigResourceName}.asset. " +
                    "Scene navigation will use fallback defaults.");
            }

            return _config;
        }
    }

    public static float GameOverDelay => Config != null ? Config.GameOverDelay : 1.2f;
    public static float GameClearDelay => Config != null ? Config.GameClearDelay : 0.8f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        _isLoadingScene = false;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    public static void StartNewGame()
    {
        string sceneName = Config != null ? Config.FirstGameplaySceneName : "NewLevel_test";
        if (TryLoadScene(sceneName))
            LastGameplaySceneName = sceneName;
    }

    public static void LoadGameOver()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        string gameOverScene = Config != null ? Config.GameOverSceneName : "GameOver";
        string startMenuScene = Config != null ? Config.StartMenuSceneName : "StartGame";

        if (activeScene.IsValid() &&
            activeScene.name != gameOverScene &&
            activeScene.name != startMenuScene)
        {
            LastGameplaySceneName = activeScene.name;
        }

        TryLoadScene(gameOverScene);
    }

    public static void LoadGameClear()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        string gameClearScene = Config != null ? Config.GameClearSceneName : "GameClear";
        string gameOverScene = Config != null ? Config.GameOverSceneName : "GameOver";
        string startMenuScene = Config != null ? Config.StartMenuSceneName : "StartGame";

        if (activeScene.IsValid() &&
            activeScene.name != gameClearScene &&
            activeScene.name != gameOverScene &&
            activeScene.name != startMenuScene)
        {
            LastGameplaySceneName = activeScene.name;
        }

        TryLoadScene(gameClearScene);
    }

    public static void RestartLastGameplayScene()
    {
        string fallback = Config != null ? Config.FirstGameplaySceneName : "NewLevel_test";
        string gameOverScene = Config != null ? Config.GameOverSceneName : "GameOver";
        string gameClearScene = Config != null ? Config.GameClearSceneName : "GameClear";
        string startMenuScene = Config != null ? Config.StartMenuSceneName : "StartGame";
        Scene activeScene = SceneManager.GetActiveScene();
        bool activeSceneIsGameplay = activeScene.IsValid() &&
                                     activeScene.name != gameOverScene &&
                                     activeScene.name != gameClearScene &&
                                     activeScene.name != startMenuScene;
        string target = activeSceneIsGameplay
            ? activeScene.name
            : string.IsNullOrWhiteSpace(LastGameplaySceneName)
                ? fallback
                : LastGameplaySceneName;

        if (TryLoadScene(target))
            LastGameplaySceneName = target;
    }

    public static void ReturnToMainMenu()
    {
        string sceneName = Config != null ? Config.StartMenuSceneName : "StartGame";
        TryLoadScene(sceneName);
    }

    public static bool IsSceneAvailable(string sceneName)
    {
        return !string.IsNullOrWhiteSpace(sceneName) &&
               Application.CanStreamedLevelBeLoaded(sceneName);
    }

    public static void QuitGame()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static bool TryLoadScene(string sceneName)
    {
        if (_isLoadingScene)
            return false;

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("[GameFlow] Cannot load an empty scene name.");
            return false;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError(
                $"[GameFlow] Scene '{sceneName}' is not available. " +
                "Add it to Build Settings or update GameFlowConfig.");
            return false;
        }

        _isLoadingScene = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        return true;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _isLoadingScene = false;
    }
}
