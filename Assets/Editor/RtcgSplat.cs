using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using GaussianSplatting.Runtime;

// Exercise 2 helpers: create the GaussianSplatAsset from the Brush .ply, build Ex02_Capture.unity, render evidence.
public static class RtcgSplat
{
    const string Ply = @"C:\Users\trist\Downloads\Nisse - 300 images (1)\Nisse_crop.ply"; // made by Tools/nisse_colmap.py: only the Gaussians inside the Nisse volume
    const string OutFolder = "Assets/GaussianAssets";
    const string ScenePath = "Assets/Scenes/Ex02_Capture.unity";

    static Type FindType(string name) =>
        AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
            .First(t => t.Name == name);

    // Calls the package's editor window code by reflection (its CreateAsset is a private instance method).
    public static void CreateSplatAsset()
    {
        var t = FindType("GaussianSplatAssetCreator");
        var w = ScriptableObject.CreateInstance(t);
        var bf = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        t.GetField("m_InputFile", bf).SetValue(w, Ply);
        t.GetField("m_OutputFolder", bf).SetValue(w, OutFolder);
        var q = t.GetField("m_Quality", bf);
        q.SetValue(w, Enum.ToObject(q.FieldType, int.Parse(Environment.GetEnvironmentVariable("RTCG_QUALITY") ?? "2"))); // 2 = Medium
        t.GetMethod("ApplyQualityLevel", bf).Invoke(w, null);
        t.GetMethod("CreateAsset", bf).Invoke(w, null);
        var err = t.GetField("m_ErrorMessage", bf).GetValue(w);
        Debug.Log("SPLAT create done, error=" + err);
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
    }

    // Adds the package's URP renderer feature to PC_Renderer (required for splats to draw in URP).
    public static void AddUrpFeature()
    {
        var rd = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>("Assets/Settings/PC_Renderer.asset");
        if (rd.rendererFeatures.Any(f => f != null && f.GetType().Name == "GaussianSplatURPFeature"))
        { Debug.Log("SPLAT feature already present"); return; }
        var f = (ScriptableRendererFeature)ScriptableObject.CreateInstance(FindType("GaussianSplatURPFeature"));
        f.name = "GaussianSplatURPFeature";
        AssetDatabase.AddObjectToAsset(f, rd);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out _, out long local);
        var so = new SerializedObject(rd);
        var list = so.FindProperty("m_RendererFeatures"); list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = f;
        var map = so.FindProperty("m_RendererFeatureMap"); map.arraySize++; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = local;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rd); AssetDatabase.SaveAssets();
        Debug.Log("SPLAT URP feature added");
    }

    public static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
        camGO.AddComponent<AudioListener>(); camGO.AddComponent<UniversalAdditionalCameraData>();
        camGO.transform.position = new Vector3(0, 0.55f, -2.3f); camGO.transform.LookAt(new Vector3(0, 0.5f, 0));
        var sun = new GameObject("Directional Light").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.1f;
        sun.transform.rotation = Quaternion.Euler(35, -25, 0);

        var mesh = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Nisse/Ex2/Nisse_Ex2.prefab"));
        mesh.name = "Nisse_Mesh"; mesh.transform.position = new Vector3(-0.8f, 0, 0);
        mesh.transform.rotation = Quaternion.Euler(0, 180, 0); // beard side faces the camera

        var asset = AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(OutFolder + "/Nisse_crop.asset");
        var go = new GameObject("Nisse_Splat");
        var r = go.AddComponent<GaussianSplatRenderer>();
        r.m_Asset = asset;
        // The crop (Tools/nisse_colmap.py) is still in COLMAP/Brush space: y points down (mean camera up = -y, slightly tilted),
        // arbitrary scale. Values below come from the COLMAP cameras: up vector, base centre of the figure, height 0.8082 units.
        Debug.Log($"SPLAT asset bounds min={asset.boundsMin} max={asset.boundsMax} count={asset.splatCount}");
        Vector3 up = new Vector3(0.037952795f, -0.99920909f, -0.011865021f);
        Vector3 baseC = new Vector3(-0.05601580f, 0.55042170f, 0.76741911f);
        float s = 1f / 0.80822526f;                                    // splat units to Unity units: Nisse 1 unit tall like the mesh
        float yaw = Environment.GetEnvironmentVariable("RTCG_YAW") is string ey ? float.Parse(ey) : -20f;
        go.transform.localScale = new Vector3(-s, s, s);              // x flip: COLMAP data is right handed, Unity is left handed (the package does not convert)
        go.transform.rotation = Quaternion.Euler(0, yaw, 0) * Quaternion.FromToRotation(up, Vector3.up);
        go.transform.position = new Vector3(0.8f, 0, 0) - go.transform.rotation * Vector3.Scale(baseC, go.transform.localScale);   // base centre sits on the floor next to the mesh
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("SPLAT scene saved " + ScenePath);
    }

    static Vector3 ParseV(string s) { var p = s.Split(','); return new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2])); }

    public static void RenderScene()
    {
        EditorSceneManager.OpenScene(ScenePath);
        var cam = Camera.main;
        string dir = "Captures/Ex02"; Directory.CreateDirectory(dir);
        var sp = GameObject.Find("Nisse_Splat").transform; Debug.Log($"SPLAT transform pos={sp.position:F4} euler={sp.eulerAngles:F2} scale={sp.localScale:F4}");
        Debug.Log("SPLAT graphics API: " + SystemInfo.graphicsDeviceType + " renderer=" + GaussianSplatRenderSystemActive());
        RtcgTools.WritePng(cam, Path.Combine(dir, "side_by_side.png"), 1600, 800);
        EditorApplication.Exit(0);
    }

    static string GaussianSplatRenderSystemActive() => UnityEngine.Object.FindObjectsByType<GaussianSplatRenderer>().Length + " splat renderers";
}
