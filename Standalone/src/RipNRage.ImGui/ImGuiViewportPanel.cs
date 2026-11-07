using System;
using RipNRage.Engine;
using RipNRage.Engine.Graphics;
using RipNRage.Engine.Graphics.PostFx;
using RipNRage.Engine.Platform;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace RipNRage.ImGuiIntegration
{
    /// <summary>
    /// A dockable ImGui panel the engine draws into: the editor's 2D canvas is rendered to an offscreen target and
    /// shown as the panel's image.
    /// <para>
    /// Drawing underneath a transparent ImGui window is not an option — a docked window's background is painted by
    /// its dock node, which cannot be made see-through — so the canvas gets its own framebuffer instead. That also
    /// clips it exactly to the panel and keeps its coordinates independent of where the panel is docked.
    /// </para>
    /// <code>
    /// // while building the UI (inside an ImGui frame)
    /// panel.Declare("Viewport");
    /// // after the engine's UI pass
    /// panel.Render(app.Renderer, (ui, rect) => DrawCanvas(ui, rect), background);
    /// </code>
    /// </summary>
    public sealed class ImGuiViewportPanel : IDisposable
    {
        private RenderTarget _target;
        private readonly string _title;

        public ImGuiViewportPanel(string title = "Viewport") => _title = title;

        /// <summary>Canvas size in pixels (the panel's content area).</summary>
        public int Width { get; private set; }
        public int Height { get; private set; }
        /// <summary>The canvas rectangle to draw into, origin bottom-left — always (0, 0, Width, Height).</summary>
        public Rect Rect => new Rect(0f, 0f, Width, Height);
        /// <summary>True when the pointer is over the panel and no ImGui widget is using it.</summary>
        public bool Hovered { get; private set; }
        /// <summary>Pointer position in canvas pixels, origin bottom-left (only meaningful while hovered).</summary>
        public Vector2 Mouse { get; private set; }
        public bool Visible { get; private set; }

        /// <summary>Declares the panel for this ImGui frame and shows the canvas rendered on the previous call to <see cref="Render"/>.</summary>
        public void Declare(ImGuiController imgui, Window window, ImGuiWindowFlags extraFlags = ImGuiWindowFlags.None)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vec2.Zero);
            Visible = ImGui.Begin(_title, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | extraFlags);
            DeclareInline(imgui, Visible);
            ImGui.End();
            ImGui.PopStyleVar();
        }

        /// <summary>
        /// The canvas alone, filling the rest of the ImGui window the caller has already begun. Use this when the
        /// panel needs widgets of its own above the canvas (the animation editor's transport row).
        /// </summary>
        public void DeclareInline(ImGuiController imgui, bool windowVisible = true)
        {
            Visible = windowVisible;
            if (Visible)
            {
                Vec2 origin = ImGui.GetCursorScreenPos();
                Vec2 avail = ImGui.GetContentRegionAvail();
                float ratio = imgui.PixelRatio;
                Width = Math.Max(1, (int)(avail.X * ratio));
                Height = Math.Max(1, (int)(avail.Y * ratio));

                if (_target != null)
                {
                    // the engine's textures are bottom-up, so V is flipped
                    ImGui.Image((IntPtr)_target.ColorTexture, avail, new Vec2(0f, 1f), new Vec2(1f, 0f));
                    Hovered = ImGui.IsItemHovered();
                }
                else
                {
                    ImGui.Dummy(avail);
                    Hovered = false;
                }

                Vec2 m = ImGui.GetIO().MousePos;
                Mouse = new Vector2((m.X - origin.X) * ratio, Height - (m.Y - origin.Y) * ratio);
            }
            else
            {
                Hovered = false;
            }
        }

        /// <summary>
        /// Runs <paramref name="draw"/> into the panel's offscreen target. Call after the engine's UI pass and before
        /// the ImGui pass — <see cref="RenderPipeline.AfterUi"/> is the right place.
        /// </summary>
        public void Render(RenderPipeline renderer, Action<UiBatch, Rect> draw, Color background, Window window)
        {
            if (!Visible || draw == null) return;
            if (_target == null) _target = new RenderTarget(Width, Height, false, false);
            else _target.Resize(Width, Height);

            _target.Bind();
            GL.Disable(GL.DEPTH_TEST);
            GL.Disable(GL.CULL_FACE);
            GL.Enable(GL.BLEND);
            GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
            GL.ClearColor(background.r, background.g, background.b, background.a);
            GL.Clear(GL.COLOR_BUFFER_BIT);

            var ui = renderer.Ui;
            ui.Begin(Width, Height);
            draw(ui, Rect);
            ui.Flush();

            RenderTarget.BindDefault(window.Width, window.Height);
        }

        public void Dispose()
        {
            _target?.Dispose();
            _target = null;
        }
    }
}
