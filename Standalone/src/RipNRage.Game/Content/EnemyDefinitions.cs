using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RipNRage.Game.Ecs.AI;

namespace RipNRage.Game.Content
{
    /// <summary>
    /// The enemy kinds, read from <c>enemies/*.json</c> (11.15). A file is the whole of an <see cref="EnemyData"/> —
    /// one source of truth per kind, where before the rifleman's numbers were a block of code in
    /// <c>GameAssets</c> and its classname the only thing a map could say. The folder is found like the prefabs'
    /// (next to the assets, or <c>RIPNRAGE_ENEMIES</c>); files load in name order, so kinds are stable across runs.
    /// </summary>
    public static class EnemyDefinitions
    {
        /// <summary>One sheet as written: frame count, sides, frames per second, and the texture under the assets root.</summary>
        public sealed class SheetFile { public int frames = 1, sides = 1, fps = 1; public string texture; }

        /// <summary>The file's shape: <see cref="EnemyData"/>'s fields, the sheets by role, the offset as three numbers.</summary>
        public sealed class EnemyFile
        {
            public string name, classname;
            public float movementSpeed, attackRange, meleeAttackRange, turnSpeed, fireRate;
            public bool melee; public float meleeDamage;
            public float attackWindup = 0.35f; public int burstCount = 3; public float burstInterval = 0.09f, staggerTime = 0.25f, alertDelay = 0.2f;
            public float wanderWait = 1f, wanderRadius = 10f, wanderMinDistance = 4f, arriveDistance = 2f;
            public float sightRange = 10f, sightFov = 180f, memory = 5f, noise = 30f;
            public float[] muzzleOffset = { 0f, 0.225f, 0.45f };
            public float maxHealth, maxArmor;
            public string skeleton = "";
            public Dictionary<string, SheetFile> sheets = new Dictionary<string, SheetFile>(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true, IncludeFields = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        };

        /// <summary>Override the folder (tools and checks); null = $RIPNRAGE_ENEMIES, else found next to the assets.</summary>
        public static string Directory = Environment.GetEnvironmentVariable("RIPNRAGE_ENEMIES");

        public static string Folder => Directory ?? Locate();

        private static string Locate()
        {
            foreach (var start in new[] { Application.dataPath, System.IO.Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var d = new DirectoryInfo(start);
                while (d != null)
                {
                    string c = Path.Combine(d.FullName, "enemies");
                    if (System.IO.Directory.Exists(c)) return c;
                    d = d.Parent;
                }
            }
            return null;
        }

        /// <summary>Reads every file in the folder into <paramref name="loadTexture"/>-backed definitions, in name order.</summary>
        public static EnemyData[] Load(Func<string, Texture2D> loadTexture, string folder = null)
        {
            folder ??= Folder;
            if (folder == null || !System.IO.Directory.Exists(folder)) return Array.Empty<EnemyData>();
            var files = System.IO.Directory.GetFiles(folder, "*.json");
            Array.Sort(files, StringComparer.Ordinal);
            var list = new List<EnemyData>(files.Length);
            foreach (var f in files)
            {
                // a bad file is a warning and a missing kind, as a bad prefab is (10.7's policy) - not a level that will not load
                try
                {
                    var file = JsonSerializer.Deserialize<EnemyFile>(File.ReadAllText(f), Options);
                    if (file == null || string.IsNullOrEmpty(file.classname)) throw new InvalidDataException("an enemy definition needs a classname");
                    list.Add(Bake(file, loadTexture));
                }
                catch (Exception e) { Debug.LogWarning($"enemies: {Path.GetFileName(f)} does not load: {e.Message}"); }
            }
            return list.ToArray();
        }

        /// <summary>The definition a file describes.</summary>
        public static EnemyData Bake(EnemyFile f, Func<string, Texture2D> loadTexture)
        {
            AnimationSheet Sheet(string role)
            {
                if (!f.sheets.TryGetValue(role, out var s) || string.IsNullOrEmpty(s.texture)) return default;
                return new AnimationSheet { AnimationFrames = s.frames, Sides = s.sides, AnimationFramesPerSecond = s.fps, TextureSheet = loadTexture?.Invoke(s.texture) };
            }
            var m = f.muzzleOffset != null && f.muzzleOffset.Length == 3 ? new Vector3(f.muzzleOffset[0], f.muzzleOffset[1], f.muzzleOffset[2]) : new Vector3(0f, 0.225f, 0.45f);
            return new EnemyData
            {
                name = f.name ?? f.classname, classname = f.classname,
                movementSpeed = f.movementSpeed, attackRange = f.attackRange, meleeAttackRange = f.meleeAttackRange, turnSpeed = f.turnSpeed, fireRate = f.fireRate,
                melee = f.melee, meleeDamage = f.meleeDamage,
                attackWindup = f.attackWindup, burstCount = f.burstCount, burstInterval = f.burstInterval, staggerTime = f.staggerTime, alertDelay = f.alertDelay,
                wanderWait = f.wanderWait, wanderRadius = f.wanderRadius, wanderMinDistance = f.wanderMinDistance, arriveDistance = f.arriveDistance,
                sightRange = f.sightRange, sightFov = f.sightFov, memory = f.memory, noise = f.noise,
                muzzleOffset = m, maxHealth = f.maxHealth, maxArmor = f.maxArmor, skeleton = f.skeleton ?? "",
                enemyAnimationData = new EnemyAnimationData
                {
                    IdleAnimation = Sheet("idle"), AttackAnimation = Sheet("attack"), MoveAnimation = Sheet("move"),
                    DeathAnimation = Sheet("death"), DeadAnimation = Sheet("dead"),
                },
            };
        }
    }
}
