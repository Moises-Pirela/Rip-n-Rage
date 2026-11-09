using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;

namespace RipNRage.Game.Ecs
{
    /// <summary>A motorised slider that reverses its motor at its limits (phys_slider "patrol 1": pistons, lifts).</summary>
    public struct SliderPatrol : IComponent { public Vector3 Origin, Axis; public float Lower, Upper; }

    public sealed class SliderPatrolConfig : ComponentConfig<SliderPatrol> { public override SliderPatrol Bake() => default; }

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
                    if (!c.Includes(i)) continue;
                    float along = Vector3.Dot(lt[i].Position - p[i].Origin, p[i].Axis);
                    float margin = 0.02f * (p[i].Upper - p[i].Lower);
                    if (along >= p[i].Upper - margin && j[i].MotorSpeed > 0f) j[i].MotorSpeed = -j[i].MotorSpeed;
                    else if (along <= p[i].Lower + margin && j[i].MotorSpeed < 0f) j[i].MotorSpeed = -j[i].MotorSpeed;
                }
            }
        }
    }
}
