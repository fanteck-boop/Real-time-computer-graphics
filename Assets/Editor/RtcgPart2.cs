using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Part 2 of the RTCG mini-project: scene builders and headless renders for Ex3 (Unity half), Ex4, Ex6 and Ex7.
// Run with  Unity.exe -batchmode -quit -projectPath <proj> -executeMethod RtcgPart2.BuildEx03  (no -nographics).
public static class RtcgPart2
{
    const string NissePrefab = "Assets/Nisse/Nisse.prefab";
    const string MatDir = "Assets/Materials/Part2";

    // ================= shared helpers =================
    static void Dir(string d) { Directory.CreateDirectory(d); }

    static Material LitMat(string name, Color c, float smooth = 0.3f, float metal = 0f)
    {
        Dir(MatDir);
        string path = $"{MatDir}/{name}.mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static GameObject Prim(PrimitiveType t, string name, Vector3 pos, Vector3 scale, Material m, Transform parent = null)
    {
        var g = GameObject.CreatePrimitive(t); g.name = name;
        UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false); g.transform.position = pos; g.transform.localScale = scale;
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        return g;
    }

    static GameObject PlaceNisse(Vector3 pos, float yaw, float scale = 1f)
    {
        var g = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(NissePrefab));
        g.transform.position = pos; g.transform.rotation = Quaternion.Euler(0, yaw, 0); g.transform.localScale = Vector3.one * scale;
        return g;
    }

    // Seamless backdrop ("cyclorama"): floor curving up into a wall, no horizon line.
    static Mesh Cyclorama(float halfWidth, float zFront, float zWall, float radius, float height)
    {
        var prof = new System.Collections.Generic.List<Vector2>(); // (z, y)
        prof.Add(new Vector2(zFront, 0));
        float zc = zWall + radius;
        const int seg = 24;
        for (int i = 0; i <= seg; i++)
        {
            float th = Mathf.PI * 0.5f * i / seg;
            prof.Add(new Vector2(zc - radius * Mathf.Sin(th), radius - radius * Mathf.Cos(th)));
        }
        prof.Add(new Vector2(zWall, height));
        var verts = new System.Collections.Generic.List<Vector3>(); var tris = new System.Collections.Generic.List<int>();
        foreach (var p in prof) { verts.Add(new Vector3(-halfWidth, p.y, p.x)); verts.Add(new Vector3(halfWidth, p.y, p.x)); }
        for (int i = 0; i < prof.Count - 1; i++)
        {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            tris.AddRange(new[] { a, c, b, b, c, d });
        }
        var mesh = new Mesh { name = "Cyclorama", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        if (mesh.normals[0].y < 0) { tris.Clear(); for (int i = 0; i < prof.Count - 1; i++) { int a = i * 2, b = a + 1, c = a + 2, d = a + 3; tris.AddRange(new[] { a, b, c, b, d, c }); } mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); }
        Dir("Assets/Meshes/Part2");
        string path = "Assets/Meshes/Part2/Cyclorama.asset";
        AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static GameObject MakeCyc(Material m, float zWall = -3.5f)
    {
        var g = new GameObject("Seamless backdrop");
        g.AddComponent<MeshFilter>().sharedMesh = Cyclorama(14, 14, zWall, 2.5f, 12);
        g.AddComponent<MeshRenderer>().sharedMaterial = m;
        return g;
    }

    static Light MakeLight(string name, LightType t, Vector3 pos, Vector3 target, Color col, float intensity, float range = 30, float spot = 40,
        LightShadows sh = LightShadows.None, int tier = 2)
    {
        var go = new GameObject(name); go.transform.position = pos; go.transform.LookAt(target);
        var l = go.AddComponent<Light>(); l.type = t; l.color = col; l.intensity = intensity; l.range = range; l.shadows = sh;
        if (t == LightType.Spot) { l.spotAngle = spot; l.innerSpotAngle = spot * 0.55f; }
        var d = go.GetComponent<UniversalAdditionalLightData>() ?? go.AddComponent<UniversalAdditionalLightData>();
        if (sh != LightShadows.None)
        {
            l.shadowBias = 0.01f; l.shadowNormalBias = 0.02f;
            var so = new SerializedObject(d);
            so.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = tier;
            so.FindProperty("m_UsePipelineSettings").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        return l;
    }

    static Camera MakeCam(Vector3 pos, Vector3 target, float focalMm)
    {
        var go = new GameObject("Main Camera"); go.tag = "MainCamera";
        var cam = go.AddComponent<Camera>();
        cam.usePhysicalProperties = true; cam.sensorSize = new Vector2(36f, 20.25f); cam.focalLength = focalMm;
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f; cam.allowHDR = true;
        cam.clearFlags = CameraClearFlags.Skybox;
        go.AddComponent<AudioListener>();
        var d = go.AddComponent<UniversalAdditionalCameraData>();
        d.renderPostProcessing = true;
        d.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        d.antialiasingQuality = AntialiasingQuality.High;
        go.transform.position = pos; go.transform.LookAt(target);
        return cam;
    }

    static VolumeProfile NewProfile(string name)
    {
        var p = ScriptableObject.CreateInstance<VolumeProfile>(); p.name = name; return p;
    }
    static T Ov<T>(VolumeProfile p) where T : VolumeComponent
    {
        if (!p.TryGet(out T c)) c = p.Add<T>(false);
        c.active = true; return c;
    }
    static void SaveProfile(VolumeProfile p, string path)
    {
        Dir(Path.GetDirectoryName(path));
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(p, path);
        foreach (var c in p.components) { AssetDatabase.AddObjectToAsset(c, p); EditorUtility.SetDirty(c); }
        EditorUtility.SetDirty(p); AssetDatabase.SaveAssets();
    }
    static Volume MakeVolume(VolumeProfile p)
    {
        var v = new GameObject("Post Volume").AddComponent<Volume>(); v.isGlobal = true; v.sharedProfile = p; return v;
    }

    // Renders through a half-float linear target. RtcgTools.WritePng targets an 8-bit RT, and URP then drops the camera
    // to LDR (scene colour clamped to 1 before bloom and tonemapping), which flattens every HDR highlight. Output is
    // display-referred after tonemapping, so only the sRGB transfer function is applied here.
    static void Shot(Camera cam, string path, int w, int h)
    {
        Dir(Path.GetDirectoryName(path));
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        cam.targetTexture = rt; cam.aspect = (float)w / h; cam.Render();
        RenderTexture.active = rt;
        var t = new Texture2D(w, h, TextureFormat.RGBAHalf, false); t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
        RtcgPlayCapture.WritePngFromHalf(t, path);
        RenderTexture.active = null; cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(t);
        Debug.Log("WROTE " + path);
    }

    // The ambient SH from a custom skybox and the first light uploads settle over a few frames; render throwaway frames first.
    static void Warm(Camera cam) { for (int i = 0; i < 4; i++) { DynamicGI.UpdateEnvironment(); Shot(cam, "Captures/_warm.png", 160, 90); } File.Delete("Captures/_warm.png"); }

    // mean linear luminance of a PNG (sRGB decoded), used to emulate average-metering auto exposure
    static float MeanLinearLuma(string path)
    {
        var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(path));
        var px = t.GetPixels32(); double s = 0;
        foreach (var c in px)
        {
            float r = Mathf.GammaToLinearSpace(c.r / 255f), g = Mathf.GammaToLinearSpace(c.g / 255f), b = Mathf.GammaToLinearSpace(c.b / 255f);
            s += 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }
        UnityEngine.Object.DestroyImmediate(t);
        return (float)(s / px.Length);
    }

    // Project asset PC_RPAsset shipped with Low Dynamic Range colour grading, which clamps the frame to 0..1 before
    // tonemapping. Exercises 3, 4, 6 and 7 need High Dynamic Range grading (catalogue: "URP with max settings").
    static void EnsureHdrGrading()
    {
        var a = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        bool dirty = false;
        if (a.colorGradingMode != ColorGradingMode.HighDynamicRange) { a.colorGradingMode = ColorGradingMode.HighDynamicRange; dirty = true; }
        // The working tree had Additional Lights = Per Vertex (git HEAD has Per Pixel). Per Vertex lights cannot cast shadows,
        // so spot and point shadows silently did nothing. Restore Per Pixel.
        if (a.additionalLightsRenderingMode != LightRenderingMode.PerPixel)
        {
            var so = new SerializedObject(a); so.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerPixel; so.ApplyModifiedPropertiesWithoutUndo(); dirty = true;
        }
        if (dirty) { EditorUtility.SetDirty(a); AssetDatabase.SaveAssets(); Debug.Log("RTCG: updated " + a.name + " (HDR grading, per-pixel additional lights)"); }
    }

    static void SaveScene(string path) { Dir("Assets/Scenes"); EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), path); }

    static void SetTonemap(VolumeProfile p, TonemappingMode m) { var t = Ov<Tonemapping>(p); t.mode.Override(m); }

    // ================= Ex3: Unity product photo =================
    static Color EnvColor(Vector3 d)
    {
        d.Normalize();
        // dark studio: faint overhead gradient plus three softboxes in the key / fill / rim directions
        float up = Mathf.Clamp01(d.y * 0.5f + 0.5f);
        Color c = Color.Lerp(new Color(0.10f, 0.10f, 0.105f), new Color(0.34f, 0.34f, 0.35f), up);
        void Box(Vector3 dir, float radiusDeg, float e, Color col)
        {
            float a = Vector3.Angle(d, dir.normalized);
            float u = Mathf.Clamp01((a - radiusDeg * 0.7f) / (radiusDeg * 0.3f)); float k = 1f - u * u * (3f - 2f * u);
            c += col * (e * k);
        }
        Box(new Vector3(3.0f, 1.83f, 2.33f), 28, 5f, new Color(1f, 0.95f, 0.88f));   // key softbox
        Box(new Vector3(-3.33f, 0.83f, 2.67f), 38, 1.2f, new Color(0.9f, 0.95f, 1f)); // fill softbox
        Box(new Vector3(-1.67f, 2.0f, -3.33f), 18, 4f, Color.white);                  // rim strip
        return c;
    }

    static Material BuildStudioSky(string name, Func<Vector3, Color> f)
    {
        Dir(MatDir);
        const int N = 128;
        var cube = new Cubemap(N, TextureFormat.RGBAHalf, true);
        for (int face = 0; face < 6; face++)
        {
            var px = new Color[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float s = (x + 0.5f) / N * 2 - 1, t = (y + 0.5f) / N * 2 - 1; // Unity cubemap rows run top-down
                Vector3 d;
                switch ((CubemapFace)face)
                {
                    case CubemapFace.PositiveX: d = new Vector3(1, -t, -s); break;
                    case CubemapFace.NegativeX: d = new Vector3(-1, -t, s); break;
                    case CubemapFace.PositiveY: d = new Vector3(s, 1, t); break;
                    case CubemapFace.NegativeY: d = new Vector3(s, -1, -t); break;
                    case CubemapFace.PositiveZ: d = new Vector3(s, -t, 1); break;
                    default: d = new Vector3(-s, -t, -1); break;
                }
                px[y * N + x] = f(d);
            }
            cube.SetPixels(px, (CubemapFace)face);
        }
        cube.Apply(true);
        string cp = $"{MatDir}/{name}_cube.asset"; AssetDatabase.DeleteAsset(cp); AssetDatabase.CreateAsset(cube, cp);
        var m = new Material(Shader.Find("Skybox/Cubemap")); m.SetTexture("_Tex", cube);
        string mp = $"{MatDir}/{name}.mat"; AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(m, mp);
        return m;
    }

    static void ApplyEnv(Material sky, float ambient, float refl)
    {
        RenderSettings.skybox = sky; RenderSettings.ambientMode = AmbientMode.Skybox; RenderSettings.ambientIntensity = ambient;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox; RenderSettings.reflectionIntensity = refl;
        DynamicGI.UpdateEnvironment();
    }

    public static void BuildEx03()
    {
        EnsureHdrGrading();
        string o = "Captures/Ex03_Unity"; Dir(o);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var sky = BuildStudioSky("Ex03_StudioEnv", EnvColor);
        ApplyEnv(sky, 1f, 0.8f);

        MakeCyc(LitMat("Ex03_Backdrop", new Color(0.5f, 0.5f, 0.5f), 0.12f));
        var nisse = PlaceNisse(Vector3.zero, 0);
        // coordinates = Blender rig * 3.333 (Nisse is 0.30 m in Blender, 1 unit here), rotated 180 degrees about Y because the prefab faces +Z
        Vector3 tgt = new Vector3(0, 0.5f, 0);
        var cam = MakeCam(new Vector3(-0.4f, 0.67f, 5.0f), tgt, 85f);
        var key = MakeLight("Key (spot)", LightType.Spot, new Vector3(3.0f, 2.7f, 2.33f), tgt, new Color(1f, 0.95f, 0.88f), 42f, 30, 38, LightShadows.Soft);
        var wash = MakeLight("Backdrop wash (spot)", LightType.Spot, new Vector3(0, 3.2f, 3.5f), new Vector3(0, 1.2f, -3.2f), new Color(0.95f, 0.95f, 1f), 28f, 30, 75, LightShadows.None);
        var fill = MakeLight("Fill (point)", LightType.Point, new Vector3(-3.33f, 0.83f, 2.67f), tgt, new Color(0.9f, 0.95f, 1f), 7f, 20, 0, LightShadows.None);
        var rim = MakeLight("Rim (spot)", LightType.Spot, new Vector3(-1.67f, 2.0f, -3.33f), tgt + Vector3.up * 0.2f, Color.white, 45f, 30, 28, LightShadows.Soft);
        var sun = MakeLight("Overhead (directional)", LightType.Directional, new Vector3(0, 5, 0), Vector3.zero, new Color(0.85f, 0.9f, 1f), 0.35f, 30, 0, LightShadows.Soft);
        sun.transform.rotation = Quaternion.Euler(62, 200, 0);

        var prof = NewProfile("Ex03_Profile");
        SetTonemap(prof, TonemappingMode.Neutral);
        var ca = Ov<ColorAdjustments>(prof); ca.postExposure.Override(0.3f); ca.contrast.Override(6f);
        var bl = Ov<Bloom>(prof); bl.threshold.Override(1.0f); bl.intensity.Override(0.15f); bl.scatter.Override(0.6f);
        var vg = Ov<Vignette>(prof); vg.intensity.Override(0.15f); vg.smoothness.Override(0.6f);
        var dof = Ov<DepthOfField>(prof); dof.mode.Override(DepthOfFieldMode.Bokeh); dof.focusDistance.Override(5.0f);
        dof.focalLength.Override(85f); dof.aperture.Override(8f);
        SaveProfile(prof, "Assets/Settings/Ex03_Profile.asset");
        MakeVolume(prof);

        // ---- hero ---- (first render only warms up the ambient SH, discarded)
        Warm(cam);
        Shot(cam, $"{o}/nisse_unity_product.png", 1920, 1080);

        // ---- build-up: key, +fill, +rim, +overhead and environment ----
        float amb = RenderSettings.ambientIntensity;
        void Set(bool k, bool f, bool r, bool s, float a, float refl) { wash.enabled = a > 0; key.enabled = k; fill.enabled = f; rim.enabled = r; sun.enabled = s; RenderSettings.ambientIntensity = a; RenderSettings.reflectionIntensity = refl; DynamicGI.UpdateEnvironment(); }
        Set(true, false, false, false, 0, 0); Shot(cam, $"{o}/buildup_1_key.png", 960, 540);
        Set(true, true, false, false, 0, 0); Shot(cam, $"{o}/buildup_2_key_fill.png", 960, 540);
        Set(true, true, true, false, 0, 0); Shot(cam, $"{o}/buildup_3_key_fill_rim.png", 960, 540);
        Set(true, true, true, true, 1, 0.8f); Shot(cam, $"{o}/buildup_4_plus_overhead_env.png", 960, 540);
        // ---- light types in isolation ----
        Set(false, false, false, true, 0, 0); sun.intensity = 2.2f; Shot(cam, $"{o}/type_directional_only.png", 960, 540); sun.intensity = 0.35f;
        Set(false, true, false, false, 0, 0); fill.intensity = 28f; Shot(cam, $"{o}/type_point_only.png", 960, 540); fill.intensity = 7f;
        Set(true, false, false, false, 0, 0); Shot(cam, $"{o}/type_spot_only.png", 960, 540);
        Set(false, false, false, false, 1, 0.8f); Shot(cam, $"{o}/type_env_softboxes_only.png", 960, 540);
        // ---- shadow quality ----
        // shadow study: key moved low and to the side so the cast shadow lands on the floor in view
        Vector3 keyPos = key.transform.position; Quaternion keyRot = key.transform.rotation;
        key.transform.position = new Vector3(3.6f, 1.0f, 0.9f); key.transform.LookAt(new Vector3(0, 0.3f, 0));
        Set(true, false, false, false, 0.25f, 0.2f);
        key.shadows = LightShadows.Hard; Shot(cam, $"{o}/shadow_hard.png", 960, 540);
        key.shadows = LightShadows.Soft; Shot(cam, $"{o}/shadow_soft.png", 960, 540);
        key.shadows = LightShadows.None; Shot(cam, $"{o}/shadow_none.png", 960, 540); key.shadows = LightShadows.Soft;
        key.transform.position = keyPos; key.transform.rotation = keyRot; Set(true, true, true, true, 1, 0.8f);
        // ---- tonemapping ----
        var tm = Ov<Tonemapping>(prof);
        tm.mode.Override(TonemappingMode.None); Shot(cam, $"{o}/tonemap_none.png", 960, 540);
        tm.mode.Override(TonemappingMode.Neutral); Shot(cam, $"{o}/tonemap_neutral.png", 960, 540);
        tm.mode.Override(TonemappingMode.ACES); Shot(cam, $"{o}/tonemap_aces.png", 960, 540); tm.mode.Override(TonemappingMode.Neutral);
        // ---- reflection / ambient from skybox on vs off ----
        RenderSettings.ambientIntensity = 0; RenderSettings.reflectionIntensity = 0; DynamicGI.UpdateEnvironment(); Shot(cam, $"{o}/env_off.png", 960, 540);
        RenderSettings.ambientIntensity = 1; RenderSettings.reflectionIntensity = 0.8f; DynamicGI.UpdateEnvironment();

        EditorUtility.SetDirty(prof); AssetDatabase.SaveAssets();
        SaveScene("Assets/Scenes/Ex03_Lighting.unity");
        Debug.Log("EX03 done");
    }

    // Debug: render the studio cubemap seen from the camera so the softbox directions can be checked.
    public static void DiagEnv()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var sky = BuildStudioSky("Ex03_StudioEnv", EnvColor); ApplyEnv(sky, 1, 1);
        Debug.Log("DIAG sky shader " + sky.shader.name + " tex=" + sky.GetTexture("_Tex") + " rs.skybox=" + RenderSettings.skybox);
        var c0 = EnvColor(new Vector3(3.0f, 1.83f, 2.33f)); Debug.Log("DIAG env at key dir " + c0);
        var cam = MakeCam(Vector3.zero, new Vector3(0, 0, -1), 14f);
        var sph = Prim(PrimitiveType.Sphere, "ball", new Vector3(0, 0, -4), Vector3.one * 2, LitMat("diag", Color.gray, 0.9f));
        Shot(cam, "Captures/Ex03_Unity/diag_env_back.png", 960, 540);
        sph.transform.position = new Vector3(0, 0, 4);
        cam.transform.LookAt(new Vector3(0, 0, 1)); Shot(cam, "Captures/Ex03_Unity/diag_env_front.png", 960, 540);
        RenderSettings.skybox = null; Shot(cam, "Captures/Ex03_Unity/diag_env_noskybox.png", 960, 540);
    }

    // ================= Ex4: camera parameters =================
    // HDR "emissive" surface: an Unlit material whose colour exceeds 1, so bloom and flare have something bright to pick up.
    static Material EmissiveMat(string name, Color c, float intensity)
    {
        Dir(MatDir);
        string path = $"{MatDir}/{name}.mat"; AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find("RTCG/HDR Emissive"));
        m.SetColor("_Color", c * intensity);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    public static void BuildEx04()
    {
        EnsureHdrGrading();
        string o = "Captures/Ex04"; Dir(o);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // all lighting is explicit: flat dim ambient, no skybox contribution
        RenderSettings.skybox = null; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.16f, 0.16f, 0.18f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom; RenderSettings.reflectionIntensity = 0.3f;

        var floorM = LitMat("Ex04_Floor", new Color(0.62f, 0.60f, 0.58f), 0.35f);
        var wallM = LitMat("Ex04_Wall", new Color(0.72f, 0.70f, 0.68f), 0.1f);
        var pedM = LitMat("Ex04_Pedestal", new Color(0.85f, 0.85f, 0.85f), 0.25f);
        Prim(PrimitiveType.Cube, "Floor", new Vector3(0, -0.05f, -2), new Vector3(16, 0.1f, 22), floorM);
        Prim(PrimitiveType.Cube, "Back wall", new Vector3(0, 3, -9), new Vector3(16, 6, 0.2f), wallM);
        Prim(PrimitiveType.Cube, "Left wall", new Vector3(-5.5f, 3, -2), new Vector3(0.2f, 6, 22), wallM);
        var rows = new (Vector3 p, float yaw)[] { (new Vector3(-0.75f, 0, 2.0f), 20), (new Vector3(0.1f, 0, -1.5f), -12), (new Vector3(0.85f, 0, -5.0f), -28) };
        foreach (var r in rows)
        {
            Prim(PrimitiveType.Cube, "Pedestal", r.p + new Vector3(0, 0.3f, 0), new Vector3(0.7f, 0.6f, 0.7f), pedM);
            PlaceNisse(r.p + new Vector3(0, 0.6f, 0), r.yaw, 0.6f);
        }
        // tungsten (about 3200 K) dominant key spot, toggled for the exposure demo; practical bulb stays in frame for bloom / lens flare
        Color tungsten = new Color(1f, 0.72f, 0.45f);
        var lamp = MakeLight("Dominant key (spot, tungsten)", LightType.Spot, new Vector3(-3.0f, 3.8f, 8.0f), new Vector3(0, 0.8f, -2.0f), tungsten, 130f, 40, 55, LightShadows.Soft);
        lamp.shadowBias = 0.02f; lamp.shadowNormalBias = 0.05f;
        var bulb = Prim(PrimitiveType.Sphere, "Practical lamp bulb", new Vector3(1.3f, 1.75f, -3.4f), Vector3.one * 0.45f, EmissiveMat("Ex04_LampOn", tungsten, 12f));
        bulb.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        MakeLight("Practical glow (point)", LightType.Point, bulb.transform.position + new Vector3(0, -0.2f, 0.3f), Vector3.zero, tungsten, 6f, 7, 0, LightShadows.None);
        MakeLight("Window fill (spot, daylight)", LightType.Spot, new Vector3(4.0f, 2.5f, 7.0f), new Vector3(0, 0.8f, -2), new Color(0.75f, 0.85f, 1f), 45f, 30, 70, LightShadows.None);

        var cam = MakeCam(new Vector3(0.0f, 1.25f, 9.0f), new Vector3(0.0f, 1.2f, -1.5f), 70f);

        var prof = NewProfile("Ex04_Profile");
        SetTonemap(prof, TonemappingMode.ACES);
        var ca = Ov<ColorAdjustments>(prof);
        var bl = Ov<Bloom>(prof); bl.threshold.Override(1.0f); bl.intensity.Override(0f); bl.scatter.Override(0.7f);
        var vg = Ov<Vignette>(prof); vg.intensity.Override(0f); vg.smoothness.Override(0.5f); vg.rounded.Override(false);
        var wb = Ov<WhiteBalance>(prof); wb.temperature.Override(0f); wb.tint.Override(0f);
        var dof = Ov<DepthOfField>(prof); dof.mode.Override(DepthOfFieldMode.Off);
        var sslf = Ov<ScreenSpaceLensFlare>(prof); sslf.intensity.Override(0f);
        var mb = Ov<MotionBlur>(prof); mb.intensity.Override(0f); mb.active = false;
        SaveProfile(prof, "Assets/Settings/Ex04_Profile.asset");
        var vol = MakeVolume(prof);

        // play-mode capture helper (inactive without -rtcgCapture)
        var rc = new GameObject("RtcgPlayCapture").AddComponent<RtcgPlayCapture>();
        rc.cam = cam; rc.volume = vol;
        var pivot = new GameObject("Orbit pivot (play-mode motion blur demo)"); pivot.transform.position = new Vector3(0, 1.2f, -1.5f);
        rc.orbitPivot = pivot.transform; rc.orbitDegPerSec = 40f;

        Warm(cam);
        // helper for the pairs; baseline = tonemapped, nothing else
        float focus = Vector3.Distance(cam.transform.position, new Vector3(-0.75f, 1.0f, 2.0f)); // near pedestal
        Debug.Log("EX04 focus distance to middle pedestal " + focus);
        void Pair(string name, Action on, Action off)
        {
            off(); Shot(cam, $"{o}/{name}_off.png", 1280, 720);
            on(); Shot(cam, $"{o}/{name}_on.png", 1280, 720);
            off();
        }
        Action none = () => { };
        // baseline
        Shot(cam, $"{o}/00_baseline.png", 1280, 720);

        // 1 depth of field
        Pair("dof", () => { dof.mode.Override(DepthOfFieldMode.Bokeh); dof.focusDistance.Override(focus); dof.focalLength.Override(70f); dof.aperture.Override(1.4f); },
                    () => dof.mode.Override(DepthOfFieldMode.Off));
        dof.mode.Override(DepthOfFieldMode.Bokeh); dof.focusDistance.Override(focus); dof.focalLength.Override(70f);
        foreach (float ap in new[] { 1.4f, 2.8f, 5.6f, 11f }) { dof.aperture.Override(ap); Shot(cam, $"{o}/dof_aperture_f{ap}.png", 1280, 720); }
        dof.aperture.Override(1.4f);
        float[] fd = { focus, 10.5f, 14f };
        for (int i = 0; i < 3; i++) { dof.focusDistance.Override(fd[i]); Shot(cam, $"{o}/dof_focus_{i}_{fd[i]:0.0}m.png", 1280, 720); }
        dof.mode.Override(DepthOfFieldMode.Off);

        // 2 exposure (post exposure EV) and lamp on/off with emulated auto exposure
        foreach (float ev in new[] { -2f, 0f, 2f }) { ca.postExposure.Override(ev); Shot(cam, $"{o}/exposure_ev{ev:+0;-0;0}.png", 1280, 720); }
        ca.postExposure.Override(0f);
        float targetLuma = MeanLinearLuma($"{o}/00_baseline.png");
        var bulbMat = bulb.GetComponent<MeshRenderer>().sharedMaterial;
        lamp.enabled = false;
        Shot(cam, $"{o}/lamp_off_no_compensation.png", 1280, 720);
        float ev0 = 0f; float lum = MeanLinearLuma($"{o}/lamp_off_no_compensation.png");
        Debug.Log($"EX04 lamp on mean luma {targetLuma:F4}, lamp off {lum:F4}");
        for (int it = 0; it < 6; it++)
        {
            ev0 = Mathf.Clamp(ev0 + Mathf.Log(targetLuma / lum, 2f) * 0.85f, -5f, 6f);
            ca.postExposure.Override(ev0); Shot(cam, $"{o}/lamp_off_compensated.png", 1280, 720);
            lum = MeanLinearLuma($"{o}/lamp_off_compensated.png");
        }
        Debug.Log($"EX04 lamp-off compensation = {ev0:F2} EV (final luma {lum:F4} vs {targetLuma:F4})");
        File.WriteAllText($"{o}/emulated_auto_exposure.txt", $"mean linear luma lamp on {targetLuma:F4}\nlamp off uncompensated {MeanLinearLuma($"{o}/lamp_off_no_compensation.png"):F4}\ncompensation {ev0:F2} EV\nlamp off compensated {lum:F4}\n");
        lamp.enabled = true; ca.postExposure.Override(0f);

        // 3 bloom
        Pair("bloom", () => { bl.intensity.Override(0.8f); bl.threshold.Override(1.0f); bl.scatter.Override(0.7f); }, () => bl.intensity.Override(0f));
        // 4 lens flare (screen space)
        Pair("lensflare", () => { sslf.intensity.Override(2.0f); sslf.tintColor.Override(new Color(1f, 0.85f, 0.7f)); sslf.streaksIntensity.Override(1.0f); bl.intensity.Override(0.3f); bl.threshold.Override(1.0f); },
                          () => { sslf.intensity.Override(0f); bl.intensity.Override(0f); });
        // 5 vignette
        Pair("vignette", () => { vg.intensity.Override(0.45f); vg.smoothness.Override(0.55f); }, () => vg.intensity.Override(0f));
        // 6 white balance: lamp is 3000 K-ish warm; cool the image to compensate, and the other direction for contrast
        Shot(cam, $"{o}/wb_0_neutral.png", 1280, 720);
        wb.temperature.Override(-35f); Shot(cam, $"{o}/wb_1_cooler_minus35.png", 1280, 720);
        wb.temperature.Override(35f); Shot(cam, $"{o}/wb_2_warmer_plus35.png", 1280, 720);
        wb.temperature.Override(0f);
        // 7 tonemapping on HDR input
        var tm = Ov<Tonemapping>(prof);
        tm.mode.Override(TonemappingMode.None); Shot(cam, $"{o}/tonemap_none.png", 1280, 720);
        tm.mode.Override(TonemappingMode.ACES);
        // hero: combined, tasteful values
        dof.mode.Override(DepthOfFieldMode.Bokeh); dof.focusDistance.Override(focus); dof.aperture.Override(2.0f);
        bl.intensity.Override(0.5f); bl.threshold.Override(1.0f); vg.intensity.Override(0.3f); wb.temperature.Override(-12f);
        sslf.intensity.Override(0.7f); sslf.tintColor.Override(new Color(1f, 0.85f, 0.7f)); ca.postExposure.Override(0.3f);
        Shot(cam, $"{o}/hero_combined.png", 1920, 1080);
        // motion blur: leave the saved profile with blur off; play mode capture turns it on
        mb.active = true; mb.intensity.Override(0.0f);
        EditorUtility.SetDirty(prof); AssetDatabase.SaveAssets();
        SaveScene("Assets/Scenes/Ex04_Camera.unity");
        Debug.Log("EX04 done");
    }

    // ================= Ex6: Shader Graph hologram =================
    public static void BuildEx06()
    {
        EnsureHdrGrading();
        string o = "Captures/Ex06"; Dir(o);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var holoShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Ex06/NisseHologram.shadergraph");
        if (holoShader == null) { Debug.LogError("EX06 shadergraph did not import as a Shader"); return; }
        var msgs = ShaderUtil.GetShaderMessages(holoShader);
        foreach (var m in msgs) Debug.Log($"EX06 shader message [{m.severity}] {m.message} {m.messageDetails}");
        Debug.Log("EX06 shader supported=" + holoShader.isSupported + " messages=" + msgs.Length);

        RenderSettings.skybox = null; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.06f, 0.07f, 0.09f);
        var floorM = LitMat("Ex06_Floor", new Color(0.04f, 0.05f, 0.07f), 0.3f);
        Prim(PrimitiveType.Cube, "Floor", new Vector3(0, -0.05f, 0), new Vector3(80, 0.1f, 80), floorM);
        var baseM = EmissiveMat("Ex06_ProjectorBase", new Color(0.1f, 0.75f, 1f), 3f);
        Prim(PrimitiveType.Cylinder, "Projector base", new Vector3(1.0f, 0.02f, 0), new Vector3(0.9f, 0.02f, 0.9f), baseM);

        var plain = PlaceNisse(new Vector3(-0.9f, 0, 0), 0);
        var holo = PlaceNisse(new Vector3(1.0f, 0.04f, 0), 0);
        Dir("Assets/Materials/Part2");
        var hm = new Material(holoShader); AssetDatabase.DeleteAsset(MatDir + "/Ex06_Hologram.mat"); AssetDatabase.CreateAsset(hm, MatDir + "/Ex06_Hologram.mat");
        foreach (var r in holo.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = hm;

        MakeLight("Key", LightType.Directional, Vector3.zero, Vector3.zero, new Color(0.7f, 0.8f, 1f), 0.8f, 30, 0, LightShadows.Soft).transform.rotation = Quaternion.Euler(40, 150, 0);
        MakeLight("Cyan glow", LightType.Point, new Vector3(1.0f, 0.5f, 0.6f), Vector3.zero, new Color(0.1f, 0.75f, 1f), 1.2f, 6, 0, LightShadows.None);
        var cam = MakeCam(new Vector3(0.0f, 0.85f, 4.2f), new Vector3(0.05f, 0.5f, 0), 50f);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.01f, 0.012f, 0.02f);

        var prof = NewProfile("Ex06_Profile");
        SetTonemap(prof, TonemappingMode.ACES);
        var bl = Ov<Bloom>(prof); bl.threshold.Override(0.9f); bl.intensity.Override(1.0f); bl.scatter.Override(0.65f);
        var vg = Ov<Vignette>(prof); vg.intensity.Override(0.3f);
        SaveProfile(prof, "Assets/Settings/Ex06_Profile.asset");
        var vol = MakeVolume(prof);
        var rc = new GameObject("RtcgPlayCapture").AddComponent<RtcgPlayCapture>();
        rc.cam = cam; rc.volume = vol; rc.orbitPivot = null; rc.spinTarget = holo.transform; rc.spinDegPerSec = 120f;

        Warm(cam);
        Shot(cam, $"{o}/hologram_vs_original.png", 1600, 900);
        // close-ups (still frames, scroll phase is whatever _Time gives in edit mode)
        cam.transform.position = new Vector3(1.0f, 0.7f, 2.3f); cam.transform.LookAt(new Vector3(1.0f, 0.5f, 0)); Shot(cam, $"{o}/hologram_closeup.png", 1280, 720);
        // property variants: dense fine scanlines / rim only
        cam.transform.position = new Vector3(0.05f, 0.85f, 4.2f); cam.transform.LookAt(new Vector3(0.05f, 0.5f, 0));
        hm.SetFloat("_ScanDensity", 160f); hm.SetFloat("_RimPower", 1.2f); Shot(cam, $"{o}/variant_dense_scanlines_wide_rim.png", 1600, 900);
        hm.SetFloat("_ScanDensity", 25f); hm.SetFloat("_RimPower", 5f); hm.SetColor("_HoloColor", new Color(1f, 0.35f, 0.1f)); Shot(cam, $"{o}/variant_coarse_orange.png", 1600, 900);
        hm.SetFloat("_ScanDensity", 60f); hm.SetFloat("_RimPower", 2.5f); hm.SetColor("_HoloColor", new Color(0.1f, 0.75f, 1f));
        EditorUtility.SetDirty(hm); AssetDatabase.SaveAssets();
        SaveScene("Assets/Scenes/Ex06_ShaderGraph.unity");
        Debug.Log("EX06 done");
    }

    // ================= Ex7: code shader =================
    public static void BuildEx07()
    {
        EnsureHdrGrading();
        string o = "Captures/Ex07"; Dir(o);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var sh = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Ex07/NisseWobbleToon.shader");
        if (sh == null) { Debug.LogError("EX07 shader missing"); return; }
        var msgs = ShaderUtil.GetShaderMessages(sh);
        foreach (var m in msgs) Debug.Log($"EX07 shader message [{m.severity}] {m.message} {m.messageDetails}");
        Debug.Log("EX07 shader supported=" + sh.isSupported + " messages=" + msgs.Length);

        var sky = BuildStudioSky("Ex03_StudioEnv", EnvColor); ApplyEnv(sky, 0.6f, 0.5f);
        MakeCyc(LitMat("Ex07_Backdrop", new Color(0.55f, 0.58f, 0.62f), 0.1f), -3f);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Nisse/Nisse_diffuse.jpeg");
        Material Toon(string name, float amp, float phase)
        {
            var m = new Material(sh); m.SetTexture("_BaseMap", tex); m.SetFloat("_Amplitude", amp); m.SetFloat("_Phase", phase);
            m.SetVector("_Axis", new Vector4(1f, 0.6f, 1f, 0f)); m.SetFloat("_Frequency", 28f);
            AssetDatabase.DeleteAsset($"{MatDir}/{name}.mat"); AssetDatabase.CreateAsset(m, $"{MatDir}/{name}.mat"); return m;
        }
        Dir(MatDir);
        var mWobbleToon = Toon("Ex07_WobbleToon", 0.022f, 0f);
        var mToonOnly = Toon("Ex07_ToonOnly", 0f, 0f);

        var g = PlaceNisse(Vector3.zero, 0);
        var sun = MakeLight("Sun", LightType.Directional, Vector3.zero, Vector3.zero, new Color(1f, 0.96f, 0.9f), 1.6f, 30, 0, LightShadows.Soft);
        sun.transform.rotation = Quaternion.Euler(38, 135, 0); sun.shadowStrength = 0.9f;
        var cam = MakeCam(new Vector3(-0.3f, 0.7f, 3.4f), new Vector3(0, 0.48f, 0), 60f);
        var prof = NewProfile("Ex07_Profile"); SetTonemap(prof, TonemappingMode.Neutral);
        var bl = Ov<Bloom>(prof); bl.intensity.Override(0.1f); bl.threshold.Override(1.2f);
        SaveProfile(prof, "Assets/Settings/Ex07_Profile.asset");
        var vol = MakeVolume(prof);
        var rc = new GameObject("RtcgPlayCapture").AddComponent<RtcgPlayCapture>(); rc.cam = cam; rc.volume = vol; rc.spinTarget = g.transform; rc.spinDegPerSec = 90f;

        void Apply(Material m) { foreach (var r in g.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = m; Warm(cam); }
        Warm(cam);
        // before: the stock URP Lit material the prefab ships with
        Shot(cam, $"{o}/0_before_urp_lit.png", 1280, 720);
        Apply(mToonOnly); Shot(cam, $"{o}/1_cell_shade_only.png", 1280, 720);
        foreach (float ph in new[] { 0f, 1.57f, 3.14f, 4.71f }) { mWobbleToon.SetFloat("_Phase", ph); Apply(mWobbleToon); Shot(cam, $"{o}/2_combined_phase{ph:0.00}.png", 1280, 720); }
        mWobbleToon.SetFloat("_Phase", 0f); mWobbleToon.SetFloat("_Amplitude", 0.06f); Shot(cam, $"{o}/3_combined_exaggerated_amp0.06.png", 1280, 720);
        mWobbleToon.SetFloat("_Amplitude", 0.022f);
        EditorUtility.SetDirty(mWobbleToon); AssetDatabase.SaveAssets();
        SaveScene("Assets/Scenes/Ex07_CodeShaders.unity");
        Debug.Log("EX07 done");
    }

    // ================= launcher for play-mode captures =================
    public static void PlayCapture()
    {
        var a = Environment.GetCommandLineArgs(); string scene = "";
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-rtcgScene") scene = a[i + 1];
        EditorSceneManager.OpenScene(scene);
        EditorApplication.isPlaying = true;
    }

    public static void DiagHdr()
    {
        EnsureHdrGrading();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.skybox = null;
        var cam = MakeCam(Vector3.zero, new Vector3(0, 0, 5), 50f); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        var prof = NewProfile("diag"); var vol = MakeVolume(prof);
        var tm = Ov<Tonemapping>(prof); tm.mode.Override(TonemappingMode.ACES);
        var g = Prim(PrimitiveType.Sphere, "b", new Vector3(0, 0, 5), Vector3.one * 2, EmissiveMat("diag_emit", Color.white, 8f));
        void Read(string tag)
        {
            var rt = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            cam.targetTexture = rt; cam.aspect = 1; cam.Render();
            RenderTexture.active = rt; var t = new Texture2D(64, 64, TextureFormat.RGBAHalf, false); t.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); t.Apply();
            Debug.Log($"DIAGHDR {tag} -> {t.GetPixel(32, 32).r:F3}");
            RenderTexture.active = null; cam.targetTexture = null;
        }
        Read("tonemap only (expect 0.971)");
        Ov<ColorAdjustments>(prof); Read("+ColorAdjustments");
        var bl = Ov<Bloom>(prof); bl.intensity.Override(0f); Read("+Bloom i=0");
        bl.intensity.Override(2.5f); bl.threshold.Override(1f); Read("Bloom i=2.5");
        var vg = Ov<Vignette>(prof); vg.intensity.Override(0f); Read("+Vignette");
        var wb = Ov<WhiteBalance>(prof); Read("+WhiteBalance");
        var d = Ov<DepthOfField>(prof); d.mode.Override(DepthOfFieldMode.Off); Read("+DoF off");
        var sl = Ov<ScreenSpaceLensFlare>(prof); sl.intensity.Override(0f); Read("+SSLF 0");
        var mb = Ov<MotionBlur>(prof); mb.intensity.Override(0f); mb.active = false; Read("+MotionBlur inactive");
        sl.intensity.Override(8f); Read("SSLF 8");
    }

    public static void DiagSpotShadow()
    {
        EnsureHdrGrading();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
        Prim(PrimitiveType.Cube, "floor", new Vector3(0, -0.5f, 0), new Vector3(20, 1, 20), LitMat("d1", Color.white, 0.1f));
        Prim(PrimitiveType.Cube, "box", new Vector3(0, 0.5f, 0), Vector3.one, LitMat("d2", Color.red, 0.1f));
        var cam = MakeCam(new Vector3(0, 6, -6), Vector3.zero, 35f);
        var lgo = new GameObject("spot"); lgo.transform.position = new Vector3(3, 4, -1); lgo.transform.LookAt(Vector3.zero);
        var l = lgo.AddComponent<Light>(); l.type = LightType.Spot; l.intensity = 80; l.range = 30; l.spotAngle = 50;
        var d = lgo.GetComponent<UniversalAdditionalLightData>() ?? lgo.AddComponent<UniversalAdditionalLightData>();
        var asset = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        Debug.Log("DIAGSPOT addShadowsSupported=" + asset.supportsAdditionalLightShadows + " mode=" + asset.additionalLightsRenderingMode + " res=" + asset.additionalLightsShadowmapResolution);
        Warm(cam);
        l.shadows = LightShadows.None; Shot(cam, "Captures/_ds/none.png", 480, 270);
        l.shadows = LightShadows.Soft; Shot(cam, "Captures/_ds/soft_default.png", 480, 270);
        l.shadows = LightShadows.Hard; Shot(cam, "Captures/_ds/hard_default.png", 480, 270);
        l.shadows = LightShadows.Soft; l.shadowBias = 0.01f; l.shadowNormalBias = 0.02f; Shot(cam, "Captures/_ds/soft_bias.png", 480, 270);
        var so = new SerializedObject(d); so.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = 2; so.FindProperty("m_UsePipelineSettings").boolValue = false; so.ApplyModifiedPropertiesWithoutUndo();
        Shot(cam, "Captures/_ds/soft_tier2_custom.png", 480, 270);
        so.FindProperty("m_UsePipelineSettings").boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); Shot(cam, "Captures/_ds/soft_tier2_pipeline.png", 480, 270);
    }

    public static void ImportOnly() { AssetDatabase.Refresh(); Debug.Log("IMPORT done"); }
}
