using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Editor helpers for the RTCG mini-project: import the Nisse scan, and render evidence PNGs headlessly.
public static class RtcgTools
{
    const string NisseDir = "Assets/Nisse/";

    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return def;
    }

    // ---------- Nisse import ----------
    [MenuItem("RTCG/Import Nisse")]
    public static void ImportNisse()
    {
        AssetDatabase.ImportAsset(NisseDir + "Nisse_diffuse.jpeg", ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(NisseDir + "Nisse_diffuse.jpeg");
        ti.maxTextureSize = 4096;               // source is 8192 x 8192, 4096 keeps VRAM sane
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.mipmapEnabled = true;
        ti.SaveAndReimport();

        var mi = (ModelImporter)AssetImporter.GetAtPath(NisseDir + "Nisse.obj");
        mi.indexFormat = ModelImporterIndexFormat.UInt32; // 145k vertices needs 32-bit indices
        mi.meshCompression = ModelImporterMeshCompression.Off;
        mi.importNormals = ModelImporterNormals.Calculate;
        mi.SaveAndReimport();

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(NisseDir + "Nisse_diffuse.jpeg");
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", 0.1f);
        mat.SetFloat("_Metallic", 0f);
        AssetDatabase.CreateAsset(mat, NisseDir + "Nisse.mat");

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(NisseDir + "Nisse.obj");
        Mesh mesh = model.GetComponentInChildren<MeshFilter>().sharedMesh;
        Bounds b = mesh.bounds;
        Debug.Log($"NISSE mesh verts={mesh.vertexCount} tris={mesh.triangles.Length / 3} bounds={b}");

        // Wrapper root at the base-centre of the model, child scaled so the scan is 1 unit tall.
        float s = 1f / b.size.y;
        var root = new GameObject("Nisse");
        var child = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        child.name = "Mesh";
        child.transform.localScale = Vector3.one * s;
        child.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z) * s;
        foreach (var r in child.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = mat;
        PrefabUtility.SaveAsPrefabAsset(root, NisseDir + "Nisse.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        Debug.Log("NISSE prefab saved, scale factor " + s);
        AssetDatabase.SaveAssets();
    }

    // ---------- headless render of the Nisse prefab ----------
    public static void RenderNisse()
    {
        string outDir = Arg("-rtcgOut", "Captures/Ex02");
        Directory.CreateDirectory(outDir);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(NisseDir + "Nisse.prefab"));
        var camGO = new GameObject("cam"); var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.2f, 0.2f, 0.22f);
        camGO.AddComponent<UniversalAdditionalCameraData>();
        camGO.transform.position = new Vector3(0, 0.5f, -1.5f); camGO.transform.LookAt(new Vector3(0, 0.45f, 0));
        var l = new GameObject("sun").AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.2f;
        l.transform.rotation = Quaternion.Euler(40, -30, 0);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.gray;
        WritePng(cam, Path.Combine(outDir, "nisse_mesh_a.png"), 1024, 1024);
        go.transform.rotation = Quaternion.Euler(0, 180, 0);
        WritePng(cam, Path.Combine(outDir, "nisse_mesh_b.png"), 1024, 1024);
    }

    // ---------- shadow diagnostic: static cubes, light off to the side, vary pipeline settings ----------
    public static void DiagShadows()
    {
        string outDir = Arg("-rtcgOut", "Captures/Ex01/diag");
        Directory.CreateDirectory(outDir);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cubePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cube.prefab");
        for (int x = 0; x < 5; x++) for (int y = 0; y < 5; y++) for (int z = 0; z < 5; z++)
        {
            var c = (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab);
            c.transform.position = new Vector3(x - 2, y - 2, z - 2);
        }
        var camGO = new GameObject("cam"); var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        camGO.AddComponent<UniversalAdditionalCameraData>();
        camGO.transform.position = new Vector3(0, 0, -12); camGO.transform.LookAt(Vector3.zero);
        var lGO = new GameObject("light"); var l = lGO.AddComponent<Light>(); l.type = LightType.Point; l.range = 50; l.intensity = 50;
        l.shadows = LightShadows.Soft; lGO.transform.position = new Vector3(3.5f, 3.5f, -6f);
        var ld = lGO.GetComponent<UniversalAdditionalLightData>() ?? lGO.AddComponent<UniversalAdditionalLightData>();
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;

        var asset = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        var so = new SerializedObject(asset);
        Debug.Log("DIAG pipeline asset: " + asset.name);
        void Shot(string name, float depth, float normal, int tier, bool pipe)
        {
            so.FindProperty("m_ShadowDepthBias").floatValue = depth;
            so.FindProperty("m_ShadowNormalBias").floatValue = normal;
            so.ApplyModifiedPropertiesWithoutUndo();
            var lso = new SerializedObject(ld);
            lso.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = tier;
            lso.FindProperty("m_UsePipelineSettings").boolValue = pipe;
            lso.ApplyModifiedPropertiesWithoutUndo();
            l.shadowBias = 0.01f; l.shadowNormalBias = 0.05f;
            WritePng(cam, Path.Combine(outDir, name + ".png"), 640, 640);
        }
        Shot("a_orig_bias0.1_0.5_tier1", 0.1f, 0.5f, 1, true);
        Shot("b_depth0.01_tier1", 0.01f, 0.5f, 1, true);
        Shot("c_normal0.05_tier1", 0.1f, 0.05f, 1, true);
        Shot("d_both_small_tier1", 0.01f, 0.05f, 1, true);
        Shot("e_both_small_tier2", 0.01f, 0.05f, 2, true);
        Shot("f_orig_but_light_own_bias", 0.1f, 0.5f, 1, false);
    }

    public static void WritePng(Camera cam, string path, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cam.targetTexture = rt; cam.aspect = (float)w / h; cam.Render();
        RenderTexture.active = rt;
        var t = new Texture2D(w, h, TextureFormat.RGB24, false);
        t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        RenderTexture.active = null; cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(t);
        Debug.Log("WROTE " + path);
    }

    // ---------- play-mode capture of the cube grid ----------
    const string Job = "rtcg_job";
    static int frame;

    [InitializeOnLoadMethod]
    static void Hook() { EditorApplication.update += Tick; }

    public static void CaptureCubeGrid()
    {
        SessionState.SetString(Job, Arg("-rtcgOut", "Captures/Ex01"));
        SessionState.SetInt("rtcg_frames", int.Parse(Arg("-rtcgFrames", "44")));
        SessionState.SetInt("rtcg_every", int.Parse(Arg("-rtcgEvery", "1"))); // simulate at 4*every fps, save every Nth frame
        SessionState.SetBool("rtcg_static", Arg("-rtcgStatic", "0") == "1");
        frame = 0;
        EditorSceneManager.OpenScene("Assets/Scenes/CubeGrid.unity");
        EditorApplication.isPlaying = true;
    }

    static void Tick()
    {
        string dir = SessionState.GetString(Job, "");
        if (dir == "" || !Application.isPlaying) return;
        int every = SessionState.GetInt("rtcg_every", 1);
        Time.captureFramerate = 4 * every;
        frame++;
        if (frame == 1 && SessionState.GetBool("rtcg_static", false))
        {
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>())
                if (mb is Transformations || mb is RayCastManipulator) mb.enabled = false;
            var cg = UnityEngine.Object.FindAnyObjectByType<CreateGrid>();
            for (int i = 0; i < cg.grid.Length; i++) { cg.grid[i].localScale = Vector3.one * 0.5f; cg.grid[i].localPosition = cg.startPos[i]; cg.grid[i].gameObject.SetActive(true); }
            Debug.Log("RTCG static: cubes=" + UnityEngine.Object.FindObjectsByType<MeshRenderer>().Length + " cam=" + Camera.main.transform.position + " lightpos=" + UnityEngine.Object.FindAnyObjectByType<Light>().transform.position);
        }
        if (frame == 3) Debug.Log("RTCG f3 cam=" + Camera.main.transform.position + " cubes=" + UnityEngine.Object.FindObjectsByType<MeshRenderer>().Length);
        if (frame < 3) return;
        if ((frame - 3) % every != 0) return;
        int idx = (frame - 3) / every;
        Directory.CreateDirectory(dir);
        WritePng(Camera.main, Path.Combine(dir, $"frame_{idx:000}.png"), 960, 540);
        if (idx + 1 >= SessionState.GetInt("rtcg_frames", 44))
        {
            SessionState.EraseString(Job);
            EditorApplication.Exit(0);
        }
    }
}
