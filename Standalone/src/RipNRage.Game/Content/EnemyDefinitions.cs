using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        /// <summary>Called when a definition file changed on disk and the table was rebuilt (the editor re-previews).</summary>
        public static Action OnReloaded;

        private static readonly Dictionary<string, DateTime> _stamps = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static float _nextPoll;

        /// <summary>
        /// Re-reads the definitions that changed since the last look, and notices new ones (as the prefabs are
        /// watched, 10.7). Returns how many files were re-read. The table keeps its order — see <see cref="Merge"/>.
        /// </summary>
        public static int PollReload(Func<string, Texture2D> loadTexture, float interval = 0.5f)
        {
            if (Time.unscaledTime < _nextPoll) return 0;
            _nextPoll = Time.unscaledTime + interval;
            string dir = Folder;
            if (dir == null || !System.IO.Directory.Exists(dir)) return 0;
            int changed = 0;
            foreach (var file in System.IO.Directory.GetFiles(dir, "*.json"))
            {
                DateTime stamp;
                try { stamp = File.GetLastWriteTimeUtc(file); } catch { continue; }
                if (_stamps.TryGetValue(file, out var known) && known == stamp) continue;
                _stamps[file] = stamp;
                changed++;
            }
            if (changed == 0) return 0;
            RipNRage.Game.Ecs.AI.Enemies.All = Merge(RipNRage.Game.Ecs.AI.Enemies.All, Load(loadTexture, dir));
            OnReloaded?.Invoke();
            return changed;
        }

        /// <summary>
        /// The reloaded table, with every kind that already existed **in the slot it already had**. <c>Enemy.Kind</c>
        /// is an index and it is stored on live entities: renumbering the table mid-session would repoint every
        /// grunt in the level at another definition. New kinds go on the end; a kind whose file has gone keeps its
        /// slot with the numbers it last had, because something may still be standing on it.
        /// </summary>
        public static EnemyData[] Merge(EnemyData[] current, EnemyData[] loaded)
        {
            var result = new List<EnemyData>(current ?? Array.Empty<EnemyData>());
            for (int i = 0; i < result.Count; i++)
            {
                var fresh = loaded.FirstOrDefault(d => string.Equals(d.classname, result[i].classname, StringComparison.OrdinalIgnoreCase));
                if (fresh != null) result[i] = fresh;
            }
            foreach (var d in loaded)
                if (!result.Any(x => string.Equals(x.classname, d.classname, StringComparison.OrdinalIgnoreCase))) result.Add(d);
            return result.ToArray();
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
