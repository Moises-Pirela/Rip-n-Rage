using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;

namespace RipNRage.Game.Ecs
{
    /// <summary>A motorised slider that reverses its motor at its limits (phys_slider "patrol 1": pistons, lifts).</summary>
    public struct SliderPatrol : IComponent { public bool Enabled; public Vector3 Origin, Axis; public float Lower, Upper; }

    public sealed class SliderPatrolConfig : ComponentConfig<SliderPatrol>
    {
        [InspectTip("reverse the motor at the limits (needs a motor and limits)")]
        public bool enabled = true;
        public override SliderPatrol Bake() => new SliderPatrol { Enabled = enabled };
    }

    /// <summary>Reads the rail off the joint the patrol rides, where the body ended up (10.4).</summary>
    [EntityFilter(typeof(SliderPatrol), typeof(Joint), typeof(LocalTransform))]
    [Reads(typeof(Joint), typeof(LocalTransform)), Writes(typeof(SliderPatrol))]
    [UpdateAfter(typeof(RipNRage.Engine.Ecs.Physics.StandOnBaseSystem))]
    public sealed class SliderPatrolInitSystem : SystemBase, IOnEntityCreated
    {
        public void OnEntityCreated(SystemContext ctx, Entity e)
        {
            var w = ctx.World;
            var j = w.Get<Joint>(e);
            var t = w.Get<LocalTransform>(e);
            ref var p = ref w.Get<SliderPatrol>(e);
            p.Origin = t.Position;
            p.Axis = (t.Rotation * j.Axis).normalized;
            p.Lower = j.LowerLimit; p.Upper = j.UpperLimit;
            if (!j.UseMotor || !j.UseLimits) p.Enabled = false; // nothing to reverse
        }
    }

    [Reads(typeof(SliderPatrol), typeof(LocalTransform)), Writes(typeof(Joint))]
    public sealed class SliderPatrolSystem : SystemBase, IFixedTick
    {
        private Query _q;
        public override void OnCreate() => _q = World.Query().WithReadOnly<SliderPatrol>().WithReadOnly<LocalTransform>().With<Joint>().Build();

        public void FixedTick(SystemContext ctx)
        {
            foreach (var c in _q)
            {
                var p = c.Get<SliderPatrol>(); var lt = c.Get<LocalTransform>(); var j = c.Get<Joint>();
                for (int i = 0; i < c.Length; i++)
                {
                    if (!c.Includes(i) || !p[i].Enabled) continue;
                    float along = Vector3.Dot(lt[i].Position - p[i].Origin, p[i].Axis);
                    float margin = 0.02f * (p[i].Upper - p[i].Lower);
                    if (along >= p[i].Upper - margin && j[i].MotorSpeed > 0f) j[i].MotorSpeed = -j[i].MotorSpeed;
                    else if (along <= p[i].Lower + margin && j[i].MotorSpeed < 0f) j[i].MotorSpeed = -j[i].MotorSpeed;
                }
            }
        }
    }
}
