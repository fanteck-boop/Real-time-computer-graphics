using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Part 3 of the RTCG mini-project: Ex8 (instancing vs LOD benchmark) and Ex9 (skinned, animated Nisse).
public static class RtcgPart3
{
    const string LodDir = "Assets/Nisse/LOD/";

    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return def;
    }

    public static void ImportLods()
    {
        foreach (var n in new[] { "Nisse_LOD1", "Nisse_LOD2", "Nisse_LOD3" })
        {
            string p = LodDir + n + ".obj";
            AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            var mi = (ModelImporter)AssetImporter.GetAtPath(p);
            mi.importNormals = ModelImporterNormals.Calculate;   // same as Nisse.obj, scan normals are noisy
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.indexFormat = ModelImporterIndexFormat.UInt32;
            mi.SaveAndReimport();
            var m = LoadMesh(p);
            Debug.Log($"LOD {n}: verts={m.vertexCount} tris={m.triangles.Length / 3} bounds={m.bounds} uv={m.uv.Length}");
        }
        AssetDatabase.SaveAssets();
    }

    static Mesh LoadMesh(string path)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is Mesh m) return m;
        return null;
    }

    // ================= Ex8 =================
    public static void BuildEx08()
    {
        ImportLods();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var plain = AssetDatabase.LoadAssetAtPath<Material>("Assets/Nisse/Nisse.mat");
        AssetDatabase.DeleteAsset("Assets/Nisse/Nisse_instanced.mat");
        var inst = new Material(plain) { name = "Nisse_instanced", enableInstancing = true };
        AssetDatabase.CreateAsset(inst, "Assets/Nisse/Nisse_instanced.mat");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Nisse/Nisse.prefab");
        var mesh0 = prefab.GetComponentInChildren<MeshFilter>().sharedMesh;
        var child = prefab.GetComponentInChildren<MeshFilter>().transform;

        var camGO = new GameObject("Main Camera"); camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 40; cam.nearClipPlane = 0.1f; cam.farClipPlane = 250; cam.allowHDR = true;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.65f, 0.78f);
        camGO.AddComponent<AudioListener>();
        var d = camGO.AddComponent<UniversalAdditionalCameraData>(); d.renderPostProcessing = false;
        camGO.transform.position = new Vector3(0, 3, -20);

        var lGO = new GameObject("Sun"); var l = lGO.AddComponent<Light>();
        l.type = LightType.Directional; l.intensity = 1.3f; l.shadows = LightShadows.None;   // no shadows: keeps the main pass the only variable
        lGO.transform.rotation = Quaternion.Euler(50, -30, 0);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.5f, 0.52f, 0.56f);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "Ground";
        UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());
        ground.transform.localScale = new Vector3(20, 1, 20);
        var gm = new Material(Shader.Find("Universal Render Pipeline/Lit")); gm.SetColor("_BaseColor", new Color(0.35f, 0.4f, 0.3f));
        AssetDatabase.DeleteAsset("Assets/Nisse/Ex08_ground.mat"); AssetDatabase.CreateAsset(gm, "Assets/Nisse/Ex08_ground.mat");
        ground.GetComponent<MeshRenderer>().sharedMaterial = gm;

        var b = new GameObject("Ex08Bench").AddComponent<Ex08Bench>();
        b.lod0 = mesh0; b.lod1 = LoadMesh(LodDir + "Nisse_LOD1.obj"); b.lod2 = LoadMesh(LodDir + "Nisse_LOD2.obj"); b.lod3 = LoadMesh(LodDir + "Nisse_LOD3.obj");
        b.matPlain = plain; b.matInstanced = inst; b.cam = cam;
        var rel = prefab.transform.worldToLocalMatrix * child.localToWorldMatrix;
        b.meshLocalPos = rel.GetPosition(); b.meshLocalScale = rel.lossyScale.x;
        Debug.Log($"EX08 meshLocalPos={b.meshLocalPos} scale={b.meshLocalScale} lod0 tris={mesh0.triangles.Length / 3}");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Ex08_Optimization.unity");
        AssetDatabase.SaveAssets();
    }

    // run in play mode; the Ex08Bench component reads -ex08bench / -ex08scenario and exits the editor when done
    public static void RunEx08()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Ex08_Optimization.unity");
        EditorApplication.isPlaying = true;
    }

    // ================= Ex9 =================
    // Analysis helper: orthographic front/side/back renders plus per-slice extents, used to place joints from the real mesh.
    public static void Ex09Analyze()
    {
        string outDir = "Captures/Ex09"; Directory.CreateDirectory(outDir);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Nisse/Nisse.prefab"));
        var mf = go.GetComponentInChildren<MeshFilter>();
        var m = mf.sharedMesh; var l2w = mf.transform.localToWorldMatrix;
        var v = m.vertices; int N = 40;
        var mn = new Vector3[N]; var mx = new Vector3[N]; var cnt = new int[N];
        for (int i = 0; i < N; i++) { mn[i] = Vector3.one * 9; mx[i] = -Vector3.one * 9; }
        foreach (var p0 in v)
        {
            var p = l2w.MultiplyPoint3x4(p0); int k = Mathf.Clamp((int)(p.y * N), 0, N - 1);
            mn[k] = Vector3.Min(mn[k], p); mx[k] = Vector3.Max(mx[k], p); cnt[k]++;
        }
        for (int i = 0; i < N; i++) Debug.Log($"SLICE y={i / (float)N:F3}-{(i + 1) / (float)N:F3} n={cnt[i]} x[{mn[i].x:F3},{mx[i].x:F3}] z[{mn[i].z:F3},{mx[i].z:F3}]");
        var camGO = new GameObject("cam"); var cam = camGO.AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 0.55f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.85f, 0.85f, 0.88f);
        camGO.AddComponent<UniversalAdditionalCameraData>();
        var l = new GameObject("sun").AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.2f; l.transform.rotation = Quaternion.Euler(30, 150, 0);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = Color.gray;
        void View(string name, Vector3 dir)
        {
            camGO.transform.position = new Vector3(0, 0.5f, 0) + dir * 3f; camGO.transform.LookAt(new Vector3(0, 0.5f, 0));
            RtcgTools.WritePng(cam, Path.Combine(outDir, name), 900, 1100);
        }
        View("analysis_front.png", Vector3.forward); View("analysis_side.png", Vector3.right); View("analysis_back.png", Vector3.back);
    }
}
