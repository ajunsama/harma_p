using UnityEditor;
using UnityEngine;

/// <summary>
/// 独立的剧情演出编辑入口。复用 PerformanceScript 自定义 Inspector 的字段绘制，
/// 但不会要求作者离开剧情编辑器后再去 Project/Inspector 中寻找资源。
/// </summary>
public class PerformanceScriptEditorWindow : EditorWindow
{
    private PerformanceScript script;
    private UnityEditor.Editor scriptEditor;
    private Vector2 scroll;

    [MenuItem("Tools/演出编辑器")]
    public static void ShowWindow()
    {
        GetWindow<PerformanceScriptEditorWindow>("演出编辑器");
    }

    public static void Open(PerformanceScript target)
    {
        var window = GetWindow<PerformanceScriptEditorWindow>("演出编辑器");
        window.minSize = new Vector2(520f, 520f);
        window.SetScript(target);
        window.Show();
        window.Focus();
    }

    void OnDisable()
    {
        if (scriptEditor != null)
            DestroyImmediate(scriptEditor);
    }

    void OnGUI()
    {
        if (Event.current.type == EventType.KeyDown &&
            Event.current.control && Event.current.keyCode == KeyCode.S)
        {
            SaveCurrent();
            Event.current.Use();
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(
            "演出脚本只描述角色槽位、动作和镜头；具体角色请在关卡编辑器的 PlayStory 步骤中绑定。",
            EditorStyles.wordWrappedLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        var selected = (PerformanceScript)EditorGUILayout.ObjectField(
            "演出脚本", script, typeof(PerformanceScript), false);
        if (EditorGUI.EndChangeCheck())
            SetScript(selected);
        if (GUILayout.Button("新建", GUILayout.Width(64f)))
            SetScript(CreateAsset());
        using (new EditorGUI.DisabledScope(script == null))
            if (GUILayout.Button(EditorUtility.IsDirty(script) ? "保存*" : "保存",
                    GUILayout.Width(64f)))
                SaveCurrent();
        if (GUILayout.Button("定位", GUILayout.Width(64f)) && script != null)
        {
            Selection.activeObject = script;
            EditorGUIUtility.PingObject(script);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        if (script == null)
        {
            EditorGUILayout.HelpBox("选择或新建一个 PerformanceScript 后开始编辑。", MessageType.Info);
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        UnityEditor.Editor.CreateCachedEditor(script, null, ref scriptEditor);
        scriptEditor.OnInspectorGUI();
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField(
            EditorUtility.IsDirty(script) ? "有未保存修改" : "已保存",
            EditorStyles.miniLabel, GUILayout.Width(90f));
        if (GUILayout.Button("保存演出脚本", EditorStyles.toolbarButton, GUILayout.Width(100f)))
            SaveCurrent();
        EditorGUILayout.EndHorizontal();
    }

    void SaveCurrent()
    {
        if (script == null) return;
        if (scriptEditor != null)
            scriptEditor.serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(script);
        AssetDatabase.SaveAssets();
        ShowNotification(new GUIContent($"已保存：{script.name}"));
        Repaint();
    }

    void SetScript(PerformanceScript target)
    {
        if (script == target) return;
        script = target;
        if (scriptEditor != null)
        {
            DestroyImmediate(scriptEditor);
            scriptEditor = null;
        }
        Repaint();
    }

    public static PerformanceScript CreateAsset()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "新建演出脚本", "NewPerformanceScript", "asset",
            "选择演出脚本保存位置", "Assets");
        if (string.IsNullOrEmpty(path)) return null;

        var asset = CreateInstance<PerformanceScript>();
        asset.scriptId = System.IO.Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        return asset;
    }
}
