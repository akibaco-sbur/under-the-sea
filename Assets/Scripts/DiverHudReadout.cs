using UnityEngine;

internal static class DiverHudReadout
{
    static readonly string[] Directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
    internal static float Heading(Vector3 forward, float previous)
    {
        if (forward.x * forward.x + forward.z * forward.z < 0.00001f) return previous;
        return Mathf.Repeat(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 360f);
    }
    internal static string HeadingText(float heading)
    {
        int degrees = Mathf.RoundToInt(Mathf.Repeat(heading, 360f)) % 360;
        string direction = Directions[Mathf.RoundToInt(degrees / 45f) % 8];
        return direction + "   " + degrees.ToString("000") + "°";
    }
    internal static string MovementText(bool available, bool moving, float speed)
        => !available ? "移動入力を待っています" : speed <= 0.005f ? (moving ? "前進を開始" : "停止中") : moving ? "前進中" : "減速中";

    internal static string ViewDirection(Vector3 forward, Vector3 bodyForward)
    {
        Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
        Vector3 body = Vector3.ProjectOnPlane(bodyForward, Vector3.up).normalized;
        float pitch = Mathf.Atan2(forward.y, flat.magnitude) * Mathf.Rad2Deg;
        float yaw = flat.sqrMagnitude > .00001f && body.sqrMagnitude > .00001f
            ? Vector3.SignedAngle(body, flat, Vector3.up) : 0f;
        string horizontal = yaw > 22.5f ? "右" : yaw < -22.5f ? "左" : "";
        if (Mathf.Abs(yaw) > 135f) horizontal = "後ろ";
        string vertical = pitch > 20f ? "上" : pitch < -20f ? "下" : "";
        return horizontal.Length == 0 && vertical.Length == 0 ? "正面" : horizontal + vertical;
    }
}
