using UnityEngine;

namespace Harma.Combat
{
public static class StompRules
{
    public static bool IsPlanarContact(
        Vector2 playerGroundPosition,
        Vector2 targetGroundPosition,
        float horizontalTolerance,
        float depthTolerance)
    {
        return Mathf.Abs(playerGroundPosition.x - targetGroundPosition.x) <=
                   Mathf.Max(0f, horizontalTolerance) &&
               Mathf.Abs(playerGroundPosition.y - targetGroundPosition.y) <=
                   Mathf.Max(0f, depthTolerance);
    }

    public static bool IsStompableHeightWhileDescending(
        float previousHeight,
        float currentHeight,
        float targetHeight,
        float tolerance)
    {
        if (currentHeight > previousHeight)
            return false;

        float safeTarget = Mathf.Max(0f, targetHeight);
        float safeTolerance = Mathf.Max(0f, tolerance);
        float lowerBound = Mathf.Max(0f, safeTarget - safeTolerance);
        float upperBound = safeTarget + safeTolerance;

        // Match the pre-refactor feel: while descending, a planar contact is
        // stompable for as long as the player's feet are still above the
        // enemy's upper-body threshold. Keep a swept crossing fallback for a
        // long frame that jumps completely across the threshold band.
        bool stillAboveTarget = currentHeight >= lowerBound;
        bool crossedEntireBand =
            previousHeight > upperBound && currentHeight < lowerBound;
        return stillAboveTarget || crossedEntireBand;
    }
}
}
