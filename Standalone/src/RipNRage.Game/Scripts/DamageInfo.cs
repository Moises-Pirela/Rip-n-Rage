// Standalone addition: the circumstances of the damage currently being dealt (hit point, direction, how hard),
// reported by the weapons right before EntityState.TakeDamage so that whatever dies inside that call (the
// ragdoll slicer) knows where the blow landed and how to throw the pieces. Consumed once.
public static class DamageInfo
{
    public static bool Available { get; private set; }
    public static Vector3 Point { get; private set; }
    /// <summary>Direction the blow travels (unit).</summary>
    public static Vector3 Direction { get; private set; }
    /// <summary>Impulse the blow carries (N·s).</summary>
    public static float Impulse { get; private set; }
    public static bool Explosive { get; private set; }
    public static float ExplosionRadius { get; private set; }

    public static void Report(Vector3 point, Vector3 direction, float impulse, bool explosive = false, float explosionRadius = 0f)
    {
        Available = true; Point = point; Direction = direction.sqrMagnitude > 1E-06f ? direction.normalized : Vector3.forward;
        Impulse = impulse; Explosive = explosive; ExplosionRadius = explosionRadius;
    }

    public static void Clear() => Available = false;
}
