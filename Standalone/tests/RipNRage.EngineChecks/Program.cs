using System;
using RipNRage.Engine;

// No third-party test runner needed. Any failed assertion terminates with exit code 1.
internal static class Program
{
    private static int _assertions;

    private static void Equal(float expected, float actual, string test)
    {
        _assertions++;
        if (MathF.Abs(expected - actual) > 0.0001f || !float.IsFinite(actual))
            throw new Exception($"{test}: expected {expected}, got {actual}");
    }

    private static void True(bool condition, string test)
    {
        _assertions++;
        if (!condition) throw new Exception(test);
    }

    private static int Main()
    {
        try
        {
            // Bounded input handling: clipping never leaves [minimum, maximum].
            Equal(0f, Mathf.Clamp(-100f, 0f, 1f), "Clamp below lower bound");
            Equal(1f, Mathf.Clamp(100f, 0f, 1f), "Clamp above upper bound");
            Equal(0.4f, Mathf.Clamp(0.4f, 0f, 1f), "Clamp in range");
            Equal(0f, Mathf.Clamp01(-0.1f), "Clamp01 below zero");
            Equal(1f, Mathf.Clamp01(1.1f), "Clamp01 above one");

            // Interpolation must not extrapolate for the clamped variant.
            Equal(10f, Mathf.Lerp(10f, 20f, -2f), "Lerp lower bound");
            Equal(20f, Mathf.Lerp(10f, 20f, 2f), "Lerp upper bound");
            Equal(15f, Mathf.Lerp(10f, 20f, 0.5f), "Lerp midpoint");

            // Movement must not overshoot its target, including when descending.
            Equal(3f, Mathf.MoveTowards(0f, 10f, 3f), "MoveTowards positive step");
            Equal(10f, Mathf.MoveTowards(8f, 10f, 3f), "MoveTowards no overshoot");
            Equal(7f, Mathf.MoveTowards(10f, 0f, 3f), "MoveTowards negative step");
            Equal(0f, Mathf.MoveTowards(2f, 0f, 3f), "MoveTowards negative no overshoot");

            // Angular wrap-around takes the shortest path.
            Equal(20f, Mathf.DeltaAngle(350f, 10f), "DeltaAngle wraps forward");
            Equal(-20f, Mathf.DeltaAngle(10f, 350f), "DeltaAngle wraps backward");
            True(Mathf.Approximately(1f, 1f + 0.0000001f), "Approximately small delta");
            Console.WriteLine($"PASS: {_assertions} bounded engine math assertions");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex.Message);
            return 1;
        }
    }
}
