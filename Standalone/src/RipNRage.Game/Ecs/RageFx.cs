using RipNRage.Engine.Ecs;
using RipNRage.Engine.UI;
using RipNRage.Game.Content;

namespace RipNRage.Game.Ecs
{
    /// <summary>
    /// What the rage looks and sounds like, as data on the player: the two screen weights (eased in when a state
    /// starts, cut when it ends — the curve the old post-processing volumes had), and the edges the cues fire on.
    /// Stepped by <see cref="RageFxSystem"/>, drawn by <see cref="RageScreen"/>.
    /// </summary>
    public struct RageFx : IComponent
    {
        /// <summary>0..<see cref="RageFxs.FullWeight"/>: how much of the red / blue grade is on screen.</summary>
        public float EnragedWeight, DepressedWeight;
        /// <summary>The state last tick: entering ENRAGED / DEPRESSED is the activation sting's edge.</summary>
        public RageState LastState;
        /// <summary>Whether each meter was full last tick: filling one is the meter-full cue's edge.</summary>
        public bool FuryWasFull, CalmWasFull;
    }

    /// <summary>The grade, the easing and the cues — one table, and the pure step the system runs.</summary>
    public static class RageFxs
    {
        /// <summary>The old volumes lerped their weight to 0.5 and read that as the whole effect.</summary>
        public const float FullWeight = 0.5f;
        /// <summary>Ease rate per second (the old coroutine's <c>Lerp(w, 0.5, 30·dt)</c>).</summary>
        public const float EaseRate = 30f;

        public static readonly Color EnragedFilter = new Color(1.25f, 0.62f, 0.62f, 1f), EnragedVignette = new Color(1f, 0f, 0f, 1f);
        public static readonly Color DepressedFilter = new Color(0.55f, 0.62f, 0.85f, 1f), DepressedVignette = new Color(0f, 0f, 0.1f, 1f);
        public const float EnragedVignetteIntensity = 0.5f, DepressedVignetteIntensity = 0.6f;
        /// <summary>The chromatic aberration ENRAGED had (DEPRESSED had none).</summary>
        public const float EnragedChromatic = 1f;

        /// <summary>What a tick asks of the speakers.</summary>
        public struct Cues { public bool Enraged, Depressed, FuryFull, CalmFull; }

        /// <summary>
        /// One tick: the active state's weight eases towards full, the other is cut at once (a state that ends cuts
        /// its grade the same frame, as the old <c>OnEnterNormal</c> did); the cues are the edges — a state entered,
        /// a meter filled to the brim while there is a choice to make (Normal).
        /// </summary>
        public static RageFx Step(in RageFx fx, in Rage rage, float dt, out Cues cues)
        {
            var f = fx;
            float k = Mathf.Min(1f, EaseRate * dt);
            f.EnragedWeight = rage.State == RageState.Enraged ? f.EnragedWeight + (FullWeight - f.EnragedWeight) * k : 0f;
            f.DepressedWeight = rage.State == RageState.Depressed ? f.DepressedWeight + (FullWeight - f.DepressedWeight) * k : 0f;

            bool furyFull = rage.Fury >= 1f, calmFull = rage.Calm >= 1f;
            cues = new Cues
            {
                Enraged = rage.State == RageState.Enraged && f.LastState != RageState.Enraged,
                Depressed = rage.State == RageState.Depressed && f.LastState != RageState.Depressed,
                FuryFull = rage.State == RageState.Normal && furyFull && !f.FuryWasFull,
                CalmFull = rage.State == RageState.Normal && calmFull && !f.CalmWasFull,
            };
            f.LastState = rage.State;
            f.FuryWasFull = furyFull;
            f.CalmWasFull = calmFull;
            return f;
        }

        /// <summary>The player's effect weights, for the drawer (outside the ECS).</summary>
        public static bool TryGet(out RageFx fx)
        {
            var w = Application.Current?.World;
            var e = PlayerLink.PlayerEntity;
            if (w != null && !e.IsNull && w.IsAlive(e) && w.Has<RageFx>(e)) { fx = w.Get<RageFx>(e); return true; }
            fx = default;
            return false;
        }
    }

    /// <summary>
    /// Steps <see cref="RageFx"/> off the player's <see cref="Rage"/> and plays the cues: a sting when a state is
    /// entered, a softer one when a meter fills. Runs alone (the audio device).
    /// </summary>
    [UpdateAfter(typeof(RageSystem))]
    [Exclusive] // the audio device
    public sealed class RageFxSystem : SystemBase, ITick
    {
        private Query _players;

        public override void OnCreate() =>
            _players = World.Query().With<RageFx>().WithReadOnly<Rage>().WithReadOnly<PlayerTag>().Build();

        public void Tick(SystemContext ctx)
        {
            foreach (var chunk in _players)
            {
                var fxs = chunk.Get<RageFx>(); var rages = chunk.Get<Rage>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    fxs[i] = RageFxs.Step(in fxs[i], in rages[i], ctx.DeltaTime, out var cues);
                    if (cues.Enraged) Play(ProceduralSounds.RageEnraged, 0.8f);
                    if (cues.Depressed) Play(ProceduralSounds.RageDepressed, 0.8f);
                    if (cues.FuryFull) Play(ProceduralSounds.RageFuryFull, 0.6f);
                    if (cues.CalmFull) Play(ProceduralSounds.RageCalmFull, 0.6f);
                }
            }
        }

        private static void Play(AudioClip clip, float volume)
        {
            if (clip != null) RipNRage.Engine.Ecs.Audio.Sfx.Play(clip, volume);
        }
    }

    /// <summary>
    /// The rage grade on screen (a UI-boundary drawer, called by the HUD): pushed into the post-processing stack
    /// when it is running, otherwise painted as a tint and a vignette. Reads <see cref="RageFx"/>; holds nothing.
    /// Replaces the <c>RagePostprocess</c> MonoBehaviour and its two coroutines.
    /// </summary>
    public static class RageScreen
    {
        public static void Draw(UiBatch ui)
        {
            if (!GraphicsSettings.RageEffectsEnabled) return; // graphics menu: "rage screen effects"
            if (!RageFxs.TryGet(out var fx)) return;
            var stack = Application.Current?.Renderer?.PostFx;
            if (stack != null && !stack.IsActive) stack = null;
            Layer(ui, stack, fx.EnragedWeight, RageFxs.EnragedFilter, RageFxs.EnragedVignette, RageFxs.EnragedVignetteIntensity, RageFxs.EnragedChromatic);
            Layer(ui, stack, fx.DepressedWeight, RageFxs.DepressedFilter, RageFxs.DepressedVignette, RageFxs.DepressedVignetteIntensity, 0f);
        }

        private static void Layer(UiBatch ui, RipNRage.Engine.Graphics.PostFx.PostProcessStack stack, float weight,
                                  Color filter, Color vignette, float vignetteIntensity, float chromatic)
        {
            if (weight <= 0.001f) return;
            float w = Mathf.Clamp01(weight / RageFxs.FullWeight);
            var tint = Color.Lerp(Color.white, filter, w);
            if (stack != null) { stack.AddOverride(vignetteIntensity * w, vignette, chromatic * w, tint); return; }
            ui.FillRectMultiply(new Rect(0, 0, ui.Width, ui.Height), tint);
            ui.DrawVignette(vignette, vignetteIntensity * w);
        }
    }
}
