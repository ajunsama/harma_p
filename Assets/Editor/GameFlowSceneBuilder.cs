using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class GameFlowSceneBuilder
{
    private const string StartScenePath = "Assets/Scenes/StartGame.unity";
    private const string GameOverScenePath = "Assets/Scenes/GameOver.unity";
    private const string GameClearScenePath = "Assets/Scenes/GameClear.unity";
    private const string TestSceneRoot = "Assets/Scenes/Tests/";
    private const string ConfigPath = "Assets/Resources/GameFlowConfig.asset";
    private const string ScanlineMaterialPath = "Assets/Materials/RetroScanlineMat.mat";
    private const string EnglishFontPath =
        "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    private const string ChineseFontPath = "Assets/Fonts/方正准圆_GBK SDF_dynamic.asset";

    private static readonly Color BackgroundColor = new Color32(5, 7, 20, 255);
    private static readonly Color PanelColor = new Color32(12, 18, 43, 247);
    private static readonly Color Cyan = new Color32(0, 240, 255, 255);
    private static readonly Color Magenta = new Color32(255, 35, 190, 255);
    private static readonly Color ArcadeRed = new Color32(255, 38, 62, 255);
    private static readonly Color WarmWhite = new Color32(245, 249, 255, 255);

    [MenuItem("Tools/Game Flow/Rebuild Game Flow Scenes")]
    public static void RebuildAll()
    {
        EnsureSharedAssets();

        Scene startScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildStartGameOnActiveScene();
        EditorSceneManager.SaveScene(startScene, StartScenePath);

        Scene gameOverScene =
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildGameOverOnActiveScene();
        EditorSceneManager.SaveScene(gameOverScene, GameOverScenePath);

        Scene gameClearScene =
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildGameClearOnActiveScene();
        EditorSceneManager.SaveScene(gameClearScene, GameClearScenePath);

        UpdateBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[GameFlowSceneBuilder] StartGame, GameOver and GameClear scenes rebuilt.");
    }

    public static void BuildStartGameOnActiveScene()
    {
        EnsureSharedAssets();
        ClearActiveScene();

        TMP_FontAsset englishFont = LoadFont(EnglishFontPath);
        TMP_FontAsset chineseFont = LoadFont(ChineseFontPath, englishFont);
        Material scanlineMaterial = AssetDatabase.LoadAssetAtPath<Material>(ScanlineMaterialPath);

        CreateCamera(BackgroundColor);
        CreateEventSystem();
        Canvas canvas = CreateCanvas();
        RectTransform canvasTransform = canvas.GetComponent<RectTransform>();

        CreateFullScreenImage("Background", canvasTransform, BackgroundColor, false);
        CreateArcadeBackgroundDecor(canvasTransform, Cyan, Magenta);

        Image scanlines = CreateFullScreenImage(
            "RetroScanlines",
            canvasTransform,
            new Color(1f, 1f, 1f, 0.5f),
            false);
        scanlines.material = scanlineMaterial;

        GameObject content = CreateUIObject("MainMenuContent", canvasTransform);
        Stretch(content.GetComponent<RectTransform>());
        CanvasGroup contentCanvasGroup = content.AddComponent<CanvasGroup>();

        RectTransform titleGroup = CreateUIObject("TitleGroup", content.transform)
            .GetComponent<RectTransform>();
        SetRect(titleGroup, new Vector2(0.5f, 0.5f), new Vector2(0f, 285f), new Vector2(1600f, 250f));
        CreateTitleLayer("MagentaShadow", titleGroup, "SHINY COLORS", englishFont, 154f,
            Magenta, new Vector2(10f, -7f));
        CreateTitleLayer("CyanShadow", titleGroup, "SHINY COLORS", englishFont, 154f,
            Cyan, new Vector2(-10f, 7f));
        CreateTitleLayer("Title", titleGroup, "SHINY COLORS", englishFont, 154f,
            WarmWhite, Vector2.zero);
        CreateText("Subtitle", titleGroup, "— PRESS START —", englishFont, 30f,
            new Color32(210, 225, 255, 255), TextAlignmentOptions.Center,
            new Vector2(0f, -100f), new Vector2(700f, 45f));

        RectTransform menu = CreateUIObject("MenuButtons", content.transform)
            .GetComponent<RectTransform>();
        SetRect(menu, new Vector2(0.5f, 0.5f), new Vector2(0f, -115f), new Vector2(470f, 340f));
        VerticalLayoutGroup layout = menu.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 20f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        Button newGameButton = CreateArcadeButton("NewGameButton", menu, "新游戏", chineseFont, Cyan);
        Button settingsButton = CreateArcadeButton("SettingsButton", menu, "设置", chineseFont, Magenta);
        Button quitButton = CreateArcadeButton("QuitButton", menu, "退出游戏", chineseFont, ArcadeRed);

        GameObject settingsPanel = BuildSettingsPanel(canvasTransform, englishFont, chineseFont,
            out Slider volumeSlider,
            out TMP_Dropdown resolutionDropdown,
            out Toggle fullScreenToggle,
            out TMP_Dropdown qualityDropdown,
            out Button applyButton,
            out Button backButton);

        MainMenuController controller = new GameObject("MainMenuController")
            .AddComponent<MainMenuController>();
        SerializedObject serializedController = new SerializedObject(controller);
        SetObjectReference(serializedController, "newGameButton", newGameButton);
        SetObjectReference(serializedController, "settingsButton", settingsButton);
        SetObjectReference(serializedController, "quitButton", quitButton);
        SetObjectReference(serializedController, "mainMenuCanvasGroup", contentCanvasGroup);
        SetObjectReference(serializedController, "titleGroup", titleGroup);
        SetObjectReference(serializedController, "settingsPanel", settingsPanel);
        SetObjectReference(serializedController, "volumeSlider", volumeSlider);
        SetObjectReference(serializedController, "resolutionDropdown", resolutionDropdown);
        SetObjectReference(serializedController, "fullScreenToggle", fullScreenToggle);
        SetObjectReference(serializedController, "qualityDropdown", qualityDropdown);
        SetObjectReference(serializedController, "applyButton", applyButton);
        SetObjectReference(serializedController, "backButton", backButton);
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        settingsPanel.SetActive(false);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    public static void BuildGameOverOnActiveScene()
    {
        EnsureSharedAssets();
        ClearActiveScene();

        TMP_FontAsset englishFont = LoadFont(EnglishFontPath);
        TMP_FontAsset chineseFont = LoadFont(ChineseFontPath, englishFont);
        Material scanlineMaterial = AssetDatabase.LoadAssetAtPath<Material>(ScanlineMaterialPath);

        CreateCamera(new Color32(10, 1, 7, 255));
        CreateEventSystem();
        Canvas canvas = CreateCanvas();
        RectTransform canvasTransform = canvas.GetComponent<RectTransform>();

        CreateFullScreenImage("Background", canvasTransform, new Color32(10, 1, 7, 255), false);
        CreateArcadeBackgroundDecor(canvasTransform, ArcadeRed, Magenta);

        Image scanlines = CreateFullScreenImage(
            "RetroScanlines",
            canvasTransform,
            new Color(1f, 1f, 1f, 0.62f),
            false);
        scanlines.material = scanlineMaterial;

        GameObject content = CreateUIObject("GameOverContent", canvasTransform);
        Stretch(content.GetComponent<RectTransform>());
        CanvasGroup contentCanvasGroup = content.AddComponent<CanvasGroup>();

        RectTransform titleGroup = CreateUIObject("TitleGroup", content.transform)
            .GetComponent<RectTransform>();
        SetRect(titleGroup, new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1550f, 310f));
        CreateTitleLayer("WhiteGlitch", titleGroup, "GAME OVER", englishFont, 190f,
            WarmWhite, new Vector2(-10f, 7f));
        CreateTitleLayer("MagentaGlitch", titleGroup, "GAME OVER", englishFont, 190f,
            Magenta, new Vector2(11f, -8f));
        CreateTitleLayer("Title", titleGroup, "GAME OVER", englishFont, 190f,
            ArcadeRed, Vector2.zero);
        TextMeshProUGUI returnPrompt = CreateText(
            "ReturnPrompt",
            titleGroup,
            "按任意键返回开始菜单",
            chineseFont,
            34f,
            WarmWhite,
            TextAlignmentOptions.Center,
            new Vector2(0f, -135f),
            new Vector2(800f, 54f));

        Button restartButton = CreateArcadeButton(
            "RestartButton",
            content.transform,
            "重新开始",
            chineseFont,
            ArcadeRed);
        RectTransform restartRect = restartButton.GetComponent<RectTransform>();
        SetRect(restartRect, new Vector2(0.5f, 0.5f), new Vector2(0f, -230f),
            new Vector2(470f, 92f));
        LayoutElement restartLayout = restartButton.GetComponent<LayoutElement>();
        if (restartLayout != null)
            UnityEngine.Object.DestroyImmediate(restartLayout);

        GameOverController controller = new GameObject("GameOverController")
            .AddComponent<GameOverController>();
        SerializedObject serializedController = new SerializedObject(controller);
        SetObjectReference(serializedController, "restartButton", restartButton);
        SetObjectReference(serializedController, "contentCanvasGroup", contentCanvasGroup);
        SetObjectReference(serializedController, "titleGroup", titleGroup);
        SetObjectReference(serializedController, "returnPrompt", returnPrompt);
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    public static void BuildGameClearOnActiveScene()
    {
        EnsureSharedAssets();
        ClearActiveScene();

        TMP_FontAsset englishFont = LoadFont(EnglishFontPath);
        TMP_FontAsset chineseFont = LoadFont(ChineseFontPath, englishFont);
        Material scanlineMaterial = AssetDatabase.LoadAssetAtPath<Material>(ScanlineMaterialPath);

        CreateCamera(new Color32(1, 10, 13, 255));
        CreateEventSystem();
        Canvas canvas = CreateCanvas();
        RectTransform canvasTransform = canvas.GetComponent<RectTransform>();

        CreateFullScreenImage("Background", canvasTransform, new Color32(1, 10, 13, 255), false);
        CreateArcadeBackgroundDecor(canvasTransform, Cyan, new Color32(255, 216, 67, 255));

        Image scanlines = CreateFullScreenImage(
            "RetroScanlines",
            canvasTransform,
            new Color(1f, 1f, 1f, 0.52f),
            false);
        scanlines.material = scanlineMaterial;

        GameObject content = CreateUIObject("GameClearContent", canvasTransform);
        Stretch(content.GetComponent<RectTransform>());
        CanvasGroup contentCanvasGroup = content.AddComponent<CanvasGroup>();

        RectTransform titleGroup = CreateUIObject("TitleGroup", content.transform)
            .GetComponent<RectTransform>();
        SetRect(titleGroup, new Vector2(0.5f, 0.5f), new Vector2(0f, 80f),
            new Vector2(1650f, 330f));
        CreateTitleLayer("GoldShadow", titleGroup, "GAME CLEAR!", englishFont, 182f,
            new Color32(255, 216, 67, 255), new Vector2(12f, -8f));
        CreateTitleLayer("CyanShadow", titleGroup, "GAME CLEAR!", englishFont, 182f,
            Cyan, new Vector2(-10f, 7f));
        CreateTitleLayer("Title", titleGroup, "GAME CLEAR!", englishFont, 182f,
            WarmWhite, Vector2.zero);
        TextMeshProUGUI returnPrompt = CreateText(
            "ReturnPrompt",
            titleGroup,
            "按任意键返回开始菜单",
            chineseFont,
            34f,
            new Color32(210, 255, 247, 255),
            TextAlignmentOptions.Center,
            new Vector2(0f, -145f),
            new Vector2(800f, 54f));

        GameClearController controller = new GameObject("GameClearController")
            .AddComponent<GameClearController>();
        SerializedObject serializedController = new SerializedObject(controller);
        SetObjectReference(serializedController, "contentCanvasGroup", contentCanvasGroup);
        SetObjectReference(serializedController, "titleGroup", titleGroup);
        SetObjectReference(serializedController, "returnPrompt", returnPrompt);
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    public static void EnsureSharedAssetsAndBuildSettings()
    {
        EnsureSharedAssets();
        UpdateBuildSettings();
        AssetDatabase.SaveAssets();
    }

    private static void EnsureSharedAssets()
    {
        EnsureFolder("Assets/Resources");
        EnsureFolder("Assets/Materials");

        GameFlowConfig config = AssetDatabase.LoadAssetAtPath<GameFlowConfig>(ConfigPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<GameFlowConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
        }

        SerializedObject serializedConfig = new SerializedObject(config);
        serializedConfig.FindProperty("startMenuSceneName").stringValue = "StartGame";
        serializedConfig.FindProperty("gameOverSceneName").stringValue = "GameOver";
        serializedConfig.FindProperty("gameClearSceneName").stringValue = "GameClear";
        if (string.IsNullOrWhiteSpace(
                serializedConfig.FindProperty("firstGameplaySceneName").stringValue))
        {
            serializedConfig.FindProperty("firstGameplaySceneName").stringValue = "NewLevel_test";
        }
        serializedConfig.FindProperty("gameOverDelay").floatValue = 1.2f;
        serializedConfig.FindProperty("gameClearDelay").floatValue = 0.8f;
        serializedConfig.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(config);

        Shader stripeShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/StripeOverlay.shader");
        Material scanlineMaterial = AssetDatabase.LoadAssetAtPath<Material>(ScanlineMaterialPath);
        if (scanlineMaterial == null && stripeShader != null)
        {
            scanlineMaterial = new Material(stripeShader) { name = "RetroScanlineMat" };
            AssetDatabase.CreateAsset(scanlineMaterial, ScanlineMaterialPath);
        }

        if (scanlineMaterial != null)
        {
            scanlineMaterial.SetFloat("_StripeAngle", 0f);
            scanlineMaterial.SetFloat("_StripeWidth", 2f);
            scanlineMaterial.SetFloat("_StripeGap", 5f);
            scanlineMaterial.SetFloat("_GradientPower", 10f);
            scanlineMaterial.SetColor("_StripeColor", new Color(1f, 1f, 1f, 0.055f));
            EditorUtility.SetDirty(scanlineMaterial);
        }
    }

    private static GameObject BuildSettingsPanel(
        RectTransform canvas,
        TMP_FontAsset englishFont,
        TMP_FontAsset chineseFont,
        out Slider volumeSlider,
        out TMP_Dropdown resolutionDropdown,
        out Toggle fullScreenToggle,
        out TMP_Dropdown qualityDropdown,
        out Button applyButton,
        out Button backButton)
    {
        GameObject root = CreateUIObject("SettingsPanel", canvas);
        Stretch(root.GetComponent<RectTransform>());
        CreateFullScreenImage("Dimmer", root.transform, new Color(0f, 0f, 0f, 0.82f), true);

        GameObject frame = CreateUIObject("Frame", root.transform);
        RectTransform frameRect = frame.GetComponent<RectTransform>();
        SetRect(frameRect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 700f));
        Image frameImage = frame.AddComponent<Image>();
        frameImage.color = PanelColor;

        GameObject cyanFrame = CreateUIObject("CyanBorder", frame.transform);
        RectTransform cyanFrameRect = cyanFrame.GetComponent<RectTransform>();
        SetRect(cyanFrameRect, new Vector2(0.5f, 0.5f), new Vector2(-8f, 8f),
            new Vector2(1012f, 712f));
        Image cyanFrameImage = cyanFrame.AddComponent<Image>();
        cyanFrameImage.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.72f);
        cyanFrame.transform.SetAsFirstSibling();

        CreateText("Title", frame.transform, "SYSTEM SETTINGS", englishFont, 54f,
            WarmWhite, TextAlignmentOptions.Center, new Vector2(0f, 285f),
            new Vector2(850f, 85f));

        DefaultControls.Resources uiResources = GetUIResources();
        TMP_DefaultControls.Resources tmpResources = GetTMPResources();

        CreateSettingsLabel(frame.transform, "总音量", chineseFont, 145f);
        GameObject sliderObject = DefaultControls.CreateSlider(uiResources);
        sliderObject.name = "MasterVolume";
        sliderObject.transform.SetParent(frame.transform, false);
        SetRect(sliderObject.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
            new Vector2(190f, 145f), new Vector2(470f, 46f));
        volumeSlider = sliderObject.GetComponent<Slider>();
        volumeSlider.minValue = 0f;
        volumeSlider.maxValue = 1f;
        volumeSlider.value = 1f;
        TintGraphics(sliderObject, Cyan);

        CreateSettingsLabel(frame.transform, "分辨率", chineseFont, 35f);
        GameObject resolutionObject = TMP_DefaultControls.CreateDropdown(tmpResources);
        resolutionObject.name = "ResolutionDropdown";
        resolutionObject.transform.SetParent(frame.transform, false);
        SetRect(resolutionObject.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
            new Vector2(190f, 35f), new Vector2(470f, 62f));
        resolutionDropdown = resolutionObject.GetComponent<TMP_Dropdown>();
        StyleTMPControl(resolutionObject, chineseFont);

        CreateSettingsLabel(frame.transform, "全屏", chineseFont, -75f);
        GameObject toggleObject = DefaultControls.CreateToggle(uiResources);
        toggleObject.name = "FullScreenToggle";
        toggleObject.transform.SetParent(frame.transform, false);
        SetRect(toggleObject.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
            new Vector2(5f, -75f), new Vector2(100f, 62f));
        fullScreenToggle = toggleObject.GetComponent<Toggle>();
        Text legacyLabel = toggleObject.GetComponentInChildren<Text>();
        if (legacyLabel != null)
            legacyLabel.gameObject.SetActive(false);
        TintGraphics(toggleObject, Magenta);

        CreateSettingsLabel(frame.transform, "画质", chineseFont, -185f);
        GameObject qualityObject = TMP_DefaultControls.CreateDropdown(tmpResources);
        qualityObject.name = "QualityDropdown";
        qualityObject.transform.SetParent(frame.transform, false);
        SetRect(qualityObject.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
            new Vector2(190f, -185f), new Vector2(470f, 62f));
        qualityDropdown = qualityObject.GetComponent<TMP_Dropdown>();
        StyleTMPControl(qualityObject, chineseFont);

        applyButton = CreateArcadeButton("ApplyButton", frame.transform, "应用", chineseFont, Cyan);
        SetRect(applyButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
            new Vector2(-180f, -285f), new Vector2(300f, 78f));
        RemoveLayoutElement(applyButton.gameObject);

        backButton = CreateArcadeButton("BackButton", frame.transform, "返回", chineseFont, Magenta);
        SetRect(backButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
            new Vector2(180f, -285f), new Vector2(300f, 78f));
        RemoveLayoutElement(backButton.gameObject);

        return root;
    }

    private static void CreateSettingsLabel(
        Transform parent,
        string label,
        TMP_FontAsset font,
        float y)
    {
        CreateText(label + "Label", parent, label, font, 38f, WarmWhite,
            TextAlignmentOptions.MidlineLeft, new Vector2(-315f, y),
            new Vector2(250f, 60f));
    }

    private static void CreateArcadeBackgroundDecor(
        RectTransform parent,
        Color primary,
        Color secondary)
    {
        CreateDecorBar("TopNeon", parent, new Vector2(0.5f, 1f), new Vector2(0f, -24f),
            new Vector2(1400f, 8f), primary);
        CreateDecorBar("BottomNeon", parent, new Vector2(0.5f, 0f), new Vector2(0f, 24f),
            new Vector2(1400f, 8f), secondary);
        CreateDecorBar("LeftAccent", parent, new Vector2(0f, 0.5f), new Vector2(28f, 0f),
            new Vector2(10f, 690f), primary);
        CreateDecorBar("RightAccent", parent, new Vector2(1f, 0.5f), new Vector2(-28f, 0f),
            new Vector2(10f, 690f), secondary);
    }

    private static void CreateDecorBar(
        string name,
        Transform parent,
        Vector2 anchor,
        Vector2 position,
        Vector2 size,
        Color color)
    {
        GameObject bar = CreateUIObject(name, parent);
        SetRect(bar.GetComponent<RectTransform>(), anchor, position, size);
        Image image = bar.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private static Button CreateArcadeButton(
        string name,
        Transform parent,
        string label,
        TMP_FontAsset font,
        Color accent)
    {
        GameObject buttonObject = TMP_DefaultControls.CreateButton(GetTMPResources());
        buttonObject.name = name;
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(470f, 88f);
        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color32(19, 27, 58, 255);

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color32(19, 27, 58, 255);
        colors.highlightedColor = new Color(accent.r * 0.45f, accent.g * 0.45f,
            accent.b * 0.45f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = accent;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        TextMeshProUGUI text = buttonObject.GetComponentInChildren<TextMeshProUGUI>();
        text.text = label;
        text.font = font;
        text.fontSize = 42f;
        text.fontStyle = FontStyles.Bold;
        text.color = WarmWhite;

        LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
        layout.preferredWidth = 470f;
        layout.preferredHeight = 88f;
        buttonObject.AddComponent<ArcadeButtonHover>();
        return button;
    }

    private static TextMeshProUGUI CreateTitleLayer(
        string name,
        Transform parent,
        string text,
        TMP_FontAsset font,
        float fontSize,
        Color color,
        Vector2 offset)
    {
        return CreateText(name, parent, text, font, fontSize, color,
            TextAlignmentOptions.Center, offset, new Vector2(1600f, 220f));
    }

    private static TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        string text,
        TMP_FontAsset font,
        float fontSize,
        Color color,
        TextAlignmentOptions alignment,
        Vector2 position,
        Vector2 size)
    {
        GameObject textObject = CreateUIObject(name, parent);
        SetRect(textObject.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), position, size);
        TextMeshProUGUI tmp = textObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Canvas CreateCanvas()
    {
        GameObject canvasObject = new GameObject(
            "Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    private static void CreateCamera(Color background)
    {
        GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = background;
        camera.orthographic = true;
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
    }

    private static void CreateEventSystem()
    {
        GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
        InputSystemUIInputModule inputModule =
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
        InputActionAsset actions =
            AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        if (actions == null)
            throw new InvalidOperationException("Assets/InputSystem_Actions.inputactions was not found.");

        inputModule.actionsAsset = actions;
        inputModule.point = CreateActionReference(actions, "UI/Point");
        inputModule.move = CreateActionReference(actions, "UI/Navigate");
        inputModule.leftClick = CreateActionReference(actions, "UI/Click");
        inputModule.rightClick = CreateActionReference(actions, "UI/RightClick");
        inputModule.middleClick = CreateActionReference(actions, "UI/MiddleClick");
        inputModule.scrollWheel = CreateActionReference(actions, "UI/ScrollWheel");
        inputModule.submit = CreateActionReference(actions, "UI/Submit");
        inputModule.cancel = CreateActionReference(actions, "UI/Cancel");
        inputModule.trackedDevicePosition =
            CreateActionReference(actions, "UI/TrackedDevicePosition");
        inputModule.trackedDeviceOrientation =
            CreateActionReference(actions, "UI/TrackedDeviceOrientation");
    }

    private static InputActionReference CreateActionReference(
        InputActionAsset actions,
        string actionPath)
    {
        return InputActionReference.Create(actions.FindAction(actionPath, true));
    }

    private static Image CreateFullScreenImage(
        string name,
        Transform parent,
        Color color,
        bool raycastTarget)
    {
        GameObject imageObject = CreateUIObject(name, parent);
        Stretch(imageObject.GetComponent<RectTransform>());
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycastTarget;
        return image;
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static void TintGraphics(GameObject root, Color accent)
    {
        Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
        foreach (Graphic graphic in graphics)
        {
            if (graphic is Text)
                continue;
            graphic.color = new Color(accent.r, accent.g, accent.b,
                Mathf.Max(0.75f, graphic.color.a));
        }
    }

    private static void StyleTMPControl(GameObject root, TMP_FontAsset font)
    {
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            text.font = font;
            text.fontSize = 28f;
            text.color = WarmWhite;
        }

        Image rootImage = root.GetComponent<Image>();
        if (rootImage != null)
            rootImage.color = new Color32(22, 31, 67, 255);
    }

    private static void RemoveLayoutElement(GameObject gameObject)
    {
        LayoutElement element = gameObject.GetComponent<LayoutElement>();
        if (element != null)
            UnityEngine.Object.DestroyImmediate(element);
    }

    private static void SetObjectReference(
        SerializedObject serializedObject,
        string propertyName,
        UnityEngine.Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
            throw new InvalidOperationException(
                $"Serialized field '{propertyName}' was not found on {serializedObject.targetObject}.");
        property.objectReferenceValue = value;
    }

    private static TMP_FontAsset LoadFont(string path, TMP_FontAsset fallback = null)
    {
        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) ?? fallback ?? TMP_Settings.defaultFontAsset;
    }

    private static TMP_DefaultControls.Resources GetTMPResources()
    {
        return new TMP_DefaultControls.Resources
        {
            standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            inputField =
                AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
            knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
            checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
            dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
            mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd")
        };
    }

    private static DefaultControls.Resources GetUIResources()
    {
        return new DefaultControls.Resources
        {
            standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            inputField =
                AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
            knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
            checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
            dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
            mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd")
        };
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        string folder = System.IO.Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folder);
    }

    private static void ClearActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
            UnityEngine.Object.DestroyImmediate(root);
    }

    private static void UpdateBuildSettings()
    {
        string[] required =
        {
            StartScenePath,
            "Assets/Scenes/NewLevel_test.unity",
            GameOverScenePath,
            GameClearScenePath
        };

        var result = new List<EditorBuildSettingsScene>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in required)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                continue;
            result.Add(new EditorBuildSettingsScene(path, true));
            seen.Add(path);
        }

        foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
        {
            if (existing.path.StartsWith(TestSceneRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            if (seen.Add(existing.path))
                result.Add(existing);
        }

        EditorBuildSettings.scenes = result.ToArray();
    }
}
