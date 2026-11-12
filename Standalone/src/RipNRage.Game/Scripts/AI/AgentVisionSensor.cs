using RipNRage.Engine.Ecs;
using RipNRage.Game.Ecs;
using RipNRage.Game.Ecs.AI;

// Ported from Assets/_AI_BehaviorTree/Scripts/AgentVisionSensor.cs. Uncalled since 2.L (the sensor volume lost its
// collider); 6.3 replaces it with a sight query. Until then it writes the brain's target instead of the blackboard.
public class AgentVisionSensor : MonoBehaviour
{
    [SerializeField] public AIState _state;

    public void OnTriggerEnter(GameObject other)
    {
        if (!other.CompareTag("Player")) return;

        // corpses are not targets (standalone addition)
        var player = other.GetComponent<PlayerState>();
        if (player != null && player.IsDead) return;

        var w = Application.Current.World;
        if (!w.IsAlive(_state.entity) || !w.Has<AiBrain>(_state.entity)) return;
        ref var brain = ref w.Get<AiBrain>(_state.entity);
        bool first = brain.Target.IsNull;
        brain.Target = PlayerLink.PlayerEntity;
        if (first) _state.GetComponent<AIAgent>()?.OnAlert(); // readable "noticed you" reaction before engaging
    }

    public void OnTriggerExit(GameObject other) { }
}
