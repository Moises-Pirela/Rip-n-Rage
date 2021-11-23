using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

public class HudController : MonoBehaviour
{
    [SerializeField] private Image healthBar;
    [SerializeField] private Image armorBar;
    [SerializeField] private TMPro.TextMeshProUGUI ammoText;
    [SerializeField] private PlayerState playerState;

    [SerializeField] private Image playerPortrait;
    [SerializeField] private Image ammoTypeIcon;

    [SerializeField] private PlayerPortraitData _portraitData;
    [SerializeField] private Sprite[] ammoTypeIcons = new Sprite[5];

    private CombatController _combatController;

    public ICrosshair CurrentCrosshair;
    [SerializeField]private GameObject crossHairObject;

    private void Awake()
    {
        EnragedInjector<HudController>.SetInstance(this);
    }

    private void Start()
    {
        CurrentCrosshair = crossHairObject.GetComponent<ICrosshair>();
        _combatController = EnragedInjector<CombatController>.GetInstance();

        _combatController.OnEquipWeapon += UpdateAmmo;
        _combatController.OnFireWeapon += UpdateAmmo;
        
        playerState.OnHealthChanged += UpdateHealth;
        playerState.OnArmorChanged += UpdateArmor;
        playerState.OnHealthChanged += UpdatePortrait;
        playerState.OnTickRage += UpdatePortrait;
        
        UpdateAmmo();
        UpdateArmor();
        UpdateHealth();
    }


    private void UpdateHealth()
    {
        float healthPercentage = 0;

        healthPercentage = Mathf.Clamp((playerState.Health * 0.5f) / 100, 0 , 0.5f);

        healthBar.fillAmount = healthPercentage;
    }

    private void UpdateArmor()
    {
        float armorPercentage = 0;

        armorPercentage = Mathf.Clamp((playerState.Armor * 0.5f) / 100, 0 , 0.5f);

        armorBar.fillAmount = armorPercentage;
    }

    private void UpdateAmmo()
    {
        int currentAmmoIndex = (int)_combatController.inventory.equippedWeapon.weaponData.ammoType;
        ammoText.text = _combatController.inventory.ammo[currentAmmoIndex].ToString();

       int ammoType = (int) _combatController.inventory.equippedWeapon.weaponData.ammoType;
       ammoTypeIcon.sprite = ammoTypeIcons[ammoType];
    }

    private void UpdatePortrait()
    {
        int playerDamageIndex = 0;
        Sprite newPortrait = null;

        if (playerState.Health == 0)
        {
            playerPortrait.sprite = _portraitData.deathPortrait;
            return;
        }

        if (playerState.Health <= 80 && playerState.Health > 60)
        {
            playerDamageIndex = 1;
        }
        else if (playerState.Health <= 60 && playerState.Health > 40)
        {
            playerDamageIndex = 2;
        }
        else if (playerState.Health <= 40 && playerState.Health > 20)
        {
            playerDamageIndex = 3;
        }
        else if (playerState.Health <= 20 && playerState.Health > 0)
        {
            playerDamageIndex = 4;
        }

        switch (playerState.RageState)
        {
            case PlayerRageState.ENRAGED:
                newPortrait = _portraitData.enragedPortrait[playerDamageIndex];
                break;
            case PlayerRageState.DEPRESSED:
                newPortrait = _portraitData.depressedPortrait[playerDamageIndex];
                break;
            case PlayerRageState.NORMAL:
            case PlayerRageState.NONE:
                newPortrait = playerState.CombatState == PlayerCombatState.VIOLENT
                    ? _portraitData.inCombatPortrait[playerDamageIndex]
                    : _portraitData.defaultPortrait[playerDamageIndex];
                break;
        }
        
        playerPortrait.sprite = newPortrait;
    }

    public void OnRageTicked()
    {
        
    }

    public void OnWeaponChanged()
    {
        UpdateAmmo();   
    }


}
