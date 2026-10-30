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
    private AudioSource _audio;
    private string _lastKillWeapon;
    private float _lastKillTime = -100f, _lastScoreTime = -100f;
    private float _visibility, _rankPop, _ammoTimer, _flash;
    private string _lastEvent = "";

    protected override void Awake() { Current = this; }
    protected override void OnDestroy() { if (Current == this) Current = null; }

    protected override void Start()
    {
        _state = GetComponent<PlayerState>();
        _combat = GetComponent<CombatController>();
        _audio = GetComponent<AudioSource>();
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
        if (_audio != null && clip != null) _audio.PlayOneShot(clip, 0.7f);
    }

    // ------------------------------------------------------------------ HUD

    private static readonly Color[] RankColors =
    {
        new Color(0.55f, 0.55f, 0.6f, 1f), new Color(0.6f, 0.7f, 0.8f, 1f), new Color(0.75f, 0.85f, 0.95f, 1f),
        new Color(0.35f, 0.65f, 1f, 1f), new Color(1f, 0.55f, 0.2f, 1f), new Color(1f, 0.85f, 0.25f, 1f),
    };

    public void DrawUi(UiBatch ui)
    {
        if (_visibility <= 0.01f) return;
        float s = ui.Scale, a = _visibility;
        int ri = (int)CurrentRank;
        Color c = RankColors[ri];
        float slide = (1f - a) * 120f * s;                        // slides in from the right
        float x = ui.Width - 190f * s + slide, top = ui.Height * 0.62f;

        // rank letter with a pop on rank-up
        float pop = 1f + _rankPop * 0.35f;
        string letter = CurrentRank.ToString();
        float letterScale = 16f * s * pop;
        Vector2 ls = ui.MeasureText(letter, letterScale);
        if (CurrentRank == Rank.S)
        {
            // gold pulse behind the S
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
            ui.FillRect(new Rect(x - 12f * s, top - ls.y - 10f * s, ls.x + 24f * s, ls.y + 20f * s), new Color(c.r, c.g, c.b, (0.12f + 0.12f * pulse) * a));
        }
        ui.DrawText(letter, new Vector2(x + 4f * s, top - 4f * s), letterScale, new Color(0f, 0f, 0f, 0.7f * a));
        ui.DrawText(letter, new Vector2(x, top), letterScale, new Color(c.r, c.g, c.b, a));

        // rank name + multiplier
        float ny = top - ls.y - 6f * s;
        ui.DrawText(RankNames[ri], new Vector2(x, ny), 2.6f * s, new Color(c.r, c.g, c.b, 0.9f * a));
        if (Multiplier > 1) ui.DrawText($"X{Multiplier}", new Vector2(x + 120f * s, ny), 3.2f * s, new Color(1f, 1f, 1f, a));

        // progress bar within the rank
        float lo = Thresholds[ri], hi = ri + 1 < Thresholds.Length ? Thresholds[ri + 1] : maxPoints;
        float t = Mathf.Clamp01((Points - lo) / Mathf.Max(1f, hi - lo));
        float bw = 170f * s, bh = 8f * s, by = ny - 30f * s;
        ui.FillRect(new Rect(x, by, bw, bh), new Color(0f, 0f, 0f, 0.55f * a));
        ui.FillRect(new Rect(x, by, bw * t, bh), new Color(c.r, c.g, c.b, a));
        MenuList.DrawBorder(ui, new Rect(x, by, bw, bh), new Color(1f, 1f, 1f, 0.25f * a), Mathf.Max(1f, s));

        // last event flash
        if (_flash > 0f && !string.IsNullOrEmpty(_lastEvent))
            ui.DrawText(_lastEvent, new Vector2(x, by - 8f * s), 2.2f * s, new Color(1f, 1f, 1f, _flash * a));

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
