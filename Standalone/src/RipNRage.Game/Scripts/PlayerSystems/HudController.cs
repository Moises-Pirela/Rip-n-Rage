using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using RipNRage.Engine.UI;

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
        var data = _combatController.CurrentWeaponData;
        if (data == null) return;
        ammoText.text = _combatController.AmmoOf(data.ammoType).ToString();
        ammoTypeIcon.sprite = ammoTypeIcons[(int)data.ammoType];
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

        RipNRage.Game.Ecs.Rages.TryGet(out var rage);
        switch (rage.State)
        {
            case RipNRage.Game.Ecs.RageState.Enraged:
                newPortrait = _portraitData.enragedPortrait[playerDamageIndex];
                break;
            case RipNRage.Game.Ecs.RageState.Depressed:
                newPortrait = _portraitData.depressedPortrait[playerDamageIndex];
                break;
            default:
                // "in combat" was the VIOLENT combat state, which the two meters replaced: a filled Fury meter is
                // the nearest thing left to "this player is in a fight"
                newPortrait = rage.Fury > 0f
                    ? _portraitData.inCombatPortrait[playerDamageIndex]
                    : _portraitData.defaultPortrait[playerDamageIndex];
                break;
        }
        
        playerPortrait.sprite = newPortrait;
    }

    /// <summary>The rage meters are a component with no change event: the portrait is repainted every frame.</summary>
    private RipNRage.Game.Ecs.RageState _lastRageState = RipNRage.Game.Ecs.RageState.Normal;
    private bool _lastHadFury;
    protected override void Update()
    {
        RipNRage.Game.Ecs.Rages.TryGet(out var rage);
        bool hasFury = rage.Fury > 0f;
        if (rage.State == _lastRageState && hasFury == _lastHadFury) return;
        _lastRageState = rage.State; _lastHadFury = hasFury;
        UpdatePortrait();
    }

    public void OnWeaponChanged()
    {
        UpdateAmmo();   
    }

    /// <summary>Draws the canvas: rage tint, then all images and texts in order.</summary>
    private readonly ClayUi _clay = new ClayUi(512);

    protected override void OnDestroy() => _clay.Dispose();

    /// <summary>
    /// The two rage meters as plain bars (FURY red, CALM blue), bright once there is something to spend and
    /// pulsing while their state runs. Placeholder for tuning: layer 8's HUD pass designs the real thing.
    /// </summary>
    private void DrawRageMeters(UiBatch ui)
    {
        if (!RipNRage.Game.Ecs.Rages.TryGet(out var rage)) return;
        float s = ui.Scale;
        float w = 220f * s, h = 12f * s, x = 150f * s, y = 118f * s, gap = 6f * s; // bottom left, right of the portrait
        DrawMeter(ui, new Rect(x, y, w, h), rage.Fury, new Color(0.9f, 0.25f, 0.2f, 1f),
                  rage.State == RipNRage.Game.Ecs.RageState.Enraged);
        DrawMeter(ui, new Rect(x, y + h + gap, w, h), rage.Calm, new Color(0.35f, 0.55f, 0.95f, 1f),
                  rage.State == RipNRage.Game.Ecs.RageState.Depressed);
    }

    private static void DrawMeter(UiBatch ui, Rect box, float value, Color color, bool active)
    {
        ui.FillRect(box, new Color(0f, 0f, 0f, 0.45f));
        if (value > 0f)
        {
            // active: the bar pulses while its state drains, so the read-out says "spending", not just "full"
            float pulse = active ? 0.75f + 0.25f * Mathf.Sin(Time.time * 8f) : (value >= 1f ? 1f : 0.7f);
            ui.FillRect(new Rect(box.x, box.y, box.width * Mathf.Clamp01(value), box.height),
                        new Color(color.r * pulse, color.g * pulse, color.b * pulse, 1f));
        }
    }

    public void DrawUi(UiBatch ui)
    {
        ragePostprocess?.Draw(ui); // full-screen tint, not layout
        DrawRageMeters(ui);

        // every HUD element is a floating Clay element pinned to the screen (anchors / pivots from HudLayout);
        // the z index keeps the prefab's draw order (frames first)
        var c = _clay;
        c.PointerEnabled = false; // the HUD never takes the mouse
        c.Begin(ui);
        short z = 1;
        foreach (var img in images) img.Build(c, z++);
        foreach (var t in texts) t.Build(c, z++);
        // no crosshair on a corpse (the death screen takes over the centre of the screen)
        if (playerState == null || !playerState.IsDead)
        {
            var cross = CurrentCrosshair as GlockCrosshair;
            if (cross != null)
            {
                cross.spread = _combatController != null ? _combatController.CurrentSpread : 0f;
                cross.Build(c, z);
            }
        }
        c.End();
    }
}
