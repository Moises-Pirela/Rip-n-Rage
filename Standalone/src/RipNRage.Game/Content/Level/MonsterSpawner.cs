using RipNRage.Level;

namespace RipNRage.Game.Content.Level
{
    /// <summary>
    /// Wave spawner placed from the editor ("monster_spawner" entity). Keys:
    /// monster (classname, default monster_rifleman), count (total to spawn), interval (seconds),
    /// radius (scatter around the origin), activate_range (0 = starts immediately, otherwise waits
    /// until the player is within that distance), max_alive (cap on simultaneous monsters).
    /// </summary>
    public class MonsterSpawner : MonoBehaviour
    {
        public string monster = "monster_rifleman";
        public int count = 3;
        public float interval = 5f;
        public float radius = 2f;
        public float activateRange = 0f;
        public int maxAlive = 4;
        public float angle;

        private int _spawned;
        public int Spawned { get => _spawned; set => _spawned = value; }
        /// <summary>Index of the spawner's entity in the level file (for saves).</summary>
        public int levelIndex = -1;
        private float _timer;
        private bool _active;
        private readonly System.Collections.Generic.List<GameObject> _alive = new System.Collections.Generic.List<GameObject>();

        public static GameObject Create(MapEntity e)
        {
            var go = new GameObject("monster_spawner");
            go.transform.position = e.origin;
            var s = go.AddComponent<MonsterSpawner>();
            s.monster = e.Get("monster", "monster_rifleman");
            s.count = e.GetInt("count", 3);
            s.interval = e.GetFloat("interval", 5f);
            s.radius = e.GetFloat("radius", 2f);
            s.activateRange = e.GetFloat("activate_range", 0f);
            s.maxAlive = e.GetInt("max_alive", 4);
            s.angle = e.angle;
            return go;
        }

        protected override void Update()
        {
            if (_spawned >= count) return;
            if (!_active)
            {
                if (activateRange <= 0f) _active = true;
                else
                {
                    var player = PlayerState.Current;
                    if (player != null && (player.transform.position - transform.position).magnitude <= activateRange) _active = true;
                }
                if (!_active) return;
                _timer = 0f; // first monster spawns right away
            }
            _alive.RemoveAll(g => !RipNRage.Engine.EngineObject.IsAlive(g));
            _timer -= Time.deltaTime;
            if (_timer > 0f || _alive.Count >= maxAlive) return;
            _timer = interval;
            var def = EntityRegistry.Get(monster);
            if (def?.spawn == null) { Debug.LogWarning("monster_spawner: unknown monster " + monster); _spawned = count; return; }
            Vector2 r = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * radius;
            var e = new MapEntity { classname = monster, origin = transform.position + new Vector3(r.x, 0f, r.y) };
            e.angle = angle;
            var go = def.spawn(e);
            if (go != null) { _alive.Add(go); go.AddComponent<LevelEntityRef>().spawnerIndex = levelIndex; }
            _spawned++;
        }
    }
}
