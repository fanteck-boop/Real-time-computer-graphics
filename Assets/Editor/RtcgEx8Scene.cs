using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Ex 8: image-based lighting with an equirectangular HDR map rendered in Blender (Docs/Ex08_make_environment.py).
// Run headless (Unity closed):
//   Unity.exe -batchmode -quit -projectPath <project> -executeMethod RtcgEx8Scene.ImportMap
//   Unity.exe -batchmode -quit -projectPath <project> -executeMethod RtcgEx8Scene.BuildScene
//   Unity.exe -batchmode -quit -projectPath <project> -executeMethod RtcgEx8Scene.CaptureSteps
// ImportMap sets the EXR import settings (cubemap from latitude-longitude, HDR, Kaiser mips).
// BuildScene makes the materials and "Assets/Scenes/Ex 8 IBL.unity", and finds the sun in the map.
// CaptureSteps drives IblController directly (keys cannot be pressed headless) and writes Captures/Ex08_IBL.
public static class RtcgEx8Scene
{
    const string ScenePath = "Assets/Scenes/Ex 8 IBL.unity";
    const string TexDir = "Assets/Textures/Ex8/";
    const string MapFull = TexDir + "Ex8_Environment.exr";
    const string MapNoSun = TexDir + "Ex8_Environment_NoSun.exr";
    const string ReflFull = TexDir + "Ex8_Environment_Reflection.exr";        // 512 x 256 copies for the probe
    const string ReflNoSun = TexDir + "Ex8_Environment_NoSun_Reflection.exr";
    const string MatDir = "Assets/Materials/Ex8/";
    const string OutDir = "Captures/Ex08_IBL";
    const float ViewOffset = 50f;   // the default view looks 50 degrees to the left of the sun

    // from the Blender script: sun lamp irradiance 3.0 and colour (1.0, 0.70, 0.42)
    const float SunIrradiance = 3.0f;
    static readonly Color SunColour = new Color(1.0f, 0.70f, 0.42f);

    // ------------------------------------------------------------------ import
    [MenuItem("RTCG/Ex8 Import Map")]
    public static void ImportMap()
    {
        foreach (var p in new[] { MapFull, MapNoSun, ReflFull, ReflNoSun })
        {
            bool reflection = p.Contains("_Reflection");
            AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(p);
            ti.textureType = TextureImporterType.Default;
            ti.textureShape = TextureImporterShape.TextureCube;
            ti.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;   // latitude-longitude layout
            var st = new TextureImporterSettings(); ti.ReadTextureSettings(st);
            // No convolution. Unity's "Glossy Reflection" (Specular) convolution would be the textbook choice, but a
            // probe holding a convolved cubemap rendered black here (tried twice). The small copies' Kaiser mips are
            // blurry enough: the roughest surface reads mip 6, which is 4 x 4 pixels per face.
            st.cubemapConvolution = TextureImporterCubemapConvolution.None;
            st.seamlessCubemap = true;               // "Fixup Edge Seams": blurry mips do not show the cube edges
            ti.SetTextureSettings(st);
            ti.sRGBTexture = false;                  // HDR data is linear
            ti.mipmapEnabled = true;
            ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            ti.streamingMipmaps = false;
            ti.isReadable = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear;
            ti.maxTextureSize = reflection ? 512 : 4096;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;   // BC6H on PC: HDR, 1 byte per pixel
            ti.SaveAndReimport();
            var t = AssetDatabase.LoadAssetAtPath<Cubemap>(p);
            Debug.Log($"EX8 imported {p}: {(t ? $"cubemap {t.width}x{t.height} {t.format} mips {t.mipmapCount}" : "NOT A CUBEMAP")}");
        }
    }

    // ------------------------------------------------------------------ helpers
    static Material Lit(string name, Color col, float smooth, float metallic)
    {
        Directory.CreateDirectory(MatDir);
        string path = MatDir + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", col);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metallic);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Material Sky(string name, string mapPath)
    {
        string path = MatDir + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find("Skybox/Cubemap")) { name = name };
        m.SetTexture("_Tex", AssetDatabase.LoadAssetAtPath<Cubemap>(mapPath));
        m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f));   // 0.5 grey = no tint (the shader doubles it)
        m.SetFloat("_Exposure", 1f);
        m.SetFloat("_Rotation", 0f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos; go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    // Finds the sun in the sky the same way the lecture does it: look for the brightest region.
    // Renders the sky in six 90 degree views into a float target, finds the brightest pixel and turns it into a direction.
    public static Vector3 MeasureSun(Material sky, out float peak)
    {
        var old = RenderSettings.skybox;
        RenderSettings.skybox = sky;
        var go = new GameObject("_sunfinder");
        var c = go.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.Skybox; c.cullingMask = 0; c.fieldOfView = 90f; c.aspect = 1f;
        c.nearClipPlane = 0.1f; c.farClipPlane = 1000f; c.allowHDR = true;
        var acd = go.AddComponent<UniversalAdditionalCameraData>(); acd.renderPostProcessing = false; acd.antialiasing = AntialiasingMode.None;
        const int N = 256;
        var rt = new RenderTexture(N, N, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var tex = new Texture2D(N, N, TextureFormat.RGBAHalf, false, true);
        Vector3[] fwd = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up, Vector3.down };
        peak = -1f; Vector3 best = Vector3.up;
        foreach (var f in fwd)
        {
            go.transform.rotation = Quaternion.LookRotation(f, Mathf.Abs(f.y) > 0.5f ? Vector3.forward : Vector3.up);
            c.targetTexture = rt;
            for (int k = 0; k < 2; k++) c.Render();   // the first render can come out empty
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, N, N), 0, 0); tex.Apply();
            RenderTexture.active = null;
            var px = tex.GetPixels();
            int bi = -1; float bl = -1f;
            for (int i = 0; i < px.Length; i++)
            {
                float l = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;
                if (l > bl) { bl = l; bi = i; }
            }
            if (bl <= peak) continue;
            // centre of the pixels above half the peak in this view
            float sx = 0, sy = 0, sw = 0;
            for (int i = 0; i < px.Length; i++)
            {
                float l = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;
                if (l > 0.5f * bl) { sx += (i % N + 0.5f) * l; sy += (i / N + 0.5f) * l; sw += l; }
            }
            peak = bl;
            best = c.ViewportPointToRay(new Vector3(sx / sw / N, sy / sw / N, 0)).direction.normalized;
        }
        c.targetTexture = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
        RenderSettings.skybox = old;
        return best;
    }

    static string AzEl(Vector3 d)
    {
        float az = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, el = Mathf.Asin(d.y) * Mathf.Rad2Deg;
        return $"yaw {az:0.00} deg (from +Z towards +X), elevation {el:0.00} deg";
    }

    // ------------------------------------------------------------------ scene
    [MenuItem("RTCG/Ex8 Build Scene")]
    public static void BuildScene()
    {
        AssetDatabase.Refresh();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Directory.CreateDirectory(MatDir);

        var skyFull = Sky("Ex8_Sky_Full", MapFull);
        var skyNoSun = Sky("Ex8_Sky_NoSun", MapNoSun);

        // find the sun in the full map (rotation 0)
        Vector3 sunDir = MeasureSun(skyFull, out float peak);
        Vector3 sunDirNo = MeasureSun(skyNoSun, out float peakNo);
        Debug.Log($"EX8 SUN in Unity: dir {sunDir.ToString("F4")}, {AzEl(sunDir)}, peak {peak:0.0} (no-sun map peak {peakNo:0.00} at {AzEl(sunDirNo)})");
        float sunYaw = Mathf.Atan2(sunDir.x, sunDir.z) * Mathf.Rad2Deg;

        // materials
        var ground = Lit("Ex8_Snow", new Color(0.70f, 0.74f, 0.82f), 0.15f, 0f);
        var plinthM = Lit("Ex8_Plinth", new Color(0.42f, 0.40f, 0.38f), 0.25f, 0f);
        var matte = Lit("Ex8_Matte_White", new Color(0.85f, 0.85f, 0.85f), 0f, 0f);
        var mirror = Lit("Ex8_Mirror", new Color(0.95f, 0.95f, 0.95f), 1f, 1f);
        float[] smooth = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        // the display faces the camera; the camera looks with the sun 50 degrees to its right,
        // so the lit sides are visible and the shadows fall across the plinth towards the left
        float viewYaw = sunYaw - ViewOffset;
        var root = new GameObject("Display").transform;
        root.rotation = Quaternion.Euler(0, viewYaw, 0);

        var floor = Prim(PrimitiveType.Cylinder, "Ground", null, new Vector3(0, -0.005f, 0), new Vector3(80f, 0.005f, 80f), ground);
        Prim(PrimitiveType.Cylinder, "Plinth", root, new Vector3(0, 0.25f, 0), new Vector3(3.6f, 0.25f, 3.6f), plinthM);

        for (int i = 0; i < 5; i++)
        {
            float x = -0.9f + 0.45f * i;
            var d = Lit($"Ex8_Dielectric_S{smooth[i]:0.00}", new Color(0.8f, 0.8f, 0.8f), smooth[i], 0f);
            var m = Lit($"Ex8_Metal_S{smooth[i]:0.00}", new Color(0.95f, 0.93f, 0.88f), smooth[i], 1f);
            Prim(PrimitiveType.Sphere, $"Dielectric_smooth_{smooth[i]:0.00}", root, new Vector3(x, 0.65f, -0.85f), Vector3.one * 0.3f, d);
            Prim(PrimitiveType.Sphere, $"Metal_smooth_{smooth[i]:0.00}", root, new Vector3(x, 0.65f, -0.35f), Vector3.one * 0.3f, m);
        }
        Prim(PrimitiveType.Sphere, "Matte_Ball", root, new Vector3(-0.85f, 0.8f, 0.45f), Vector3.one * 0.6f, matte);
        Prim(PrimitiveType.Sphere, "Mirror_Ball", root, new Vector3(0.85f, 0.8f, 0.45f), Vector3.one * 0.6f, mirror);

        // Nisse: wrapper at the base centre, child scaled to 0.8 m tall, facing the camera (local -Z)
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Nisse/Nisse.obj");
        var nisseMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Nisse/Nisse.mat");
        Mesh mesh = model.GetComponentInChildren<MeshFilter>().sharedMesh;
        Bounds b = mesh.bounds;
        float s = 0.8f / b.size.y;
        var wrap = new GameObject("Nisse").transform; wrap.SetParent(root, false);
        wrap.localPosition = new Vector3(0f, 0.5f, 0.45f); wrap.localRotation = Quaternion.Euler(0, 180f, 0);
        var child = (GameObject)PrefabUtility.InstantiatePrefab(model, wrap);
        child.name = "Mesh"; child.transform.localScale = Vector3.one * s;
        child.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z) * s;
        foreach (var r in child.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = nisseMat;

        // the sun as a light
        var sunGO = new GameObject("Sun (directional light)");
        var sun = sunGO.AddComponent<Light>();
        sun.type = LightType.Directional; sun.color = SunColour; sun.intensity = SunIrradiance / Mathf.PI;
        sun.shadows = LightShadows.Soft; sun.shadowStrength = 1f;
        sunGO.AddComponent<UniversalAdditionalLightData>();
        sunGO.transform.rotation = Quaternion.LookRotation(-sunDir, Vector3.up);

        // camera
        var camGO = new GameObject("Main Camera"); camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 50f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 500f; cam.allowHDR = true;
        var acd = camGO.AddComponent<UniversalAdditionalCameraData>(); acd.renderPostProcessing = true; acd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var camSky = camGO.AddComponent<Skybox>(); camSky.material = skyFull;
        var orbit = camGO.AddComponent<OrbitCamera>();
        orbit.SetView(new Vector3(0, 0.75f, 0), viewYaw, 12f, 3.9f);
        camGO.AddComponent<AudioListener>();

        // post: ACES tone mapping and a little bloom, so values above 1 roll off instead of clipping
        string profPath = MatDir + "Ex8_VolumeProfile.asset";
        AssetDatabase.DeleteAsset(profPath);
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(prof, profPath);
        var tm = prof.Add<Tonemapping>(false); tm.mode.Override(TonemappingMode.ACES);
        var bl = prof.Add<Bloom>(false); bl.threshold.Override(2.0f); bl.intensity.Override(0.35f); bl.scatter.Override(0.5f);
        var ca = prof.Add<ColorAdjustments>(false); ca.postExposure.Override(0.5f);   // half a stop brighter overall
        foreach (var c in prof.components) { AssetDatabase.AddObjectToAsset(c, prof); EditorUtility.SetDirty(c); }
        EditorUtility.SetDirty(prof);
        var vol = new GameObject("Post Volume (ACES + bloom)").AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = prof;

        // controller + HUD
        var ctrlGO = new GameObject("IBL Controller");
        var ctrl = ctrlGO.AddComponent<IblController>();
        ctrl.cam = cam; ctrl.cameraSkybox = camSky; ctrl.skyFull = skyFull; ctrl.skyNoSun = skyNoSun; ctrl.sun = sun;
        ctrl.sunDirection = sunDir; ctrl.sunIntensity = SunIrradiance / Mathf.PI;
        var hud = ctrlGO.AddComponent<IblHud>(); hud.controller = ctrl;

        // reflections: the default skybox reflection is not re-made when the sky is changed from a script (it stays
        // Unity's default sky), and realtime probes are switched off in this project's quality settings. So a probe in
        // Custom mode holds a small copy of the map; URP 17 turns the probe with its transform.
        var probeGO = new GameObject("Sky Reflection Probe (custom)");
        probeGO.transform.position = new Vector3(0, 1f, 0);
        var probe = probeGO.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Custom;
        probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Cubemap>(ReflFull);
        ctrl.reflectionFull = AssetDatabase.LoadAssetAtPath<Cubemap>(ReflFull);
        ctrl.reflectionNoSun = AssetDatabase.LoadAssetAtPath<Cubemap>(ReflNoSun);
        string blackPath = MatDir + "Ex8_NoReflection.cubemap";
        AssetDatabase.DeleteAsset(blackPath);
        var black = new Cubemap(16, TextureFormat.RGBAHalf, false);
        var zeros = new Color[16 * 16];
        for (int f = 0; f < 6; f++) black.SetPixels(zeros, (CubemapFace)f);
        black.Apply();
        AssetDatabase.CreateAsset(black, blackPath);
        ctrl.reflectionBlack = black;
        probe.hdr = true;
        probe.size = new Vector3(200f, 200f, 200f); probe.boxProjection = false;
        probe.importance = 1; probe.intensity = 1f;
        ctrl.skyProbe = probe;

        // flat ambient colour = the average of the map's own ambient light (evaluated from its SH in 26 directions)
        ctrl.ResetAll(); ctrl.Apply();
        var sh = RenderSettings.ambientProbe;
        var dirs = new System.Collections.Generic.List<Vector3>();
        for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
            if (x != 0 || y != 0 || z != 0) dirs.Add(new Vector3(x, y, z).normalized);
        var res = new Color[dirs.Count];
        sh.Evaluate(dirs.ToArray(), res);
        Color avg = Color.black; foreach (var c in res) avg += c; avg /= res.Length;
        ctrl.flatAmbient = new Color(avg.r, avg.g, avg.b, 1f);
        Debug.Log($"EX8 ambient SH: up {Eval(sh, Vector3.up)} down {Eval(sh, Vector3.down)} towards sun {Eval(sh, sunDir)} away {Eval(sh, -sunDir)} -> flat {ctrl.flatAmbient}");
        ctrl.Apply();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"EX8 built: nisse scale {s}, view yaw {viewYaw:0.0}, sun yaw {sunYaw:0.0}");
    }

    static string Eval(SphericalHarmonicsL2 sh, Vector3 d)
    {
        var o = new Color[1]; sh.Evaluate(new[] { d }, o);
        return $"({o[0].r:0.000}, {o[0].g:0.000}, {o[0].b:0.000})";
    }

    // ------------------------------------------------------------------ captures
    static Camera cam;
    static IblController ctrl;
    static OrbitCamera orbit;

    static void Shot(string name, int w = 1280, int h = 720)
    {
        ctrl.Apply();
        // throwaway renders: the environment update and the lazy texture loads settle over a couple of frames
        for (int i = 0; i < 3; i++) RtcgTools.WritePng(cam, Path.Combine(OutDir, "_warmup.png"), 320, 180);
        RtcgTools.WritePng(cam, Path.Combine(OutDir, name + ".png"), w, h);
        var sh = RenderSettings.ambientProbe;
        Debug.Log($"EX8 SHOT {name}: skybox {RenderSettings.skybox?.name} ambient {RenderSettings.ambientMode} up {Eval(sh, Vector3.up)} side {Eval(sh, ctrl.RotatedSunDirection())} refl {RenderSettings.reflectionIntensity} probe {ctrl.skyProbe.customBakedTexture.name} sun {ctrl.sun.enabled} rot {ctrl.rotation}");
    }

    // like Shot, but returns the 1280 x 720 picture instead of saving it
    static Texture2D Grab(string label)
    {
        ctrl.Apply();
        for (int i = 0; i < 3; i++) RtcgTools.WritePng(cam, Path.Combine(OutDir, "_warmup.png"), 320, 180);
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cam.targetTexture = rt; cam.aspect = 1280f / 720f; cam.Render();
        RenderTexture.active = rt;
        var t = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        t.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); t.Apply();
        RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(rt);
        Debug.Log($"EX8 GRAB {label}: skybox {RenderSettings.skybox.name} sun {ctrl.sun.enabled} probe {ctrl.skyProbe.customBakedTexture.name}");
        return t;
    }

    [MenuItem("RTCG/Ex8 Capture Steps")]
    public static void CaptureSteps()
    {
        EditorSceneManager.OpenScene(ScenePath);
        Directory.CreateDirectory(OutDir);
        cam = Camera.main;
        ctrl = Object.FindAnyObjectByType<IblController>();
        orbit = cam.GetComponent<OrbitCamera>();
        var display = GameObject.Find("Display");
        var ground = GameObject.Find("Ground");
        Vector3 sd = ctrl.sunDirection;
        float sunYaw = Mathf.Atan2(sd.x, sd.z) * Mathf.Rad2Deg;
        float viewYaw = sunYaw - ViewOffset;
        Vector3 centre = new Vector3(0, 0.75f, 0);
        Vector3 L(Vector3 local) => Quaternion.Euler(0, viewYaw, 0) * local;   // display-local to world

        ctrl.ResetAll();
        orbit.SetView(centre, viewYaw, 12f, 3.9f);
        for (int i = 0; i < 4; i++) RtcgTools.WritePng(cam, Path.Combine(OutDir, "_warmup.png"), 320, 180);

        // 0b: just the sky, display hidden, wide view towards the sun
        display.SetActive(false); ground.SetActive(false);
        cam.fieldOfView = 75f;
        orbit.SetView(new Vector3(0, 1.0f, 0), sunYaw - 35f, -8f, 0.01f);
        Shot("step0b_map_cubemap_view");
        display.SetActive(true); ground.SetActive(true);
        cam.fieldOfView = 50f;

        // 1: background off / on (everything else as default: SH ambient, reflections, no sun light)
        orbit.SetView(centre, viewYaw, 12f, 3.9f);
        ctrl.ResetAll(); ctrl.background = false; Shot("step1_background_off");
        ctrl.ResetAll(); Shot("step1_background_on");

        // 2: diffuse only (no reflections, no sun light), close on the matte ball and the Nisse in the back row.
        // The sun is 50 degrees to the right of this view, so with SH ambient the right side of the ball turns warm.
        orbit.SetView(L(new Vector3(-0.42f, 0.85f, 0.45f)), viewYaw, 12f, 1.6f);
        ctrl.ResetAll(); ctrl.reflections = false;
        ctrl.ambient = IblController.Ambient.None; Shot("step2a_no_ambient");
        ctrl.ambient = IblController.Ambient.Flat; Shot("step2b_flat_ambient");
        ctrl.ambient = IblController.Ambient.Skybox; Shot("step2c_skybox_ambient");

        // 3: sphere rows, reflections off / on
        orbit.SetView(L(new Vector3(0f, 0.65f, -0.55f)), viewYaw, 24f, 2.4f);
        ctrl.ResetAll(); ctrl.reflections = false; Shot("step3a_reflections_off");
        ctrl.ResetAll(); Shot("step3b_reflections_on");

        // 3c: the alignment check on the mirror ball, two halves of one picture from the same camera.
        // Left: the sun only inside the map (no light): its reflection is a spot on the ball.
        // Right: the sun taken out of the map and added as the directional light: the light's own highlight.
        // If the light direction was found correctly, the two spots sit in the same place on the ball.
        // The camera looks at the ball from the right side of the display (nothing in between), the sun is behind
        // and to the right of the camera, so its reflection lands well inside the ball, not on the rim.
        Vector3 ball = L(new Vector3(0.85f, 0.8f, 0.45f));
        orbit.SetView(ball, viewYaw - 90f, 4f, 1.25f);
        ctrl.ResetAll(); ctrl.sunLight = false;
        var left = Grab("step3c_left_map_sun");
        ctrl.ResetAll(); ctrl.sunLight = true;
        var right = Grab("step3c_right_light");
        var both = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        both.SetPixels(0, 0, 640, 720, left.GetPixels(320, 0, 640, 720));
        both.SetPixels(640, 0, 640, 720, right.GetPixels(320, 0, 640, 720));
        var line = new Color[2 * 720]; for (int i = 0; i < line.Length; i++) line[i] = Color.white;
        both.SetPixels(639, 0, 2, 720, line);
        both.Apply();
        File.WriteAllBytes(Path.Combine(OutDir, "step3c_mirror_ball_sun.png"), both.EncodeToPNG());
        Object.DestroyImmediate(left); Object.DestroyImmediate(right); Object.DestroyImmediate(both);

        // 4: the sun: only in the map vs. taken out and added as a light
        orbit.SetView(centre, viewYaw, 22f, 4.0f);
        ctrl.ResetAll(); Shot("step4a_no_sun_light");
        ctrl.ResetAll(); ctrl.sunLight = true; Shot("step4b_with_sun_light");

        // 5: rotation 0 and 90, sun light on so the shadows follow
        orbit.SetView(centre, viewYaw, 22f, 4.0f);
        ctrl.ResetAll(); ctrl.sunLight = true; ctrl.rotation = 0f; Shot("step5a_rotation_0");
        ctrl.rotation = 90f; Shot("step5b_rotation_90");

        // 6: final overview, everything on
        orbit.SetView(centre + new Vector3(0, 0.1f, 0), viewYaw - 15f, 16f, 5.4f);
        ctrl.ResetAll(); ctrl.sunLight = true; Shot("step6_final_overview");

        File.Delete(Path.Combine(OutDir, "_warmup.png"));
        ctrl.ResetAll(); ctrl.Apply();
        orbit.SetView(centre, viewYaw, 12f, 3.9f);
        Debug.Log("EX8 captures done");
    }
}
