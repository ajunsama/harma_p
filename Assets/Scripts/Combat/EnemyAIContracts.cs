using UnityEngine;

namespace Harma.Combat
{
/// <summary>
/// Receives the current player target without requiring spawners to know the
/// concrete enemy brain type.
/// </summary>
public interface IPlayerTargetReceiver
{
    void SetPlayerTarget(Transform target);
}

/// <summary>
/// Exposes the active attack window used by enemy hit detection.
/// </summary>
public interface IEnemyAttackState
{
    bool IsAttackActive { get; }
    float AttackLaneTolerance { get; }
}

/// <summary>
/// Lets the shared enemy health component pause and resume a brain during a
/// hit reaction without depending on its concrete implementation.
/// </summary>
public interface IEnemyHitReactionReceiver
{
    void SetHitReactionActive(bool active);
}

/// <summary>
/// Represents an enemy that can be hit while the player descends through its
/// virtual height. Ground position stays on the X/Y gameplay plane.
/// </summary>
public interface IStompTarget
{
    Transform StompTransform { get; }
    float StompHeight { get; }
    bool CanReceiveStomp { get; }
    void ReceiveStomp(Vector2 sourceGroundPosition);
}
}
