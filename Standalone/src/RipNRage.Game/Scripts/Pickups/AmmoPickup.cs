using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Pickups/AmmoPickup.cs. The trigger is an ECS Trigger entity owned by a
// TriggerVolume; GameTriggerBridge relays its overlaps to the callbacks below.
public class AmmoPickup : MonoBehaviour, RipNRage.Game.Ecs.ITriggerListener
{
    [SerializeField] public int ammoCount;
    [SerializeField] public AmmoType ammoType;
    [SerializeField] public Sprite[] ammoTypeSprite;
    [SerializeField] public SpriteRenderer pickupSpriteRenderer;

    private CombatController _combatController;

    protected override void Start()
    {
        _combatController = EnragedInjector<CombatController>.GetInstance();

        int ammoIndex = (int) ammoType;
        pickupSpriteRenderer.sprite = ammoTypeSprite[ammoIndex];
    }

    public void OnPickup()
    {
        if (!_combatController.AddAmmo(ammoType, ammoCount)) return;
        
        _combatController.OnAmmoChanged?.Invoke();
        Destroy(gameObject);
    }

    public void OnTriggerEnter(GameObject other)
    {
        if (other.CompareTag("Player"))
            OnPickup();
    }

    public void OnTriggerExit(GameObject other) { }
}
