using System.Collections;
using System.Collections.Generic;

// Ported from Assets/_Game/Scripts/RageSystem/RagePostprocess.cs.
// URP post-processing Volumes are replaced by a colour filter + vignette drawn over the frame,
// scaled by the volume "weight" the original code lerps. The values mirror the volume profiles:
// EnragedPP = ColorAdjustments colorFilter (1.98, 0.78, 0.78) + red vignette 0.5,
// DepressedPP = desaturated blue filter + dark vignette.
public class Volume
{
    public float weight;
    public Color colorFilter;
    public Color vignetteColor;
    public float vignetteIntensity;
    public Volume(Color colorFilter, Color vignetteColor, float vignetteIntensity)
    {
        this.colorFilter = colorFilter; this.vignetteColor = vignetteColor; this.vignetteIntensity = vignetteIntensity;
    }
}

public class RagePostprocess : MonoBehaviour
{
    public Volume enragedVolume = new Volume(new Color(1.25f, 0.62f, 0.62f, 1f), new Color(1f, 0f, 0f, 1f), 0.5f);
    public Volume depressedVolume = new Volume(new Color(0.55f, 0.62f, 0.85f, 1f), new Color(0f, 0f, 0.1f, 1f), 0.6f);

    private Coroutine _enragedTransition;
    private Coroutine _depressedTransition;

    public void OnEnterEnraged()
    {
        if (_enragedTransition != null)
            StopCoroutine(_enragedTransition);

        _enragedTransition = StartCoroutine(DoEnragedTransition());
    }
    
    public void OnEnterDepressed()
    {
        if (_depressedTransition != null)
            StopCoroutine(_depressedTransition);
        
        _depressedTransition = StartCoroutine(DoDepressedTransition());
    }

    public void OnEnterNormal()
    {
        StopAllCoroutines();
        enragedVolume.weight = 0;
        depressedVolume.weight = 0;
    }

    private IEnumerator DoEnragedTransition()
    {
        while (enragedVolume.weight < 0.5f)
        {
            enragedVolume.weight = Mathf.Lerp(enragedVolume.weight, 0.5f, 30 * Time.deltaTime);
            yield return new WaitForSeconds(0.01f);
        }
    }

    private IEnumerator DoDepressedTransition()
    {
        while (depressedVolume.weight < 0.5f)
        {
            depressedVolume.weight = Mathf.Lerp(depressedVolume.weight, 0.5f, 30 * Time.deltaTime);
            yield return new WaitForSeconds(0.01f);
        }
    }

    /// <summary>
    /// Applies the volumes. With the post-processing stack active they are pushed into it
    /// (colour filter, vignette and the chromatic aberration EnragedPP had); otherwise they are
    /// drawn as screen tints by the HUD.
    /// </summary>
    public void Draw(UiBatch ui)
    {
        if (!RipNRage.Game.Content.GraphicsSettings.RageEffectsEnabled) return; // graphics menu: "rage screen effects"
        var stack = Application.Current?.Renderer?.PostFx;
        bool useStack = stack != null && stack.IsActive;
        DrawVolume(ui, enragedVolume, useStack ? stack : null, chromatic: 1.0f);
        DrawVolume(ui, depressedVolume, useStack ? stack : null, chromatic: 0.0f);
    }

    private static void DrawVolume(UiBatch ui, Volume v, RipNRage.Engine.Graphics.PostFx.PostProcessStack stack, float chromatic)
    {
        if (v.weight <= 0.001f) return;
        float w = Mathf.Clamp01(v.weight * 2f); // the code lerps weight to 0.5 = full effect
        var filter = Color.Lerp(Color.white, v.colorFilter, w);
        if (stack != null)
        {
            stack.AddOverride(v.vignetteIntensity * w, v.vignetteColor, chromatic * w, filter);
            return;
        }
        ui.FillRectMultiply(new Rect(0, 0, ui.Width, ui.Height), filter);
        ui.DrawVignette(v.vignetteColor, v.vignetteIntensity * w);
    }

    // The Unity prefab wired these to PlayerState through UnityEvents; here we poll the state.
    private PlayerRageState _lastState = PlayerRageState.NORMAL;
    protected override void Update()
    {
        var ps = PlayerState.Current;
        if (ps == null || ps.RageState == _lastState) return;
        _lastState = ps.RageState;
        switch (_lastState)
        {
            case PlayerRageState.ENRAGED: OnEnterEnraged(); break;
            case PlayerRageState.DEPRESSED: OnEnterDepressed(); break;
            default: OnEnterNormal(); break;
        }
    }
}
