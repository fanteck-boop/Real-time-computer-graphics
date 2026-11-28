// Ex 4. Automatic exposure for URP, which has none built in.
// This is scripted average metering, not a port of the HDRP histogram exposure:
//   1. A hidden copy of the main camera renders the scene into a small 64 x 36 half-float target with
//      post-processing off, so the pixels are the linear HDR scene before tonemapping and before post exposure.
//   2. The pixels are read back (AsyncGPUReadback) and the log-average luminance is computed:
//      Lavg = exp(mean(log(max(L, 0.0001)))), with L = 0.2126 R + 0.7152 G + 0.0722 B.
//   3. The target luminance is recorded once with all lights on. The exposure that brings the current
//      measurement back to it is targetEV = log2(Ltarget / Lavg), clamped to [minEV, maxEV].
//   4. The applied exposure moves towards targetEV over time with an exponential curve, slower when the
//      scene gets darker (eyes take longer to open up) and faster when it gets brighter.
//   5. The result goes into Color Adjustments > Post Exposure of the volume profile.
// Because the meter camera has post-processing off, the value it measures does not depend on the exposure
// that is applied, so there is no feedback loop.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class AutoExposure : MonoBehaviour
{
    public Camera sourceCamera;            // the camera whose view is metered (the main camera)
    public Volume volume;                  // global volume with a Color Adjustments override
    public bool adapt = true;              // off: exposure stays where it is (key 6)

    [Header("Metering")]
    public int meterWidth = 64;
    public int meterHeight = 36;
    public float targetLuminance = 0f;     // recorded with all lights on; 0 means record it after recordDelay seconds of play
    public float recordDelay = 1f;         // the first frames are not settled yet (shadows, SSAO), so wait before recording
    public float exposureCompensation = 0f; // EV added on top of the computed value

    [Header("Adaptation")]
    public float minEV = -4f;
    public float maxEV = 8f;
    public float speedLightToDark = 0.8f;  // 1/s, when the scene got darker and exposure has to go up
    public float speedDarkToLight = 2.5f;  // 1/s, when the scene got brighter and exposure has to go down

    // read-only state, shown in the HUD
    public float MeasuredLuminance { get; private set; } = -1f;   // log average, used for exposure
    public float MeasuredMean { get; private set; } = -1f;        // plain arithmetic mean, only for comparison
    public float TargetEV { get; private set; }
    public float CurrentEV { get; private set; }

    Camera meter;
    RenderTexture meterRT;
    bool readbackPending;

    // Play mode works on the volume's runtime copy of the profile, edit mode on the shared one.
    public static VolumeProfile ProfileOf(Volume v) => v == null ? null : (Application.isPlaying ? v.profile : v.sharedProfile);

    void OnEnable() { readbackPending = false; }

    void OnDisable()
    {
        if (meter != null) DestroySafe(meter.gameObject);
        if (meterRT != null) { meterRT.Release(); DestroySafe(meterRT); }
        meter = null; meterRT = null;
    }

    static void DestroySafe(Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

    void EnsureMeter()
    {
        if (sourceCamera == null) sourceCamera = Camera.main;
        if (meterRT == null)
        {
            meterRT = new RenderTexture(meterWidth, meterHeight, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            { name = "Ex4 exposure meter", hideFlags = HideFlags.HideAndDontSave };
            meterRT.Create();
        }
        if (meter == null)
        {
            var go = new GameObject("Ex4 exposure meter camera") { hideFlags = HideFlags.HideAndDontSave };
            meter = go.AddComponent<Camera>();
            meter.enabled = false;         // rendered by hand below
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;   // linear HDR scene, no tonemapping, no post exposure
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorOption = CameraOverrideOption.Off;
        }
        meter.CopyFrom(sourceCamera);
        meter.enabled = false;
        meter.allowHDR = true;
        meter.allowMSAA = false;
        meter.targetTexture = meterRT;
        meter.transform.SetPositionAndRotation(sourceCamera.transform.position, sourceCamera.transform.rotation);
        meter.aspect = sourceCamera.aspect;
    }

    ColorAdjustments ColorAdjust()
    {
        var p = ProfileOf(volume);
        return p != null && p.TryGet(out ColorAdjustments ca) ? ca : null;
    }

    static float LogAverage(Unity.Collections.NativeArray<ushort> px, out float mean)
    {
        double sum = 0, lin = 0; int n = px.Length / 4;
        for (int i = 0; i < n; i++)
        {
            float r = Mathf.HalfToFloat(px[i * 4]), g = Mathf.HalfToFloat(px[i * 4 + 1]), b = Mathf.HalfToFloat(px[i * 4 + 2]);
            float l = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            sum += Mathf.Log(Mathf.Max(l, 1e-4f));
            lin += l;
        }
        mean = (float)(lin / n);
        return Mathf.Exp((float)(sum / n));
    }

    void OnReadback(AsyncGPUReadbackRequest req)
    {
        readbackPending = false;
        if (req.hasError || this == null) return;
        MeasuredLuminance = LogAverage(req.GetData<ushort>(), out float mean);
        MeasuredMean = mean;
    }

    // Renders the meter view and starts a readback. wait = true blocks until the value is there (used by the editor capture).
    public void Measure(bool wait)
    {
        if (readbackPending && !wait) return;
        EnsureMeter();
        meter.Render();
        var req = AsyncGPUReadback.Request(meterRT, 0, TextureFormat.RGBAHalf, OnReadback);
        readbackPending = true;
        if (wait) req.WaitForCompletion();
    }

    public void RecordTarget() { Measure(true); targetLuminance = MeasuredLuminance; }

    // One adaptation step of dt seconds on the latest measurement.
    public void Adapt(float dt)
    {
        if (MeasuredLuminance > 0f && targetLuminance > 0f)
            TargetEV = Mathf.Clamp(Mathf.Log(targetLuminance / MeasuredLuminance, 2f) + exposureCompensation, minEV, maxEV);
        if (adapt)
        {
            float speed = TargetEV > CurrentEV ? speedLightToDark : speedDarkToLight;
            CurrentEV += (TargetEV - CurrentEV) * (1f - Mathf.Exp(-speed * dt));
        }
        Apply();
    }

    public void SetEV(float ev) { CurrentEV = ev; Apply(); }

    void Apply()
    {
        var ca = ColorAdjust();
        if (ca != null) { ca.active = true; ca.postExposure.Override(CurrentEV); }
    }

    void LateUpdate()
    {
        if (!Application.isPlaying) return;
        Measure(false);
        if (targetLuminance <= 0f)
        {
            if (Time.timeSinceLevelLoad < recordDelay || MeasuredLuminance <= 0f) return;
            targetLuminance = MeasuredLuminance;   // all lights on at start: this is the brightness to keep
        }
        Adapt(Time.deltaTime);
    }
}
