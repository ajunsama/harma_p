using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class GameFlowEditorTests
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void GameFlowConfig_HasExpectedDefaultScenes()
    {
        UnityEngine.Object config =
            AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                "Assets/Resources/GameFlowConfig.asset");

        Assert.That(config, Is.Not.Null);
        Assert.That(ReadProperty<string>(config, "StartMenuSceneName"), Is.EqualTo("StartGame"));
        Assert.That(ReadProperty<string>(config, "GameOverSceneName"), Is.EqualTo("GameOver"));
        Assert.That(ReadProperty<string>(config, "GameClearSceneName"), Is.EqualTo("GameClear"));
        Assert.That(
            ReadProperty<string>(config, "FirstGameplaySceneName"),
            Is.EqualTo("NewLevel_test"));
        Assert.That(ReadProperty<float>(config, "GameOverDelay"), Is.EqualTo(1.2f).Within(0.001f));
        Assert.That(ReadProperty<float>(config, "GameClearDelay"), Is.EqualTo(0.8f).Within(0.001f));
    }

    [Test]
    public void BuildSettings_ContainsGameFlowScenesInExpectedOrder()
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;

        Assert.That(scenes.Length, Is.GreaterThanOrEqualTo(4));
        Assert.That(scenes[0].path, Is.EqualTo("Assets/Scenes/StartGame.unity"));
        Assert.That(scenes[0].enabled, Is.True);
        Assert.That(scenes.Any(scene =>
            scene.enabled && scene.path == "Assets/Scenes/NewLevel_test.unity"), Is.True);
        Assert.That(scenes.Any(scene =>
            scene.enabled && scene.path == "Assets/Scenes/Bridge_PV.unity"), Is.True);
        Assert.That(scenes.Any(scene =>
            scene.enabled && scene.path == "Assets/Scenes/GameOver.unity"), Is.True);
        Assert.That(scenes.Any(scene =>
            scene.enabled && scene.path == "Assets/Scenes/GameClear.unity"), Is.True);
    }

    [Test]
    public void GameOverScene_HasBlinkingReturnPromptBinding()
    {
        string sceneYaml = File.ReadAllText("Assets/Scenes/GameOver.unity");

        StringAssert.Contains("m_Name: ReturnPrompt", sceneYaml);
        StringAssert.Contains(
            "m_text: \"\\u6309\\u4EFB\\u610F\\u952E\\u8FD4\\u56DE\\u5F00\\u59CB\\u83DC\\u5355\"",
            sceneYaml);
        StringAssert.Contains("returnPrompt: {fileID:", sceneYaml);
        StringAssert.Contains("inputDelay: 0.75", sceneYaml);
    }

    [Test]
    public void GameClearScene_HasBlinkingReturnPromptBinding()
    {
        string sceneYaml = File.ReadAllText("Assets/Scenes/GameClear.unity");

        StringAssert.Contains("m_Name: ReturnPrompt", sceneYaml);
        StringAssert.Contains(
            "m_text: \"\\u6309\\u4EFB\\u610F\\u952E\\u8FD4\\u56DE\\u5F00\\u59CB\\u83DC\\u5355\"",
            sceneYaml);
        StringAssert.Contains("returnPrompt: {fileID:", sceneYaml);
        StringAssert.Contains("inputDelay: 0.75", sceneYaml);
    }

    [Test]
    public void CameraLock_FreezesTheCurrentViewWithoutRecenteringOnPlayer()
    {
        Type controllerType = Type.GetType("LevelCameraController, Assembly-CSharp", true);
        GameObject cameraObject = new GameObject("CameraLockTest");
        Camera camera = cameraObject.AddComponent<Camera>();
        Component controller = cameraObject.AddComponent(controllerType);
        cameraObject.transform.position = new Vector3(12f, 3f, -10f);

        try
        {
            FieldInfo cameraField = controllerType.GetField("_cam", InstanceMembers);
            Assert.That(cameraField, Is.Not.Null);
            cameraField.SetValue(controller, camera);

            MethodInfo lockCurrentView =
                controllerType.GetMethod("LockCurrentView", InstanceMembers);
            lockCurrentView.Invoke(controller, null);

            Assert.That(ReadField<bool>(controller, "lockPosition"), Is.True);
            Assert.That(ReadField<float>(controller, "lockX"), Is.EqualTo(12f).Within(0.001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    [Test]
    public void CameraLock_RemainsActiveWhileDelayedGroupIsStillSpawning()
    {
        Type controllerType = Type.GetType("LevelCameraController, Assembly-CSharp", true);
        Type builderType = Type.GetType("LevelSceneBuilder, Assembly-CSharp", true);
        Type groupType = Type.GetType("ElementGroup, Assembly-CSharp", true);
        GameObject root = new GameObject("DelayedGroupLockTest");
        root.AddComponent<Camera>();
        Component controller = root.AddComponent(controllerType);
        Component builder = root.AddComponent(builderType);

        try
        {
            object group = Activator.CreateInstance(groupType);
            groupType.GetField("groupId", InstanceMembers).SetValue(group, "delayed-group");
            builderType.GetField("_activeLockGroup", InstanceMembers).SetValue(builder, group);
            builderType.GetField("_cameraController", InstanceMembers).SetValue(builder, controller);

            controllerType.GetMethod("LockCurrentView", InstanceMembers).Invoke(controller, null);
            MethodInfo updateLock = builderType.GetMethod("UpdateCameraLock", InstanceMembers);
            updateLock.Invoke(builder, null);
            Assert.That(ReadField<bool>(controller, "lockPosition"), Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void PlayerHp_DiedEventFiresOnlyOnce()
    {
        Type playerHpType = Type.GetType("PlayerHP, Assembly-CSharp", true);
        GameObject player = new GameObject("PlayerHpTest");
        Component hp = player.AddComponent(playerHpType);
        var counter = new DeathCounter();
        EventInfo diedEvent = playerHpType.GetEvent("Died", InstanceMembers);
        Delegate handler = Delegate.CreateDelegate(
            diedEvent.EventHandlerType,
            counter,
            typeof(DeathCounter).GetMethod(nameof(DeathCounter.Handle)));
        diedEvent.AddEventHandler(hp, handler);

        try
        {
            FieldInfo currentHp = playerHpType.GetField("currentHP", InstanceMembers);
            Assert.That(currentHp, Is.Not.Null);
            currentHp.SetValue(hp, 3);

            MethodInfo takeDamage = playerHpType.GetMethod("TakeDamage", InstanceMembers);
            takeDamage.Invoke(hp, new object[] { 999 });
            takeDamage.Invoke(hp, new object[] { 999 });

            Assert.That(ReadProperty<bool>(hp, "IsDead"), Is.True);
            Assert.That(counter.Count, Is.EqualTo(1));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    private static T ReadProperty<T>(object target, string propertyName)
    {
        PropertyInfo property = target.GetType().GetProperty(propertyName, InstanceMembers);
        Assert.That(property, Is.Not.Null, $"Missing property {propertyName}");
        return (T)property.GetValue(target);
    }

    private static T ReadField<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, InstanceMembers);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName}");
        return (T)field.GetValue(target);
    }

    private sealed class DeathCounter
    {
        public int Count { get; private set; }

        public void Handle(object playerHp)
        {
            Count++;
        }
    }
}
