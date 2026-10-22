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
    const string Ply = @"C:\Users\trist\Downloads\Nisse - 300 images (1)\Nisse.ply";
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
        camGO.transform.position = new Vector3(0, 0.6f, -3.2f); camGO.transform.LookAt(new Vector3(0, 0.5f, 0));
        var sun = new GameObject("Directional Light").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.1f;
        sun.transform.rotation = Quaternion.Euler(35, -25, 0);

        var mesh = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Nisse/Nisse.prefab"));
        mesh.name = "Nisse_Mesh"; mesh.transform.position = new Vector3(-0.8f, 0, 0);
        mesh.transform.rotation = Quaternion.Euler(0, 180, 0); // beard side faces the camera

        var asset = AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(OutFolder + "/Nisse.asset");
        var go = new GameObject("Nisse_Splat");
        var r = go.AddComponent<GaussianSplatRenderer>();
        r.m_Asset = asset;
        // Splat file is in COLMAP/Brush space (arbitrary scale, y down): fit to about 1 unit tall and flip.
        Vector3 size = asset.boundsMax - asset.boundsMin;
        Debug.Log($"SPLAT asset bounds min={asset.boundsMin} max={asset.boundsMax} size={size} count={asset.splatCount}");
        // Asset bounds are dominated by floaters (900 units wide). The Nisse core, found with a density analysis of the .ply,
        // is about 0.45 x 1.25 x 0.45 splat units around (-0.06, 0.4, 0.75); scale that core to 1 unit tall.
        float s = 1f / 1.25f;
        go.transform.localScale = Vector3.one * s;
        go.transform.rotation = Quaternion.Euler(Environment.GetEnvironmentVariable("RTCG_SPLAT_ROT") is string e ? ParseV(e) : new Vector3(180, 180, 0));
        Vector3 c = new Vector3(-0.06f, 0.375f, 0.75f);
        go.transform.position = new Vector3(0.8f, 0.5f, 0) - go.transform.rotation * (c * s);
        // The Brush splat contains the whole room (floaters, chairs, walls). A box cutout, in splat units, keeps only the Nisse.
        var cut = new GameObject("NisseCutout"); cut.transform.SetParent(go.transform, false);
        cut.transform.localPosition = c; cut.transform.localScale = new Vector3(0.6f, 1.3f, 0.6f);
        var gc = cut.AddComponent<GaussianCutout>(); gc.m_Type = GaussianCutout.Type.Box;
        r.m_Cutouts = new[] { gc };
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("SPLAT scene saved " + ScenePath);
    }

    static Vector3 ParseV(string s) { var p = s.Split(','); return new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2])); }

    public static void RenderScene()
    {
        EditorSceneManager.OpenScene(ScenePath);
        var cam = Camera.main;
        string dir = "Captures/Ex02"; Directory.CreateDirectory(dir);
        Debug.Log("SPLAT graphics API: " + SystemInfo.graphicsDeviceType + " renderer=" + GaussianSplatRenderSystemActive());
        RtcgTools.WritePng(cam, Path.Combine(dir, "side_by_side.png"), 1600, 800);
        EditorApplication.Exit(0);
    }

    static string GaussianSplatRenderSystemActive() => UnityEngine.Object.FindObjectsByType<GaussianSplatRenderer>().Length + " splat renderers";
}
