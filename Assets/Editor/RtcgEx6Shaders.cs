using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Ex 6: two code shaders on the Nisse.
//   Shader 1: object outline (Ronja's Shader Tutorials, tutorial 052), ported to a URP Render Graph renderer feature.
//   Shader 2: scrolling texture (adrian-miasik/unity-shaders, "Scrolling Texture.shader"), ported to URP HLSL.
// Run headless: RtcgEx6Shaders.BuildScene (textures, materials, renderer feature, scene), then RtcgEx6Shaders.CaptureSteps.
public static class RtcgEx6Shaders
{
    const string ScenePath = "Assets/Scenes/Ex 6 Shaders.unity";
    const string Dir = "Assets/Shaders/Ex6/";
    const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    const string OutDir = "Captures/Ex06_Steps";

    // ---------------- textures ----------------
    static Texture2D SavePng(string name, int size, System.Func<float, float, Color> f)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                t.SetPixel(x, y, f((x + 0.5f) / size, (y + 0.5f) / size));
        string path = Dir + name + ".png";
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = true;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static float Smooth(float edge, float w, float v) => Mathf.Clamp01((v - edge) / w + 0.5f);

    // Diagonal candy-cane stripes: white where the stripe is (alpha 1), transparent between. Seamless.
    static Texture2D MakeStripes() => SavePng("Ex6_Stripes", 256, (u, v) =>
    {
        float s = Mathf.Repeat((u + v) * 2f, 1f);                 // 2 stripes per tile
        float a = Smooth(0.0f, 0.02f, s) * (1f - Smooth(0.45f, 0.02f, s));
        return new Color(1, 1, 1, a);
    });

    // Conveyor belt: dark rubber, light edges, yellow chevrons pointing left (the way the original scroll moves the image).
    static Texture2D MakeBelt() => SavePng("Ex6_Belt", 256, (u, v) =>
    {
        var rubber = new Color(0.13f, 0.13f, 0.14f, 1);
        var edge = new Color(0.55f, 0.55f, 0.58f, 1);
        var yellow = new Color(1f, 0.78f, 0.1f, 1);
        if (v < 0.08f || v > 0.92f) return edge;
        float s = Mathf.Repeat(u - Mathf.Abs(v - 0.5f) * 0.9f, 1f);
        float a = Smooth(0.0f, 0.02f, s) * (1f - Smooth(0.3f, 0.02f, s));
        return Color.Lerp(rubber, yellow, a);
    });

    // ---------------- materials ----------------
    static Material NewMat(string name, Shader shader)
    {
        string path = Dir + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(shader) { name = name };
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Material Lit(string name, Color c, float smooth)
    {
        var m = NewMat(name, Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0);
        return m;
    }

    public static void MakeAssets()
    {
        AssetDatabase.Refresh();
        Directory.CreateDirectory(Dir);
        var stripes = MakeStripes();
        var belt = MakeBelt();

        NewMat("Ex6_WriteObject", Shader.Find("RTCG/Ex6/Write Object"));
        var apply = NewMat("Ex6_ApplyOutline", Shader.Find("RTCG/Ex6/Apply Outline"));
        apply.SetColor("_OutlineColor", new Color(1f, 0.62f, 0.12f, 1f));
        apply.SetFloat("_OutlineWidth", 0.0042f);   // 3 pixels at 720 pixels height
        apply.SetFloat("_DebugSelection", 0);

        var nisseTex = AssetDatabase.LoadAssetAtPath<Material>("Assets/Nisse/Nisse.mat").GetTexture("_BaseMap");
        var np = NewMat("Ex6_NissePattern", Shader.Find("RTCG/Ex6/Nisse Scrolling Pattern"));
        np.SetTexture("_BaseMap", nisseTex);
        np.SetTexture("_Texture", stripes);
        np.SetColor("_PatternColor", new Color(0.10f, 0.55f, 0.22f, 1f));   // green, shows on both the red hat and the white beard
        np.SetFloat("_PatternStrength", 0.75f);
        np.SetFloat("_VerticalSpeed", 0.2f);
        np.SetFloat("_HorizontalSpeed", 0f);
        np.SetFloat("_Scale", 4f);

        var bm = NewMat("Ex6_Belt", Shader.Find("RTCG/Ex6/Scrolling Texture"));
        bm.SetTexture("_Texture", belt);
        bm.SetTextureScale("_Texture", new Vector2(4, 1));   // quad is 4 times wider than tall, keep the tiles square
        bm.SetFloat("_VerticalSpeed", 0f);
        bm.SetFloat("_HorizontalSpeed", 0.2f);
        bm.SetFloat("_Scale", 1f);

        Lit("Ex6_Floor", new Color(0.78f, 0.76f, 0.72f), 0.15f);
        Lit("Ex6_Wall", new Color(0.88f, 0.87f, 0.84f), 0.05f);
        Lit("Ex6_Sphere", new Color(0.25f, 0.45f, 0.75f), 0.55f);
        Lit("Ex6_Cube", new Color(0.30f, 0.62f, 0.38f), 0.3f);
        Lit("Ex6_BeltPlain", new Color(0.35f, 0.35f, 0.37f), 0.2f);
        AssetDatabase.SaveAssets();
    }

    // ---------------- renderer feature ----------------
    // Same as pressing "Add Renderer Feature" on PC_Renderer in the Inspector: a sub-asset plus an entry in both lists.
    public static void AddFeature()
    {
        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        var feature = data.rendererFeatures.OfType<ObjectOutlineFeature>().FirstOrDefault();
        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<ObjectOutlineFeature>();
            feature.name = "ObjectOutline";
            feature.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

            var so = new SerializedObject(data);
            var list = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        feature.settings.WriteObject = AssetDatabase.LoadAssetAtPath<Material>(Dir + "Ex6_WriteObject.mat");
        feature.settings.ApplyOutline = AssetDatabase.LoadAssetAtPath<Material>(Dir + "Ex6_ApplyOutline.mat");
        feature.settings.passEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        feature.SetActive(true);
        EditorUtility.SetDirty(feature);
        EditorUtility.SetDirty(data);
        data.SetDirty();
        AssetDatabase.SaveAssets();
        Debug.Log("EX6 feature on " + RendererPath + ": " + string.Join(", ", data.rendererFeatures.Select(f => f.name)));
    }

    // ---------------- scene ----------------
    static Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>(Dir + n + ".mat");

    static GameObject Prim(PrimitiveType t, string name, Vector3 pos, Vector3 scale, Material mat, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(t);
        go.name = name; go.transform.position = pos; go.transform.localScale = scale;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    public static void BuildScene()
    {
        MakeAssets();
        AddFeature();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // stage: floor and a light back wall
        Prim(PrimitiveType.Plane, "Floor", new Vector3(0, 0, 0.5f), new Vector3(1.2f, 1, 0.8f), M("Ex6_Floor"), false);
        Prim(PrimitiveType.Cube, "Wall", new Vector3(0, 1.5f, 2.05f), new Vector3(12, 3, 0.1f), M("Ex6_Wall"), false);

        // Nisse: empty wrapper at the feet, mesh child offset and scaled to 1 unit tall, turned to face the camera
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Nisse/Nisse.obj");
        Mesh mesh = model.GetComponentInChildren<MeshFilter>().sharedMesh;
        Bounds b = mesh.bounds;
        float s = 1f / b.size.y;
        var nisse = new GameObject("Nisse");
        nisse.transform.position = Vector3.zero;
        nisse.transform.rotation = Quaternion.Euler(0, 180, 0);
        var child = (GameObject)PrefabUtility.InstantiatePrefab(model, nisse.transform);
        child.name = "Mesh";
        child.transform.localScale = Vector3.one * s;
        child.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z) * s;
        var nisseRenderer = child.GetComponentInChildren<MeshRenderer>();
        nisseRenderer.sharedMaterial = M("Ex6_NissePattern");
        // collider fitted to the renderer bounds, so a click on the Nisse hits it
        var box = nisse.AddComponent<BoxCollider>();
        Bounds wb = nisseRenderer.bounds;
        box.center = nisse.transform.InverseTransformPoint(wb.center);
        box.size = wb.size;   // the wrapper is only turned 180 degrees about Y, so the size stays the same
        nisse.AddComponent<Selectable>().target = nisseRenderer;

        // other selectable objects
        var sphere = Prim(PrimitiveType.Sphere, "Sphere", new Vector3(-1.05f, 0.3f, 0.3f), Vector3.one * 0.6f, M("Ex6_Sphere"), true);
        sphere.AddComponent<Selectable>();
        var cube = Prim(PrimitiveType.Cube, "Cube", new Vector3(1.05f, 0.25f, 0.25f), Vector3.one * 0.5f, M("Ex6_Cube"), true);
        cube.transform.rotation = Quaternion.Euler(0, 30, 0);
        cube.AddComponent<Selectable>();
        var belt = Prim(PrimitiveType.Quad, "Belt (UV scroll)", new Vector3(0, 1.45f, 1.98f), new Vector3(2.4f, 0.6f, 1), M("Ex6_Belt"), true);
        belt.transform.rotation = Quaternion.identity;   // a Unity quad faces -Z, towards the camera
        belt.AddComponent<Selectable>();

        // key light (directional, soft shadows) and ambient
        var sun = new GameObject("Key Light").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.4f; sun.color = new Color(1f, 0.95f, 0.88f);
        sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.8f;
        sun.transform.rotation = Quaternion.Euler(38, -35, 0);
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.65f);
        RenderSettings.ambientEquatorColor = new Color(0.45f, 0.44f, 0.42f);
        RenderSettings.ambientGroundColor = new Color(0.25f, 0.23f, 0.21f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom; RenderSettings.reflectionIntensity = 0.3f;
        DynamicGI.UpdateEnvironment();

        // camera with the click selector
        var camGO = new GameObject("Main Camera"); camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.86f, 0.85f, 0.82f);
        cam.fieldOfView = 38f; cam.allowHDR = false; cam.allowMSAA = false;
        camGO.transform.position = new Vector3(0, 1.0f, -2.75f);
        camGO.transform.LookAt(new Vector3(0, 0.66f, 0.3f));
        var acd = camGO.AddComponent<UniversalAdditionalCameraData>();
        acd.renderPostProcessing = false;
        camGO.AddComponent<ObjectSelector>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"EX6 scene built, nisse scale {s}, collider {box.center} {box.size}");
    }

    // ---------------- captures ----------------
    static Material[] scrollMats;
    static void SetTime(float t)
    {
        // URP feeds _Time.y from Time.realtimeSinceStartup in edit mode; the offset turns that into the wanted time.
        foreach (var m in scrollMats) m.SetFloat("_TimeOffset", t - Time.realtimeSinceStartup);
    }

    static void Shot(Camera cam, string name, float t)
    {
        SetTime(t);
        RtcgTools.WritePng(cam, Path.Combine(OutDir, name), 1280, 720);
    }

    public static void CaptureSteps()
    {
        EditorSceneManager.OpenScene(ScenePath);
        Directory.CreateDirectory(OutDir);
        var cam = Camera.main;
        var sel = cam.GetComponent<ObjectSelector>();
        var nisseR = GameObject.Find("Nisse").GetComponent<Selectable>().GetRenderer();
        var sphereR = GameObject.Find("Sphere").GetComponent<Renderer>();
        var beltR = GameObject.Find("Belt (UV scroll)").GetComponent<Renderer>();
        var apply = M("Ex6_ApplyOutline");
        var patternMat = M("Ex6_NissePattern");
        var beltMat = M("Ex6_Belt");
        var plainNisse = AssetDatabase.LoadAssetAtPath<Material>("Assets/Nisse/Nisse.mat");
        scrollMats = new[] { patternMat, beltMat };
        Vector3 camPos = cam.transform.position; Quaternion camRot = cam.transform.rotation;
        string warm = Path.Combine(OutDir, "_warmup.png");

        // first renders of a session come out untextured
        sel.Select(nisseR);
        for (int i = 0; i < 3; i++) RtcgTools.WritePng(cam, warm, 640, 360);
        File.Delete(warm);

        // step 0: plain stage, nothing selected, plain Nisse and plain belt
        nisseR.sharedMaterial = plainNisse; beltR.sharedMaterial = M("Ex6_BeltPlain");
        sel.Select(null);
        Shot(cam, "step0_scene_plain.png", 0);

        // step 1: the selection buffer itself (debug switch in the apply shader)
        sel.Select(nisseR);
        apply.SetFloat("_DebugSelection", 1);
        Shot(cam, "step1_selection_buffer.png", 0);
        apply.SetFloat("_DebugSelection", 0);

        // step 2: outline on the Nisse, then on the sphere
        Shot(cam, "step2_outline_nisse.png", 0);
        sel.Select(sphereR);
        Shot(cam, "step2b_outline_other.png", 0);

        // step 3: scrolling pattern on the Nisse, nothing selected, closer camera, three times
        sel.Select(null);
        nisseR.sharedMaterial = patternMat; beltR.sharedMaterial = beltMat;
        cam.transform.position = new Vector3(0, 0.58f, -1.8f); cam.transform.LookAt(new Vector3(0, 0.47f, 0));
        Shot(cam, "step3a_scroll_t0.png", 0f);
        Shot(cam, "step3b_scroll_t1.png", 1f);
        Shot(cam, "step3c_scroll_t2.png", 2f);

        // step 3d: the original UV scroll on the belt quad
        cam.transform.position = new Vector3(0, 1.45f, -0.6f); cam.transform.LookAt(new Vector3(0, 1.45f, 2f));
        Shot(cam, "step3d_uv_scroll_original.png", 0.7f);

        // step 4: both shaders together
        cam.transform.SetPositionAndRotation(camPos, camRot);
        sel.Select(nisseR);
        Shot(cam, "step4_combined_t0.png", 0f);
        Shot(cam, "step4_combined_t1.png", 1.5f);

        // put the materials back as saved (nothing here is written to disk)
        foreach (var m in scrollMats) m.SetFloat("_TimeOffset", 0);
        sel.Select(null);
        Debug.Log("EX6 captures done");
    }

    // Check that another scene looks the same with the feature on and off (it has no ObjectSelector, so no pass should run).
    // Run with -ex6Scene "<scene path>" -ex6Out "<folder outside Assets>".
    public static void CheckOtherScene()
    {
        var a = System.Environment.GetCommandLineArgs();
        string scenePath = "Assets/Scenes/Ex 2 Capture.unity", outDir = "Temp/ex6_check";
        for (int i = 0; i < a.Length - 1; i++) { if (a[i] == "-ex6Scene") scenePath = a[i + 1]; if (a[i] == "-ex6Out") outDir = a[i + 1]; }
        Directory.CreateDirectory(outDir);
        EditorSceneManager.OpenScene(scenePath);
        var cam = Camera.main ?? Object.FindAnyObjectByType<Camera>();
        var feature = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath).rendererFeatures.OfType<ObjectOutlineFeature>().First();
        string on = Path.Combine(outDir, "feature_on.png"), off = Path.Combine(outDir, "feature_off.png");
        for (int i = 0; i < 3; i++) RtcgTools.WritePng(cam, on, 640, 360);
        feature.SetActive(false); RtcgTools.WritePng(cam, off, 640, 360);
        feature.SetActive(true); RtcgTools.WritePng(cam, on, 640, 360);
        var ta = new Texture2D(2, 2); ta.LoadImage(File.ReadAllBytes(on));
        var tb = new Texture2D(2, 2); tb.LoadImage(File.ReadAllBytes(off));
        var pa = ta.GetPixels32(); var pb = tb.GetPixels32();
        int diff = 0;
        for (int i = 0; i < pa.Length; i++) if (pa[i].r != pb[i].r || pa[i].g != pb[i].g || pa[i].b != pb[i].b) diff++;
        Debug.Log($"EX6 CHECK {scenePath}: {diff} of {pa.Length} pixels differ between feature on and off");
    }
}
