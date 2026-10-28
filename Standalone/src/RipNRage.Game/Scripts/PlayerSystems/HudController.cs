using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

// Ported from Assets/_Game/Scripts/PlayerSystems/HudController.cs.
// UnityEngine.UI.Image / TextMeshProUGUI became UiImage / UiText (engine HUD elements) and the
// controller draws them itself through IUiDrawer (the Canvas is gone). The update logic is unchanged.
public class HudController : MonoBehaviour, IUiDrawer
{
    [SerializeField] public UiImage healthBar;
    [SerializeField] public UiImage armorBar;
    [SerializeField] public UiText ammoText;
    [SerializeField] public PlayerState playerState;

    [SerializeField] public UiImage playerPortrait;
    [SerializeField] public UiImage ammoTypeIcon;

    [SerializeField] public PlayerPortraitData _portraitData;
    [SerializeField] public Sprite[] ammoTypeIcons = new Sprite[5];

    private CombatController _combatController;

    public ICrosshair CurrentCrosshair;
    [SerializeField] public GameObject crossHairObject;

    /// <summary>Every HUD element in draw order (background frames first).</summary>
    public readonly List<UiImage> images = new List<UiImage>();
    public readonly List<UiText> texts = new List<UiText>();

    /// <summary>Full-screen tint driven by the rage post-processing stand-in.</summary>
    public RagePostprocess ragePostprocess;

    protected override void Awake()
    {
        EnragedInjector<HudController>.SetInstance(this);
    }

    protected override void Start()
    {
        CurrentCrosshair = crossHairObject.GetComponent<ICrosshair>();
        _combatController = EnragedInjector<CombatController>.GetInstance();

        _combatController.OnAmmoChanged += UpdateAmmo;
        
        playerState.OnHealthChanged += UpdateHealth;
        playerState.OnArmorChanged += UpdateArmor;
        playerState.OnHealthChanged += UpdatePortrait;
        playerState.OnUpdateRage += UpdatePortrait;
        
        UpdateAmmo();
        UpdateArmor();
        UpdateHealth();
        UpdatePortrait();
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
        if (_combatController.inventory.equippedWeapon == null) return;
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

    /// <summary>Draws the canvas: rage tint, then all images and texts in order.</summary>
    public void DrawUi(UiBatch ui)
    {
        ragePostprocess?.Draw(ui);
        foreach (var img in images) img.Draw(ui);
        foreach (var t in texts) t.Draw(ui);
        (CurrentCrosshair as GlockCrosshair)?.Draw(ui);
    }
}
