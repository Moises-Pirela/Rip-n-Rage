using System;
using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/Pickups/AmmoPickup.cs (trigger callback via ITriggerListener).
public class AmmoPickup : MonoBehaviour, IPickup, ITriggerListener
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
        if (!_combatController.inventory.AddToAmmo(ammoType, ammoCount)) return;
        
        _combatController.OnAmmoChanged?.Invoke();
        Destroy(gameObject);
    }

    public void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Player"))
            OnPickup();
    }

    public void OnTriggerExit(Collider other) { }
}
