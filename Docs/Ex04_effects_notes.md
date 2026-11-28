# Exercise 4, part 2: camera effects in the gallery

Built with Claude Code.

## What this part adds
The gallery in `Assets/Scenes/Ex 4 Galleri.unity` already had its own lights (four picture spots, three Nisse key spots, a cool back fill and a lamp in the back right corner). This part adds the camera side of the exercise on top of it: bloom, depth of field, lens flare, tonemapping, an automatic exposure that adapts when the main lights are switched off, and a few extra effects. Nothing in the gallery was rebuilt. The editor script only adds objects and components, and the scene diff in git has 459 added lines and no removed lines.

Files:
- `Assets/Scripts/Ex4/AutoExposure.cs`: scripted exposure metering and adaptation (full code below).
- `Assets/Scripts/Ex4/EffectsController.cs`: keys, walking and the HUD (full code below).
- `Assets/Editor/RtcgEx4Effects.cs`: `AddEffects` sets everything up, `CaptureSteps` renders the images, `PlayCheck` runs the exposure in real play mode frames.
- `Assets/Gallery/Ex4_GalleryProfile.asset`: the volume profile.
- `Assets/Gallery/Ex4_LampFlare.asset` and `Assets/Gallery/Flare/Ex4_Starburst.png`: the lens flare data and its generated texture.
- `Captures/Ex04_Effects/`: twelve 1920 x 1080 images and `capture_log.txt` with the measured numbers.

Added to the scene: a global volume `Ex4 Post Volume` (priority 10), a `UniversalAdditionalCameraData`, `AutoExposure` and `EffectsController` on the Main Camera, a `LensFlareComponentSRP` on `Gallery/Lamp`, and an object `Ex4 Focus Points` with four empty children placed at the centres of the three Nisse and of the picture `art_cats_sleigh`.

## Camera
Post-processing on, HDR on, anti-aliasing SMAA at High quality, Stop NaN on. I chose SMAA over TAA because the captures are single frames rendered from a script, and TAA needs a history of earlier frames to converge. MSAA stays off in the pipeline asset (see the settings section).

## The effects and their numbers

**Tonemapping.** ACES. Without it (step 0) everything above 1.0 clips hard: the lamp is a flat disc and the lit walls lose their gradient.

**Bloom.** Threshold 1.2, intensity 2.0, scatter 0.75, high quality filtering on, white tint. Bloom only picks up pixels brighter than the threshold, so it needs something that is really bright in HDR. The lamp turned out not to be (see problems below). After the fix the lamp gets a clear glow, and the brightest parts of the pictures (the moon in the cat picture, the white beard and hat of the cubist Santa) get a soft halo. The walls and Nisse stay readable.

**Depth of field.** Bokeh mode, focal length 200 mm, aperture f/1.4, 6 blades, blade curvature 0.7. The focal length here only feeds the blur model, it does not change the field of view of the camera (62 degrees). Bokeh depth of field works on view depth, so the focus distance is the depth of the target along the camera's forward axis, not the straight line distance: `focusDistance = dot(target - camera, camera.forward)`. From the entrance view this gives Nisse 2 at 9.90 m, Nisse 1 and 3 at 7.40 m and the near pictures at 5.89 m. The script recomputes it every frame, so focus stays on the target while walking. With the thin lens model the blur circle for Nisse 1 when focused on Nisse 2 is f^2 / (N (s - f)) x |d - s| / d = 0.2^2 / (1.4 x 9.7) x 2.5 / 7.4, about 1.0 mm on the sensor, which is why the side Nisse are clearly soft in step 2b. In step 2c the focus moves to the near picture and everything further away goes soft.

**Lens flare.** URP data-driven lens flare (`LensFlareComponentSRP`) on the Lamp, with a `LensFlareDataSRP` asset built by the editor script. "Data Driven Lens Flare" was already enabled in `PC_RPAsset`. Elements, all additive and warm tinted (1, 0.82, 0.6) unless noted:
- glow: procedural circle at the light, scale 1.5, intensity 1.5, edge offset 1, falloff 0.75 (a soft round glow);
- starburst: image element using the generated `Ex4_Starburst.png` (256 x 256, six spikes plus thin random rays fading out from the centre), scale 3, intensity 5;
- streak: procedural circle stretched to 10 x 0.05, intensity 8, tint (1, 0.75, 0.5), a thin horizontal line through the lamp like an anamorphic lens gives;
- ghosts: five soft hexagons (polygon, 6 sides, roundness 0.25, edge offset 0.9, falloff 0.5), random distribution from position 0.35 along a spread of 1.7, scale 0.9 with variation, intensity 1.6, colours from warm through green to blue.

Component settings: intensity 1, scale 1, occlusion on with radius 0.08, 32 samples and an occlusion offset of 0.3 m towards the camera. The offset matters: the lamp is a solid sphere, and without it the sphere hides the light's own centre so the flare is always occluded. `lightOverride` points at `LampLight`. When a Nisse or a pedestal covers the lamp, the occlusion test fades the flare out.

**Color Adjustments.** Only post exposure is used, and the auto exposure script writes it.

**Extra effects (key 7, off by default, on in step 5).** Vignette intensity 0.32, smoothness 0.45. Chromatic aberration 0.35. White balance temperature -12 (slightly cooler, against the warm tungsten lights). Motion blur, camera only, high quality, intensity 0.35, clamp 0.05. Motion blur only shows while the camera turns or walks, so it is not visible in the still images.

## Exposure control
URP has no automatic exposure, so `AutoExposure.cs` does it with scripted average metering. It is not a port of HDRP's histogram exposure.

1. A hidden copy of the main camera renders the same view into a 64 x 36 half-float target with post-processing off. The pixels are therefore the linear HDR scene before tonemapping and before post exposure.
2. `AsyncGPUReadback` reads the pixels back and the script computes the log-average luminance, Lavg = exp(mean(ln(max(L, 0.0001)))), with L = 0.2126 R + 0.7152 G + 0.0722 B. The log average keeps a few very bright pixels (the lamp) from dominating.
3. The target luminance is recorded with all lights on. In play mode that happens 1 s after start, because the first frames are not settled.
4. Target exposure: EV = log2(Ltarget / Lavg), clamped to the range -4 to +8.
5. The applied exposure moves towards it with EV += (EVtarget - EV)(1 - e^(-k dt)). k is 0.8 per second when the scene got darker (exposure goes up, time constant 1.25 s) and 2.5 per second when it got brighter (time constant 0.4 s). So the eye opens up slowly and closes quickly, as described in the lecture.
6. The result goes into Color Adjustments, Post Exposure.

Because the meter camera has no post-processing, the value it measures does not depend on the exposure being applied, so there is no feedback loop.

Dominant light: key 5 switches a group of eight lights, the four picture spots, the three Nisse key spots and the back fill. My first try used only the seven spots, but the back fill lights the back wall, which fills a big part of the frame, and the log average only dropped by 1.29 EV. With the back fill in the group only the lamp in the corner and the dim flat ambient are left, and the drop is about 3 EV, which makes the adaptation easy to see.

Measured in the capture (entrance view, 16:9):

| State | Log-average luminance | Target EV | Applied post exposure |
|---|---|---|---|
| All lights on (4a) | 0.00393 | 0 | 0 |
| Main lights off, not adapted yet (4b) | 0.00046 | +3.08 | 0 |
| Main lights off, adapted (4c) | 0.00046 | +3.08 | +3.07 after 7.2 s |

The arithmetic mean of the same meter pixels went from 0.113 to 0.026 (ratio 4.4), against a ratio of 8.5 for the log average. The mean display luminance of the saved images is 0.0755 (4a), 0.0177 (4b) and 0.0576 (4c), so after adapting the picture is back to about three quarters of the original brightness. It does not come all the way back because ACES compresses the highlights around the lamp, which is now much brighter on screen than before. The adaptation curve in the capture (steps of 1/30 s): +1.02 EV after 0.5 s, +1.70 after 1 s, +2.46 after 2 s, +2.96 after 4 s.

`PlayCheck` runs the same thing in real play mode frames at a fixed 30 fps, with AsyncGPUReadback instead of the blocking read. With all lights on the measured log average was 0.00314 and the target was recorded at that value. Switching the lights off at frame 90 gave a measurement of 0.00055 and a target of +2.50 EV. The applied exposure was +1.15 after 1 s, +2.23 after 3 s and +2.50 after about 9 s. Switching the lights back on brought it to +0.04 after 2 s and 0.00 after 3 s. The play mode numbers differ from the capture numbers because the headless game view has a different aspect ratio than the 16:9 capture, so the meter sees a slightly different view. An earlier version recorded the target on the very first measurement and ended up 0.53 EV too bright with all lights on, which is why the 1 s delay is there.

## Keys and walking
`EffectsController.cs` uses the new Input System only (`Keyboard.current`, `Mouse.current`), since `Input.GetKey` throws in this project.

| Key | Action |
|---|---|
| 1 | bloom on/off |
| 2 | depth of field on/off |
| 3 | next focus target: Nisse 1, Nisse 2, Nisse 3, picture |
| 4 | lens flare on/off |
| 5 | main lights (7 spots and back fill) off/on |
| 6 | auto exposure on/off (off freezes the current value) |
| 7 | extra effects on/off |
| H | HUD on/off |
| WASD, Shift, mouse | walk, run, look; Esc frees the cursor, a click catches it again |

The walking copies the pattern of `Ex7/FirstPersonWalker.cs` (yaw on the body, clamped pitch, Shift to run). The gallery has no colliders (the builder removed them), so instead of a CharacterController the eye is kept at 1.6 m, inside the room (x within 4.6 m and z within 7.6 m of the centre) and at least 0.85 m from each pedestal centre. The HUD shows every state, the focus distance, the measured luminance, the target luminance, the target EV and the applied post exposure.

## Editor steps
With Unity closed, from the project folder:
```
Unity.exe -batchmode -quit -projectPath . -executeMethod RtcgEx4Effects.AddEffects
Unity.exe -batchmode -quit -projectPath . -executeMethod RtcgEx4Effects.CaptureSteps
Unity.exe -batchmode -projectPath . -executeMethod RtcgEx4Effects.PlayCheck
```
In the editor the first two are also in the menu, RTCG > Ex4 Add Effects and RTCG > Ex4 Capture Steps. `AddEffects` can be run again. It updates the components it added and rewrites the profile values, so hand changes to those would be overwritten. `CaptureSteps` works on an in-memory copy of the profile and does not save the scene.

By hand the same setup is: add a Global Volume with a new profile and the overrides above, tick Post Processing and HDR on the camera and set anti-aliasing to SMAA, add a Lens Flare (SRP) component to the lamp with a Lens Flare Data asset, and add the two scripts to the camera with their references filled in.

The captures were rendered in edit mode with `Camera.Render()` into a half-float target. Post-processing, bloom, depth of field and the lens flare all appear in those renders, so play mode capture was not needed. Each image is rendered twice and the second frame is saved, because the lens flare occlusion is computed one frame late after the flare is switched on.

## Images (Captures/Ex04_Effects/)
- `step0_no_post`: camera post-processing off. No tonemapping, the lamp and lit walls clip.
- `step1a_bloom_off` / `step1b_bloom_on`: the lamp gets a glow, the moon and the white areas of the Santa get a halo.
- `step2a_dof_off` / `step2b_dof_on` / `step2c_dof_focus_other`: sharp everywhere; focus on Nisse 2 (9.90 m) with the side Nisse and near pictures blurred; focus on the near picture (5.89 m) with the Nisse and back wall blurred.
- `step3a_flare_off` / `step3b_flare_on`: glow, starburst, horizontal streak and soft ghosts running from the lamp through the centre of the frame.
- `step4a_light_on` / `step4b_light_off_no_adapt` / `step4c_light_off_adapted`: see the table above. In 4b only the corner around the lamp is visible. In 4c the room is back, with strong glare around the lamp.
- `step5_final`: all effects on, focus on Nisse 2, vignette and chromatic aberration visible at the edges.

## Quality settings ("max settings")
The project uses quality level PC (index 1), which points at `Assets/Settings/PC_RPAsset.asset`. The renderer `PC_Renderer` runs Forward+. Forward+ has no per-object light limit (the limit of 4 only applies to plain Forward), so none of the nine lights in the gallery is dropped. The renderer, with its SSAO, Gaussian splat and ObjectOutline features, was not touched.

`PC_RPAsset`, before and after (a copy of the old file is kept outside the project):

| Setting | Before | After |
|---|---|---|
| HDR | on | on |
| HDR precision | 32 bit (R11G11B10) | 64 bit (RGBA16F) |
| Render scale | 1.0 | 1.0 |
| MSAA | off | off |
| Main light shadow resolution | 2048 | 4096 |
| Additional light shadow atlas | 2048 | 4096 |
| Additional light shadow tiers low / medium / high | 256 / 512 / 1024 | 512 / 1024 / 2048 |
| Additional lights per object limit | 4 | 8 (no effect in Forward+) |
| Shadow distance | 50 | 50 |
| Shadow cascades | 4 | 4 |
| Soft shadow quality | High | High |
| Color grading mode | HDR | HDR |
| Color grading LUT size | 32 | 64 |
| Data driven lens flare | on | on |

64-bit HDR avoids banding in the dark scene once the exposure pushes it up by 3 EV. The bigger shadow atlas lets the three Nisse key spots use 2048 px shadow maps each. MSAA stays off: SMAA on the camera handles the edges, and MSAA would change the render targets for every scene, including the custom ObjectOutline pass, which I did not want to risk. Render scale above 1 was also left alone.

`ProjectSettings/QualitySettings.asset` was not changed. For PC it has anisotropic filtering forced on, pixel light count 2, and shadow and anti-aliasing values that URP does not use (URP reads those from the pipeline asset).

## Problems met
- The lamp material `Gallery_Lamp` had an HDR emission colour of (5, 3.8, 2.4) but the `_EMISSION` keyword was not set, so the lamp rendered as a grey ball and bloom had nothing to work with. I switched the keyword on.
- Even with emission the bulb was darker than the wall right behind it. The lamp's point light (intensity 8) is 1.2 m from the back wall, which puts the wall at about 4 in HDR, so bloom glowed the wall and not the lamp. I raised the emission four times to (20, 15.2, 9.6). These two lamp changes are the only changes to existing content, and the material is used only in this scene.
- The procedural flare shapes looked like hard discs at first. In the flare shader the edge offset sets where the gradient starts and the falloff sets an exponent of e^(4(1 - falloff)), so a soft glow needs a high edge offset and a high falloff.
- The first frame after the flare is switched on has no occlusion result yet, so the first capture of the flare was weaker than later ones. Fixed by rendering each image twice.
- With only the seven spots as the dominant light the log average dropped by only 1.29 EV (see above).
- In play mode the first measurement came before the frame was settled. Fixed with the 1 s delay.

## Not verified
- The keys, mouse look, walking and the HUD were not pressed or looked at by a person. Headless play mode has no keyboard (`Keyboard.current` is null there), so `PlayCheck` switches the lights from code instead of key 5.
- Motion blur was set up but cannot be seen in still frames.
- The exposure numbers depend on the view and the window aspect ratio. Walking to another spot gives a different target EV.
- Frame rate at 1080p with all effects on was not measured.

## Full code: AutoExposure.cs
```csharp
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
```

## Full code: EffectsController.cs
```csharp
// Ex 4. Keys for the camera effects in the gallery, a small first person walk, and an OnGUI HUD.
//   1 bloom   2 depth of field   3 next focus target   4 lens flare   5 main lights off/on
//   6 auto exposure on/off   7 extra effects (vignette, chromatic aberration, white balance, motion blur)   H HUD
//   WASD walk, Shift run, mouse look, Esc frees the cursor, a click catches it again.
// The project only has the new Input System switched on, so this reads Keyboard.current and Mouse.current
// (Input.GetKey would throw). The walk follows Assets/Scripts/Ex7/FirstPersonWalker.cs, but the gallery has no
// colliders, so instead of a CharacterController the position is kept inside the room and outside the pedestals.
// Put it on the main camera, next to AutoExposure.
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class EffectsController : MonoBehaviour
{
    public Volume volume;
    public AutoExposure autoExposure;
    public LensFlareComponentSRP lensFlare;
    public Light[] mainLights;             // the dominant group switched by key 5
    public Transform[] focusTargets;       // cycled by key 3
    public string[] focusNames;

    [Header("Walk")]
    public float eyeHeight = 1.6f;
    public float walkSpeed = 2f, runSpeed = 5f;
    public float lookSensitivity = 0.1f;
    public Vector2 roomHalfSize = new Vector2(4.6f, 7.6f);   // x and z limits for the eye
    public Vector3[] pedestals;            // centres of the pedestals to walk around
    public float pedestalRadius = 0.85f;

    [Header("State")]
    public bool bloomOn = true, dofOn = true, flareOn = true, lightsOn = true, extrasOn = false, hudOn = true;
    public int focusIndex = 1;

    float yaw, pitch;
    Bloom bloom; DepthOfField dof; Vignette vignette; ChromaticAberration chroma; WhiteBalance whiteBalance; MotionBlur motionBlur;

    void Start()
    {
        Bind();
        Vector3 e = transform.eulerAngles; yaw = e.y; pitch = e.x > 180f ? e.x - 360f : e.x;
        LockCursor(true);
        ApplyAll();
    }

    void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // Used by the keys and by the editor capture script.
    public void ApplyAll()
    {
        if (bloom == null) Bind();
        bloom.active = bloomOn;
        dof.active = dofOn;
        if (lensFlare != null) lensFlare.enabled = flareOn;
        foreach (var l in mainLights) if (l != null) l.enabled = lightsOn;
        vignette.active = extrasOn; chroma.active = extrasOn; whiteBalance.active = extrasOn; motionBlur.active = extrasOn;
        UpdateFocus();
    }

    // Finds the overrides in the profile the volume uses (call again after the profile is swapped).
    public void Bind()
    {
        var p = AutoExposure.ProfileOf(volume);
        p.TryGet(out bloom); p.TryGet(out dof); p.TryGet(out vignette); p.TryGet(out chroma); p.TryGet(out whiteBalance); p.TryGet(out motionBlur);
    }

    // Bokeh depth of field works on view depth, so the focus distance is the target's depth along the view axis.
    public float FocusDistance()
    {
        if (focusTargets == null || focusTargets.Length == 0) return 10f;
        Transform t = focusTargets[Mathf.Clamp(focusIndex, 0, focusTargets.Length - 1)];
        return Mathf.Max(0.1f, Vector3.Dot(t.position - transform.position, transform.forward));
    }

    public void UpdateFocus() { if (dof != null) dof.focusDistance.Override(FocusDistance()); }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null) return;

        if (kb.digit1Key.wasPressedThisFrame) { bloomOn = !bloomOn; ApplyAll(); }
        if (kb.digit2Key.wasPressedThisFrame) { dofOn = !dofOn; ApplyAll(); }
        if (kb.digit3Key.wasPressedThisFrame) { focusIndex = (focusIndex + 1) % focusTargets.Length; ApplyAll(); }
        if (kb.digit4Key.wasPressedThisFrame) { flareOn = !flareOn; ApplyAll(); }
        if (kb.digit5Key.wasPressedThisFrame) { lightsOn = !lightsOn; ApplyAll(); }
        if (kb.digit6Key.wasPressedThisFrame && autoExposure != null) autoExposure.adapt = !autoExposure.adapt;
        if (kb.digit7Key.wasPressedThisFrame) { extrasOn = !extrasOn; ApplyAll(); }
        if (kb.hKey.wasPressedThisFrame) hudOn = !hudOn;

        // cursor: Esc frees it, a click catches it again
        if (kb.escapeKey.wasPressedThisFrame) LockCursor(false);
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);

        // mouse look
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 d = mouse.delta.ReadValue() * lookSensitivity;
            yaw += d.x;
            pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
        }
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        // walking on the floor plane
        Vector2 input = new Vector2(
            (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f),
            (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));
        input = Vector2.ClampMagnitude(input, 1f);
        float speed = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? runSpeed : walkSpeed;
        Quaternion body = Quaternion.Euler(0f, yaw, 0f);
        Vector3 pos = transform.position + (body * new Vector3(input.x, 0f, input.y)) * speed * Time.deltaTime;
        pos.x = Mathf.Clamp(pos.x, -roomHalfSize.x, roomHalfSize.x);
        pos.z = Mathf.Clamp(pos.z, -roomHalfSize.y, roomHalfSize.y);
        pos.y = eyeHeight;
        if (pedestals != null)
            foreach (var c in pedestals)
            {
                Vector2 d = new Vector2(pos.x - c.x, pos.z - c.z);
                if (d.magnitude < pedestalRadius) { d = d.normalized * pedestalRadius; pos.x = c.x + d.x; pos.z = c.z + d.y; }
            }
        transform.position = pos;

        UpdateFocus();   // the focus target keeps its distance while walking
    }

    void OnGUI()
    {
        if (!hudOn) return;
        string focus = focusNames != null && focusIndex < focusNames.Length ? focusNames[focusIndex] : focusIndex.ToString();
        string ae = autoExposure == null ? "none" :
            $"{(autoExposure.adapt ? "on" : "off")}\n" +
            $"  measured log-avg luminance {autoExposure.MeasuredLuminance:0.0000}\n" +
            $"  target luminance (all lights on) {autoExposure.targetLuminance:0.0000}\n" +
            $"  target EV {autoExposure.TargetEV:+0.00;-0.00;0.00}   applied post exposure {autoExposure.CurrentEV:+0.00;-0.00;0.00} EV";
        string text =
            "Ex 4 camera effects\n" +
            $"1 bloom: {(bloomOn ? "on" : "off")}\n" +
            $"2 depth of field (Bokeh): {(dofOn ? "on" : "off")}\n" +
            $"3 focus: {focus} at {FocusDistance():0.00} m\n" +
            $"4 lens flare: {(flareOn ? "on" : "off")}\n" +
            $"5 main lights (7 spots + back fill): {(lightsOn ? "on" : "off")}\n" +
            $"6 auto exposure: {ae}\n" +
            $"7 extra effects (vignette, chromatic aberration, white balance, motion blur): {(extrasOn ? "on" : "off")}\n" +
            "H hide this   WASD walk, Shift run, mouse look, Esc frees the cursor";
        var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = false };
        style.normal.textColor = Color.white;
        GUI.Box(new Rect(10, 10, 560, 230), text, style);
    }
}
```
