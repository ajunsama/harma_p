using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class GameFlowEditorTests
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticMembers =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

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
            scene.enabled && scene.path == "Assets/Scenes/GameOver.unity"), Is.True);
        Assert.That(scenes.Any(scene =>
            scene.enabled && scene.path == "Assets/Scenes/GameClear.unity"), Is.True);
    }

    [Test]
    public void BridgePv_RemainsAvailableAsTestSceneButIsExcludedFromBuild()
    {
        const string testScenePath = "Assets/Scenes/Tests/Bridge_PV.unity";

        Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(testScenePath), Is.Not.Null);
        Assert.That(
            EditorBuildSettings.scenes.Any(scene => scene.path == testScenePath),
            Is.False);
    }

    [Test]
    public void SpineExamples_AreRemovedButSpineRuntimeAndProjectSkeletonsRemain()
    {
        Assert.That(AssetDatabase.IsValidFolder("Assets/Spine Examples"), Is.False);
        Assert.That(AssetDatabase.IsValidFolder("Assets/Spine"), Is.True);
        Assert.That(AssetDatabase.IsValidFolder("Assets/Spine Skeletons"), Is.True);
    }

    [Test]
    public void SpineColorSpaceValidator_CoversEnabledBuildDependencies()
    {
        Type validatorType = Type.GetType(
            "Harma.EditorTools.SpineColorSpaceValidation, Assembly-CSharp-Editor",
            true);
        MethodInfo scanMethod = validatorType.GetMethod(
            "ScanEnabledBuildScenes",
            StaticMembers);

        Assert.That(scanMethod, Is.Not.Null);
        object result = scanMethod.Invoke(null, null);
        string[] enabledScenes = ReadProperty<string[]>(result, "EnabledScenePaths");
        string[] checkedMaterials = ReadProperty<string[]>(result, "CheckedMaterialPaths");
        string[] incompatibleMaterials =
            ReadProperty<string[]>(result, "IncompatibleMaterialPaths");

        string[] expectedScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled && !string.IsNullOrEmpty(scene.path))
            .Select(scene => scene.path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        CollectionAssert.AreEqual(expectedScenes, enabledScenes);
        Assert.That(checkedMaterials, Is.Not.Empty);
        Assert.That(incompatibleMaterials, Is.SubsetOf(checkedMaterials));
        Assert.That(incompatibleMaterials, Is.Ordered);
    }

    [Test]
    public void ScenesAndPrefabs_HaveNoMissingScriptGuids()
    {
        string[] sceneFiles = Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories);
        string[] prefabFiles = Directory.GetFiles("Assets", "*.prefab", SearchOption.AllDirectories);
        string[] files = sceneFiles.Concat(prefabFiles).ToArray();
        var missing = new System.Collections.Generic.List<string>();
        var scriptPattern = new Regex(
            @"m_Script:\s*\{fileID:\s*11500000,\s*guid:\s*([0-9a-f]{32}),\s*type:\s*3\}",
            RegexOptions.Compiled);

        foreach (string file in files)
        {
            foreach (Match match in scriptPattern.Matches(File.ReadAllText(file)))
            {
                string guid = match.Groups[1].Value;
                if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
                    missing.Add($"{file}: {guid}");
            }
        }

        Assert.That(files.Length, Is.GreaterThan(0));
        Assert.That(missing, Is.Empty, string.Join("\n", missing));
    }

    [Test]
    public void StoryTrigger_LevelStartUsesCurrentLevelBuilderContract()
    {
        Type triggerType = Type.GetType("StoryTrigger, Assembly-CSharp", true);
        Type builderType = Type.GetType("LevelSceneBuilder, Assembly-CSharp", true);

        Assert.That(
            builderType.GetProperty("IsLevelReady", InstanceMembers),
            Is.Not.Null);
        Assert.That(
            triggerType.GetField("_levelSceneBuilder", InstanceMembers),
            Is.Not.Null);
        Assert.That(
            triggerType.GetMethod("TrySubscribe", InstanceMembers),
            Is.Null,
            "Legacy reflection subscription should not return.");
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
    public void CameraLock_OwnerCleanupClearsSharedState()
    {
        Type controllerType = Type.GetType("LevelCameraController, Assembly-CSharp", true);
        PropertyInfo isLockedProperty = controllerType.GetProperty("IsLocked", StaticMembers);
        GameObject cameraObject = new GameObject("CameraLockOwnerTest");
        cameraObject.AddComponent<Camera>();
        Component controller = cameraObject.AddComponent(controllerType);

        try
        {
            controllerType.GetMethod("LockCurrentView", InstanceMembers).Invoke(controller, null);
            Assert.That((bool)isLockedProperty.GetValue(null), Is.True);

            controllerType.GetMethod("OnDestroy", InstanceMembers).Invoke(controller, null);

            Assert.That((bool)isLockedProperty.GetValue(null), Is.False);
        }
        finally
        {
            controllerType.GetMethod("Unlock", InstanceMembers)?.Invoke(controller, null);
            UnityEngine.Object.DestroyImmediate(cameraObject);
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

    [Test]
    public void PlayerScriptedMovement_KeepsRigidBodyOnGroundPlaneWhileAirborne()
    {
        Type movementType = Type.GetType("PlayerMovement, Assembly-CSharp", true);
        GameObject player = new GameObject("PlanarPlayerMovementTest");
        Rigidbody2D body = player.AddComponent<Rigidbody2D>();
        Component movement = player.AddComponent(movementType);

        try
        {
            movementType.GetField("rb", InstanceMembers).SetValue(movement, body);
            movementType.GetField("baseY", InstanceMembers).SetValue(movement, 3f);
            movementType.GetField("jumpOffset", InstanceMembers).SetValue(movement, 2f);
            movementType.GetField("isJumping", InstanceMembers).SetValue(movement, true);
            body.position = new Vector2(2f, 3f);

            movementType.GetMethod("MoveScriptedTowards", InstanceMembers).Invoke(
                movement,
                new object[] { new Vector2(2f, 3f), 1f, 0.01f });

            Assert.That(body.position.y, Is.EqualTo(3f).Within(0.001f));
            Assert.That(ReadProperty<float>(movement, "GroundY"), Is.EqualTo(3f).Within(0.001f));
            Assert.That(ReadProperty<float>(movement, "JumpHeight"), Is.EqualTo(2f).Within(0.001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void PlayerJumpVisual_ConvertsWorldHeightToScaledSkeletonUnits()
    {
        Type movementType = Type.GetType("PlayerMovement, Assembly-CSharp", true);
        MethodInfo convertHeight = movementType.GetMethod(
            "WorldHeightToSkeletonUnits",
            StaticMembers);
        GameObject skeletonObject = new GameObject("ScaledSkeletonHeightTest");

        try
        {
            Assert.That(convertHeight, Is.Not.Null);
            skeletonObject.transform.localScale = new Vector3(0.19f, 0.2f, 1f);
            float skeletonUnits = (float)convertHeight.Invoke(
                null,
                new object[] { skeletonObject.transform, 3.5f });
            float reconstructedWorldHeight = skeletonObject.transform
                .TransformVector(Vector3.up * skeletonUnits)
                .y;

            Assert.That(skeletonUnits, Is.EqualTo(17.5f).Within(0.001f));
            Assert.That(reconstructedWorldHeight, Is.EqualTo(3.5f).Within(0.001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(skeletonObject);
        }
    }

    [Test]
    public void EnemyStompHeight_DefaultsToLegacyColliderCenterHeight()
    {
        Type enemyType = Type.GetType("Enemy, Assembly-CSharp", true);
        GameObject enemyObject = new GameObject("LegacyStompHeightTest");

        try
        {
            enemyObject.transform.position = new Vector3(1f, -3f, 0f);
            enemyObject.transform.localScale = new Vector3(0.19f, 0.19f, 1f);
            BoxCollider2D enemyCollider = enemyObject.AddComponent<BoxCollider2D>();
            enemyCollider.offset = new Vector2(0.09f, 9.1f);
            enemyCollider.size = new Vector2(6.62f, 19.2f);
            Component enemy = enemyObject.AddComponent(enemyType);

            enemyType.GetField("colliders", InstanceMembers)
                .SetValue(enemy, new Collider2D[] { enemyCollider });
            enemyType.GetMethod("CacheAutomaticStompHeight", InstanceMembers)
                .Invoke(enemy, null);

            Assert.That(
                ReadProperty<float>(enemy, "StompHeight"),
                Is.EqualTo(9.1f * 0.19f).Within(0.001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(enemyObject);
        }
    }

    [Test]
    public void StompRules_RequirePlanarOverlapAndRestoreDescendingHeightWindow()
    {
        Type rulesType = Type.GetType(
            "Harma.Combat.StompRules, Harma.Combat.Contracts",
            true);
        MethodInfo planarContact = rulesType.GetMethod("IsPlanarContact", StaticMembers);
        MethodInfo stompableHeight = rulesType.GetMethod(
            "IsStompableHeightWhileDescending",
            StaticMembers);

        Assert.That((bool)planarContact.Invoke(null, new object[]
        {
            new Vector2(1f, 2f), new Vector2(1.5f, 2.25f), 1.2f, 0.6f
        }), Is.True);
        Assert.That((bool)planarContact.Invoke(null, new object[]
        {
            new Vector2(1f, 2f), new Vector2(1.5f, 3f), 1.2f, 0.6f
        }), Is.False);
        Assert.That((bool)stompableHeight.Invoke(null, new object[]
        {
            1.5f, 0.7f, 1f, 0.2f
        }), Is.True);
        Assert.That((bool)stompableHeight.Invoke(null, new object[]
        {
            3f, 2.5f, 1f, 0.2f
        }), Is.True, "Descending contact above the target should keep the old stomp window.");
        Assert.That((bool)stompableHeight.Invoke(null, new object[]
        {
            0.7f, 0.6f, 1f, 0.2f
        }), Is.False, "Contact below the target should not count as a head stomp.");
        Assert.That((bool)stompableHeight.Invoke(null, new object[]
        {
            0.7f, 1f, 1f, 0.2f
        }), Is.False);
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
