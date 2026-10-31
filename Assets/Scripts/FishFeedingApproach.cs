using UnityEngine;

/// <summary>World-space geometry for approaching food from the viewer's front.</summary>
internal static class FishFeedingApproach
{
    internal static Vector3 Forward(Vector3 viewForward)
    {
        Vector3 flat = Vector3.ProjectOnPlane(viewForward, Vector3.up);
        return flat.sqrMagnitude > 0.00001f ? flat.normalized : Vector3.forward;
    }

    internal static bool InFront(Vector3 mouth, Vector3 food, Vector3 forward)
        => Vector3.Dot(mouth - food, forward) >= 0f;

    internal static bool PreferCandidate(bool inFront, float distance, bool hasCandidate, bool bestInFront, float bestDistance)
        => !hasCandidate || (inFront != bestInFront ? inFront : distance < bestDistance);

    internal static float Side(Vector3 mouth, Vector3 viewer, Vector3 forward)
        => Vector3.Dot(mouth - viewer, Vector3.Cross(Vector3.up, forward)) < 0f ? -1f : 1f;

    internal static Vector3 SideWaypoint(Vector3 viewer, Vector3 forward, Vector3 food,
        float side, float clearance, float rearDepth)
    {
        Vector3 point = viewer + Vector3.Cross(Vector3.up, forward) * (side * clearance) + forward * rearDepth;
        point.y = food.y;
        return point;
    }

    internal static Vector3 FrontWaypoint(Vector3 food, Vector3 forward, float distance)
        => food + forward * distance;

    internal static Vector3 SideFrontWaypoint(Vector3 viewer, Vector3 forward, Vector3 food,
        float side, float clearance, float frontDistance)
    {
        float depth = Vector3.Dot(food - viewer, forward) + frontDistance;
        return SideWaypoint(viewer, forward, food, side, clearance, depth);
    }

    internal static bool CanBite(Vector3 mouth, Vector3 food, Vector3 forward, Vector3 swimmingForward, float biteDistance)
        => InFront(mouth, food, forward) && (mouth - food).sqrMagnitude <= biteDistance * biteDistance &&
            Vector3.Dot(swimmingForward.normalized, -forward) >= 0.25f;
}
