using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class PlayerGroundPlanePlayModeTests
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [UnityTest]
    public IEnumerator Jump_DoesNotConsumeGroundPlaneDepthAxis()
    {
        Type levelManagerType = Type.GetType("LevelManager, Assembly-CSharp", true);
        Type movementType = Type.GetType("PlayerMovement, Assembly-CSharp", true);

        GameObject floor = new GameObject("PlayModeTestFloor");
        BoxCollider2D floorCollider = floor.AddComponent<BoxCollider2D>();
        floorCollider.size = new Vector2(100f, 100f);

        GameObject managerObject = new GameObject("PlayModeTestLevelManager");
        managerObject.SetActive(false);
        Component levelManager = managerObject.AddComponent(levelManagerType);
        SetField(levelManager, "floorCollider", floorCollider);
        managerObject.SetActive(true);

        GameObject player = new GameObject("PlayModePlanarPlayer");
        player.SetActive(false);
        Rigidbody2D body = player.AddComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.freezeRotation = true;
        Component movement = player.AddComponent(movementType);
        SetField(movement, "jumpHeight", 2f);
        SetField(movement, "jumpDuration", 0.8f);
        SetField(movement, "hangTimeFactor", 0f);
        player.transform.position = new Vector3(0f, -2f, 0f);
        player.SetActive(true);

        yield return null;

        float startingGroundY = body.position.y;
        movementType.GetMethod("Jump", InstanceMembers)?.Invoke(movement, null);
        yield return new WaitForSeconds(0.1f);

        float airborneHeight = ReadProperty<float>(movement, "JumpHeight");
        Assert.That(airborneHeight, Is.GreaterThan(0.1f));
        Assert.That(body.position.y, Is.EqualTo(startingGroundY).Within(0.01f));

        MethodInfo moveScripted = movementType.GetMethod("MoveScriptedTowards", InstanceMembers);
        Vector2 depthTarget = new Vector2(body.position.x, startingGroundY + 1f);
        for (int i = 0; i < 12; i++)
        {
            moveScripted?.Invoke(movement, new object[] { depthTarget, 5f, 0.01f });
            yield return null;
        }

        Assert.That(body.position.y, Is.GreaterThan(startingGroundY + 0.05f));
        Assert.That(
            ReadProperty<float>(movement, "GroundY"),
            Is.EqualTo(body.position.y).Within(0.01f));
        Assert.That(ReadProperty<float>(movement, "JumpHeight"), Is.GreaterThan(0f));

        float timeoutAt = Time.realtimeSinceStartup + 2f;
        while (ReadProperty<bool>(movement, "IsJumping") &&
               Time.realtimeSinceStartup < timeoutAt)
        {
            yield return null;
        }

        Assert.That(ReadProperty<bool>(movement, "IsJumping"), Is.False);
        Assert.That(ReadProperty<float>(movement, "JumpHeight"), Is.EqualTo(0f).Within(0.001f));
        Assert.That(
            ReadProperty<float>(movement, "GroundY"),
            Is.EqualTo(body.position.y).Within(0.01f));

        UnityEngine.Object.Destroy(player);
        UnityEngine.Object.Destroy(managerObject);
        UnityEngine.Object.Destroy(floor);
        yield return null;
    }

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, InstanceMembers);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName}");
        field.SetValue(target, value);
    }

    private static T ReadProperty<T>(object target, string propertyName)
    {
        PropertyInfo property = target.GetType().GetProperty(propertyName, InstanceMembers);
        Assert.That(property, Is.Not.Null, $"Missing property {propertyName}");
        return (T)property.GetValue(target);
    }
}
