using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Ex 4 part two: camera effects on top of the existing gallery in "Assets/Scenes/Ex 4 Galleri.unity".
// AddEffects only adds to the scene (volume, camera data, lens flare, scripts, focus points); it does not rebuild the gallery.
// CaptureSteps renders the off/on pairs into Captures/Ex04_Effects/ without saving the scene.
// Run headless: Unity -batchmode -projectPath . -executeMethod RtcgEx4Effects.AddEffects -quit   (then CaptureSteps)
public static class RtcgEx4Effects
{
    const string ScenePath = "Assets/Scenes/Ex 4 Galleri.unity";
    const string ProfilePath = "Assets/Gallery/Ex4_GalleryProfile.asset";
    const string FlarePath = "Assets/Gallery/Ex4_LampFlare.asset";
    const string FlareTexDir = "Assets/Gallery/Flare/";
    const string OutDir = "Captures/Ex04_Effects";

    // ---------- values used for the effects (also quoted in Docs/Ex04_effects_notes.md) ----------
    const float BloomThreshold = 1.2f, BloomIntensity = 2.0f, BloomScatter = 0.75f;
    const float DofFocalLength = 200f, DofAperture = 1.4f;
    const int FocusDefault = 1;   // Nisse 2, the middle one at the back

    [MenuItem("RTCG/Ex4 Add Effects")]
    public static void AddEffects()
    {
        MaxSettings();
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var gallery = GameObject.Find("Gallery").transform;
        var cam = Camera.main;

        // ---- volume profile ----
        var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (prof == null)
        {
            prof = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(prof, ProfilePath);
        }
        var tm = Ov<Tonemapping>(prof); tm.mode.Override(TonemappingMode.ACES);
        var ca = Ov<ColorAdjustments>(prof); ca.postExposure.Override(0f);
        var bl = Ov<Bloom>(prof);
        bl.threshold.Override(BloomThreshold); bl.intensity.Override(BloomIntensity); bl.scatter.Override(BloomScatter);
        bl.highQualityFiltering.Override(true); bl.tint.Override(Color.white);
        var dof = Ov<DepthOfField>(prof);
        dof.mode.Override(DepthOfFieldMode.Bokeh); dof.focalLength.Override(DofFocalLength); dof.aperture.Override(DofAperture);
        dof.bladeCount.Override(6); dof.bladeCurvature.Override(0.7f); dof.bladeRotation.Override(0f); dof.focusDistance.Override(10f);
        var vg = Ov<Vignette>(prof); vg.intensity.Override(0.32f); vg.smoothness.Override(0.45f); vg.active = false;
        var chr = Ov<ChromaticAberration>(prof); chr.intensity.Override(0.35f); chr.active = false;
        var wb = Ov<WhiteBalance>(prof); wb.temperature.Override(-12f); wb.tint.Override(0f); wb.active = false;
        var mb = Ov<MotionBlur>(prof); mb.mode.Override(MotionBlurMode.CameraOnly); mb.quality.Override(MotionBlurQuality.High);
        mb.intensity.Override(0.35f); mb.clamp.Override(0.05f); mb.active = false;
        foreach (var c in prof.components) { if (!AssetDatabase.IsSubAsset(c) && AssetDatabase.GetAssetPath(c) == "") AssetDatabase.AddObjectToAsset(c, prof); EditorUtility.SetDirty(c); }
        EditorUtility.SetDirty(prof);

        // ---- global volume ----
        var volGO = GameObject.Find("Ex4 Post Volume") ?? new GameObject("Ex4 Post Volume");
        var vol = volGO.GetComponent<Volume>() ?? volGO.AddComponent<Volume>();
        vol.isGlobal = true; vol.priority = 10; vol.sharedProfile = prof;

        // ---- camera: post processing, HDR, SMAA ----
        var data = cam.GetComponent<UniversalAdditionalCameraData>() ?? cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;
        data.stopNaN = true;
        cam.allowHDR = true;

        // ---- lens flare on the lamp ----
        var lamp = gallery.Find("Lamp");
        // The lamp material has an HDR emission colour (5, 3.8, 2.4) but its _EMISSION keyword was not set, so it rendered
        // as a plain grey ball. Keyword switched on,
        var lampMat = lamp.GetComponent<MeshRenderer>().sharedMaterial;
        lampMat.EnableKeyword("_EMISSION");
        // and the bulb was dimmer than the wall it lights (point light 8 at 1.2 m gives the wall about 4), so bloom could not
        // pick it out. Emission raised x4 (+2 EV), from (5, 3.8, 2.4) to (20, 15.2, 9.6).
        lampMat.SetColor("_EmissionColor", new Color(20f, 15.2f, 9.6f));
        lampMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(lampMat);
        var flareData = BuildFlareAsset();
        var lf = lamp.GetComponent<LensFlareComponentSRP>() ?? lamp.gameObject.AddComponent<LensFlareComponentSRP>();
        lf.lensFlareData = flareData;
        lf.intensity = 1f; lf.scale = 1f;
        lf.useOcclusion = true; lf.occlusionRadius = 0.08f; lf.sampleCount = 32;
        lf.occlusionOffset = 0.3f;     // the test point sits in front of the bulb, otherwise the sphere would hide its own flare
        lf.lightOverride = lamp.GetComponentInChildren<Light>();
        lf.attenuationByLightShape = false; lf.allowOffScreen = false;
        lf.maxAttenuationDistance = 100f; lf.maxAttenuationScale = 100f;

        // ---- focus points: centres of the three Nisse and one picture ----
        var fpRoot = GameObject.Find("Ex4 Focus Points");
        if (fpRoot) Object.DestroyImmediate(fpRoot);
        fpRoot = new GameObject("Ex4 Focus Points");
        var targets = new List<Transform>(); var names = new List<string>();
        for (int i = 1; i <= 3; i++)
        {
            var n = gallery.Find("Nisse_" + i);
            var b = new Bounds(n.position, Vector3.zero);
            foreach (var r in n.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            targets.Add(Point(fpRoot.transform, "Focus Nisse_" + i, b.center)); names.Add("Nisse " + i);
        }
        var pic = gallery.Find("Picture_art_cats_sleigh");
        targets.Add(Point(fpRoot.transform, "Focus Picture_art_cats_sleigh", pic.position)); names.Add("picture (cats sleigh)");

        // ---- scripts on the camera ----
        var ae = cam.GetComponent<AutoExposure>() ?? cam.gameObject.AddComponent<AutoExposure>();
        ae.sourceCamera = cam; ae.volume = vol;
        var fx = cam.GetComponent<EffectsController>() ?? cam.gameObject.AddComponent<EffectsController>();
        fx.volume = vol; fx.autoExposure = ae; fx.lensFlare = lf;
        fx.mainLights = MainLights(gallery).ToArray();
        fx.focusTargets = targets.ToArray(); fx.focusNames = names.ToArray(); fx.focusIndex = FocusDefault;
        fx.pedestals = new[] { gallery.Find("Pedestal_1").position, gallery.Find("Pedestal_2").position, gallery.Find("Pedestal_3").position };
        fx.bloomOn = true; fx.dofOn = true; fx.flareOn = true; fx.lightsOn = true; fx.extrasOn = false;
        dof.focusDistance.Override(Vector3.Dot(targets[FocusDefault].position - cam.transform.position, cam.transform.forward));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"EX4FX added: main lights {fx.mainLights.Length}, focus distance Nisse 2 {dof.focusDistance.value:0.00} m");
    }

    static Transform Point(Transform parent, string name, Vector3 pos)
    {
        var t = new GameObject(name).transform; t.SetParent(parent, false); t.position = pos; return t;
    }

    // The dominant group for the exposure demo: the four picture spots, the three Nisse key spots and the ceiling back fill.
    // Only the practical lamp in the corner (and the dim flat ambient) stays on.
    static List<Light> MainLights(Transform gallery)
    {
        var list = new List<Light>();
        foreach (var l in gallery.GetComponentsInChildren<Light>(true))
            if (l.name.StartsWith("PicLight_") || l.name.StartsWith("NisseKey_") || l.name == "BackFill") list.Add(l);
        return list;
    }

    static T Ov<T>(VolumeProfile p) where T : VolumeComponent
    {
        if (!p.TryGet(out T c)) c = p.Add<T>(false);
        c.active = true; return c;
    }

    // ---------- "max settings" on the PC pipeline asset ----------
    static void MaxSettings()
    {
        var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        var so = new SerializedObject(a);
        var sb = new StringBuilder("EX4FX PC_RPAsset settings (before -> after):\n");
        void Set(string prop, int v)
        {
            var p = so.FindProperty(prop);
            sb.Append($"  {prop}: {p.intValue} -> {v}\n");
            p.intValue = v;
        }
        Set("m_HDRColorBufferPrecision", 1);           // 0 = 32 bit R11G11B10, 1 = 64 bit RGBA16F
        Set("m_MainLightShadowmapResolution", 4096);
        Set("m_AdditionalLightsShadowmapResolution", 4096);
        Set("m_AdditionalLightsShadowResolutionTierLow", 512);
        Set("m_AdditionalLightsShadowResolutionTierMedium", 1024);
        Set("m_AdditionalLightsShadowResolutionTierHigh", 2048);
        Set("m_AdditionalLightsPerObjectLimit", 8);
        Set("m_ColorGradingLutSize", 64);
        Set("m_SupportDataDrivenLensFlare", 1);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(a); AssetDatabase.SaveAssets();
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        sb.Append($"  renderer rendering mode: {rd.renderingMode} (unchanged)\n");
        sb.Append($"  render scale {a.renderScale}, MSAA {a.msaaSampleCount}, HDR {a.supportsHDR}, shadow distance {a.shadowDistance}, cascades {a.shadowCascadeCount}, soft shadow quality {so.FindProperty("m_SoftShadowQuality").intValue}");
        Debug.Log(sb.ToString());
    }

    // ---------- lens flare: generated starburst texture + data asset ----------
    static Texture2D StarburstTexture()
    {
        Directory.CreateDirectory(FlareTexDir);
        string path = FlareTexDir + "Ex4_Starburst.png";
        const int N = 256;
        var t = new Texture2D(N, N, TextureFormat.RGB24, false);
        var rnd = new System.Random(7);
        var rayGain = new float[360];
        for (int i = 0; i < 360; i++) rayGain[i] = (float)rnd.NextDouble();
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float ang = Mathf.Atan2(v, u) * Mathf.Rad2Deg; if (ang < 0) ang += 360f;
                // six main spikes (hexagonal aperture) plus thin random rays
                float spikes = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * Mathf.Deg2Rad * 3f)), 60f);
                float rays = Mathf.Pow(rayGain[(int)ang % 360], 6f) * 0.5f;
                float fall = Mathf.Clamp01(1f - r);
                float val = Mathf.Exp(-r * 9f) + (spikes * 0.8f + rays) * fall * fall * fall;
                val = Mathf.Clamp01(val);
                t.SetPixel(x, y, new Color(val, val, val));
            }
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true; ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static LensFlareDataSRP BuildFlareAsset()
    {
        var star = StarburstTexture();
        var d = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(FlarePath);
        if (d == null) { d = ScriptableObject.CreateInstance<LensFlareDataSRP>(); AssetDatabase.CreateAsset(d, FlarePath); }
        Color warm = new Color(1f, 0.82f, 0.6f, 1f);

        var glow = new LensFlareDataElementSRP
        {
            flareType = SRPLensFlareType.Circle, position = 0f, uniformScale = 1.5f, localIntensity = 1.5f,
            tint = warm, fallOff = 0.75f, edgeOffset = 1.0f, blendMode = SRPLensFlareBlendMode.Additive,
        };
        var starEl = new LensFlareDataElementSRP
        {
            flareType = SRPLensFlareType.Image, lensFlareTexture = star, position = 0f, uniformScale = 3.0f,
            localIntensity = 5.0f, tint = warm, preserveAspectRatio = true, blendMode = SRPLensFlareBlendMode.Additive,
        };
        var streak = new LensFlareDataElementSRP
        {
            flareType = SRPLensFlareType.Circle, position = 0f, uniformScale = 1.0f, sizeXY = new Vector2(10f, 0.05f),
            localIntensity = 8.0f, tint = new Color(1f, 0.75f, 0.5f, 1f), fallOff = 0.6f, edgeOffset = 1.0f,
            blendMode = SRPLensFlareBlendMode.Additive,
        };
        var ghosts = new LensFlareDataElementSRP
        {
            flareType = SRPLensFlareType.Polygon, sideCount = 6, sdfRoundness = 0.25f, fallOff = 0.5f, edgeOffset = 0.9f,
            position = 0.35f, uniformScale = 0.9f, localIntensity = 1.6f, tint = Color.white,
            allowMultipleElement = true, count = 5, distribution = SRPLensFlareDistribution.Random, lengthSpread = 1.7f, seed = 3,
            intensityVariation = 0.5f, positionVariation = new Vector2(1f, 0f), scaleVariation = 1.2f, rotationVariation = 0f,
            blendMode = SRPLensFlareBlendMode.Additive,
        };
        var cg = new Gradient();
        cg.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.7f, 0.4f), 0f), new GradientColorKey(new Color(0.5f, 1f, 0.7f), 0.5f), new GradientColorKey(new Color(0.45f, 0.65f, 1f), 1f) },
                   new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        ghosts.colorGradient = cg;
        d.elements = new[] { glow, starEl, streak, ghosts };
        EditorUtility.SetDirty(d);
        AssetDatabase.SaveAssets();
        return d;
    }

    // ---------- capture ----------
    // Half-float linear target, so URP keeps the camera in HDR (an 8-bit target would clamp before bloom).
    static void Shot(Camera cam, string file, int w = 1920, int h = 1080)
    {
        string path = Path.Combine(OutDir, file);
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        cam.targetTexture = rt; cam.aspect = (float)w / h;
        cam.Render();   // first frame after a toggle: lens flare occlusion and other per-camera history settle here
        cam.Render();
        RenderTexture.active = rt;
        var t = new Texture2D(w, h, TextureFormat.RGBAHalf, false); t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
        RtcgPlayCapture.WritePngFromHalf(t, path);
        RenderTexture.active = null; cam.targetTexture = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(t);
        Debug.Log("WROTE " + path);
    }

    static float MeanDisplayLuma(string file)
    {
        var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(Path.Combine(OutDir, file)));
        var px = t.GetPixels32(); double s = 0;
        foreach (var c in px)
            s += 0.2126 * Mathf.GammaToLinearSpace(c.r / 255f) + 0.7152 * Mathf.GammaToLinearSpace(c.g / 255f) + 0.0722 * Mathf.GammaToLinearSpace(c.b / 255f);
        Object.DestroyImmediate(t);
        return (float)(s / px.Length);
    }

    [MenuItem("RTCG/Ex4 Capture Steps")]
    public static void CaptureSteps()
    {
        EditorSceneManager.OpenScene(ScenePath);
        Directory.CreateDirectory(OutDir);
        var cam = Camera.main;
        var data = cam.GetComponent<UniversalAdditionalCameraData>();
        var vol = Object.FindAnyObjectByType<Volume>();
        var ae = cam.GetComponent<AutoExposure>();
        var fx = cam.GetComponent<EffectsController>();
        // work on an in-memory copy (components included), the saved profile stays as it is
        var copy = ScriptableObject.CreateInstance<VolumeProfile>();
        foreach (var c in vol.sharedProfile.components) copy.components.Add(Object.Instantiate(c));
        copy.isDirty = true;
        vol.sharedProfile = copy;
        fx.Bind();
        var log = new StringBuilder();

        void State(bool bloom, bool dof, bool flare, bool extras, int focus = FocusDefault)
        {
            fx.bloomOn = bloom; fx.dofOn = dof; fx.flareOn = flare; fx.extrasOn = extras; fx.focusIndex = focus; fx.lightsOn = true;
            fx.ApplyAll();
        }

        State(false, false, false, false);
        ae.SetEV(0f);
        for (int i = 0; i < 4; i++) Shot(cam, "_warm.png", 320, 180);   // first renders come out untextured
        File.Delete(Path.Combine(OutDir, "_warm.png"));

        // 0: post processing off on the camera (no tonemapping, HDR values clip at 1)
        data.renderPostProcessing = false; Shot(cam, "step0_no_post.png"); data.renderPostProcessing = true;
        // 1: bloom
        State(false, false, false, false); Shot(cam, "step1a_bloom_off.png");
        State(true, false, false, false); Shot(cam, "step1b_bloom_on.png");
        // 2: depth of field, focus on Nisse 2, then on the picture
        State(true, false, false, false); Shot(cam, "step2a_dof_off.png");
        State(true, true, false, false, 1); Shot(cam, "step2b_dof_on.png");
        log.Append($"DoF Bokeh focal {DofFocalLength} mm f/{DofAperture}: focus Nisse 2 at {fx.FocusDistance():0.00} m\n");
        State(true, true, false, false, 3); Shot(cam, "step2c_dof_focus_other.png");
        log.Append($"  other focus {fx.focusNames[3]} at {fx.FocusDistance():0.00} m\n");
        for (int i = 0; i < 4; i++) { fx.focusIndex = i; log.Append($"  depth of {fx.focusNames[i]}: {fx.FocusDistance():0.00} m\n"); }
        // 3: lens flare
        State(true, false, false, false); Shot(cam, "step3a_flare_off.png");
        State(true, false, true, false); Shot(cam, "step3b_flare_on.png");

        // 4: exposure. Target recorded with all lights on.
        State(true, false, true, false);
        ae.RecordTarget(); ae.SetEV(0f); ae.Adapt(0f);
        float lumOn = ae.MeasuredLuminance, meanOn = ae.MeasuredMean;
        Shot(cam, "step4a_light_on.png");
        fx.lightsOn = false; fx.ApplyAll();
        ae.Measure(true); ae.Adapt(0f);                     // dt = 0: target known, exposure not moved yet
        float lumOff = ae.MeasuredLuminance, evTarget = ae.TargetEV, meanOff = ae.MeasuredMean;
        Shot(cam, "step4b_light_off_no_adapt.png");
        const float dt = 1f / 30f; int steps = 0; var curve = new StringBuilder();
        while (Mathf.Abs(ae.TargetEV - ae.CurrentEV) > 0.01f && steps < 900)
        {
            ae.Measure(true); ae.Adapt(dt); steps++;
            if (steps % 15 == 0) curve.Append($"    t {steps * dt:0.0} s  EV {ae.CurrentEV:+0.00}\n");
        }
        float tAdapt = steps * dt;
        Shot(cam, "step4c_light_off_adapted.png");
        log.Append($"Exposure (scripted log-average metering of the HDR scene before post exposure, 64x36):\n");
        log.Append($"  all lights on : luminance {lumOn:0.00000}  target EV 0.00  applied 0.00\n");
        log.Append($"  main lights off, no adaptation: luminance {lumOff:0.00000}  target EV {evTarget:+0.00}  applied 0.00\n");
        log.Append($"  main lights off, adapted after {tAdapt:0.0} s: luminance {ae.MeasuredLuminance:0.00000}  target EV {ae.TargetEV:+0.00}  applied {ae.CurrentEV:+0.00}\n");
        log.Append($"  ratio on/off {lumOn / lumOff:0.0} = {Mathf.Log(lumOn / lumOff, 2f):0.00} EV\n");
        log.Append($"  (arithmetic mean of the meter pixels: on {meanOn:0.00000}, off {meanOff:0.00000}, ratio {meanOn / meanOff:0.0})\n");
        log.Append($"  mean display luminance of the PNGs: 4a {MeanDisplayLuma("step4a_light_on.png"):0.0000}  4b {MeanDisplayLuma("step4b_light_off_no_adapt.png"):0.0000}  4c {MeanDisplayLuma("step4c_light_off_adapted.png"):0.0000}\n");
        log.Append("  adaptation curve:\n").Append(curve);
        fx.lightsOn = true; fx.ApplyAll(); ae.SetEV(0f);

        // 5: everything on
        State(true, true, true, true, 1); Shot(cam, "step5_final.png");

        File.WriteAllText(Path.Combine(OutDir, "capture_log.txt"), log.ToString());
        Debug.Log("EX4FX capture\n" + log);
    }

    // ---------- play mode check: AsyncGPUReadback metering and adaptation in real frames ----------
    // Run without -quit: Unity -batchmode -projectPath . -executeMethod RtcgEx4Effects.PlayCheck
    const string PlayJob = "ex4fx_play";
    static int playFrame;

    public static void PlayCheck()
    {
        SessionState.SetBool(PlayJob, true);
        playFrame = 0;
        EditorSceneManager.OpenScene(ScenePath);
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    static void HookPlay() { EditorApplication.update += PlayTick; }

    static void PlayTick()
    {
        if (!SessionState.GetBool(PlayJob, false) || !Application.isPlaying) return;
        Time.captureFramerate = 30;   // fixed 1/30 s steps
        playFrame++;
        var cam = Camera.main; if (cam == null) return;
        var ae = cam.GetComponent<AutoExposure>(); var fx = cam.GetComponent<EffectsController>();
        void Log(string what) => Debug.Log($"EX4PLAY frame {playFrame} {what}: measured {ae.MeasuredLuminance:0.00000} target lum {ae.targetLuminance:0.00000} targetEV {ae.TargetEV:+0.00} EV {ae.CurrentEV:+0.00}");
        if (playFrame % 30 == 0) Log(fx.lightsOn ? "lights on" : "lights off");
        if (playFrame == 90) { fx.lightsOn = false; fx.ApplyAll(); Log("switch lights off"); }
        if (playFrame == 390) { fx.lightsOn = true; fx.ApplyAll(); Log("switch lights on"); }
        if (playFrame == 540)
        {
            SessionState.EraseBool(PlayJob);
            EditorApplication.Exit(0);
        }
    }
}
