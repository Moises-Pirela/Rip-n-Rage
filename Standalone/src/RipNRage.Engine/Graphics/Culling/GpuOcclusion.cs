using System;
using System.Collections.Generic;
using RipNRage.Engine.Platform;

namespace RipNRage.Engine.Graphics.Culling
{
    /// <summary>
    /// Hardware occlusion culling: after the opaque world pass has filled the depth buffer, every frustum-visible
    /// mesh's world AABB is drawn as a box (no colour / depth writes) inside an <c>ANY_SAMPLES_PASSED</c> query.
    /// The result is read back the next frame (never stalling the pipeline) and decides whether the mesh is drawn;
    /// unknown = visible, so nothing ever disappears, and a mesh that becomes visible again shows up one frame
    /// later. This replaces the CPU software rasterizer (<see cref="OcclusionBuffer"/>): the GPU does the work it is
    /// built for and the CPU only issues ~one tiny draw per chunk.
    /// </summary>
    public sealed unsafe class GpuOcclusion : IDisposable
    {
        private sealed class State
        {
            public uint query;
            public bool pending;
            public bool visible = true;
            public int lastSeen;
            public int hiddenSince;
        }

        /// <summary>Meshes whose bounds come within this distance of the camera are never culled (the floor you stand on, the wall next to you).</summary>
        public float NearAlwaysVisible = 6f;
        /// <summary>A hidden mesh whose query result has not arrived for this many frames is drawn again (deep GPU queues must not leave holes).</summary>
        public int MaxPendingFrames = 3;

        private readonly Dictionary<MeshRenderer, State> _states = new Dictionary<MeshRenderer, State>();
        private readonly List<MeshRenderer> _stale = new List<MeshRenderer>();
        private readonly Shader _shader;
        private uint _vao, _vbo, _ebo;
        private int _lastSweep;

        /// <summary>Counters of the last frame.</summary>
        public int Tested { get; private set; }
        public int Hidden { get; private set; }
        public int Pending { get; private set; }

        public static bool Available => GL.HasTimerQueries; // same query entry points

        public GpuOcclusion()
        {
            _shader = new Shader(BoxVert, BoxFrag, "occlusion.box");
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            _ebo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            float[] cube =
            {
                0, 0, 0,  1, 0, 0,  1, 1, 0,  0, 1, 0,
                0, 0, 1,  1, 0, 1,  1, 1, 1,  0, 1, 1,
            };
            fixed (float* p = cube) GL.BufferData(GL.ARRAY_BUFFER, cube.Length * sizeof(float), p, GL.STATIC_DRAW);
            uint[] idx =
            {
                0, 1, 2, 0, 2, 3,  4, 6, 5, 4, 7, 6,  0, 4, 5, 0, 5, 1,
                3, 2, 6, 3, 6, 7,  0, 3, 7, 0, 7, 4,  1, 5, 6, 1, 6, 2,
            };
            GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _ebo);
            fixed (uint* p = idx) GL.BufferData(GL.ELEMENT_ARRAY_BUFFER, idx.Length * sizeof(uint), p, GL.STATIC_DRAW);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, GL.FLOAT, false, 3 * sizeof(float), 0);
            GL.BindVertexArray(0);
        }

        private State Get(MeshRenderer mr)
        {
            if (!_states.TryGetValue(mr, out var st)) { st = new State(); _states[mr] = st; }
            return st;
        }

        /// <summary>Visibility decision for this frame from the last finished query (unknown = visible).</summary>
        public bool IsVisible(MeshRenderer mr, int frame)
        {
            var st = Get(mr);
            // back in the frustum after being away: the stored answer is stale → draw it and test afresh
            if (frame - st.lastSeen > 1) { st.visible = true; st.pending = false; }
            st.lastSeen = frame;
            if (!st.visible && frame - st.hiddenSince > MaxPendingFrames && st.pending) st.visible = true;
            return st.visible;
        }

        /// <summary>
        /// Call with the depth buffer of the opaque pass bound: reads back finished queries, then issues a box query
        /// for every candidate (frustum-visible mesh). Leaves colour / depth writes enabled again.
        /// </summary>
        public void IssueQueries(List<MeshRenderer> candidates, in Matrix4x4 viewProj, Vector3 camPos, float nearPlane, int frame)
        {
            Tested = 0; Hidden = 0; Pending = 0;
            if (candidates.Count == 0) { Sweep(frame); return; }

            GL.ColorMask(false, false, false, false);
            GL.DepthMask(false);
            GL.Enable(GL.DEPTH_TEST);
            GL.DepthFunc(GL.LEQUAL);
            GL.Disable(GL.CULL_FACE);
            GL.Disable(GL.BLEND);
            _shader.Use();
            _shader.SetMatrix("uViewProj", viewProj);
            GL.BindVertexArray(_vao);

            Vector3 pad = RenderPipeline.BoundsPadding + Vector3.one * (0.25f + nearPlane * 2f);
            foreach (var mr in candidates)
            {
                var st = Get(mr);
                if (st.pending)
                {
                    if (!GL.QueryResultAvailable(st.query)) { Pending++; if (!st.visible) Hidden++; continue; }
                    bool vis = GL.QueryResult64(st.query) != 0;
                    if (!vis && st.visible) st.hiddenSince = frame;
                    st.visible = vis;
                    st.pending = false;
                }
                if (!RendererBounds.GetWorldAABB(mr, out var min, out var max)) { st.visible = true; continue; }
                min -= pad; max += pad;
                // camera inside or right next to the box: never cull (the ground under the player, the wall beside it)
                float dx = camPos.x < min.x ? min.x - camPos.x : camPos.x > max.x ? camPos.x - max.x : 0f;
                float dy = camPos.y < min.y ? min.y - camPos.y : camPos.y > max.y ? camPos.y - max.y : 0f;
                float dz = camPos.z < min.z ? min.z - camPos.z : camPos.z > max.z ? camPos.z - max.z : 0f;
                if (dx * dx + dy * dy + dz * dz < NearAlwaysVisible * NearAlwaysVisible) { st.visible = true; continue; }
                if (!st.visible) Hidden++;
                if (st.query == 0) st.query = GL.GenQuery();
                _shader.SetVector("uMin", min);
                _shader.SetVector("uMax", max);
                GL.BeginQuery(GL.ANY_SAMPLES_PASSED, st.query);
                GL.DrawElements(GL.TRIANGLES, 36, GL.UNSIGNED_INT, 0);
                GL.EndQuery(GL.ANY_SAMPLES_PASSED);
                st.pending = true;
                Tested++;
            }

            GL.BindVertexArray(0);
            GL.ColorMask(true, true, true, true);
            GL.DepthMask(true);
            Sweep(frame);
        }

        /// <summary>Drops the state (and query object) of renderers not seen for ten seconds (destroyed / far away).</summary>
        private void Sweep(int frame)
        {
            if (frame - _lastSweep < 600) return;
            _lastSweep = frame;
            _stale.Clear();
            foreach (var kv in _states)
                if (frame - kv.Value.lastSeen > 600 || kv.Key.IsDestroyed) _stale.Add(kv.Key);
            foreach (var mr in _stale)
            {
                if (_states[mr].query != 0) GL.DeleteQuery(_states[mr].query);
                _states.Remove(mr);
            }
        }

        /// <summary>Forget every result (level change).</summary>
        public void Reset()
        {
            foreach (var st in _states.Values) if (st.query != 0) GL.DeleteQuery(st.query);
            _states.Clear();
        }

        public void Dispose()
        {
            Reset();
            _shader.Dispose();
            GL.DeleteVertexArray(_vao); GL.DeleteBuffer(_vbo); GL.DeleteBuffer(_ebo);
        }

        private const string BoxVert = @"#version 330 core
layout(location = 0) in vec3 aPos;
uniform mat4 uViewProj; uniform vec3 uMin; uniform vec3 uMax;
void main() { gl_Position = uViewProj * vec4(mix(uMin, uMax, aPos), 1.0); }";

        private const string BoxFrag = @"#version 330 core
out vec4 FragColor;
void main() { FragColor = vec4(1.0); }";
    }
}
