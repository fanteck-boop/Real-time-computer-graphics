using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Ex 6, simple version: wobble + scrolling pattern. Headless: -executeMethod RtcgEx6Simple.Run
public static class RtcgEx6Simple
{
    const string ScenePath = "Assets/Scenes/Ex 6 Shaders.unity";
    const string Dir = "Assets/Shaders/Ex6/";
    const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    const string OutDir = "Captures/Ex06_Simple";
    const float Amplitude = 0.03f, Strength = 0.75f;

    public static void Run() { RemoveOutlineFeature(); UpdateScene(); Capture(); }

    public static void RemoveOutlineFeature()
    {
        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        var feature = data.rendererFeatures.OfType<ObjectOutlineFeature>().FirstOrDefault();
        if (feature == null) return;
        int i = data.rendererFeatures.IndexOf(feature);
        var so = new SerializedObject(data);
        var list = so.FindProperty("m_RendererFeatures");
        list.GetArrayElementAtIndex(i).objectReferenceValue = null;
        list.DeleteArrayElementAtIndex(i);
        so.FindProperty("m_RendererFeatureMap").DeleteArrayElementAtIndex(i);
        so.ApplyModifiedPropertiesWithoutUndo();
        Object.DestroyImmediate(feature, true);
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
    }

    static Material Mat()
    {
        string path = Dir + "Ex6_NisseWobbleScroll.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("RTCG/Ex6/Nisse Wobble Scroll"));
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Material>("Assets/Nisse/Nisse.mat").GetTexture("_BaseMap"));
        m.SetTexture("_Texture", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "Ex6_Stripes.png"));
        AssetDatabase.CreateAsset(m, path);
        AssetDatabase.SaveAssets();
        return m;
    }

    public static void UpdateScene()
    {
        var mat = Mat();
        var scene = EditorSceneManager.OpenScene(ScenePath);
        GameObject.Find("Nisse").GetComponentInChildren<MeshRenderer>().sharedMaterial = mat;
        // parts that only served click-to-outline
        foreach (var s in Object.FindObjectsByType<Selectable>(FindObjectsInactive.Include)) Object.DestroyImmediate(s);
        foreach (var s in Object.FindObjectsByType<ObjectSelector>(FindObjectsInactive.Include)) Object.DestroyImmediate(s);
        foreach (var n in new[] { "Sphere", "Cube" }) { var g = GameObject.Find(n); if (g) Object.DestroyImmediate(g); }
        foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include)) Object.DestroyImmediate(c);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static void Capture()
    {
        EditorSceneManager.OpenScene(ScenePath);
        Directory.CreateDirectory(OutDir);
        var cam = Camera.main;
        cam.transform.position = new Vector3(0, 0.58f, -1.8f);   // closer than the saved camera; scene is not saved
        cam.transform.LookAt(new Vector3(0, 0.5f, 0));
        var mat = Mat();
        var belt = GameObject.Find("Belt (UV scroll)").GetComponent<Renderer>().sharedMaterial;

        var warm = Path.Combine(OutDir, "_warmup.png");
        for (int i = 0; i < 4; i++) RtcgTools.WritePng(cam, warm, 640, 360);   // first renders come out untextured
        File.Delete(warm);

        void Shot(string name, float amplitude, float strength, float t)
        {
            mat.SetFloat("_Amplitude", amplitude);
            mat.SetFloat("_PatternStrength", strength);
            // _Time.y keeps running in the editor, so the offset cancels it and leaves t
            foreach (var m in new[] { mat, belt }) m.SetFloat("_TimeOffset", t - Time.realtimeSinceStartup);
            RtcgTools.WritePng(cam, Path.Combine(OutDir, name + ".png"), 1280, 720);
            Debug.Log($"EX6S {name} t={t} realtime={Time.realtimeSinceStartup}");
        }
        Shot("plain", 0, 0, 0);
        Shot("wobble_only", Amplitude, 0, 0);
        Shot("scroll_only", 0, Strength, 0);
        Shot("both_t0", Amplitude, Strength, 0);
        Shot("both_t1", Amplitude, Strength, 1);
        Shot("both_t2", Amplitude, Strength, 2);

        mat.SetFloat("_Amplitude", Amplitude);
        mat.SetFloat("_PatternStrength", Strength);
        foreach (var m in new[] { mat, belt }) m.SetFloat("_TimeOffset", 0);
        AssetDatabase.SaveAssets();
    }
}
