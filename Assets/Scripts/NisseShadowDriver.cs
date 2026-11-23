using UnityEngine;

// "Become a shadow" loop for the Nisse (exercise 5).
// Timeline per loop: solid, dissolve into smoke, body gone while the specks hang in the air, re-form, solid.
// The same dissolve value drives the NisseShadow graph (_Dissolve) and the emission rate of the two
// particle systems (smoke puffs and small glowing specks) that emit from the Nisse's surface.
public class NisseShadowDriver : MonoBehaviour
{
    public Renderer[] targets;
    public ParticleSystem smoke;
    public ParticleSystem specks;

    [Header("Timeline (seconds)")]
    public float solidTime = 1.0f;      // whole figure before the dissolve
    public float dissolveTime = 3.0f;   // 0 -> 1
    public float goneTime = 2.2f;       // body gone, specks linger
    public float reformTime = 1.5f;     // 1 -> 0
    public float restTime = 1.5f;       // whole figure again

    [Header("Emission")]
    public float smokeRate = 380f;      // puffs per second at the peak of the dissolve
    public float speckRate = 2200f;     // specks per second at the end of the dissolve

    MaterialPropertyBlock mpb;

    public float Period => solidTime + dissolveTime + goneTime + reformTime + restTime;

    void Update() { Apply(Mathf.Repeat(Time.time, Period)); }

    // Dissolve value and whether it is rising (only the rising half emits smoke and specks).
    public float DissolveAt(float t, out bool rising)
    {
        rising = false;
        t = Mathf.Repeat(t, Period);
        if (t < solidTime) return 0f;
        t -= solidTime;
        if (t < dissolveTime) { rising = true; return Mathf.SmoothStep(0f, 1f, t / dissolveTime); }
        t -= dissolveTime;
        if (t < goneTime) return 1f;
        t -= goneTime;
        if (t < reformTime) return 1f - Mathf.SmoothStep(0f, 1f, t / reformTime);
        return 0f;
    }

    // t = seconds into the loop.
    public void Apply(float t)
    {
        float d = DissolveAt(t, out bool rising);
        if (mpb == null) mpb = new MaterialPropertyBlock();
        foreach (var r in targets)
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetFloat("_Dissolve", d);
            r.SetPropertyBlock(mpb);
        }
        // smoke peaks in the middle of the dissolve, specks keep coming until the body is gone
        float smokeK = rising ? Mathf.Clamp01(Mathf.Sin(Mathf.PI * Mathf.Min(1f, d * 1.15f)) + 0.15f) : 0f;
        float speckK = rising ? Mathf.Sqrt(d) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 1f, d))) : 0f;
        if (smoke != null) { var e = smoke.emission; e.rateOverTime = smokeRate * smokeK; }
        if (specks != null) { var e = specks.emission; e.rateOverTime = speckRate * speckK; }
    }
}
