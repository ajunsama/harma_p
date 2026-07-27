using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Editor focused on authoring story dialogue JSON used by StoryManager.
/// </summary>
public class StoryEditorWindow : EditorWindow
{
    private const string DefaultDirectory = "Assets/LevelData/StoryData";
    private const float DialogueCardHeight = 292f;
    private const float DialogueCardPadding = 12f;
    private const float DialogueHeaderHeight = 24f;
    private const float DialogueFieldGap = 4f;
    private const float PerformanceCueHeaderHeight = 30f;
    private const float PerformanceCueRowHeight = 70f;

    private readonly string[] _tabNames = { "剧情编辑", "角色模板", "样式模板", "图片模板", "JSON预览" };
    private int _selectedTab;
    private Vector2 _scrollPos;

    private TextAsset _storyJsonAsset;
    private StoryTemplateLibrary _templateLibrary;
    private StoryDataCollection _data = new StoryDataCollection();
    private int _selectedStoryIndex = -1;
    private int _selectedDialogueIndex = -1;
    private string _loadedAssetPath;
    private string _jsonPreview;
    private StorySequence _dialogueListStory;
    private ReorderableList _dialogueList;

    [MenuItem("Tools/剧情编辑器")]
    public static void ShowWindow()
    {
        var window = GetWindow<StoryEditorWindow>("剧情编辑器");
        window.minSize = new Vector2(760, 520);
    }

    public static void OpenStory(TextAsset storyJson, string storyId)
    {
        var window = GetWindow<StoryEditorWindow>("剧情编辑器");
        window.minSize = new Vector2(760, 520);
        if (storyJson != null)
        {
            window._storyJsonAsset = storyJson;
            window.LoadFromTextAsset(storyJson);
        }

        int storyIndex = window._data?.stories?.FindIndex(story =>
            story != null && story.storyId == storyId) ?? -1;
        if (storyIndex >= 0)
        {
            window._selectedTab = 0;
            window._selectedStoryIndex = storyIndex;
            var story = window._data.stories[storyIndex];
            window._selectedDialogueIndex = story.dialogues != null && story.dialogues.Count > 0 ? 0 : -1;
            window._dialogueListStory = null;
            window._dialogueList = null;
        }
        window.Show();
        window.Focus();
    }

    private void OnEnable()
    {
        if (_data == null)
            _data = new StoryDataCollection();
    }

    private void OnGUI()
    {
        DrawHeader();

        _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames);
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        switch (_selectedTab)
        {
            case 0: DrawStoryEditTab(); break;
            case 1: DrawAvatarTemplateTab(); break;
            case 2: DrawStyleTemplateTab(); break;
            case 3: DrawImageTemplateTab(); break;
            case 4: DrawJsonPreviewTab(); break;
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(
            "剧情编辑器负责台词及“哪句进入演出”；演出动作在独立演出编辑器中制作，具体角色在关卡编辑器中绑定。",
            EditorStyles.wordWrappedLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        _storyJsonAsset = (TextAsset)EditorGUILayout.ObjectField("剧情 JSON", _storyJsonAsset, typeof(TextAsset), false);
        if (EditorGUI.EndChangeCheck() && _storyJsonAsset != null)
            LoadFromTextAsset(_storyJsonAsset);

        if (GUILayout.Button("新建", GUILayout.Width(64)))
            NewCollection();
        if (GUILayout.Button("打开", GUILayout.Width(64)))
            OpenJson();
        if (GUILayout.Button("保存", GUILayout.Width(64)))
            SaveJson(false);
        if (GUILayout.Button("另存为", GUILayout.Width(72)))
            SaveJson(true);
        EditorGUILayout.EndHorizontal();

        _templateLibrary = (StoryTemplateLibrary)EditorGUILayout.ObjectField("模板库", _templateLibrary, typeof(StoryTemplateLibrary), false);
        EditorGUILayout.EndVertical();
    }

    private void DrawStoryEditTab()
    {
        if (_data.stories == null)
            _data.stories = new List<StorySequence>();

        EditorGUILayout.BeginHorizontal();
        DrawStoryList();
        DrawStoryDetail();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawStoryList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(230));
        EditorGUILayout.LabelField("剧情段落", EditorStyles.boldLabel);

        for (int i = 0; i < _data.stories.Count; i++)
        {
            var story = _data.stories[i];
            string label = string.IsNullOrEmpty(story.storyId) ? "(未命名)" : story.storyId;
            if (GUILayout.Toggle(_selectedStoryIndex == i, label, "Button"))
            {
                _selectedStoryIndex = i;
                _selectedDialogueIndex = story.dialogues != null && story.dialogues.Count > 0 ? 0 : -1;
            }
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("+ 新剧情"))
        {
            _data.stories.Add(CreateStory());
            _selectedStoryIndex = _data.stories.Count - 1;
            _selectedDialogueIndex = -1;
        }

        using (new EditorGUI.DisabledScope(_selectedStoryIndex < 0 || _selectedStoryIndex >= _data.stories.Count))
        {
            if (GUILayout.Button("复制剧情"))
            {
                var copy = JsonUtility.FromJson<StorySequence>(JsonUtility.ToJson(_data.stories[_selectedStoryIndex]));
                copy.storyId = GenerateStoryId();
                _data.stories.Insert(_selectedStoryIndex + 1, copy);
                _selectedStoryIndex++;
            }
            if (GUILayout.Button("删除剧情") && EditorUtility.DisplayDialog("删除剧情", "确定删除当前剧情段落？", "删除", "取消"))
            {
                _data.stories.RemoveAt(_selectedStoryIndex);
                _selectedStoryIndex = Mathf.Clamp(_selectedStoryIndex, -1, _data.stories.Count - 1);
                _selectedDialogueIndex = -1;
            }
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawStoryDetail()
    {
        EditorGUILayout.BeginVertical();
        if (_selectedStoryIndex < 0 || _selectedStoryIndex >= _data.stories.Count)
        {
            EditorGUILayout.HelpBox("选择或新建一段剧情后开始编辑。", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        var story = _data.stories[_selectedStoryIndex];
        if (story.dialogues == null)
            story.dialogues = new List<StoryDialogue>();
        if (story.performanceCues == null)
            story.performanceCues = new List<StoryPerformanceCueDefinition>();

        EditorGUILayout.LabelField("段落信息", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        story.storyId = EditorGUILayout.TextField("剧情 ID", story.storyId);
        if (GUILayout.Button("生成 ID", GUILayout.Width(80)))
            story.storyId = GenerateStoryId();
        EditorGUILayout.EndHorizontal();
        story.chapterId = EditorGUILayout.TextField("章节 ID", story.chapterId);
        story.sectionId = EditorGUILayout.TextField("小节 ID", story.sectionId);
        story.maskBackground = EditorGUILayout.Toggle(
            "对游戏背景应用遮罩/模糊", story.maskBackground);

        EditorGUILayout.Space(8);
        DrawDialogueCards(story);
        EditorGUILayout.EndVertical();
    }

    private void DrawDialogueCards(StorySequence story)
    {
        EnsureDialogueList(story);
        _dialogueList.DoLayoutList();

        EditorGUILayout.Space(10);
        if (GUILayout.Button("+ 新增对话", GUILayout.Height(40)))
        {
            Undo.RecordObject(this, "Add Story Dialogue");
            story.dialogues.Add(CreateDialogue(story, GetNextDialogueId(story)));
            _dialogueList.index = story.dialogues.Count - 1;
            _selectedDialogueIndex = _dialogueList.index;
        }
    }

    private void EnsureDialogueList(StorySequence story)
    {
        if (_dialogueListStory == story && _dialogueList != null && _dialogueList.list == story.dialogues)
            return;

        _dialogueListStory = story;
        _dialogueList = new ReorderableList(story.dialogues, typeof(StoryDialogue), true, false, false, false)
        {
            footerHeight = 0f,
            showDefaultBackground = false
        };

        _dialogueList.elementHeightCallback = index =>
        {
            if (index < 0 || index >= story.dialogues.Count) return DialogueCardHeight;
            int cueCount = story.performanceCues?.Count(cue =>
                cue != null && cue.dialogueId == story.dialogues[index].id) ?? 0;
            return DialogueCardHeight + PerformanceCueHeaderHeight +
                   cueCount * PerformanceCueRowHeight;
        };

        _dialogueList.drawElementCallback = (rect, index, active, focused) =>
        {
            if (index < 0 || index >= story.dialogues.Count) return;
            DrawDialogueCard(rect, story, index);
        };

        _dialogueList.onReorderCallback = _ => GUI.changed = true;
    }

    private void DrawDialogueCard(Rect rect, StorySequence story, int index)
    {
        var dialogue = story.dialogues[index];
        rect.y += 4f;
        rect.height -= 8f;

        GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

        var headerRect = new Rect(rect.x + DialogueCardPadding, rect.y + 8f, rect.width - DialogueCardPadding * 2f, DialogueHeaderHeight);
        EditorGUI.LabelField(new Rect(headerRect.x, headerRect.y, 170f, headerRect.height), $"对话 #{index + 1}", EditorStyles.boldLabel);

        var addRect = new Rect(headerRect.xMax - 112f, headerRect.y, 52f, headerRect.height);
        var deleteRect = new Rect(headerRect.xMax - 56f, headerRect.y, 52f, headerRect.height);
        if (GUI.Button(addRect, "添加"))
        {
            story.dialogues.Insert(index + 1, CreateDialogue(story, GetNextDialogueId(story)));
            _dialogueList.index = index + 1;
            _selectedDialogueIndex = _dialogueList.index;
            GUI.changed = true;
        }

        if (GUI.Button(deleteRect, "删除"))
        {
            story.performanceCues?.RemoveAll(cue => cue != null && cue.dialogueId == dialogue.id);
            story.dialogues.RemoveAt(index);
            _dialogueList.index = Mathf.Clamp(index - 1, -1, story.dialogues.Count - 1);
            _selectedDialogueIndex = _dialogueList.index;
            GUI.changed = true;
            return;
        }

        var bodyRect = new Rect(rect.x + DialogueCardPadding, rect.y + 40f, rect.width - DialogueCardPadding * 2f, rect.height - 52f);
        float gap = 12f;
        float leftWidth = Mathf.Clamp(bodyRect.width * 0.42f, 260f, 340f);
        if (bodyRect.width - leftWidth - gap < 260f)
            leftWidth = Mathf.Max(220f, bodyRect.width - gap - 260f);
        var leftRect = new Rect(bodyRect.x, bodyRect.y, leftWidth, bodyRect.height);
        var rightRect = new Rect(leftRect.xMax + gap, bodyRect.y, bodyRect.width - leftWidth - gap, bodyRect.height);

        DrawDialogueMetaFields(leftRect, story, dialogue);
        DrawDialogueContentFields(rightRect, dialogue);
        DrawDialoguePerformanceCues(rect, story, dialogue);
    }

    private void DrawDialoguePerformanceCues(Rect cardRect, StorySequence story, StoryDialogue dialogue)
    {
        if (story.performanceCues == null)
            story.performanceCues = new List<StoryPerformanceCueDefinition>();

        var cues = story.performanceCues
            .Where(cue => cue != null && cue.dialogueId == dialogue.id)
            .ToList();
        float x = cardRect.x + DialogueCardPadding;
        float width = cardRect.width - DialogueCardPadding * 2f;
        float y = cardRect.y + DialogueCardHeight - 6f;

        EditorGUI.LabelField(new Rect(x, y, width - 120f, 22f),
            cues.Count == 0 ? "演出 Cue：无" : $"演出 Cue：{cues.Count} 个",
            EditorStyles.miniBoldLabel);
        if (GUI.Button(new Rect(x + width - 116f, y, 116f, 22f), "+ 添加演出 Cue"))
        {
            story.performanceCues.Add(new StoryPerformanceCueDefinition
            {
                dialogueId = dialogue.id
            });
            GUI.changed = true;
            return;
        }
        y += PerformanceCueHeaderHeight;

        foreach (var cue in cues)
        {
            var row = new Rect(x, y, width, PerformanceCueRowHeight - 4f);
            GUI.Box(row, GUIContent.none, EditorStyles.helpBox);

            var script = FindPerformanceScript(cue.scriptId);
            var scriptRect = new Rect(row.x + 6f, row.y + 4f, row.width - 174f, 18f);
            var selected = (PerformanceScript)EditorGUI.ObjectField(
                scriptRect, "演出脚本", script, typeof(PerformanceScript), false);
            if (selected != script)
                cue.scriptId = selected != null ? selected.scriptId : "";

            if (GUI.Button(new Rect(row.xMax - 162f, row.y + 4f, 50f, 18f), "新建"))
            {
                selected = PerformanceScriptEditorWindow.CreateAsset();
                if (selected != null)
                {
                    cue.scriptId = selected.scriptId;
                    PerformanceScriptEditorWindow.Open(selected);
                }
                GUI.changed = true;
            }
            using (new EditorGUI.DisabledScope(selected == null))
            {
                if (GUI.Button(new Rect(row.xMax - 108f, row.y + 4f, 50f, 18f), "编辑"))
                    PerformanceScriptEditorWindow.Open(selected);
            }
            if (GUI.Button(new Rect(row.xMax - 54f, row.y + 4f, 48f, 18f), "删除"))
            {
                story.performanceCues.Remove(cue);
                GUI.changed = true;
                return;
            }

            cue.delay = Mathf.Max(0f, EditorGUI.FloatField(
                new Rect(row.x + 6f, row.y + 26f, Mathf.Min(210f, row.width * 0.48f), 18f),
                "触发延迟（未缩放秒）", cue.delay));
            cue.blockDialogueAdvance = EditorGUI.ToggleLeft(
                new Rect(row.x + Mathf.Min(222f, row.width * 0.5f), row.y + 26f,
                    row.width - Mathf.Min(228f, row.width * 0.5f), 18f),
                "演出完成前禁止下一句", cue.blockDialogueAdvance);
            cue.triggerTiming = (StoryPerformanceCueTriggerTiming)EditorGUI.Popup(
                new Rect(row.x + 6f, row.y + 48f, row.width - 12f, 18f),
                "触发时机", (int)cue.triggerTiming,
                new[] { "台词开始显示时", "玩家点击进入下一句后" });

            if (!string.IsNullOrEmpty(cue.scriptId) && selected == null)
                EditorGUI.LabelField(new Rect(row.x + 76f, row.y + 4f, row.width - 250f, 18f),
                    $"找不到脚本 ID：{cue.scriptId}", EditorStyles.miniLabel);
            y += PerformanceCueRowHeight;
        }
    }

    private void DrawDialogueMetaFields(Rect rect, StorySequence story, StoryDialogue dialogue)
    {
        float y = rect.y;
        float line = EditorGUIUtility.singleLineHeight;
        float gap = DialogueFieldGap;

        dialogue.id = EditorGUI.IntField(new Rect(rect.x, y, rect.width, line), "序号", dialogue.id);
        y += line + gap;
        dialogue.chapterId = EditorGUI.TextField(new Rect(rect.x, y, rect.width, line), "章节 ID", string.IsNullOrEmpty(dialogue.chapterId) ? story.chapterId : dialogue.chapterId);
        y += line + gap;
        dialogue.sectionId = EditorGUI.TextField(new Rect(rect.x, y, rect.width, line), "小节 ID", string.IsNullOrEmpty(dialogue.sectionId) ? story.sectionId : dialogue.sectionId);
        y += line + gap;
        DrawSpeakerPresetPopup(new Rect(rect.x, y, rect.width, line), dialogue);
        y += line + gap;
        dialogue.playSpeed = EditorGUI.FloatField(new Rect(rect.x, y, rect.width, line), "播放速度", dialogue.playSpeed);
        y += line + gap;
        dialogue.styleId = DrawIdPopup(new Rect(rect.x, y, rect.width, line), "样式 ID", dialogue.styleId, GetStyleIds());
        y += line + gap;
        dialogue.avatarId = DrawIdPopup(new Rect(rect.x, y, rect.width, line), "头像 ID", dialogue.avatarId, GetAvatarIds());
        y += line + gap;
        dialogue.avatarPosition = DrawIdPopup(new Rect(rect.x, y, rect.width, line), "头像位置", dialogue.avatarPosition, new List<string> { "left", "center", "right" });
        y += line + gap;
        dialogue.showImage = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, line), "显示图片", dialogue.showImage);
        y += line + gap;
        using (new EditorGUI.DisabledScope(!dialogue.showImage))
            dialogue.imageId = DrawIdPopup(new Rect(rect.x, y, rect.width, line), "图片 ID", dialogue.imageId, GetImageIds());
    }

    private void DrawDialogueContentFields(Rect rect, StoryDialogue dialogue)
    {
        float line = EditorGUIUtility.singleLineHeight;
        dialogue.speakerName = EditorGUI.TextField(new Rect(rect.x, rect.y, rect.width, line), "说话人", dialogue.speakerName);

        var contentLabelRect = new Rect(rect.x, rect.y + line + 8f, rect.width, line);
        EditorGUI.LabelField(contentLabelRect, "说话内容");
        var contentRect = new Rect(rect.x, contentLabelRect.yMax + 2f, rect.width, 96f);
        dialogue.content = EditorGUI.TextArea(contentRect, dialogue.content);

        var extraRect = new Rect(rect.x, contentRect.yMax + 8f, rect.width, line);
        dialogue.extraJson = EditorGUI.TextField(extraRect, "扩展 JSON", dialogue.extraJson);
    }

    private void DrawDialogueList(StorySequence story)
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(250));
        EditorGUILayout.LabelField("对话", EditorStyles.boldLabel);

        for (int i = 0; i < story.dialogues.Count; i++)
        {
            var d = story.dialogues[i];
            string speaker = string.IsNullOrEmpty(d.speakerName) ? d.styleId : d.speakerName;
            string label = $"{i + 1}. {speaker}  {Trim(d.content, 18)}";
            if (GUILayout.Toggle(_selectedDialogueIndex == i, label, "Button"))
                _selectedDialogueIndex = i;
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("+ 新对话"))
        {
            story.dialogues.Add(CreateDialogue(story, GetNextDialogueId(story)));
            _selectedDialogueIndex = story.dialogues.Count - 1;
        }

        using (new EditorGUI.DisabledScope(_selectedDialogueIndex < 0 || _selectedDialogueIndex >= story.dialogues.Count))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("上移") && _selectedDialogueIndex > 0)
            {
                Swap(story.dialogues, _selectedDialogueIndex, _selectedDialogueIndex - 1);
                _selectedDialogueIndex--;
            }
            if (GUILayout.Button("下移") && _selectedDialogueIndex < story.dialogues.Count - 1)
            {
                Swap(story.dialogues, _selectedDialogueIndex, _selectedDialogueIndex + 1);
                _selectedDialogueIndex++;
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("删除对话"))
            {
                story.dialogues.RemoveAt(_selectedDialogueIndex);
                _selectedDialogueIndex = Mathf.Clamp(_selectedDialogueIndex, -1, story.dialogues.Count - 1);
            }
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawDialogueDetail(StorySequence story)
    {
        EditorGUILayout.BeginVertical();
        if (_selectedDialogueIndex < 0 || _selectedDialogueIndex >= story.dialogues.Count)
        {
            EditorGUILayout.HelpBox("选择或新建一条对话。", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        var d = story.dialogues[_selectedDialogueIndex];
        DrawSpeakerPresetPicker(d);

        d.id = EditorGUILayout.IntField("序号", d.id);
        d.chapterId = EditorGUILayout.TextField("章节 ID", string.IsNullOrEmpty(d.chapterId) ? story.chapterId : d.chapterId);
        d.sectionId = EditorGUILayout.TextField("小节 ID", string.IsNullOrEmpty(d.sectionId) ? story.sectionId : d.sectionId);
        d.speakerName = EditorGUILayout.TextField("说话人", d.speakerName);
        d.content = EditorGUILayout.TextArea(d.content, GUILayout.MinHeight(72));
        d.playSpeed = EditorGUILayout.FloatField("播放速度", d.playSpeed);

        d.styleId = DrawIdPopup("样式 ID", d.styleId, GetStyleIds());
        d.avatarPosition = DrawIdPopup("头像位置", d.avatarPosition, new List<string> { "left", "center", "right" });
        d.avatarId = DrawIdPopup("头像 ID", d.avatarId, GetAvatarIds());
        d.showImage = EditorGUILayout.Toggle("显示图片", d.showImage);
        using (new EditorGUI.DisabledScope(!d.showImage))
            d.imageId = DrawIdPopup("图片 ID", d.imageId, GetImageIds());

        d.extraJson = EditorGUILayout.TextField("扩展 JSON", d.extraJson);
        EditorGUILayout.EndVertical();
    }

    private void DrawAvatarTemplateTab()
    {
        DrawSpeakerPresetEditor();

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("现有角色模板", EditorStyles.boldLabel);
        if (_templateLibrary == null)
        {
            EditorGUILayout.HelpBox("拖入 StoryTemplateLibrary 后可以查看角色头像模板。模板本身仍在 Inspector 中调整。", MessageType.Info);
            return;
        }

        foreach (var avatar in _templateLibrary.avatarTemplates.Where(a => a != null))
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label(AssetPreview.GetAssetPreview(avatar.avatarSprite) ?? Texture2D.grayTexture, GUILayout.Width(64), GUILayout.Height(64));
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(avatar.displayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("ID", avatar.avatarId);
            EditorGUILayout.LabelField("缩放", avatar.scale.ToString("0.###"));
            if (GUILayout.Button("选中模板", GUILayout.Width(90)))
                Selection.activeObject = avatar;
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawSpeakerPresetPicker(StoryDialogue dialogue)
    {
        var presets = GetSpeakerPresets();
        if (presets.Count == 0)
        {
            EditorGUILayout.HelpBox("可在“角色模板”页添加说话人组合，之后这里就能一键套用名称、样式和头像。", MessageType.Info);
            return;
        }

        var labels = new List<string> { "手动设置" };
        labels.AddRange(presets.Select(GetSpeakerPresetLabel));

        int currentIndex = 0;
        for (int i = 0; i < presets.Count; i++)
        {
            if (IsPresetMatch(dialogue, presets[i]))
            {
                currentIndex = i + 1;
                break;
            }
        }

        EditorGUI.BeginChangeCheck();
        int selectedIndex = EditorGUILayout.Popup("说话人组合", currentIndex, labels.ToArray());
        if (EditorGUI.EndChangeCheck() && selectedIndex > 0)
            ApplySpeakerPreset(dialogue, presets[selectedIndex - 1]);
    }

    private void DrawSpeakerPresetPopup(Rect rect, StoryDialogue dialogue)
    {
        var presets = GetSpeakerPresets();
        if (presets.Count == 0)
        {
            EditorGUI.LabelField(rect, "说话人组合", "未配置");
            return;
        }

        var labels = new List<string> { "手动设置" };
        labels.AddRange(presets.Select(GetSpeakerPresetLabel));

        int currentIndex = 0;
        for (int i = 0; i < presets.Count; i++)
        {
            if (IsPresetMatch(dialogue, presets[i]))
            {
                currentIndex = i + 1;
                break;
            }
        }

        EditorGUI.BeginChangeCheck();
        int selectedIndex = EditorGUI.Popup(rect, "说话人组合", currentIndex, labels.ToArray());
        if (EditorGUI.EndChangeCheck() && selectedIndex > 0)
            ApplySpeakerPreset(dialogue, presets[selectedIndex - 1]);
    }

    private void DrawSpeakerPresetEditor()
    {
        EditorGUILayout.LabelField("说话人组合", EditorStyles.boldLabel);
        if (_templateLibrary == null)
        {
            EditorGUILayout.HelpBox("拖入 StoryTemplateLibrary 后可以配置说话人组合。", MessageType.Info);
            return;
        }

        if (_templateLibrary.speakerPresets == null)
            _templateLibrary.speakerPresets = new List<StorySpeakerPreset>();

        EditorGUILayout.HelpBox("组合用于把说话人名称、样式 ID、头像 ID 和头像位置绑定在一起。编辑剧情时选择组合即可自动填充。", MessageType.Info);

        for (int i = 0; i < _templateLibrary.speakerPresets.Count; i++)
        {
            var preset = _templateLibrary.speakerPresets[i];
            if (preset == null)
            {
                preset = new StorySpeakerPreset();
                _templateLibrary.speakerPresets[i] = preset;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            preset.displayName = EditorGUILayout.TextField("显示名", preset.displayName);
            if (GUILayout.Button("删除", GUILayout.Width(52)))
            {
                Undo.RecordObject(_templateLibrary, "Remove Speaker Preset");
                _templateLibrary.speakerPresets.RemoveAt(i);
                EditorUtility.SetDirty(_templateLibrary);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();

            preset.presetId = EditorGUILayout.TextField("组合 ID", preset.presetId);
            preset.speakerName = EditorGUILayout.TextField("说话人", preset.speakerName);
            preset.styleId = DrawIdPopup("样式 ID", preset.styleId, GetStyleIds());
            preset.avatarId = DrawIdPopup("头像 ID", preset.avatarId, GetAvatarIds());
            preset.avatarPosition = DrawIdPopup("头像位置", preset.avatarPosition, new List<string> { "left", "center", "right" });
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ 新组合"))
        {
            Undo.RecordObject(_templateLibrary, "Add Speaker Preset");
            _templateLibrary.speakerPresets.Add(new StorySpeakerPreset
            {
                presetId = GenerateSpeakerPresetId(),
                displayName = "新说话人",
                speakerName = "新说话人",
                styleId = GetStyleIds().FirstOrDefault() ?? "",
                avatarId = GetAvatarIds().FirstOrDefault() ?? "",
                avatarPosition = "left"
            });
            EditorUtility.SetDirty(_templateLibrary);
        }

        using (new EditorGUI.DisabledScope(!TryGetCurrentDialogue(out var currentDialogue)))
        {
            if (GUILayout.Button("从当前对话创建组合"))
            {
                Undo.RecordObject(_templateLibrary, "Add Speaker Preset From Dialogue");
                _templateLibrary.speakerPresets.Add(new StorySpeakerPreset
                {
                    presetId = GenerateSpeakerPresetId(),
                    displayName = string.IsNullOrEmpty(currentDialogue.speakerName) ? "新说话人" : currentDialogue.speakerName,
                    speakerName = currentDialogue.speakerName,
                    styleId = currentDialogue.styleId,
                    avatarId = currentDialogue.avatarId,
                    avatarPosition = currentDialogue.avatarPosition
                });
                EditorUtility.SetDirty(_templateLibrary);
            }
        }
        EditorGUILayout.EndHorizontal();

        if (GUI.changed)
            EditorUtility.SetDirty(_templateLibrary);
    }

    private void DrawStyleTemplateTab()
    {
        DrawTemplateList("样式模板", _templateLibrary?.styleTemplates?.Cast<UnityEngine.Object>());
    }

    private void DrawImageTemplateTab()
    {
        DrawTemplateList("图片模板", _templateLibrary?.imageTemplates?.Cast<UnityEngine.Object>());
    }

    private void DrawTemplateList(string title, IEnumerable<UnityEngine.Object> templates)
    {
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        if (_templateLibrary == null || templates == null)
        {
            EditorGUILayout.HelpBox("拖入 StoryTemplateLibrary 后可以查看模板；具体参数在 Inspector 中调整。", MessageType.Info);
            return;
        }

        foreach (var template in templates.Where(t => t != null))
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.ObjectField(template, template.GetType(), false);
            if (GUILayout.Button("选中", GUILayout.Width(60)))
                Selection.activeObject = template;
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawJsonPreviewTab()
    {
        if (GUILayout.Button("刷新预览"))
            _jsonPreview = JsonUtility.ToJson(_data, true);

        if (string.IsNullOrEmpty(_jsonPreview))
            _jsonPreview = JsonUtility.ToJson(_data, true);

        EditorGUILayout.TextArea(_jsonPreview, GUILayout.MinHeight(360));
    }

    private void LoadFromTextAsset(TextAsset asset)
    {
        try
        {
            _data = JsonUtility.FromJson<StoryDataCollection>(asset.text) ?? new StoryDataCollection();
            if (_data.stories == null)
                _data.stories = new List<StorySequence>();
            _loadedAssetPath = AssetDatabase.GetAssetPath(asset);
            _selectedStoryIndex = _data.stories.Count > 0 ? 0 : -1;
            _selectedDialogueIndex = _selectedStoryIndex >= 0 && _data.stories[0].dialogues.Count > 0 ? 0 : -1;
            _jsonPreview = null;
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("加载失败", e.Message, "确定");
        }
    }

    private void NewCollection()
    {
        _storyJsonAsset = null;
        _loadedAssetPath = null;
        _data = new StoryDataCollection();
        _data.stories.Add(CreateStory());
        _selectedStoryIndex = 0;
        _selectedDialogueIndex = -1;
    }

    private void OpenJson()
    {
        string path = EditorUtility.OpenFilePanel("打开剧情 JSON", DefaultDirectory, "json");
        if (string.IsNullOrEmpty(path)) return;
        if (path.StartsWith(Application.dataPath))
            path = "Assets" + path.Substring(Application.dataPath.Length);

        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        if (asset == null)
        {
            EditorUtility.DisplayDialog("打开失败", "请选择 Assets 目录内的 JSON 文件。", "确定");
            return;
        }

        _storyJsonAsset = asset;
        LoadFromTextAsset(asset);
    }

    private void SaveJson(bool saveAs)
    {
        if (!ValidateDialogueIds())
            return;

        string path = _loadedAssetPath;
        if (saveAs || string.IsNullOrEmpty(path))
        {
            path = EditorUtility.SaveFilePanelInProject("保存剧情 JSON", "story_collection", "json", "选择保存位置", DefaultDirectory);
            if (string.IsNullOrEmpty(path)) return;
        }

        File.WriteAllText(path, JsonUtility.ToJson(_data, true), System.Text.Encoding.UTF8);
        AssetDatabase.Refresh();
        _loadedAssetPath = path;
        _storyJsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        _jsonPreview = null;
    }

    private StorySequence CreateStory()
    {
        return new StorySequence
        {
            storyId = GenerateStoryId(),
            chapterId = "chapter_1",
            sectionId = "section_1",
            dialogues = new List<StoryDialogue>(),
            performanceCues = new List<StoryPerformanceCueDefinition>()
        };
    }

    private StoryDialogue CreateDialogue(StorySequence story, int id)
    {
        var dialogue = new StoryDialogue
        {
            id = id,
            chapterId = story.chapterId,
            sectionId = story.sectionId,
            content = "",
            styleId = GetStyleIds().FirstOrDefault() ?? "",
            playSpeed = 0.05f,
            avatarPosition = "left",
            avatarId = GetAvatarIds().FirstOrDefault() ?? ""
        };

        var firstPreset = GetSpeakerPresets().FirstOrDefault();
        if (firstPreset != null)
            ApplySpeakerPreset(dialogue, firstPreset);

        return dialogue;
    }

    private string GenerateStoryId()
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string baseId = $"story_{stamp}";
        var used = new HashSet<string>(_data.stories?.Select(s => s.storyId) ?? Enumerable.Empty<string>());
        if (!used.Contains(baseId)) return baseId;

        int index = 2;
        while (used.Contains($"{baseId}_{index}"))
            index++;
        return $"{baseId}_{index}";
    }

    private string DrawIdPopup(string label, string current, List<string> ids)
    {
        ids = ids.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        if (!ids.Contains(current))
            ids.Insert(0, string.IsNullOrEmpty(current) ? "" : current);

        int index = Mathf.Max(0, ids.IndexOf(current));
        index = EditorGUILayout.Popup(label, index, ids.ToArray());
        return ids.Count > 0 ? ids[index] : current;
    }

    private string DrawIdPopup(Rect rect, string label, string current, List<string> ids)
    {
        ids = ids.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        if (!ids.Contains(current))
            ids.Insert(0, string.IsNullOrEmpty(current) ? "" : current);

        int index = Mathf.Max(0, ids.IndexOf(current));
        index = EditorGUI.Popup(rect, label, index, ids.ToArray());
        return ids.Count > 0 ? ids[index] : current;
    }

    private List<string> GetStyleIds()
    {
        return _templateLibrary?.styleTemplates?.Where(t => t != null).Select(t => t.styleId).ToList() ?? new List<string>();
    }

    private List<string> GetAvatarIds()
    {
        return _templateLibrary?.avatarTemplates?.Where(t => t != null).Select(t => t.avatarId).ToList() ?? new List<string>();
    }

    private List<string> GetImageIds()
    {
        return _templateLibrary?.imageTemplates?.Where(t => t != null).Select(t => t.imageId).ToList() ?? new List<string>();
    }

    private List<StorySpeakerPreset> GetSpeakerPresets()
    {
        return _templateLibrary?.speakerPresets?.Where(p => p != null).ToList() ?? new List<StorySpeakerPreset>();
    }

    private static void ApplySpeakerPreset(StoryDialogue dialogue, StorySpeakerPreset preset)
    {
        if (dialogue == null || preset == null) return;

        dialogue.speakerName = preset.speakerName;
        dialogue.styleId = preset.styleId;
        dialogue.avatarId = preset.avatarId;
        dialogue.avatarPosition = string.IsNullOrEmpty(preset.avatarPosition) ? "left" : preset.avatarPosition;
    }

    private static bool IsPresetMatch(StoryDialogue dialogue, StorySpeakerPreset preset)
    {
        if (dialogue == null || preset == null) return false;

        return dialogue.speakerName == preset.speakerName &&
               dialogue.styleId == preset.styleId &&
               dialogue.avatarId == preset.avatarId &&
               (dialogue.avatarPosition ?? "left") == (preset.avatarPosition ?? "left");
    }

    private static string GetSpeakerPresetLabel(StorySpeakerPreset preset)
    {
        if (preset == null) return "";
        if (!string.IsNullOrEmpty(preset.displayName)) return preset.displayName;
        if (!string.IsNullOrEmpty(preset.speakerName)) return preset.speakerName;
        return string.IsNullOrEmpty(preset.presetId) ? "(未命名组合)" : preset.presetId;
    }

    private string GenerateSpeakerPresetId()
    {
        var used = new HashSet<string>(GetSpeakerPresets().Select(p => p.presetId));
        int index = used.Count + 1;
        string id;
        do
        {
            id = $"speaker_{index}";
            index++;
        }
        while (used.Contains(id));

        return id;
    }

    private bool TryGetCurrentDialogue(out StoryDialogue dialogue)
    {
        dialogue = null;
        if (_selectedStoryIndex < 0 || _selectedStoryIndex >= (_data.stories?.Count ?? 0)) return false;

        var story = _data.stories[_selectedStoryIndex];
        int index = _dialogueListStory == story && _dialogueList != null ? _dialogueList.index : _selectedDialogueIndex;
        if (index < 0 || index >= (story.dialogues?.Count ?? 0)) return false;

        dialogue = story.dialogues[index];
        return dialogue != null;
    }

    private static void Swap<T>(IList<T> list, int a, int b)
    {
        (list[a], list[b]) = (list[b], list[a]);
    }

    private static int GetNextDialogueId(StorySequence story)
    {
        if (story?.dialogues == null || story.dialogues.Count == 0) return 1;
        return story.dialogues.Where(dialogue => dialogue != null).Select(dialogue => dialogue.id)
            .DefaultIfEmpty(0).Max() + 1;
    }

    private bool ValidateDialogueIds()
    {
        foreach (var story in _data?.stories ?? new List<StorySequence>())
        {
            if (story?.dialogues == null) continue;
            var ids = new HashSet<int>();
            foreach (var dialogue in story.dialogues)
            {
                if (dialogue == null || dialogue.id <= 0 || !ids.Add(dialogue.id))
                {
                    EditorUtility.DisplayDialog("无法保存",
                        $"剧情 '{story.storyId}' 中存在空对话、非正数或重复的对话 ID。对话 ID 是演出 Cue 的稳定引用，不能自动重新编号。",
                        "确定");
                    return false;
                }
            }

            foreach (var cue in story.performanceCues ?? new List<StoryPerformanceCueDefinition>())
            {
                if (cue == null || !ids.Contains(cue.dialogueId))
                {
                    EditorUtility.DisplayDialog("无法保存",
                        $"剧情 '{story.storyId}' 中存在未绑定到有效台词的演出 Cue。",
                        "确定");
                    return false;
                }
                if (cue.delay < 0f || string.IsNullOrWhiteSpace(cue.scriptId))
                {
                    EditorUtility.DisplayDialog("无法保存",
                        $"剧情 '{story.storyId}' 的台词 #{cue.dialogueId} 存在无效演出 Cue。",
                        "确定");
                    return false;
                }

                var matches = FindPerformanceScripts(cue.scriptId);
                if (matches.Count != 1)
                {
                    string reason = matches.Count == 0 ? "找不到" : "存在重复 ID";
                    EditorUtility.DisplayDialog("无法保存",
                        $"剧情 '{story.storyId}' 的演出脚本 '{cue.scriptId}' {reason}。",
                        "确定");
                    return false;
                }
            }
        }
        return true;
    }

    private static PerformanceScript FindPerformanceScript(string scriptId)
    {
        return FindPerformanceScripts(scriptId).FirstOrDefault();
    }

    private static List<PerformanceScript> FindPerformanceScripts(string scriptId)
    {
        if (string.IsNullOrWhiteSpace(scriptId))
            return new List<PerformanceScript>();

        return AssetDatabase.FindAssets("t:PerformanceScript")
            .Select(guid => AssetDatabase.LoadAssetAtPath<PerformanceScript>(
                AssetDatabase.GUIDToAssetPath(guid)))
            .Where(asset => asset != null && asset.scriptId == scriptId)
            .ToList();
    }

    private static string Trim(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= max ? value : value.Substring(0, max) + "...";
    }
}
