using System;
using System.Collections.Generic;
using RipNRage.Engine.Platform;

namespace RipNRage.Engine.Graphics
{
    /// <summary>
    /// Immediate-mode world-space lines (grids, wireframes, gizmos). Submit every frame; drawn by
    /// the render pipeline after the world (depth tested) or as an overlay (no depth test).
    /// </summary>
    public static unsafe class DebugDraw
    {
        private static readonly List<float> Depth = new List<float>();
        private static readonly List<float> Overlay = new List<float>();
        private static Shader _shader;
        private static uint _vao, _vbo;
        private static int _capacity;

        public static void Line(Vector3 a, Vector3 b, Color c, bool depthTest = true)
        {
            var list = depthTest ? Depth : Overlay;
            list.Add(a.x); list.Add(a.y); list.Add(a.z); list.Add(c.r); list.Add(c.g); list.Add(c.b); list.Add(c.a);
            list.Add(b.x); list.Add(b.y); list.Add(b.z); list.Add(c.r); list.Add(c.g); list.Add(c.b); list.Add(c.a);
        }

        public static void Box(Vector3 min, Vector3 max, Color c, bool depthTest = true)
        {
            Vector3[] p =
            {
                new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z),
            };
            for (int i = 0; i < 4; i++)
            {
                Line(p[i], p[(i + 1) % 4], c, depthTest);
                Line(p[4 + i], p[4 + (i + 1) % 4], c, depthTest);
                Line(p[i], p[4 + i], c, depthTest);
            }
        }

        public static void Polygon(IList<Vector3> poly, Color c, bool depthTest = true)
        {
            for (int i = 0; i < poly.Count; i++) Line(poly[i], poly[(i + 1) % poly.Count], c, depthTest);
        }

        public static void Cross(Vector3 p, float size, Color c, bool depthTest = true)
        {
            Line(p - Vector3.right * size, p + Vector3.right * size, c, depthTest);
            Line(p - Vector3.up * size, p + Vector3.up * size, c, depthTest);
            Line(p - Vector3.forward * size, p + Vector3.forward * size, c, depthTest);
        }

        public static void Clear() { Depth.Clear(); Overlay.Clear(); }

        internal static void Render(Matrix4x4 viewProj)
        {
            if (Depth.Count == 0 && Overlay.Count == 0) return;
            EnsureResources();
            _shader.Use();
            _shader.SetMatrix("uViewProj", viewProj);
            GL.Disable(GL.CULL_FACE);
            GL.Enable(GL.BLEND);
            GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
            GL.DepthMask(false);
            if (Depth.Count > 0) { GL.Enable(GL.DEPTH_TEST); Flush(Depth); }
            if (Overlay.Count > 0) { GL.Disable(GL.DEPTH_TEST); Flush(Overlay); }
            GL.DepthMask(true);
            GL.BindVertexArray(0);
            Clear();
        }

        private static void Flush(List<float> data)
        {
            float[] arr = data.ToArray();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            if (arr.Length > _capacity)
            {
                _capacity = Math.Max(arr.Length, _capacity * 2);
                GL.BufferData(GL.ARRAY_BUFFER, _capacity * sizeof(float), null, GL.DYNAMIC_DRAW);
            }
            fixed (float* p = arr) GL.BufferSubData(GL.ARRAY_BUFFER, 0, arr.Length * sizeof(float), p);
            GL.DrawArrays(GL.LINES, 0, arr.Length / 7);
        }

        private static void EnsureResources()
        {
            if (_shader != null) return;
            _shader = new Shader(@"#version 330 core
layout(location = 0) in vec3 aPos; layout(location = 1) in vec4 aColor;
uniform mat4 uViewProj; out vec4 vColor;
void main() { vColor = aColor; gl_Position = uViewProj * vec4(aPos, 1.0); }", @"#version 330 core
in vec4 vColor; out vec4 FragColor; void main() { FragColor = vColor; }", "debugline");
            _vao = GL.GenVertexArray(); _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            _capacity = 7 * 4096;
            GL.BufferData(GL.ARRAY_BUFFER, _capacity * sizeof(float), null, GL.DYNAMIC_DRAW);
            GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, GL.FLOAT, false, 7 * sizeof(float), 0);
            GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 4, GL.FLOAT, false, 7 * sizeof(float), 3 * sizeof(float));
            GL.BindVertexArray(0);
        }
    }
}
