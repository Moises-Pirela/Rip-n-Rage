using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

public class HudController : MonoBehaviour
{
    [SerializeField] private TMPro.TextMeshProUGUI healthText;
    [SerializeField] private TMPro.TextMeshProUGUI armorText;
    [SerializeField] private TMPro.TextMeshProUGUI ammoText;
    [SerializeField] private Image rageMeterFill; 
    [SerializeField] private FPSCombat fpsCombat;
    [SerializeField] private PlayerState playerState;

    private void Start()
    {
        UpdateAmmo();
        UpdateArmor();
        UpdateHealth();
        UpdateRageMeter();
    }

    private void UpdateRageMeter()
    {
        rageMeterFill.fillAmount = playerState.Rage;
    }


    private void UpdateHealth()
    {
        healthText.text = playerState.Health.ToString();
    }

    private void UpdateArmor()
    {
        armorText.text = playerState.Armor.ToString();
    }

    private void UpdateAmmo()
    {
        ammoText.text = fpsCombat.inventoryComponent.GetCurrentWeaponAmmo().ToString();
    }

    public void OnHealthChanged()
    {
        UpdateHealth();
    }

    public void OnArmorChanged()
    {
        UpdateArmor();
    }

    public void OnAmmoChanged()
    {
        UpdateAmmo();
    }

    public void OnRageTicked()
    {
        UpdateRageMeter();
    }

    public void OnWeaponChanged()
    {
        UpdateAmmo();   
    }


}
