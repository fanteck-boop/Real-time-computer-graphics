using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Exercise 5 (shader graph effects on the Nisse scan): builds "Ex 5 Shout" and "Ex 5 Shadow" and renders
// contact-sheet frames. Run headless: Tools/unity.sh RtcgEx5Effects.BuildShout <log>, etc.
// The graphs themselves are written by Tools/make_shout_graph.py and Tools/make_shadow_graph.py.
public static class RtcgEx5Effects
{
    const string Dir5 = "Assets/Shaders/Ex05";
    const string ShoutScene = "Assets/Scenes/Ex 5 Shout.unity";
    const string ShadowScene = "Assets/Scenes/Ex 5 Shadow.unity";

    // ---------------- shared ----------------
    static Shader LoadGraph(string file)
    {
        string p = $"{Dir5}/{file}";
        AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
        var sh = AssetDatabase.LoadAssetAtPath<Shader>(p);
        if (sh == null) { Debug.LogError("EX5 graph did not import as a Shader: " + p); return null; }
        var msgs = ShaderUtil.GetShaderMessages(sh);
        foreach (var m in msgs) Debug.Log($"EX5 shader message {file} [{m.severity}] {m.message} {m.messageDetails} line {m.line}");
        Debug.Log($"EX5 shader {sh.name} supported={sh.isSupported} hasError={ShaderUtil.ShaderHasError(sh)} messages={msgs.Length}");
        return sh;
    }

    static Material SaveMat(Material m, string name)
    {
        string p = $"{Dir5}/{name}.mat";
        AssetDatabase.DeleteAsset(p); AssetDatabase.CreateAsset(m, p); return m;
    }

    static Texture2D NisseTex()
    {
        var nm = AssetDatabase.LoadAssetAtPath<Material>("Assets/Nisse/Nisse.mat");
        var t = nm != null ? nm.GetTexture("_BaseMap") as Texture2D : null;
        return t != null ? t : AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Nisse/Nisse_diffuse.jpeg");
    }

    // Nisse.obj instanced, 1 unit tall, feet on the floor, base centred on the root, facing -Z (the camera).
    static GameObject PlaceNisse(Material m)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Nisse/Nisse.obj");
        var mf = model.GetComponentInChildren<MeshFilter>();
        Bounds b = mf.sharedMesh.bounds;
        float s = 1f / b.size.y;
        var root = new GameObject("Nisse");
        var child = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        child.name = "Nisse mesh";
        child.transform.localScale = Vector3.one * s;
        child.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z) * s;
        foreach (var r in child.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = m;
        root.transform.rotation = Quaternion.Euler(0, 180, 0);
        Debug.Log($"EX5 Nisse bounds {b} scale {s}");
        return root;
    }

    static Texture2D GridTex()
    {
        string p = $"{Dir5}/Ex5_GridTex.asset";
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        if (t != null) return t;
        const int n = 256;
        t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Ex5_GridTex", wrapMode = TextureWrapMode.Repeat, anisoLevel = 8 };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            bool major = x < 4 || y < 4; bool minor = (x % 64) < 2 || (y % 64) < 2;
            byte v = major ? (byte)200 : minor ? (byte)90 : (byte)28;
            px[y * n + x] = new Color32(v, v, v, 255);
        }
        t.SetPixels32(px); t.Apply(true);
        AssetDatabase.CreateAsset(t, p);
        return t;
    }

    static Material LitMat(string name, Color c, float smooth, Texture2D tex = null, float tiling = 1f)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
        if (tex != null) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", Vector2.one * tiling); }
        return SaveMat(m, name);
    }

    static GameObject Prim(PrimitiveType t, string name, Vector3 pos, Vector3 scale, Material m)
    {
        var g = GameObject.CreatePrimitive(t); g.name = name;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.position = pos; g.transform.localScale = scale;
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        return g;
    }

    static Camera MakeCam(Vector3 pos, Vector3 target, float focalMm, Color bg)
    {
        var go = new GameObject("Main Camera"); go.tag = "MainCamera";
        var cam = go.AddComponent<Camera>();
        cam.usePhysicalProperties = true; cam.sensorSize = new Vector2(36f, 20.25f); cam.focalLength = focalMm;
        cam.nearClipPlane = 0.05f; cam.farClipPlane = 60f; cam.allowHDR = true;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = bg;
        go.AddComponent<AudioListener>();
        var d = go.AddComponent<UniversalAdditionalCameraData>();
        d.renderPostProcessing = true;
        d.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        go.transform.position = pos; go.transform.LookAt(target);
        return cam;
    }

    static Light KeyLight(Vector3 euler, Color c, float intensity)
    {
        var go = new GameObject("Key light"); go.transform.rotation = Quaternion.Euler(euler);
        var l = go.AddComponent<Light>(); l.type = LightType.Directional; l.color = c; l.intensity = intensity;
        l.shadows = LightShadows.Soft; l.shadowBias = 0.02f; l.shadowNormalBias = 0.1f;
        go.AddComponent<UniversalAdditionalLightData>();
        return l;
    }

    // Bloom + ACES only, so the HDR ring and edge colours glow. One small shared profile, not a pipeline asset.
    static void MakeVolume()
    {
        string p = $"{Dir5}/Ex5_Effects_Profile.asset";
        var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(p);
        if (prof == null)
        {
            prof = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(prof, p);
            var bl = prof.Add<Bloom>(false); bl.threshold.Override(1.0f); bl.intensity.Override(0.9f); bl.scatter.Override(0.6f);
            var tm = prof.Add<Tonemapping>(false); tm.mode.Override(TonemappingMode.ACES);
            foreach (var c in prof.components) { AssetDatabase.AddObjectToAsset(c, prof); EditorUtility.SetDirty(c); }
            EditorUtility.SetDirty(prof); AssetDatabase.SaveAssets();
        }
        var v = new GameObject("Post Volume (bloom + ACES)").AddComponent<Volume>(); v.isGlobal = true; v.sharedProfile = prof;
    }

    static void Stage(Color ambient)
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = ambient;
        var grid = GridTex();
        var floorM = LitMat("Ex5_Floor", new Color(0.16f, 0.16f, 0.18f), 0.35f, grid, 12f);
        Prim(PrimitiveType.Cube, "Floor", new Vector3(0, -0.05f, 0), new Vector3(12, 0.1f, 12), floorM);
        var wallM = LitMat("Ex5_Backdrop", new Color(0.12f, 0.12f, 0.14f), 0.2f, grid, 8f);
        Prim(PrimitiveType.Cube, "Backdrop", new Vector3(0, 2.5f, 2.2f), new Vector3(12, 5f, 0.1f), wallM);
    }

    static void SaveScene(string path) { EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), path); }

    // ---------------- A: shout ----------------
    [MenuItem("RTCG/Ex5/Build Shout scene")]
    public static void BuildShout()
    {
        var sh = LoadGraph("NisseShout.shadergraph");
        var shellSh = LoadGraph("NisseShoutShell.shadergraph");
        if (sh == null || shellSh == null) return;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Stage(new Color(0.09f, 0.09f, 0.11f));

        var m = new Material(sh); m.SetTexture("_BaseMap", NisseTex()); SaveMat(m, "NisseShout");
        var nisse = PlaceNisse(m);
        var originGO = new GameObject("Shout origin"); originGO.transform.position = new Vector3(-0.9f, 0.55f, -0.35f);

        var shellM = SaveMat(new Material(shellSh), "NisseShoutShell");
        var shell = Prim(PrimitiveType.Sphere, "Shout shell", originGO.transform.position, Vector3.one * 0.1f, shellM);
        var sr = shell.GetComponent<MeshRenderer>(); sr.shadowCastingMode = ShadowCastingMode.Off; sr.receiveShadows = false;

        KeyLight(new Vector3(35, 25, 0), new Color(1f, 0.95f, 0.88f), 1.3f);
        MakeCam(new Vector3(-0.3f, 0.7f, -2.9f), new Vector3(-0.25f, 0.5f, 0f), 32f, new Color(0.012f, 0.012f, 0.018f));
        MakeVolume();

        var drv = new GameObject("Shout driver").AddComponent<NisseShoutDriver>();
        drv.targets = nisse.GetComponentsInChildren<MeshRenderer>();
        drv.shell = sr; drv.origin = originGO.transform;
        drv.speed = 1.0f; drv.life = 2.2f; drv.period = 3.2f; drv.delay = 0.4f;
        drv.Apply(0.8f);
        AssetDatabase.SaveAssets();
        SaveScene(ShoutScene);
        Debug.Log("EX5 shout scene saved");
    }

    [MenuItem("RTCG/Ex5/Capture Shout frames")]
    public static void CaptureShout()
    {
        EditorSceneManager.OpenScene(ShoutScene);
        var drv = Object.FindAnyObjectByType<NisseShoutDriver>();
        var cam = Camera.main;
        string o = "Captures/Ex05_Shout";
        Directory.CreateDirectory(o);
        Warm(cam);
        float[] ts = { 0.3f, 0.7f, 0.85f, 1.0f, 1.15f, 1.6f };
        foreach (float t in ts) { drv.Apply(t); Shot(cam, $"{o}/shout_t{t:0.00}.png", 960, 540); }
        drv.Apply(-1f); Shot(cam, $"{o}/shout_idle.png", 960, 540);
        // shell off, to show what the distortion layer adds
        drv.Apply(0.85f); drv.shell.enabled = false; Shot(cam, $"{o}/shout_t0.85_noshell.png", 960, 540);
        // close-up on the ring crossing the body
        cam.transform.position = new Vector3(0.0f, 0.6f, -1.6f); cam.transform.LookAt(new Vector3(0f, 0.5f, 0f));
        drv.Apply(0.85f); Shot(cam, $"{o}/shout_closeup_t0.85.png", 1280, 720);
        Debug.Log("EX5 shout capture done");
    }

    // ---------------- B: shadow ----------------
    // Soft smoke puff: radial falloff times a few octaves of Perlin noise, white RGB, shape in alpha.
    static Texture2D SmokeTex()
    {
        string p = $"{Dir5}/Ex5_SmokePuff.asset";
        AssetDatabase.DeleteAsset(p);
        const int n = 128;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Ex5_SmokePuff", wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
            float r = Mathf.Sqrt(u * u + v * v);
            float fall = Mathf.Clamp01(1f - r); fall = fall * fall * (3f - 2f * fall);
            float nz = 0f, amp = 0.5f, f = 1.6f;
            for (int o = 0; o < 4; o++) { nz += amp * Mathf.PerlinNoise(x / (float)n * f + 17.3f, y / (float)n * f + 5.1f); amp *= 0.5f; f *= 2f; }
            float a = Mathf.Clamp01(Mathf.Pow(fall, 1.4f) * (0.5f + 0.9f * nz) - 0.04f);
            float shade = 0.8f + 0.2f * nz;
            px[y * n + x] = new Color(shade, shade, shade, a);
        }
        t.SetPixels(px); t.Apply(true);
        AssetDatabase.CreateAsset(t, p);
        return t;
    }

    // Nisse.obj is imported with Read/Write off and its import settings must not change, so the particle
    // Shape module gets a readable copy of the mesh (the editor can still read the original).
    static Mesh EmitMesh(Mesh src)
    {
        string p = $"{Dir5}/Ex5_NisseEmitMesh.asset";
        AssetDatabase.DeleteAsset(p);
        var m = new Mesh { name = "Ex5_NisseEmitMesh", indexFormat = IndexFormat.UInt32 };
        m.vertices = src.vertices; m.normals = src.normals; m.triangles = src.triangles;
        m.RecalculateBounds();
        AssetDatabase.CreateAsset(m, p);
        Debug.Log($"EX5 emit mesh copied: verts {m.vertexCount} tris {m.triangles.Length / 3} readable {m.isReadable}");
        return m;
    }

    static Material ParticleMat(string graph, string name, Texture2D tex)
    {
        var sh = LoadGraph(graph);
        var m = new Material(sh); m.SetTexture("_MainTex", tex);
        return SaveMat(m, name);
    }

    static ParticleSystem SurfaceEmitter(string name, Transform meshT, Mesh emit, Material mat, float normalOffset)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(meshT.position, meshT.rotation);
        go.transform.localScale = meshT.lossyScale;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.duration = 10f; main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;   // shape follows the Nisse scale, particle sizes stay in metres
        var sh = ps.shape;
        sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Mesh; sh.mesh = emit;
        sh.meshShapeType = ParticleSystemMeshShapeType.Triangle; sh.normalOffset = normalOffset;
        var em = ps.emission; em.enabled = true; em.rateOverTime = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.sortMode = ParticleSystemSortMode.Distance;
        return ps;
    }

    static Gradient Fade(float inT, float midT, float midA)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inT), new GradientAlphaKey(midA, midT), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    static ParticleSystem MakeSmoke(Transform meshT, Mesh emit, Material mat)
    {
        var ps = SurfaceEmitter("Smoke (puffs)", meshT, emit, mat, 0.02f);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.36f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.08f, 0.06f, 0.11f, 0.5f), new Color(0.2f, 0.16f, 0.25f, 0.35f));
        main.maxParticles = 3000;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f); vel.y = new ParticleSystem.MinMaxCurve(0.2f, 0.42f); vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        var nz = ps.noise; nz.enabled = true; nz.strength = 0.2f; nz.frequency = 1.3f; nz.scrollSpeed = 0.3f; nz.octaveCount = 2; nz.quality = ParticleSystemNoiseQuality.High; nz.damping = true;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.9f)));
        var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(0.2f, 0.5f, 0.75f);
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
        return ps;
    }

    static ParticleSystem MakeSpecks(Transform meshT, Mesh emit, Material mat)
    {
        var ps = SurfaceEmitter("Specks (remaining particles)", meshT, emit, mat, 0.004f);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 4.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.03f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.02f);
        var pal = new Gradient { mode = GradientMode.Fixed };
        pal.SetKeys(new[] { new GradientColorKey(new Color(0.48f, 0.22f, 0.85f), 0.55f), new GradientColorKey(new Color(0.75f, 0.06f, 0.1f), 0.95f), new GradientColorKey(new Color(1f, 0.78f, 0.55f), 1f) },
                    new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0.75f, 1f) });
        main.startColor = new ParticleSystem.MinMaxGradient(pal) { mode = ParticleSystemGradientMode.RandomColor };
        main.maxParticles = 14000;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.015f, 0.015f); vel.y = new ParticleSystem.MinMaxCurve(0.02f, 0.07f); vel.z = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
        var nz = ps.noise; nz.enabled = true; nz.strength = 0.05f; nz.frequency = 1.5f; nz.scrollSpeed = 0.2f; nz.quality = ParticleSystemNoiseQuality.High; nz.damping = true;
        var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(0.04f, 0.45f, 0.7f);
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.45f)));
        return ps;
    }

    [MenuItem("RTCG/Ex5/Build Shadow scene")]
    public static void BuildShadow()
    {
        var sh = LoadGraph("NisseShadow.shadergraph");
        if (sh == null) return;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // lighter stage than the shout so dark smoke reads against it (own materials; the shout scene keeps its own)
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.2f, 0.2f, 0.24f);
        var floorM = LitMat("Ex5_ShadowFloor", new Color(0.3f, 0.31f, 0.36f), 0.25f);
        Prim(PrimitiveType.Cube, "Floor", new Vector3(0, -0.05f, 0), new Vector3(12, 0.1f, 12), floorM);
        var wallM = LitMat("Ex5_ShadowBackdrop", new Color(0.42f, 0.44f, 0.5f), 0.1f);
        Prim(PrimitiveType.Cube, "Backdrop", new Vector3(0, 2.5f, 2.2f), new Vector3(12, 5f, 0.1f), wallM);

        var m = new Material(sh); m.SetTexture("_BaseMap", NisseTex()); SaveMat(m, "NisseShadow");
        var nisse = PlaceNisse(m);
        KeyLight(new Vector3(35, 25, 0), new Color(1f, 0.96f, 0.9f), 1.4f);
        var back = new GameObject("Back light").AddComponent<Light>(); back.type = LightType.Directional;
        back.transform.rotation = Quaternion.Euler(25, 200, 0); back.color = new Color(0.75f, 0.7f, 1f); back.intensity = 1.1f;
        back.gameObject.AddComponent<UniversalAdditionalLightData>();
        var pool = new GameObject("Backdrop spot").AddComponent<Light>(); pool.type = LightType.Spot;
        pool.transform.position = new Vector3(0f, 2.4f, -0.6f); pool.transform.LookAt(new Vector3(0f, 0.9f, 2.2f));
        pool.spotAngle = 70f; pool.innerSpotAngle = 25f; pool.range = 8f; pool.intensity = 5f; pool.color = new Color(0.85f, 0.85f, 1f);
        pool.gameObject.AddComponent<UniversalAdditionalLightData>();
        MakeCam(new Vector3(0f, 0.8f, -2.6f), new Vector3(0f, 0.68f, 0f), 35f, new Color(0.2f, 0.2f, 0.24f));
        MakeVolume();

        var meshR = nisse.GetComponentInChildren<MeshRenderer>();
        var emit = EmitMesh(meshR.GetComponent<MeshFilter>().sharedMesh);
        var puff = SmokeTex();
        var smoke = MakeSmoke(meshR.transform, emit, ParticleMat("NisseShadowSmoke.shadergraph", "NisseShadowSmoke", puff));
        var specks = MakeSpecks(meshR.transform, emit, ParticleMat("NisseShadowSpecks.shadergraph", "NisseShadowSpecks", puff));

        var drv = new GameObject("Shadow driver").AddComponent<NisseShadowDriver>();
        drv.targets = nisse.GetComponentsInChildren<MeshRenderer>();
        drv.smoke = smoke; drv.specks = specks;
        drv.Apply(0f);
        AssetDatabase.SaveAssets();
        SaveScene(ShadowScene);
        Debug.Log("EX5 shadow scene saved");
    }

    // Edit-mode capture: steps the driver and both particle systems at 30 fps from the start of the loop
    // (fixed random seed) and renders the strip frames on the way.
    [MenuItem("RTCG/Ex5/Capture Shadow frames")]
    public static void CaptureShadow()
    {
        EditorSceneManager.OpenScene(ShadowScene);
        var drv = Object.FindAnyObjectByType<NisseShadowDriver>();
        var cam = Camera.main;
        string o = "Captures/Ex05_Shadow";
        Directory.CreateDirectory(o);
        foreach (var f in Directory.GetFiles(o, "*.png")) File.Delete(f);
        drv.Apply(0f);
        Warm(cam);
        var systems = new[] { drv.smoke, drv.specks };
        foreach (var ps in systems)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false; ps.randomSeed = 1234;
            ps.Simulate(0f, true, true, false);
        }
        var shots = new (float t, string name)[] {
            (0.8f, "1_solid"), (1.98f, "2_dissolve25"), (2.5f, "3_dissolve50"), (3.02f, "4_dissolve75"),
            (4.3f, "5_gone_specks"), (5.6f, "6_specks_drifting"), (6.8f, "7_reforming"), (8.7f, "8_solid_again") };
        const float dt = 1f / 30f;
        int next = 0; float t = 0f;
        Vector3 camPos = cam.transform.position; Quaternion camRot = cam.transform.rotation;
        while (next < shots.Length)
        {
            t += dt;
            drv.Apply(t);
            foreach (var ps in systems) ps.Simulate(dt, true, false, false);
            if (t >= shots[next].t - 1e-4f)
            {
                Shot(cam, $"{o}/shadow_{shots[next].name}_t{t:0.00}.png", 960, 540);
                if (shots[next].name == "4_dissolve75" || shots[next].name == "5_gone_specks")
                {
                    cam.transform.position = new Vector3(0.0f, 0.75f, -1.5f); cam.transform.LookAt(new Vector3(0f, 0.65f, 0f));
                    Shot(cam, $"{o}/shadow_closeup_{shots[next].name}.png", 1280, 720);
                    cam.transform.SetPositionAndRotation(camPos, camRot);
                }
                Debug.Log($"EX5 t={t:0.00} d={drv.DissolveAt(t, out _):0.00} smoke={drv.smoke.particleCount} specks={drv.specks.particleCount}");
                next++;
            }
        }
        Debug.Log("EX5 shadow capture done");
    }

    // ---------------- rendering ----------------
    // Half-float target so HDR survives into bloom and tonemapping (an 8-bit target makes URP drop to LDR).
    static void Shot(Camera cam, string path, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        cam.targetTexture = rt; cam.aspect = (float)w / h; cam.Render();
        RenderTexture.active = rt;
        var t = new Texture2D(w, h, TextureFormat.RGBAHalf, false); t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
        RtcgPlayCapture.WritePngFromHalf(t, path);
        RenderTexture.active = null; cam.targetTexture = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(t);
        Debug.Log("WROTE " + path);
    }

    // The first renders of a session come out untextured while textures and shaders load; throw a few away.
    static void Warm(Camera cam)
    {
        for (int i = 0; i < 4; i++) Shot(cam, "Captures/_warm_ex5.png", 160, 90);
        File.Delete("Captures/_warm_ex5.png");
    }
}
