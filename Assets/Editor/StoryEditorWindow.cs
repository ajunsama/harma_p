using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// 剧情系统编辑器工具窗口
/// 提供：JSON导入/导出、模板管理、预览功能
/// </summary>
public class StoryEditorWindow : EditorWindow
{
    private Vector2 _scrollPos;
    private int _selectedTab;
    private readonly string[] _tabNames = { "JSON导入/导出", "样式模板", "头像模板", "图片模板", "预览" };

    // JSON导入
    private TextAsset _importJsonAsset;
    private string _jsonPreview = "";

    // 模板管理
    private StoryTemplateLibrary _templateLibrary;

    // 样式创建
    private string _newStyleId = "";
    private string _newStyleName = "";

    // 头像创建
    private string _newAvatarId = "";
    private string _newAvatarName = "";
    private Sprite _newAvatarSprite;

    // 图片创建
    private string _newImageId = "";
    private string _newImageName = "";
    private Sprite _newImageSprite;

    [MenuItem("工具/剧情系统编辑器")]
    public static void ShowWindow()
    {
        var window = GetWindow<StoryEditorWindow>("剧情系统编辑器");
        window.minSize = new Vector2(500, 400);
    }

    void OnGUI()
    {
        EditorGUILayout.Space(5);
        _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames);
        EditorGUILayout.Space(10);

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        switch (_selectedTab)
        {
            case 0: DrawJsonTab(); break;
            case 1: DrawStyleTab(); break;
            case 2: DrawAvatarTab(); break;
            case 3: DrawImageTab(); break;
            case 4: DrawPreviewTab(); break;
        }

        EditorGUILayout.EndScrollView();
    }

    // ==================== JSON导入/导出 ====================

    void DrawJsonTab()
    {
        EditorGUILayout.LabelField("JSON数据管理", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        // 导入
        EditorGUILayout.LabelField("导入JSON", EditorStyles.miniLabel);
        _importJsonAsset = (TextAsset)EditorGUILayout.ObjectField(
            "JSON文件", _importJsonAsset, typeof(TextAsset), false);

        if (_importJsonAsset != null)
        {
            if (GUILayout.Button("预览JSON内容"))
            {
                _jsonPreview = _importJsonAsset.text;
            }

            if (GUILayout.Button("验证JSON格式"))
            {
                ValidateJson(_importJsonAsset.text);
            }
        }

        if (!string.IsNullOrEmpty(_jsonPreview))
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("JSON预览：");
            EditorGUILayout.TextArea(_jsonPreview, GUILayout.MaxHeight(200));
        }

        EditorGUILayout.Space(15);

        // 导出模板
        EditorGUILayout.LabelField("导出JSON模板", EditorStyles.miniLabel);
        if (GUILayout.Button("生成空白JSON模板文件"))
        {
            GenerateJsonTemplate();
        }

        if (GUILayout.Button("从场景中StoryManager导出当前数据"))
        {
            ExportFromScene();
        }
    }

    void ValidateJson(string json)
    {
        try
        {
            var data = JsonUtility.FromJson<StoryDataCollection>(json);
            if (data?.stories != null)
            {
                int totalDialogues = 0;
                foreach (var story in data.stories)
                    totalDialogues += story.dialogues?.Count ?? 0;

                EditorUtility.DisplayDialog("验证成功",
                    $"JSON格式正确！\n" +
                    $"共 {data.stories.Count} 段剧情\n" +
                    $"共 {totalDialogues} 条对话",
                    "确定");
            }
            else
            {
                EditorUtility.DisplayDialog("验证警告",
                    "JSON格式正确，但没有找到剧情数据。\n请检查stories字段。",
                    "确定");
            }
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("验证失败",
                $"JSON格式错误：\n{e.Message}",
                "确定");
        }
    }

    void GenerateJsonTemplate()
    {
        string path = EditorUtility.SaveFilePanel(
            "保存JSON模板", Application.dataPath, "story_template", "json");

        if (string.IsNullOrEmpty(path)) return;

        var template = new StoryDataCollection();
        var story = new StorySequence
        {
            storyId = "story_001",
            chapterId = "chapter_1",
            sectionId = "section_1",
            dialogues = new System.Collections.Generic.List<StoryDialogue>
            {
                new StoryDialogue
                {
                    id = 1,
                    chapterId = "chapter_1",
                    sectionId = "section_1",
                    content = "在此填写对话内容...",
                    styleId = "protagonist",
                    playSpeed = 0.05f,
                    avatarPosition = "left",
                    avatarId = "hero_normal",
                    showImage = false,
                    imageId = "",
                    speakerName = "主人公"
                },
                new StoryDialogue
                {
                    id = 2,
                    chapterId = "chapter_1",
                    sectionId = "section_1",
                    content = "Boss的台词...",
                    styleId = "boss",
                    playSpeed = 0.03f,
                    avatarPosition = "right",
                    avatarId = "boss_normal",
                    showImage = true,
                    imageId = "bg_sunset",
                    speakerName = "BOSS"
                }
            }
        };
        template.stories.Add(story);

        string json = JsonUtility.ToJson(template, true);
        File.WriteAllText(path, json, System.Text.Encoding.UTF8);
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("成功", $"JSON模板已保存到:\n{path}", "确定");
    }

    void ExportFromScene()
    {
        var manager = FindFirstObjectByType<StoryManager>();
        if (manager == null || manager.storyJsonFile == null)
        {
            EditorUtility.DisplayDialog("导出失败",
                "场景中没有找到StoryManager或其JSON数据为空", "确定");
            return;
        }

        string path = EditorUtility.SaveFilePanel(
            "导出JSON", Application.dataPath, "story_export", "json");

        if (!string.IsNullOrEmpty(path))
        {
            File.WriteAllText(path, manager.storyJsonFile.text, System.Text.Encoding.UTF8);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("成功", "导出完成", "确定");
        }
    }

    // ==================== 样式模板管理 ====================

    void DrawStyleTab()
    {
        EditorGUILayout.LabelField("文字样式模板管理", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "样式模板用于定义不同角色的文字表现：字体大小、颜色、对话框样式等。\n" +
            "建议为主人公、Boss、小怪各创建一套。", MessageType.Info);

        EditorGUILayout.Space(5);
        _templateLibrary = (StoryTemplateLibrary)EditorGUILayout.ObjectField(
            "模板库", _templateLibrary, typeof(StoryTemplateLibrary), false);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("快速创建样式模板", EditorStyles.miniLabel);

        _newStyleId = EditorGUILayout.TextField("样式ID", _newStyleId);
        _newStyleName = EditorGUILayout.TextField("显示名称", _newStyleName);

        if (GUILayout.Button("创建新样式模板") && !string.IsNullOrEmpty(_newStyleId))
        {
            CreateStyleTemplate(_newStyleId, _newStyleName);
        }

        EditorGUILayout.Space(10);
        if (GUILayout.Button("批量创建默认样式（主人公/Boss/小怪）"))
        {
            CreateStyleTemplate("protagonist", "主人公样式");
            CreateStyleTemplate("boss", "Boss样式");
            CreateStyleTemplate("minion", "小怪样式");
            CreateStyleTemplate("narrator", "旁白样式");
        }
    }

    void CreateStyleTemplate(string styleId, string displayName)
    {
        string dir = "Assets/LevelData/StoryData/Styles";
        if (!AssetDatabase.IsValidFolder(dir))
        {
            CreateFolderRecursive(dir);
        }

        string path = $"{dir}/{styleId}_style.asset";
        if (AssetDatabase.LoadAssetAtPath<StoryStyleTemplate>(path) != null)
        {
            Debug.LogWarning($"样式 {styleId} 已存在: {path}");
            return;
        }

        var template = CreateInstance<StoryStyleTemplate>();
        template.styleId = styleId;
        template.displayName = displayName;

        // 设置不同角色的默认颜色
        switch (styleId)
        {
            case "protagonist":
                template.dialogueBoxColor = new Color(0f, 0.8f, 1f, 0.9f);
                template.textColor = Color.white;
                template.defaultSpeakerName = "主人公";
                break;
            case "boss":
                template.dialogueBoxColor = new Color(0.8f, 0.2f, 0.2f, 0.9f);
                template.textColor = Color.white;
                template.defaultSpeakerName = "BOSS";
                break;
            case "minion":
                template.dialogueBoxColor = new Color(0.5f, 0.5f, 0.5f, 0.9f);
                template.textColor = Color.white;
                template.defaultSpeakerName = "小怪";
                break;
            case "narrator":
                template.dialogueBoxColor = new Color(0f, 0f, 0f, 0.8f);
                template.textColor = new Color(0.9f, 0.9f, 0.9f);
                template.defaultSpeakerName = "";
                break;
        }

        AssetDatabase.CreateAsset(template, path);
        AssetDatabase.SaveAssets();

        // 自动添加到模板库
        if (_templateLibrary != null)
        {
            _templateLibrary.styleTemplates.Add(template);
            EditorUtility.SetDirty(_templateLibrary);
        }

        Debug.Log($"[StoryEditor] 创建样式模板: {path}");
    }

    // ==================== 头像模板管理 ====================

    void DrawAvatarTab()
    {
        EditorGUILayout.LabelField("头像模板管理", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "头像模板定义角色头像的代号与对应图片。\n" +
            "在JSON数据中通过avatarId引用。", MessageType.Info);

        EditorGUILayout.Space(5);
        _templateLibrary = (StoryTemplateLibrary)EditorGUILayout.ObjectField(
            "模板库", _templateLibrary, typeof(StoryTemplateLibrary), false);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("创建头像模板", EditorStyles.miniLabel);

        _newAvatarId = EditorGUILayout.TextField("头像代号", _newAvatarId);
        _newAvatarName = EditorGUILayout.TextField("显示名称", _newAvatarName);
        _newAvatarSprite = (Sprite)EditorGUILayout.ObjectField(
            "头像图片", _newAvatarSprite, typeof(Sprite), false);

        if (GUILayout.Button("创建头像模板") && !string.IsNullOrEmpty(_newAvatarId))
        {
            CreateAvatarTemplate(_newAvatarId, _newAvatarName, _newAvatarSprite);
        }
    }

    void CreateAvatarTemplate(string avatarId, string displayName, Sprite sprite)
    {
        string dir = "Assets/LevelData/StoryData/Avatars";
        if (!AssetDatabase.IsValidFolder(dir))
        {
            CreateFolderRecursive(dir);
        }

        string path = $"{dir}/{avatarId}_avatar.asset";
        if (AssetDatabase.LoadAssetAtPath<StoryAvatarTemplate>(path) != null)
        {
            Debug.LogWarning($"头像 {avatarId} 已存在: {path}");
            return;
        }

        var template = CreateInstance<StoryAvatarTemplate>();
        template.avatarId = avatarId;
        template.displayName = displayName;
        template.avatarSprite = sprite;

        AssetDatabase.CreateAsset(template, path);
        AssetDatabase.SaveAssets();

        if (_templateLibrary != null)
        {
            _templateLibrary.avatarTemplates.Add(template);
            EditorUtility.SetDirty(_templateLibrary);
        }

        Debug.Log($"[StoryEditor] 创建头像模板: {path}");
    }

    // ==================== 图片模板管理 ====================

    void DrawImageTab()
    {
        EditorGUILayout.LabelField("图片模板管理", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "图片模板定义展示图片的代号与对应图片资源。\n" +
            "在JSON数据中通过imageId引用。", MessageType.Info);

        EditorGUILayout.Space(5);
        _templateLibrary = (StoryTemplateLibrary)EditorGUILayout.ObjectField(
            "模板库", _templateLibrary, typeof(StoryTemplateLibrary), false);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("创建图片模板", EditorStyles.miniLabel);

        _newImageId = EditorGUILayout.TextField("图片代号", _newImageId);
        _newImageName = EditorGUILayout.TextField("显示名称", _newImageName);
        _newImageSprite = (Sprite)EditorGUILayout.ObjectField(
            "图片资源", _newImageSprite, typeof(Sprite), false);

        if (GUILayout.Button("创建图片模板") && !string.IsNullOrEmpty(_newImageId))
        {
            CreateImageTemplate(_newImageId, _newImageName, _newImageSprite);
        }
    }

    void CreateImageTemplate(string imageId, string displayName, Sprite sprite)
    {
        string dir = "Assets/LevelData/StoryData/Images";
        if (!AssetDatabase.IsValidFolder(dir))
        {
            CreateFolderRecursive(dir);
        }

        string path = $"{dir}/{imageId}_image.asset";
        if (AssetDatabase.LoadAssetAtPath<StoryImageTemplate>(path) != null)
        {
            Debug.LogWarning($"图片 {imageId} 已存在: {path}");
            return;
        }

        var template = CreateInstance<StoryImageTemplate>();
        template.imageId = imageId;
        template.displayName = displayName;
        template.imageSprite = sprite;

        AssetDatabase.CreateAsset(template, path);
        AssetDatabase.SaveAssets();

        if (_templateLibrary != null)
        {
            _templateLibrary.imageTemplates.Add(template);
            EditorUtility.SetDirty(_templateLibrary);
        }

        Debug.Log($"[StoryEditor] 创建图片模板: {path}");
    }

    // ==================== 预览 ====================

    void DrawPreviewTab()
    {
        EditorGUILayout.LabelField("剧情预览", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "拖入JSON文件可以预览剧情内容。\n" +
            "注意：预览仅供检查数据，完整效果请在运行时查看。", MessageType.Info);

        EditorGUILayout.Space(5);
        _importJsonAsset = (TextAsset)EditorGUILayout.ObjectField(
            "JSON文件", _importJsonAsset, typeof(TextAsset), false);

        if (_importJsonAsset == null) return;

        EditorGUILayout.Space(10);

        try
        {
            var data = JsonUtility.FromJson<StoryDataCollection>(_importJsonAsset.text);
            if (data?.stories == null) return;

            foreach (var story in data.stories)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"剧情: {story.storyId}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"章: {story.chapterId}  节: {story.sectionId}");

                if (story.dialogues != null)
                {
                    foreach (var d in story.dialogues)
                    {
                        EditorGUILayout.BeginVertical(EditorStyles.textArea);
                        EditorGUILayout.LabelField(
                            $"[{d.id}] 样式:{d.styleId} 头像:{d.avatarId}({d.avatarPosition})" +
                            $" 图片:{(d.showImage ? d.imageId : "无")}");
                        EditorGUILayout.LabelField(d.content, EditorStyles.wordWrappedLabel);
                        EditorGUILayout.EndVertical();
                    }
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }
        }
        catch (System.Exception e)
        {
            EditorGUILayout.HelpBox($"解析JSON失败: {e.Message}", MessageType.Error);
        }
    }

    // ==================== 工具方法 ====================

    /// <summary>
    /// 递归创建文件夹
    /// </summary>
    void CreateFolderRecursive(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0]; // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }
}
