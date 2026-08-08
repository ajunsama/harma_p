using System;
using UnityEngine;

[Serializable]
public readonly struct PlayerGameplaySignal
{
    public readonly string id;
    public readonly float value;
    public readonly Vector2 position;
    public readonly GameObject source;

    public PlayerGameplaySignal(string id, float value, Vector2 position, GameObject source)
    {
        this.id = id;
        this.value = value;
        this.position = position;
        this.source = source;
    }
}

public static class PlayerGameplaySignals
{
    public const string MoveStarted = "move_started";
    public const string MoveStopped = "move_stopped";
    public const string JumpStarted = "jump_started";
    public const string Landed = "landed";
    public const string Damaged = "damaged";
    public const string Died = "died";
}

/// <summary>
/// Player-local event hub used by reactive environment actors and custom gameplay scripts.
/// </summary>
public sealed class PlayerGameplaySignalHub : MonoBehaviour
{
    public event Action<PlayerGameplaySignal> SignalPublished;

    public void Publish(string signalId, float value = 0f, GameObject source = null)
    {
        if (string.IsNullOrWhiteSpace(signalId)) return;
        SignalPublished?.Invoke(new PlayerGameplaySignal(
            signalId,
            value,
            transform.position,
            source != null ? source : gameObject));
    }
}
