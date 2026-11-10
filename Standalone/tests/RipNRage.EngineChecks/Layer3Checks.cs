using System;
using System.Collections.Generic;
using RipNRage.Engine;
using RipNRage.Engine.Animation;
using RipNRage.Engine.Audio;
using RipNRage.Engine.Destruction;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Animation;
using RipNRage.Engine.Ecs.Audio;
using RipNRage.Engine.Ecs.Destruction;
using RipNRage.Engine.Ecs.Effects;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Graphics;
using RagdollOptions = RipNRage.Engine.Ecs.Destruction.RagdollOptions;

[DisableAutoCreation]
public sealed class Layer3EventLog : SystemBase, IConsumes<AnimationEvent>, IConsumes<RagdollPieceDetachedEvent>
{
    public readonly List<string> Events = new List<string>();
    public readonly List<Entity> Detached = new List<Entity>();
    public void Consume(SystemContext ctx, ReadOnlySpan<AnimationEvent> events) { foreach (var e in events) Events.Add(e.Function); }
    public void Consume(SystemContext ctx, ReadOnlySpan<RagdollPieceDetachedEvent> events) { foreach (var e in events) Detached.Add(e.Piece); }
}

/// <summary>Layer 3: sprite animation, audio, effects and sprite ragdolls on the ECS.</summary>
internal static class Layer3Checks
{
    public static void Run(Action<bool, string> True)
    {
        using var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly);
        w.FixedDeltaTime = 1f / 60f;
        var log = w.AddSystem(new Layer3EventLog());
        void Step(int n, float dt = 1f / 60f) { for (int i = 0; i < n; i++) w.Update(dt); }

        // ---- animation: states, a bool-driven transition, a trigger, clip events, enabled keys
        var s0 = new Sprite(null, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 100f, "idle0");
        var s1 = new Sprite(null, new Rect(8, 0, 8, 8), new Vector2(0.5f, 0.5f), 100f, "fire0");
        var s2 = new Sprite(null, new Rect(16, 0, 8, 8), new Vector2(0.5f, 0.5f), 100f, "fire1");
        var idle = new SpriteAnimationClip("t_idle", 1f, loop: true).AddFrame(0f, s0);
        var fire = new SpriteAnimationClip("t_fire", 0.2f).AddFrame(0f, s1).AddFrame(0.1f, s2).AddEvent(0.05f, "OnFire").AddEnabled(0.15f, false);
        var c = new AnimatorController("t_ctrl");
        c.clips["t_idle"] = idle; c.clips["t_fire"] = fire;
        c.AddParameter("IsFiring", AnimatorParameterType.Bool).AddParameter("Jab", AnimatorParameterType.Trigger);
        var stIdle = c.AddState("Idle", "t_idle"); var stFire = c.AddState("Fire", "t_fire");
        var toFire = stIdle.AddTransition(stFire); toFire.conditions.Add(new AnimatorCondition { parameter = "IsFiring", mode = AnimatorConditionMode.If });
        var back = stFire.AddTransition(stIdle); back.hasExitTime = true; back.exitTime = 1f;
        var jab = c.AddAnyStateTransition(stFire); jab.conditions.Add(new AnimatorCondition { parameter = "Jab", mode = AnimatorConditionMode.If });
        AnimationLibrary.Register(c);

        var e = w.CreateEntity();
        w.Add(e, LocalTransform.At(Vector3.zero));
        w.Add(e, new SpriteRender { Color = Color.white });
        w.Add(e, new SpriteAnimatorConfig { controller = "t_ctrl" }.Bake());
        Step(2);
        True(w.Get<SpriteAnimator>(e).StateName == "Idle" && ReferenceEquals(RenderAssets.Sprites.Resolve(w.Get<SpriteRender>(e).Sprite), s0), "the animator enters the default state and shows its frame");
        w.Get<SpriteAnimator>(e).SetBool("IsFiring", true);
        Step(1); w.Get<SpriteAnimator>(e).SetBool("IsFiring", false);
        Step(5); // 5 frames ≈ 0.083 s into Fire: past the event (0.05) and the second frame (0.1 not yet)
        True(w.Get<SpriteAnimator>(e).StateName == "Fire" && log.Events.Contains("OnFire"), "a bool transition plays the next state and posts its clip event");
        Step(4); // ≈ 0.15 s: the enabled key switches the sprite off
        True(w.Get<SpriteRender>(e).Disabled && ReferenceEquals(RenderAssets.Sprites.Resolve(w.Get<SpriteRender>(e).Sprite), s2), "clip enabled keys and later frames apply");
        Step(10);
        True(w.Get<SpriteAnimator>(e).StateName == "Idle", "an exit-time transition returns to idle");
        w.Get<SpriteAnimator>(e).SetTrigger("Jab");
        Step(2);
        True(w.Get<SpriteAnimator>(e).StateName == "Fire" && !w.Get<SpriteAnimator>(e).IsTriggerSet("Jab"), "an any-state trigger fires once and is consumed");
        w.Add(e, new Hidden()); float tBefore = w.Get<SpriteAnimator>(e).Time;
        Step(3);
        True(w.Get<SpriteAnimator>(e).Time == tBefore, "hidden animators are paused");

        // ---- audio: one-shots through the event path (no device here: counted, not played)
        var clip = AudioClip.Create("t_beep", new short[480], 48000);
        var audio = w.GetSystem<AudioSystem>();
        int before = audio.Played;
        w.Post(new PlaySoundEvent { Clip = AudioAssets.Clips.Intern(clip), Volume = 1f, Pitch = 1f });
        w.Post(new PlaySoundEvent { Clip = default, Volume = 1f }); // no clip: ignored
        Step(1);
        True(audio.Played == before + 1, "PlaySoundEvents are consumed by the AudioSystem (unknown clips ignored)");

        // ---- effects: spawn events for unknown names are counted, never thrown
        var fx = w.GetSystem<EffectSystem>();
        w.Post(new SpawnEffectEvent { Effect = "no_such_effect", Position = Vector3.zero, Forward = Vector3.forward, Scale = 1f });
        Step(1);
        True(fx.Unknown >= 1, "spawning an unknown effect is counted, not fatal");

        // ---- sprite ragdoll: a 3-part skeleton (torso, head, arm) sliced into jointed pieces that fall and stay jointed
        var floor = w.CreateEntity(); w.Add(floor, LocalTransform.At(new Vector3(0, -0.5f, 50)));
        w.Add(floor, new ColliderShape { Kind = ShapeKind.Box, Size = new Vector3(40, 1, 40), IsStatic = true });
        w.Add(floor, new CollisionFilter { Layer = Layers.Walkable, Mask = ~0, UseLayerMatrix = true });
        var skel = new SpriteSkeleton { name = "t_skel", depth = 0.2f, lifetime = 3f };
        var view = new SkeletonView();
        view.parts.Add(Part("torso", "", 0.5f, 0.55f, 0.3f, 0.7f, 0.3f, 0.75f, 10f));
        view.parts.Add(Part("head", "torso", 0.5f, 0.85f, 0.4f, 0.75f, 0.6f, 0.95f, 3f));
        view.parts.Add(Part("arm", "torso", 0.75f, 0.7f, 0.7f, 0.4f, 0.85f, 0.72f, 2f, capsule: true));
        skel.views.Add(view);
        var body = new Sprite(null, new Rect(0, 0, 100, 200), new Vector2(0.5f, 0.0f), 100f, "t_body");
        var opts = RagdollOptions.Default; opts.lifetime = 3f;
        opts.impulse = new Vector3(5, 0, 0); opts.impulsePoint = new Vector3(0, 1.7f, 50);
        var pieces = SpriteRagdolls.Spawn(w, skel, body, new Vector3(0, 0.5f, 50), Vector3.one, new Vector3(0, 1, 40), opts);
        True(pieces.Count == 3, "every part with a polygon becomes a piece");
        Step(2);
        int joints = 0; foreach (var p in pieces) if (w.Has<Joint>(p)) joints++;
        True(joints == 2 && w.Has<Body>(pieces[0]) && w.Has<PhysicsHandle>(pieces[0]) && !w.Has<PendingImpulse>(pieces[1]), "child parts are ball-jointed to their parent; the blow was applied once the bodies existed");
        Step(90);
        var tp = w.Get<LocalTransform>(pieces[0]).Position; var hp = w.Get<LocalTransform>(pieces[1]).Position;
        True(tp.y < 1.2f && tp.y > -0.1f && (hp - tp).magnitude < 0.8f, $"the ragdoll falls onto the floor and the head stays attached (torso y={tp.y:0.00}, head gap={(hp - tp).magnitude:0.00})");
        SpriteRagdolls.Detach(w, pieces[2]);
        Step(2);
        True(!w.Has<Joint>(pieces[2]) && log.Detached.Contains(pieces[2]) && w.GetSystem<JointSystem>().ActiveJoints >= 1, "Detach tears a piece off and reports it; the other joint stays");
        float alphaEarly = w.Get<SpriteRender>(pieces[0]).Color.a;
        Step(80); // ≈ 2.9 s of life: fading
        True(alphaEarly == 1f && w.Get<SpriteRender>(pieces[0]).Color.a < 0.5f, "pieces fade over their last second");
        Step(20);
        True(!w.IsAlive(pieces[0]) && !w.IsAlive(pieces[1]), "and are removed at the end of their lifetime");
    }

    private static SkeletonPart Part(string name, string parent, float px, float py, float x0, float y0, float x1, float y1, float mass, bool capsule = false)
    {
        var p = new SkeletonPart { name = name, parent = parent, pivot = new Vector2(px, py), mass = mass, shape = capsule ? PartShape.Capsule : PartShape.Box };
        p.polygon.Add(new Vector2(x0, y0)); p.polygon.Add(new Vector2(x1, y0)); p.polygon.Add(new Vector2(x1, y1)); p.polygon.Add(new Vector2(x0, y1));
        return p;
    }
}
