using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

public class HudController : MonoBehaviour
{
    [SerializeField] private TMPro.TextMeshProUGUI healthText;
    [SerializeField] private TMPro.TextMeshProUGUI ammoText;
    [SerializeField] private FPSCombat fpsCombat;
    [SerializeField] private PlayerState playerState;

    private void Start()
    {
        UpdateAmmo();
        UpdateHealth();
    }


    private void UpdateHealth()
    {
        healthText.text = playerState.healthComponent.Health.ToString(CultureInfo.CurrentCulture);
    }

    private void UpdateAmmo()
    {
        ammoText.text = fpsCombat.inventoryComponent.GetCurrentWeaponAmmo().ToString();
    }

    public void OnHealthChanged()
    {
        UpdateHealth();
    }

    public void OnAmmoChanged()
    {
        UpdateAmmo();
    }

    public void OnWeaponChanged()
    {
        UpdateAmmo();   
    }


}
