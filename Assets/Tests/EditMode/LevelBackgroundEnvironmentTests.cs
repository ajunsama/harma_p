using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class LevelBackgroundEnvironmentTests
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void LegacySingleBackground_MigratesToRepeatedMidLayer()
    {
        var texture = new Texture2D(8, 8);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 1f);
        try
        {
            object settings = NewRuntime("BackgroundSettings");
            Set(settings, "dataVersion", 0);
            Set(settings, "mode", EnumRuntime("BackgroundMode", "SingleInfiniteScroll"));
            Set(settings, "singleBackground", sprite);
            Set(settings, "singleParallaxFactor", 0f);

            Assert.That((bool)Invoke(settings, "MigrateLegacyData"), Is.True);
            Assert.That(Get<int>(settings, "dataVersion"), Is.EqualTo(3));
            IList layers = List(settings, "layers");
            Assert.That(layers, Has.Count.EqualTo(1));
            Assert.That(Get(layers[0], "contentType").ToString(), Is.EqualTo("RepeatedSprite"));
            Assert.That(Get(layers[0], "depthBand").ToString(), Is.EqualTo("Mid"));
            Assert.That((float)Property(layers[0], "MotionMultiplierX"), Is.EqualTo(1f).Within(0.001f));
            Assert.That((bool)Invoke(settings, "MigrateLegacyData"), Is.False,
                "Migration must be idempotent");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sprite);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void LayerMotion_UsesFarMidNearScreenMultipliers()
    {
        Type controllerType = RuntimeType("LayeredBackgroundController");
        MethodInfo calculate = controllerType.GetMethod("CalculateCameraOffset", BindingFlags.Public | BindingFlags.Static);
        object far = NewLayer("Far", true, 1.25f);
        object mid = NewLayer("Mid", false, 1.25f);
        object near = NewLayer("Near", false, 1.5f);
        Vector2 cameraDelta = new Vector2(10f, 3f);

        Assert.That((Vector2)calculate.Invoke(null, new[] { (object)cameraDelta, far }),
            Is.EqualTo(new Vector2(10f, 3f)));
        Assert.That((Vector2)calculate.Invoke(null, new[] { (object)cameraDelta, mid }),
            Is.EqualTo(Vector2.zero));
        Assert.That((Vector2)calculate.Invoke(null, new[] { (object)cameraDelta, near }),
            Is.EqualTo(new Vector2(-5f, 0f)));

        MethodInfo tileCount = controllerType.GetMethod("GetRequiredTileCount", BindingFlags.Public | BindingFlags.Static);
        Assert.That((int)tileCount.Invoke(null, new object[] { 20f, 3f }), Is.EqualTo(9));
    }

    [Test]
    public void JsonRoundTrip_PreservesLayersEnvironmentActorsAndElementParameters()
    {
        var source = (ScriptableObject)ScriptableObject.CreateInstance(RuntimeType("LevelData"));
        var target = (ScriptableObject)ScriptableObject.CreateInstance(RuntimeType("LevelData"));
        try
        {
            object background = Get(source, "backgroundSettings");
            Set(background, "dataVersion", 2);
            object layer = NewRuntime("BackgroundLayerData");
            Set(layer, "layerId", "clouds");
            Set(layer, "displayName", "Clouds");
            Set(layer, "depthBand", EnumRuntime("BackgroundDepthBand", "Far"));
            Set(layer, "contentType", EnumRuntime("BackgroundLayerContentType", "RepeatedSprite"));
            Set(layer, "autoScrollDirection", EnumRuntime("BackgroundScrollDirection", "RightToLeft"));
            Set(layer, "autoScrollSpeed", 1.25f);
            Set(layer, "enableVerticalMotion", true);
            List(background, "layers").Add(layer);

            object element = NewRuntime("LevelElement");
            Set(element, "elementId", "element");
            object parameter = NewRuntime("ElementCustomParameter");
            Set(parameter, "componentTypeName", "Example");
            Set(parameter, "fieldName", "speed");
            Set(parameter, "valueTypeName", "System.Single");
            Set(parameter, "serializedValue", "2.5");
            List(element, "customParameters").Add(parameter);
            List(source, "elements").Add(element);

            object actor = NewRuntime("EnvironmentActorData");
            Set(actor, "actorId", "windmill");
            Set(actor, "displayName", "Windmill");
            object trigger = NewRuntime("EnvironmentActorTrigger");
            Set(trigger, "triggerType", EnumRuntime("EnvironmentTriggerType", "PlayerSignal"));
            Set(trigger, "signalId", "jump_started");
            object action = NewRuntime("EnvironmentActorAction");
            Set(action, "actionType", EnumRuntime("EnvironmentActionType", "EmitActorSignal"));
            Set(action, "name", "react");
            List(trigger, "onEnterActions").Add(action);
            List(actor, "triggers").Add(trigger);
            List(source, "environmentActors").Add(actor);

            Type jsonType = EditorType("LevelDataJsonUtility");
            string json = (string)jsonType.GetMethod("Export", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { source });
            jsonType.GetMethod("Import", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { target, json });

            IList importedLayers = List(Get(target, "backgroundSettings"), "layers");
            Assert.That(importedLayers, Has.Count.EqualTo(1));
            Assert.That(Get(importedLayers[0], "autoScrollDirection").ToString(), Is.EqualTo("RightToLeft"));
            Assert.That(Get<float>(importedLayers[0], "autoScrollSpeed"), Is.EqualTo(1.25f));
            IList importedElements = List(target, "elements");
            Assert.That(Get<string>(List(importedElements[0], "customParameters")[0], "serializedValue"),
                Is.EqualTo("2.5"));
            IList importedActors = List(target, "environmentActors");
            Assert.That(importedActors, Has.Count.EqualTo(1));
            Assert.That(Get<string>(List(List(importedActors[0], "triggers")[0], "onEnterActions")[0], "name"),
                Is.EqualTo("react"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void EnvironmentActor_PlayerSignalExecutesConfiguredAction()
    {
        var player = new GameObject("Player");
        var actorObject = new GameObject("Actor");
        try
        {
            Component hub = player.AddComponent(RuntimeType("PlayerGameplaySignalHub"));
            var renderer = actorObject.AddComponent<SpriteRenderer>();
            Component controller = actorObject.AddComponent(RuntimeType("EnvironmentActorController"));
            object actor = NewRuntime("EnvironmentActorData");
            Set(actor, "actorId", "signal_actor");
            object trigger = NewRuntime("EnvironmentActorTrigger");
            Set(trigger, "triggerType", EnumRuntime("EnvironmentTriggerType", "PlayerSignal"));
            Set(trigger, "signalId", "jump_started");
            object action = NewRuntime("EnvironmentActorAction");
            Set(action, "actionType", EnumRuntime("EnvironmentActionType", "SetVisualActive"));
            Set(action, "boolValue", false);
            List(trigger, "onEnterActions").Add(action);
            List(actor, "triggers").Add(trigger);

            controller.GetType().GetMethod("Initialize").Invoke(
                controller,
                new object[] { actor, player.transform, null, hub });
            hub.GetType().GetMethod("Publish").Invoke(hub, new object[] { "jump_started", 0f, null });

            Assert.That(renderer.enabled, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(actorObject);
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void PerformanceBinding_ResolvesEnvironmentActorByStableId()
    {
        var builderObject = new GameObject("Builder");
        var actorObject = new GameObject("Actor");
        try
        {
            Component builder = builderObject.AddComponent(RuntimeType("LevelSceneBuilder"));
            IDictionary instances = (IDictionary)builder.GetType()
                .GetField("_environmentActorInstances", InstanceMembers)
                .GetValue(builder);
            instances.Add("windmill", actorObject);

            object binding = NewRuntime("PerformanceActorBinding");
            Set(binding, "targetType", EnumRuntime("PerformanceActorTargetType", "EnvironmentActor"));
            Set(binding, "environmentActorId", "windmill");
            object[] arguments = { binding, null };
            bool found = (bool)builder.GetType().GetMethod("TryResolvePerformanceActor")
                .Invoke(builder, arguments);

            Assert.That(found, Is.True);
            Assert.That(arguments[1], Is.EqualTo(actorObject));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(actorObject);
            UnityEngine.Object.DestroyImmediate(builderObject);
        }
    }

    [Test]
    public void Validation_ReportsInvalidScrollingLayerAndDuplicateActorId()
    {
        var level = (ScriptableObject)ScriptableObject.CreateInstance(RuntimeType("LevelData"));
        var prefab = new GameObject("EnvironmentPrefab");
        try
        {
            Set(level, "playerPrefab", prefab);
            object background = Get(level, "backgroundSettings");
            Set(background, "dataVersion", 2);
            object layer = NewRuntime("BackgroundLayerData");
            Set(layer, "layerId", "bad-scroll");
            Set(layer, "displayName", "Bad Scroll");
            Set(layer, "contentType", EnumRuntime("BackgroundLayerContentType", "SingleSprite"));
            Set(layer, "autoScrollDirection", EnumRuntime("BackgroundScrollDirection", "LeftToRight"));
            Set(layer, "autoScrollSpeed", 1f);
            List(background, "layers").Add(layer);

            object firstActor = NewRuntime("EnvironmentActorData");
            Set(firstActor, "actorId", "duplicate");
            Set(firstActor, "displayName", "First");
            Set(firstActor, "prefab", prefab);
            object secondActor = NewRuntime("EnvironmentActorData");
            Set(secondActor, "actorId", "duplicate");
            Set(secondActor, "displayName", "Second");
            Set(secondActor, "prefab", prefab);
            List(level, "environmentActors").Add(firstActor);
            List(level, "environmentActors").Add(secondActor);

            object validation = Invoke(level, "Validate");
            IList errors = List(validation, "errors");
            string combined = string.Join("\n", errors.Cast<string>());

            StringAssert.Contains("must use repeated content", combined);
            StringAssert.Contains("Duplicate environment actor ID", combined);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(prefab);
            UnityEngine.Object.DestroyImmediate(level);
        }
    }

    [Test]
    public void AutoScroll_UsesDirectionSpeedAndTimeOnly()
    {
        object layer = NewLayer("Far", true, 1.25f);
        Set(layer, "autoScrollDirection", EnumRuntime("BackgroundScrollDirection", "RightToLeft"));
        Set(layer, "autoScrollSpeed", 2f);

        float atThreeSeconds = (float)layer.GetType()
            .GetMethod("CalculateAutoScrollOffset", InstanceMembers)
            .Invoke(layer, new object[] { 3f });
        MethodInfo calculateCamera = RuntimeType("LayeredBackgroundController")
            .GetMethod("CalculateCameraOffset", BindingFlags.Public | BindingFlags.Static);
        Vector2 stillCameraOffset = (Vector2)calculateCamera.Invoke(
            null, new[] { (object)Vector2.zero, layer });
        Vector2 movedCameraOffset = (Vector2)calculateCamera.Invoke(
            null, new[] { (object)new Vector2(10f, 0f), layer });
        float stillCameraScrollDelta = (stillCameraOffset.x + atThreeSeconds) - stillCameraOffset.x;
        float movedCameraScrollDelta = (movedCameraOffset.x + atThreeSeconds) - movedCameraOffset.x;

        Assert.That(atThreeSeconds, Is.EqualTo(-6f).Within(0.001f));
        Assert.That(movedCameraScrollDelta, Is.EqualTo(stillCameraScrollDelta).Within(0.001f));

        Set(layer, "autoScrollDirection", EnumRuntime("BackgroundScrollDirection", "LeftToRight"));
        float oppositeDirection = (float)layer.GetType()
            .GetMethod("CalculateAutoScrollOffset", InstanceMembers)
            .Invoke(layer, new object[] { 3f });
        Assert.That(oppositeDirection, Is.EqualTo(6f).Within(0.001f));
    }

    [Test]
    public void VersionTwoSignedScroll_MigratesToExplicitDirectionAndSpeed()
    {
        object settings = NewRuntime("BackgroundSettings");
        Set(settings, "dataVersion", 2);
        object layer = NewRuntime("BackgroundLayerData");
        Set(layer, "horizontalScrollSpeed", -3.5f);
        List(settings, "layers").Add(layer);

        Assert.That((bool)Invoke(settings, "MigrateLegacyData"), Is.True);
        Assert.That(Get(layer, "autoScrollDirection").ToString(), Is.EqualTo("RightToLeft"));
        Assert.That(Get<float>(layer, "autoScrollSpeed"), Is.EqualTo(3.5f));
        Assert.That(Get<float>(layer, "horizontalScrollSpeed"), Is.Zero);
    }

    private static object NewLayer(string band, bool vertical, float nearMultiplier)
    {
        object layer = NewRuntime("BackgroundLayerData");
        Set(layer, "depthBand", EnumRuntime("BackgroundDepthBand", band));
        Set(layer, "enableVerticalMotion", vertical);
        Set(layer, "nearMotionMultiplier", nearMultiplier);
        return layer;
    }

    private static Type RuntimeType(string name) => Type.GetType($"{name}, Assembly-CSharp", true);
    private static Type EditorType(string name) => Type.GetType($"{name}, Assembly-CSharp-Editor", true);
    private static object NewRuntime(string name) => Activator.CreateInstance(RuntimeType(name));
    private static object EnumRuntime(string name, string value) => Enum.Parse(RuntimeType(name), value);
    private static IList List(object target, string name) => (IList)Get(target, name);
    private static T Get<T>(object target, string name) => (T)Get(target, name);
    private static object Get(object target, string name) =>
        target.GetType().GetField(name, InstanceMembers).GetValue(target);
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, InstanceMembers).SetValue(target, value);
    private static object Property(object target, string name) =>
        target.GetType().GetProperty(name, InstanceMembers).GetValue(target);
    private static object Invoke(object target, string name) =>
        target.GetType().GetMethod(name, InstanceMembers).Invoke(target, null);
}
