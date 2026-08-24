using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class PunkPCombatPlayModeTests
{
    const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    readonly List<GameObject> createdObjects = new List<GameObject>();

    Type aiType;
    Type enemyType;
    Type knifeType;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        aiType = Type.GetType("PunkPThrowAttack, Assembly-CSharp", true);
        enemyType = Type.GetType("Enemy, Assembly-CSharp", true);
        knifeType = Type.GetType("PunkPKnife, Assembly-CSharp", true);
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (UnityEngine.Object knifeObject in UnityEngine.Object.FindObjectsByType(
                     knifeType, FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Component knife = knifeObject as Component;
            if (knife != null)
                UnityEngine.Object.Destroy(knife.gameObject);
        }

        foreach (GameObject createdObject in createdObjects)
        {
            if (createdObject != null)
                UnityEngine.Object.Destroy(createdObject);
        }

        createdObjects.Clear();
        yield return null;
    }

    [UnityTest]
    public IEnumerator NormalVolley_ThrowsTwoKnivesWithInterval_WhileStationary()
    {
        TestContext context = CreateContext(new Vector2(6f, 0f));
        SetField(context.Ai, "throwWindupTime", 0.05f);
        SetField(context.Ai, "doubleThrowInterval", 0.12f);
        SetField(context.Ai, "postThrowRecoveryTime", 0.02f);

        Vector2 startPosition = context.Body.position;
        StartPrivateCoroutine(context.Brain, context.Ai, "DoNormalVolley");

        yield return new WaitForSeconds(0.08f);
        Assert.That(GetSpawnedKnives(context.KnifeTemplate).Length, Is.EqualTo(1));
        Assert.That(Vector2.Distance(context.Body.position, startPosition), Is.LessThan(0.001f));

        yield return new WaitForSeconds(0.13f);
        Assert.That(GetSpawnedKnives(context.KnifeTemplate).Length, Is.EqualTo(2));
        Assert.That(Vector2.Distance(context.Body.position, startPosition), Is.LessThan(0.001f));
    }

    [UnityTest]
    public IEnumerator HitReaction_InterruptsRemainingNormalVolley()
    {
        TestContext context = CreateContext(new Vector2(6f, 0f));
        SetField(context.Ai, "throwWindupTime", 0.03f);
        SetField(context.Ai, "doubleThrowInterval", 0.25f);
        SetField(context.Ai, "postThrowRecoveryTime", 0.02f);

        StartPrivateCoroutine(context.Brain, context.Ai, "DoNormalVolley");
        yield return new WaitForSeconds(0.07f);
        Assert.That(GetSpawnedKnives(context.KnifeTemplate).Length, Is.EqualTo(1));

        aiType.GetMethod("SetHitReactionActive", InstanceMembers)
            ?.Invoke(context.Ai, new object[] { true });
        yield return new WaitForSeconds(0.3f);

        Assert.That(GetSpawnedKnives(context.KnifeTemplate).Length, Is.EqualTo(1));
        Assert.That(context.Body.linearVelocity.sqrMagnitude, Is.LessThan(0.0001f));
    }

    [UnityTest]
    public IEnumerator SpecialAttack_RetreatsEightUnitsAndThrowsThreeAngles()
    {
        TestContext context = CreateContext(new Vector2(3f, 0f));
        SetField(context.Ai, "specialRetreatDuration", 0.08f);
        SetField(context.Ai, "postThrowRecoveryTime", 0.02f);
        SetField(context.Ai, "bodyWidth", 2f);
        SetField(context.Ai, "specialRetreatBodyLengths", 4f);

        StartPrivateCoroutine(context.Brain, context.Ai, "DoSpecialAttack");
        yield return new WaitForSeconds(0.13f);

        Assert.That(context.Body.position.x, Is.EqualTo(-8f).Within(0.1f));
        Assert.That(context.Body.position.y, Is.EqualTo(0f).Within(0.01f));

        UnityEngine.Object[] knives = GetSpawnedKnives(context.KnifeTemplate);
        Assert.That(knives.Length, Is.EqualTo(3));

        float[] angles = knives
            .Select(knife => (Vector2)ReadField(knife, "moveDirection"))
            .Select(direction => Mathf.Round(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg))
            .OrderBy(angle => angle)
            .ToArray();
        Assert.That(angles, Is.EqualTo(new[] { -30f, 0f, 30f }));

        bool specialReady = (bool)aiType.GetMethod("IsSpecialAttackReady", InstanceMembers)
            .Invoke(context.Ai, null);
        Assert.That(specialReady, Is.False);
    }

    [UnityTest]
    public IEnumerator SpecialAttack_StopsAtHorizontalBoundary()
    {
        TestContext context = CreateContext(new Vector2(3f, 0f));
        SetField(context.Ai, "leftBound", -3f);
        SetField(context.Ai, "rightBound", 3f);
        SetField(context.Ai, "specialRetreatDuration", 0.05f);
        SetField(context.Ai, "postThrowRecoveryTime", 0.01f);

        StartPrivateCoroutine(context.Brain, context.Ai, "DoSpecialAttack");
        yield return new WaitForSeconds(0.09f);

        Assert.That(context.Body.position.x, Is.EqualTo(-3f).Within(0.05f));
        Assert.That(context.Body.position.y, Is.EqualTo(0f).Within(0.01f));
        Assert.That(GetSpawnedKnives(context.KnifeTemplate).Length, Is.EqualTo(3));
    }

    [Test]
    public void StateSelection_RespectsRetreatPriorityAndHardCooldowns()
    {
        TestContext context = CreateContext(new Vector2(3f, 0f));
        SetField(context.Ai, "closeRetreatPriority", 100f);

        SetField(context.Ai, "specialAttackChance", 100f);
        SetField(context.Ai, "lastSpecialAttackTime", Time.time - 5f);
        Assert.That(ChooseState(context.Ai, 3f), Is.EqualTo("SpecialAttack"));

        SetField(context.Ai, "specialAttackChance", 0f);

        Assert.That(ChooseState(context.Ai, 3f), Is.EqualTo("Retreat"));

        SetField(context.Ai, "closeRetreatPriority", 0f);
        SetField(context.Ai, "attackDesire", 100f);
        SetField(context.Ai, "forceAttackTime", 0f);
        SetField(context.Ai, "throwCooldown", 10f);
        SetField(context.Ai, "lastAttackTime", Time.time);
        Assert.That(ChooseState(context.Ai, 3f), Is.Not.EqualTo("ThrowKnife"));

        SetField(context.Ai, "lastAttackTime", Time.time - 11f);
        Assert.That(ChooseState(context.Ai, 3f), Is.EqualTo("ThrowKnife"));
    }

    [UnityTest]
    public IEnumerator AngledKnife_AdvancesItsVirtualLane()
    {
        GameObject knifeObject = CreateObject("AngledKnife");
        Component knife = knifeObject.AddComponent(knifeType);
        SetField(knife, "speed", 2f);
        knifeType.GetMethod("SetDirection", InstanceMembers)
            ?.Invoke(knife, new object[] { new Vector2(Mathf.Cos(30f * Mathf.Deg2Rad), 0.5f) });
        knifeType.GetMethod("SetLaneY", InstanceMembers)?.Invoke(knife, new object[] { 1f });

        yield return new WaitForSeconds(0.2f);

        float laneY = (float)ReadField(knife, "laneY");
        Assert.That(laneY, Is.EqualTo(1.2f).Within(0.05f));
    }

    string ChooseState(Component ai, float distance)
    {
        object state = aiType.GetMethod("ChooseNextState", InstanceMembers)
            ?.Invoke(ai, new object[] { true, 0f, distance });
        Assert.That(state, Is.Not.Null);
        return state.ToString();
    }

    TestContext CreateContext(Vector2 playerPosition)
    {
        GameObject player = CreateObject("PunkPTestPlayer");
        player.transform.position = playerPosition;

        GameObject knifeTemplate = CreateObject("PunkPTestKnifeTemplate");
        knifeTemplate.transform.position = new Vector3(1000f, 1000f, 0f);
        knifeTemplate.AddComponent<SpriteRenderer>();
        Rigidbody2D knifeBody = knifeTemplate.AddComponent<Rigidbody2D>();
        knifeBody.bodyType = RigidbodyType2D.Kinematic;
        BoxCollider2D knifeCollider = knifeTemplate.AddComponent<BoxCollider2D>();
        knifeCollider.isTrigger = true;
        Component knife = knifeTemplate.AddComponent(knifeType);
        SetField(knife, "speed", 0f);
        SetField(knife, "lifetime", 30f);

        GameObject enemyObject = CreateObject("PunkPTestEnemy");
        enemyObject.SetActive(false);
        Rigidbody2D body = enemyObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        enemyObject.AddComponent<BoxCollider2D>();
        enemyObject.AddComponent(enemyType);
        Component ai = enemyObject.AddComponent(aiType);
        SetField(ai, "player", player.transform);
        SetField(ai, "knifePrefab", knifeTemplate);
        SetField(ai, "idleAnimName", string.Empty);
        SetField(ai, "walkAnimName", string.Empty);
        SetField(ai, "attackAnimName", string.Empty);
        SetField(ai, "isKnockedBack", true);
        enemyObject.SetActive(true);

        MonoBehaviour brain = (MonoBehaviour)ai;
        brain.StopAllCoroutines();
        SetField(ai, "isKnockedBack", false);
        SetField(ai, "isAttacking", false);
        body.position = Vector2.zero;
        body.linearVelocity = Vector2.zero;

        return new TestContext(ai, brain, body, knifeTemplate);
    }

    void StartPrivateCoroutine(MonoBehaviour host, Component target, string methodName)
    {
        IEnumerator routine = (IEnumerator)target.GetType()
            .GetMethod(methodName, InstanceMembers)
            ?.Invoke(target, null);
        Assert.That(routine, Is.Not.Null, $"Missing coroutine {methodName}");
        host.StartCoroutine(routine);
    }

    UnityEngine.Object[] GetSpawnedKnives(GameObject template)
    {
        return UnityEngine.Object.FindObjectsByType(
                knifeType, FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(knife => ((Component)knife).gameObject != template)
            .ToArray();
    }

    GameObject CreateObject(string name)
    {
        GameObject gameObject = new GameObject(name);
        createdObjects.Add(gameObject);
        return gameObject;
    }

    static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, InstanceMembers);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName}");
        field.SetValue(target, value);
    }

    static object ReadField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, InstanceMembers);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName}");
        return field.GetValue(target);
    }

    sealed class TestContext
    {
        public readonly Component Ai;
        public readonly MonoBehaviour Brain;
        public readonly Rigidbody2D Body;
        public readonly GameObject KnifeTemplate;

        public TestContext(Component ai, MonoBehaviour brain, Rigidbody2D body, GameObject knifeTemplate)
        {
            Ai = ai;
            Brain = brain;
            Body = body;
            KnifeTemplate = knifeTemplate;
        }
    }
}
