using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AmmoPickup : MonoBehaviour, IPickup
{
    [SerializeField] private int ammoCount;
    [SerializeField] private AmmoType ammoType;
    [SerializeField] private Sprite[] ammoTypeSprite;
    [SerializeField] private SpriteRenderer pickupSpriteRenderer;

    private CombatController _combatController;

    private void Start()
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

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Player"))
            OnPickup();
    }
}
