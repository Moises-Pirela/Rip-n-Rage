using System;

// Gameplay-side checks (layer 5 onwards): Health / damage, weapons, inventory — the components and systems that live
// in RipNRage.Game and therefore cannot be reached from EngineChecks, which only references the engine.
// Same harness as EngineChecks: no third-party runner, any failed assertion terminates with exit code 1.
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
            Layer5Checks.Run(True, Equal);
            Layer6Checks.Run(True, Equal);
            Console.WriteLine($"PASS: {_assertions} gameplay assertions");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex.Message);
            return 1;
        }
    }
}
