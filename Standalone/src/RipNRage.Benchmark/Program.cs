using System;
using System.Collections.Generic;
using System.Globalization;
using RipNRage.Engine;
using RipNRage.Game.Content;

namespace RipNRage.Benchmark
{
    /// <summary>
    /// Rip-n-Rage physics / engine benchmark (separate executable, no game logic).
    ///
    ///   RipNRageBenchmark [--scenario crates|ground|all] [--duration s] [--warmup s]
    ///                     [--scale f] [--crates N] [--ragdolls N] [--projectiles N] [--runners N]
    ///                     [--no-render] [--fixed-dt s] [--budget-frame ms] [--budget-physics ms]
    ///                     [--report out.md] [--csv out.csv] [--strict] [--assets dir] [--gpu auto|high|software]
    ///
    /// Runs the scenarios one after another in one window — a colliding crate pile, sprite destruction ragdolls,
    /// a stream of swept projectiles and fast characters on a rough mesh — measures frame / physics / scene /
    /// render times, counts (bodies, contacts, queries, allocations) and prints a table with a PASS / FAIL per
    /// scenario against the time budgets. <c>--strict</c> makes a failed budget the exit code (CI).
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var options = new ApplicationOptions { Title = "Rip-n-Rage Benchmark", Width = 1280, Height = 720, Mute = true, VSync = false };
            var cfg = new BenchmarkConfig();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Missing value for " + a);
                float F() => float.Parse(Next(), CultureInfo.InvariantCulture);
                int I() => int.Parse(Next(), CultureInfo.InvariantCulture);
                switch (a)
                {
                    case "--scenario": case "-s": cfg.scenarios = Next().ToLowerInvariant(); break;
                    case "--duration": case "-d": cfg.duration = F(); break;
                    case "--warmup": cfg.warmup = F(); break;
                    case "--scale": cfg.scale = F(); break;
                    case "--crates": cfg.crates = I(); break;
                    case "--ragdolls": cfg.ragdolls = I(); break;
                    case "--projectiles": cfg.projectiles = I(); break;
                    case "--runners": cfg.runners = I(); break;
                    case "--threads": cfg.threads = I(); break;
                    case "--no-render": options.NoRender = true; break;
                    case "--no-postfx": options.DisablePostFx = true; break;
                    case "--fixed-dt": options.FixedFrameDelta = F(); break;
                    case "--budget-frame": cfg.budgetFrameMs = F(); break;
                    case "--budget-physics": cfg.budgetPhysicsMs = F(); break;
                    case "--report": cfg.reportPath = Next(); break;
                    case "--csv": cfg.csvPath = Next(); break;
                    case "--strict": cfg.strict = true; break;
                    case "--assets": options.AssetRoot = Next(); break;
                    case "--skeletons": GameAssets.SkeletonsPath = Next(); break;
                    case "--tunables": options.TunablesPath = Next(); break;
                    case "--width": options.Width = I(); break;
                    case "--height": options.Height = I(); break;
                    case "--vsync": options.VSync = true; break;
                    case "--gpu": options.Gpu = Next().ToLowerInvariant() switch { "high" => Engine.Platform.GpuPreference.High, "software" => Engine.Platform.GpuPreference.AllowSoftware, _ => Engine.Platform.GpuPreference.Auto }; break;
                    case "--verbose": options.Verbose = true; break;
                    case "--help": case "-h":
                        Console.WriteLine("RipNRageBenchmark [--scenario crates|ground|all] [--duration s] [--warmup s] [--scale f]");
                        Console.WriteLine("                  [--crates N] [--ragdolls N] [--projectiles N] [--runners N] [--no-render] [--fixed-dt s]");
                        Console.WriteLine("                  [--budget-frame ms] [--budget-physics ms] [--report out.md] [--csv out.csv] [--strict] [--threads N]");
                        return 0;
                    default:
                        if (a.StartsWith("--")) { Console.WriteLine("Unknown option " + a); return 1; }
                        cfg.scenarios = a.ToLowerInvariant(); break;
                }
            }
            if (options.NoRender) options.DisablePostFx = true;

            try
            {
                using var app = new Application(options);
                GameAssets.Load();
                Materials.Load();
                Physics.SetCollisionMatrix(BenchmarkScene.CollisionMatrix);
                if (cfg.threads >= 0) { Physics.World.Rigid.threads = cfg.threads; PhysicsSettings.Current.threads = cfg.threads; }
                var go = new GameObject("Benchmark");
                var runner = go.AddComponent<BenchmarkRunner>();
                runner.config = cfg;
                runner.scenarios = BenchmarkScene.Select(cfg.scenarios);
                if (runner.scenarios.Count == 0) { Console.WriteLine("No such scenario: " + cfg.scenarios); return 1; }
                app.Run();
                return cfg.strict && runner.Failed ? 3 : 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Fatal: " + e);
                return 2;
            }
        }
    }

    /// <summary>Everything the command line can set.</summary>
    public sealed class BenchmarkConfig
    {
        public string scenarios = "all";
        /// <summary>Measured seconds per scenario (after the warm-up).</summary>
        public float duration = 12f;
        public float warmup = 3f;
        /// <summary>Multiplies every count (2 = twice as many of everything).</summary>
        public float scale = 1f;
        public int crates = 1000;
        public int ragdolls = 500;
        public int projectiles = 1000;
        public int runners = 200;
        /// <summary>Rigid solver threads (-1 = leave tunables.cfg's value; 0 = one per core; 1 = single-threaded).</summary>
        public int threads = -1;
        public float budgetFrameMs = 16.7f;
        public float budgetPhysicsMs = 8f;
        public string reportPath;
        public string csvPath;
        public bool strict;

        public int Count(int n) => Math.Max(1, (int)MathF.Round(n * scale));
    }
}
