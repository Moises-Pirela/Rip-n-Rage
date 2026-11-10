using RipNRage.Engine.UI;
using RipNRage.Engine.UI.Clay;
using System;
using RipNRage.Game.Content;

/// <summary>
/// Style meter (standalone addition): kills, headshots and weapon variety in quick succession fill a meter that
/// ranks F → D → C → B → A → S. Points decay after a pause; switching weapons between kills inside the combo
/// window raises a multiplier (up to x4). The meter stays invisible at F/D/C and slides in once B is reached.
/// At S the player regenerates health, moves faster (<see cref="SpeedMultiplier"/>, read by FPSController) and
/// generates ammo for every type. Lives on the player prefab; weapons call <see cref="RegisterHit"/>.
/// </summary>
public class StyleMeter : MonoBehaviour, IUiDrawer, IUiLayer
{
    public int UiLayer => UiLayers.Hud + 1;
    public static StyleMeter Current { get; private set; }

    public enum Rank { F, D, C, B, A, S }
    public static readonly string[] RankNames = { "FORGETTABLE", "DULL", "CRUDE", "BRUTAL", "ATROCIOUS", "SAVAGE" };
    public static readonly float[] Thresholds = { 0f, 120f, 320f, 620f, 1050f, 1600f };
    public float maxPoints = 2000f;

    [Header("Scoring")]
    public int hitPoints = 6;
    public int headshotHitPoints = 25;
    public int killPoints = 120;
    public int headshotKillBonus = 100;
    public float comboWindow = 6f;
    public int maxMultiplier = 4;

    [Header("Decay")]
    public float decayDelay = 2.5f;
    public float decayRate = 70f;

    [Header("S rank perks")]
    public float sRegenPerSecond = 6f;
    public float sSpeedMultiplier = 1.25f;
    public float sAmmoInterval = 0.5f;
    public int sAmmoAmount = 1;

    public float Points { get; private set; }
    public int Multiplier { get; private set; } = 1;
    public Rank CurrentRank { get; private set; } = Rank.F;
    public bool Visible => _visibility > 0.01f;

    /// <summary>Movement speed multiplier read by the FPS controller (1 unless the meter is at S).</summary>
    public static float SpeedMultiplier => Current != null && !Current.IsDestroyed && Current.CurrentRank == Rank.S ? Current.sSpeedMultiplier : 1f;

    private PlayerState _state;
    private CombatController _combat;
    private string _lastKillWeapon;
    private float _lastKillTime = -100f, _lastScoreTime = -100f;
    private float _visibility, _rankPop, _ammoTimer, _flash;
    private string _lastEvent = "";

    protected override void Awake() { Current = this; }
    protected override void OnDestroy() { if (Current == this) Current = null; _clay.Dispose(); }

    protected override void Start()
    {
        _state = GetComponent<PlayerState>();
        _combat = GetComponent<CombatController>();
    }

    /// <summary>Called by weapons: a hit on an enemy, optionally a headshot, optionally the killing blow.</summary>
    public void RegisterHit(string weaponName, bool headshot, bool killed)
    {
        if (_state != null && _state.IsDead) return;
        float pts = hitPoints + (headshot ? headshotHitPoints : 0f);
        float now = Time.time;
        if (killed)
        {
            if (now - _lastKillTime > comboWindow) Multiplier = 1;                               // combo expired
            else if (!string.IsNullOrEmpty(_lastKillWeapon) && weaponName != _lastKillWeapon)      // variety: different weapon, quick succession
                Multiplier = Math.Min(maxMultiplier, Multiplier + 1);
            pts += (killPoints + (headshot ? headshotKillBonus : 0f)) * Multiplier;
            _lastKillWeapon = weaponName;
            _lastKillTime = now;
            _lastEvent = headshot ? "HEADSHOT KILL" : "KILL";
            if (Multiplier > 1) _lastEvent += $"  X{Multiplier}";
            _flash = 1f;
        }
        else if (headshot) { _lastEvent = "HEADSHOT"; _flash = 0.6f; }
        Points = Mathf.Min(maxPoints, Points + pts);
        _lastScoreTime = now;
        UpdateRank();
    }

    private void UpdateRank()
    {
        Rank r = Rank.F;
        for (int i = Thresholds.Length - 1; i >= 0; i--) if (Points >= Thresholds[i]) { r = (Rank)i; break; }
        if (r == CurrentRank) return;
        bool up = r > CurrentRank;
        CurrentRank = r;
        if (up)
        {
            _rankPop = 1f;
            Play(r == Rank.S ? ProceduralSounds.StyleS : ProceduralSounds.StyleUp);
        }
    }

    protected override void Update()
    {
        float dt = Time.deltaTime;
        float now = Time.time;
        if (_state != null && _state.IsDead) { Points = 0f; Multiplier = 1; UpdateRank(); }

        if (now - _lastKillTime > comboWindow) Multiplier = 1;
        if (Points > 0f && now - _lastScoreTime > decayDelay)
        {
            Points = Mathf.Max(0f, Points - decayRate * (1f + (int)CurrentRank * 0.25f) * dt);
            UpdateRank();
        }

        // visibility: slides in at B, fades out below
        float target = CurrentRank >= Rank.B ? 1f : 0f;
        _visibility = Mathf.MoveTowards(_visibility, target, dt / (target > _visibility ? 0.25f : 0.8f));
        _rankPop = Mathf.MoveTowards(_rankPop, 0f, dt / 0.45f);
        _flash = Mathf.MoveTowards(_flash, 0f, dt / 1.2f);

        // S rank perks
        if (CurrentRank == Rank.S && _state != null && !_state.IsDead)
        {
            if (_state.Health < PlayerState.MaxHealthValue) _state.Heal(sRegenPerSecond * dt);
            _ammoTimer += dt;
            if (_ammoTimer >= sAmmoInterval && _combat != null)
            {
                _ammoTimer -= sAmmoInterval;
                bool any = false;
                foreach (AmmoType t in Enum.GetValues(typeof(AmmoType))) any |= _combat.inventory.AddToAmmo(t, sAmmoAmount);
                if (any) _combat.OnAmmoChanged?.Invoke();
            }
        }
        else _ammoTimer = 0f;
    }

    private void Play(AudioClip clip)
    {
        if (clip != null) RipNRage.Engine.Ecs.Audio.Sfx.Play(clip, 0.7f);
    }

    // ------------------------------------------------------------------ HUD

    private static readonly Color[] RankColors =
    {
        new Color(0.55f, 0.55f, 0.6f, 1f), new Color(0.6f, 0.7f, 0.8f, 1f), new Color(0.75f, 0.85f, 0.95f, 1f),
        new Color(0.35f, 0.65f, 1f, 1f), new Color(1f, 0.55f, 0.2f, 1f), new Color(1f, 0.85f, 0.25f, 1f),
    };

    private readonly ClayUi _clay = new ClayUi(256);

    public void DrawUi(UiBatch ui)
    {
        if (_visibility <= 0.01f) return;
        float a = _visibility;
        int ri = (int)CurrentRank;
        Color col = RankColors[ri];

        var c = _clay;
        c.PointerEnabled = false;
        c.Begin(ui);
        // column pinned 190 px from the right edge, top at 62% of the height; slides in from the right
        float slide = (1f - a) * 120f;
        using (c.Box(new El
        {
            vertical = true, gap = 6f,
            floating = new Float { enabled = true, attachTo = Clay_FloatingAttachToElement.Root, passthrough = true,
                element = Clay_FloatingAttachPointType.LeftTop, parent = Clay_FloatingAttachPointType.RightTop,
                offset = new Vector2(-190f + slide, 0.38f * ClayScreen.RefHeight) },
        }))
        {
            // rank letter with a pop on rank-up, gold pulse behind the S
            float pop = 1f + _rankPop * 0.35f;
            Color pulseBg = default;
            if (CurrentRank == Rank.S)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
                pulseBg = new Color(col.r, col.g, col.b, 0.12f + 0.12f * pulse);
            }
            using (c.Box(new El { padding = new Pad(12f, 10f), background = pulseBg }))
                c.Text(CurrentRank.ToString(), ClayScreen.Line(16f * pop, col, shadow: true));

            // rank name + multiplier
            using (c.Box(new El { alignY = Clay_LayoutAlignmentY.Center }))
            {
                using (c.Box(new El { width = Size.Fixed(120f) })) c.Text(RankNames[ri], ClayScreen.Line(2.6f, new Color(col.r, col.g, col.b, 0.9f)));
                if (Multiplier > 1) c.Text($"X{Multiplier}", ClayScreen.Line(3.2f, Color.white));
            }

            // progress bar within the rank
            float lo = Thresholds[ri], hi = ri + 1 < Thresholds.Length ? Thresholds[ri + 1] : maxPoints;
            float t = Mathf.Clamp01((Points - lo) / Mathf.Max(1f, hi - lo));
            ClayScreen.Gap(c, 14f);
            using (c.Box(new El { width = Size.Fixed(170f), height = Size.Fixed(8f), background = new Color(0f, 0f, 0f, 0.55f), borderColor = new Color(1f, 1f, 1f, 0.25f), border = 1f }))
                c.Leaf(new El { width = Size.Fixed(170f * t), height = Size.Grow(), background = col });

            // last event flash
            if (_flash > 0f && !string.IsNullOrEmpty(_lastEvent))
                c.Text(_lastEvent, ClayScreen.Line(2.2f, new Color(1f, 1f, 1f, _flash)));
        }
        c.End(a);

        // S rank: warm gold vignette through the post stack (or drawn directly when the stack is off)
        if (CurrentRank == Rank.S)
        {
            var stack = Application.Current?.Renderer?.PostFx;
            float k = 0.35f * a;
            if (stack != null && stack.IsActive) stack.AddOverride(k, new Color(1f, 0.8f, 0.3f, 1f), 0f, Color.white, 0.15f * a);
            else ui.DrawVignette(new Color(1f, 0.8f, 0.3f, 1f), k);
        }
    }
}
