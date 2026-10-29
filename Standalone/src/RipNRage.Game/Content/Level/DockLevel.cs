using System.Collections.Generic;
using RipNRage.Level;

namespace RipNRage.Game.Content.Level
{
    /// <summary>
    /// Writes the built-in dock test area as a .map file (levels/dock.map): the TrenchBroom dock
    /// brushes from dock.obj (imported like Unity: X mirrored, 0.25 units/unit, y -3.09), crates as
    /// box brushes, and the props/pickups/enemies/spawn as entities. Editable with the level editor.
    /// </summary>
    public static class DockLevel
    {
        public static MapDocument Create()
        {
            var doc = new MapDocument();
            doc.worldspawn.Set("message", "Rip-n-Rage dock test area");
            doc.worldspawn.Set("sun_elevation", "28");
            doc.worldspawn.Set("sun_azimuth", "35");

            string objPath = Application.AssetPath("Levels/dock.obj");
            foreach (var b in MapFile.ImportObjBrushes(objPath, TestArea.MapScale, true, TestArea.DockOffset, "planks", 8f))
                doc.Brushes.Add(b);

            float floor = TestArea.DockOffset.y + 16f * TestArea.MapScale; // 0.91
            void Crate(Vector3 basePos, float size, string tex)
            {
                var min = basePos - new Vector3(size * 0.5f, 0, size * 0.5f);
                doc.Brushes.Add(Brush.Box(min, min + Vector3.one * size, tex, new Vector2(size, size)));
            }
            Crate(new Vector3(6f, floor, -34f), 2.2f, "orange");
            Crate(new Vector3(8.4f, floor, -35.5f), 1.6f, "orange");
            Crate(new Vector3(7.2f, floor + 2.2f, -34.6f), 1.4f, "orange");
            Crate(new Vector3(-9f, floor, -52f), 2.6f, "green");
            Crate(new Vector3(-5f, floor, -80f), 2f, "purple");
            Crate(new Vector3(10f, floor, -100f), 2.4f, "red");
            // a sloped ramp to show off convex (non axis-aligned) brushes
            var ramp = Brush.Box(new Vector3(-15f, floor, -70f), new Vector3(-9f, floor + 2f, -62f), "grid", new Vector2(2f, 2f));
            ramp.Rotate(Quaternion.Euler(0, 0, -18f), new Vector3(-12f, floor, -66f));
            doc.Brushes.Add(ramp);

            MapEntity E(string cls, Vector3 origin, float angle = 0f, params (string, string)[] kv)
            {
                var e = new MapEntity { classname = cls, origin = origin };
                if (angle != 0f) e.angle = angle;
                foreach (var (k, v) in kv) e.Set(k, v);
                doc.entities.Add(e);
                return e;
            }

            E("info_player_start", new Vector3(0f, floor + 0.7f, 8f), 180f);
            foreach (float z in new[] { -28f, -62f, -96f })
                foreach (float x in new[] { -13.5f, 13.5f })
                    E("lamp_post", new Vector3(x, floor, z));
            E("item_ammo_shell", new Vector3(-3f, floor, -20f));
            E("item_ammo_heavy", new Vector3(3f, floor, -40f));
            E("item_ammo_light", new Vector3(-4f, floor, -60f));
            E("item_ammo_explosive", new Vector3(4f, floor, -90f));
            E("monster_rifleman", new Vector3(-5f, floor, -16f), 0f);
            E("monster_rifleman", new Vector3(2f, floor, -75f), 180f);
            E("monster_rifleman", new Vector3(-6f, floor, -110f), 180f);
            E("monster_rifleman", new Vector3(60f, floor, -105f), 90f);
            E("water_plane", new Vector3(474.6f, -0.5f, 0f), 0f, ("size", "1732 1000"));
            E("floor_plane", new Vector3(474.6f, -15.5f, 0f), 0f, ("size", "1732 1000"), ("texture", "sand"));
            E("trigger_checkpoint", new Vector3(0f, floor + 2f, -50f), 0f, ("size", "30 4 4"));
            return doc;
        }

        public static void Export(string path)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".");
            MapFile.Save(Create(), path);
            Debug.LogWarning("Wrote " + path);
        }
    }
}
