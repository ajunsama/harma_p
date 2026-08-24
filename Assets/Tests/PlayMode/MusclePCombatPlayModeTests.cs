using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class MusclePCombatPlayModeTests
{
    const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    readonly List<GameObject> createdObjects = new List<GameObject>();
    Type aiType;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        aiType = Type.GetType("MuscleP_AI_Movement, Assembly-CSharp", true);
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (GameObject createdObject in createdObjects)
        {
            if (createdObject != null)
                UnityEngine.Object.Destroy(createdObject);
        }

        createdObjects.Clear();
        yield return null;
    }

    [Test]
    public void DanceDecision_RespectsInitialCooldownDistanceAndChance()
    {
        TestContext context = CreateContext(new Vector2(12f, 0f), false);

        Assert.That(ShouldStartDance(context.Ai, 12f), Is.False,
            "The initial cooldown should prevent an immediate spawn dance.");

        SetField(context.Ai, "nextDanceAllowedTime", Time.time - 1f);
        Assert.That(ShouldStartDance(context.Ai, 9f), Is.False,
            "Distances below the 10-unit entry threshold must not start a dance.");

        SetField(context.Ai, "danceChance", 0f);
        Assert.That(ShouldStartDance(context.Ai, 12f), Is.False);

        SetField(context.Ai, "danceChance", 100f);
        Assert.That(ShouldStartDance(context.Ai, 12f), Is.True);
    }

    [Test]
    public void DanceDecision_PrecedesForcedAttackOnceThenCooldownBlocksIt()
    {
        TestContext context = CreateContext(new Vector2(12f, 0f));
        SetField(context.Ai, "danceChance", 100f);

        string state = ChooseDecisionState(context.Ai, 12f, false, false, 999f);
        Assert.That(state, Is.EqualTo("Dance"));

        SetField(context.Ai, "nextDanceAllowedTime", Time.time + 8f);
        state = ChooseDecisionState(context.Ai, 12f, false, false, 999f);
        Assert.That(state, Is.EqualTo("Approach"));
    }

    [UnityTest]
    public IEnumerator Dance_RemainsStationaryAndStopsAtMaximumDuration()
    {
        TestContext context = CreateContext(new Vector2(12f, 0f));
        SetField(context.Ai, "danceMaxDuration", 0.08f);
        SetField(context.Ai, "danceCooldown", 0.5f);

        Vector2 startPosition = context.Body.position;
        context.Body.linearVelocity = new Vector2(5f, 2f);
        StartPrivateCoroutine(context.Brain, context.Ai, "Dance");

        yield return new WaitForSeconds(0.03f);
        Assert.That((bool)ReadField(context.Ai, "isDancing"), Is.True);
        Assert.That(context.Body.linearVelocity.sqrMagnitude, Is.LessThan(0.0001f));
        Assert.That(Vector2.Distance(context.Body.position, startPosition), Is.LessThan(0.001f));
        Assert.That(context.Ai.GetType().GetProperty("IsAttackActive", InstanceMembers)?.GetValue(context.Ai),
            Is.EqualTo(false), "Dancing must not open the attack damage window.");

        yield return new WaitForSeconds(0.08f);
        Assert.That((bool)ReadField(context.Ai, "isDancing"), Is.False);
        Assert.That((float)ReadField(context.Ai, "nextDanceAllowedTime"), Is.GreaterThan(Time.time));
    }

    [UnityTest]
    public IEnumerator Dance_PlayerApproachInterruptsImmediatelyAndStartsCooldown()
    {
        TestContext context = CreateContext(new Vector2(12f, 0f));
        SetField(context.Ai, "danceMaxDuration", 2f);
        SetField(context.Ai, "danceCooldown", 0.5f);
        StartPrivateCoroutine(context.Brain, context.Ai, "Dance");

        yield return null;
        Assert.That((bool)ReadField(context.Ai, "isDancing"), Is.True);

        context.Player.position = new Vector2(8f, 0f);
        yield return null;

        Assert.That((bool)ReadField(context.Ai, "isDancing"), Is.False);
        Assert.That((float)ReadField(context.Ai, "nextDanceAllowedTime"), Is.GreaterThan(Time.time));
        Assert.That(ShouldStartDance(context.Ai, 12f), Is.False);
    }

    [UnityTest]
    public IEnumerator Dance_HitReactionInterruptsAndClearsMovement()
    {
        TestContext context = CreateContext(new Vector2(12f, 0f));
        SetField(context.Ai, "danceMaxDuration", 2f);
        StartPrivateCoroutine(context.Brain, context.Ai, "Dance");

        yield return null;
        aiType.GetMethod("SetHitReactionActive", InstanceMembers)
            ?.Invoke(context.Ai, new object[] { true });

        Assert.That((bool)ReadField(context.Ai, "isDancing"), Is.False);
        Assert.That(context.Body.linearVelocity.sqrMagnitude, Is.LessThan(0.0001f));
        Assert.That((float)ReadField(context.Ai, "nextDanceAllowedTime"), Is.GreaterThan(Time.time));
    }

    [UnityTest]
    public IEnumerator Dance_OffscreenObjectCannotStart()
    {
        GameObject cameraObject = CreateObject("MusclePTestCamera");
        Camera camera = cameraObject.AddComponent<Camera>();
        cameraObject.tag = "MainCamera";
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        cameraObject.transform.position = Vector3.zero;

        yield return null;

        Camera activeMainCamera = Camera.main;
        Assert.That(activeMainCamera, Is.Not.Null);
        float cameraHalfWidth = activeMainCamera.orthographicSize * activeMainCamera.aspect;
        float offscreenX = activeMainCamera.transform.position.x + cameraHalfWidth + 2f;

        TestContext context = CreateContext(new Vector2(offscreenX + 12f, 0f));
        context.Body.position = new Vector2(offscreenX, 0f);
        yield return null;

        Assert.That(ShouldStartDance(context.Ai, 12f), Is.False);
    }

    [Test]
    public void DanceAnimation_DefaultSequenceMatchesPmachoAssets()
    {
        TestContext context = CreateContext(new Vector2(12f, 0f));

        Assert.That(ReadField(context.Ai, "danceIntroAnimation"), Is.EqualTo("dance03"));
        Assert.That(ReadField(context.Ai, "danceTransitionAnimation"), Is.EqualTo("dance to dance02"));
        Assert.That(ReadField(context.Ai, "danceLoopAnimation"), Is.EqualTo("dance02"));
    }

    TestContext CreateContext(Vector2 playerPosition, bool makeDanceReady = true)
    {
        GameObject player = CreateObject("MusclePTestPlayer");
        player.transform.position = playerPosition;

        GameObject enemyObject = CreateObject("MusclePTestEnemy");
        enemyObject.SetActive(false);
        Rigidbody2D body = enemyObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        Component ai = enemyObject.AddComponent(aiType);
        SetField(ai, "player", player.transform);
        SetField(ai, "enableDance", true);
        SetField(ai, "danceStartDistance", 10f);
        SetField(ai, "danceExitDistance", 8f);
        SetField(ai, "danceChance", 100f);
        SetField(ai, "danceCooldown", 8f);
        SetField(ai, "idleAnimName", string.Empty);
        SetField(ai, "walkAnimName", string.Empty);
        SetField(ai, "attackAnimName", string.Empty);
        SetField(ai, "isKnockedBack", true);
        enemyObject.SetActive(true);

        MonoBehaviour brain = (MonoBehaviour)ai;
        brain.StopAllCoroutines();
        SetField(ai, "isKnockedBack", false);
        body.position = Vector2.zero;
        body.linearVelocity = Vector2.zero;
        if (makeDanceReady)
            SetField(ai, "nextDanceAllowedTime", Time.time - 1f);

        return new TestContext(ai, brain, body, player.transform);
    }

    bool ShouldStartDance(Component ai, float distance)
    {
        object result = aiType.GetMethod("ShouldStartDance", InstanceMembers)
            ?.Invoke(ai, new object[] { distance });
        Assert.That(result, Is.Not.Null);
        return (bool)result;
    }

    string ChooseDecisionState(Component ai, float distance, bool yAligned, bool inRange, float timeSinceAttack)
    {
        object state = aiType.GetMethod("ChooseDecisionState", InstanceMembers)
            ?.Invoke(ai, new object[] { distance, yAligned, inRange, timeSinceAttack });
        Assert.That(state, Is.Not.Null);
        return state.ToString();
    }

    void StartPrivateCoroutine(MonoBehaviour host, Component target, string methodName)
    {
        IEnumerator routine = (IEnumerator)target.GetType()
            .GetMethod(methodName, InstanceMembers)
            ?.Invoke(target, null);
        Assert.That(routine, Is.Not.Null, $"Missing coroutine {methodName}");
        host.StartCoroutine(routine);
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
        public readonly Transform Player;

        public TestContext(Component ai, MonoBehaviour brain, Rigidbody2D body, Transform player)
        {
            Ai = ai;
            Brain = brain;
            Body = body;
            Player = player;
        }
    }
}
