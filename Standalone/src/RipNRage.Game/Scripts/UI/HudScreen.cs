using System.Collections.Generic;
using RipNRage.Engine.UI;
using RipNRage.Game.Ecs;
using RipNRage.Game.Ecs.Combat;

/// <summary>
/// The in-game HUD (8.7; replaces the <c>HudController</c> MonoBehaviour): health and armour as the portrait ring's
/// two radial fills, the portrait, the ammo counter and icon, the two rage meters and the crosshair. A plain
/// <see cref="IUiScreen"/> that reads the player's components every frame — no change events, no
/// <c>PlayerState</c>, no injector; built by the player prefab, closed by the scene reset.
/// </summary>
public sealed class HudScreen : IUiScreen, IUiLayer
{
    public int UiLayer => UiLayers.Default;

    /// <summary>The live HUD (the weapon and hit systems flash the crosshair through it).</summary>
    public static HudScreen Current { get; private set; }

    // the elements HudLayout builds (draw order: frames first)
    public readonly List<UiImage> images = new List<UiImage>();
    public readonly List<UiText> texts = new List<UiText>();
    public UiImage healthBar, armorBar, playerPortrait, ammoTypeIcon;
    public UiText ammoText;
    public Sprite[] ammoTypeIcons = new Sprite[5];
    public PlayerPortraitData portraitData;
    public GlockCrosshair Crosshair;

    private readonly ClayUi _clay = new ClayUi(512);

    public HudScreen() => Current = this;

    public void Close()
    {
        _clay.Dispose();
        if (Current == this) Current = null;
    }

    // ---------------------------------------------------------------- the pure reads (pinned by the layer-8 checks)

    /// <summary>Health / armour to radial fill: each value owns half of the portrait ring.</summary>
    public static float BarFill(float value) => Mathf.Clamp(value * 0.5f / 100f, 0f, 0.5f);

    /// <summary>How beaten up the portrait looks: 0 unhurt, 1..4 at 80 / 60 / 40 / 20 health.</summary>
    public static int PortraitDamageIndex(float health) =>
        health > 80f ? 0 : health > 60f ? 1 : health > 40f ? 2 : health > 20f ? 3 : 4;

    /// <summary>The portrait for a health and a rage: dead, enraged, depressed, in a fight (any Fury banked — the
    /// nearest thing left to the old VIOLENT combat state), else calm, each in its damage column.</summary>
    public static Sprite Portrait(in PlayerPortraitData faces, float health, in Rage rage)
    {
        if (health <= 0f) return faces.deathPortrait;
        int i = PortraitDamageIndex(health);
        return rage.State switch
        {
            RageState.Enraged => faces.enragedPortrait[i],
            RageState.Depressed => faces.depressedPortrait[i],
            _ => rage.Fury > 0f ? faces.inCombatPortrait[i] : faces.defaultPortrait[i],
        };
    }

    // ---------------------------------------------------------------- the frame

    private int _lastAmmo = -1, _lastAmmoType = -1;

    public void Update()
    {
        var w = Application.Current?.World;
        var p = PlayerLink.PlayerEntity;
        if (w == null || p.IsNull || !w.IsAlive(p) || !w.Has<Health>(p)) return;

        float health = w.Get<Health>(p).Current;
        healthBar.fillAmount = BarFill(health);
        armorBar.fillAmount = BarFill(w.Has<Armor>(p) ? w.Get<Armor>(p).Current : 0f);

        Rages.TryGet(out var rage);
        playerPortrait.sprite = Portrait(in portraitData, health, in rage);

        if (w.Has<Weapon>(p) && w.Has<Inventory>(p) && Weapons.All.Length > 0)
        {
            var data = Weapons.Data(w.Get<Weapon>(p).Slot);
            int type = (int)data.ammoType;
            int ammo = w.Get<Inventory>(p).Ammo[type];
            if (ammo != _lastAmmo) { ammoText.text = ammo.ToString(); _lastAmmo = ammo; } // the string only when it changed
            if (type != _lastAmmoType) { ammoTypeIcon.sprite = ammoTypeIcons[type]; _lastAmmoType = type; }
        }
    }

    // ---------------------------------------------------------------- the draw (unchanged from HudController)

    /// <summary>
    /// The two rage meters as plain bars (FURY red, CALM blue), bright once there is something to spend and
    /// pulsing while their state runs.
    /// </summary>
    private static void DrawRageMeters(UiBatch ui)
    {
        if (!Rages.TryGet(out var rage)) return;
        float s = ui.Scale;
        float w = 220f * s, h = 12f * s, x = 150f * s, y = 118f * s, gap = 6f * s; // bottom left, right of the portrait
        DrawMeter(ui, new Rect(x, y, w, h), rage.Fury, new Color(0.9f, 0.25f, 0.2f, 1f), rage.State == RageState.Enraged);
        DrawMeter(ui, new Rect(x, y + h + gap, w, h), rage.Calm, new Color(0.35f, 0.55f, 0.95f, 1f), rage.State == RageState.Depressed);
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
        RageScreen.Draw(ui); // the rage grade: full screen, not layout
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
        var phase = Flows.Current.Phase;
        if (Crosshair != null && phase != FlowPhase.Dying && phase != FlowPhase.Dead)
        {
            var w = Application.Current?.World;
            var p = PlayerLink.PlayerEntity;
            Crosshair.spread = w != null && !p.IsNull && w.IsAlive(p) && w.Has<Weapon>(p) && Weapons.All.Length > 0
                ? Weapons.CurrentSpread(in w.Get<Weapon>(p), Vector3.zero, true)
                : 0f;
            Crosshair.Build(c, z);
        }
        c.End();
    }
}
