using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Builds the Ex 4 gallery inside "Assets/Scenes/Ex 4.unity": a closed room, four framed pictures on the side
// walls, three Nisse on pedestals, and lights placed by hand (spots on pictures and Nisse, a lamp for bloom).
// Run: RtcgEx4Gallery.Build (adds/replaces the "Gallery" root), RtcgEx4Gallery.Capture (screenshots).
public static class RtcgEx4Gallery
{
    const string ScenePath = "Assets/Scenes/Ex 4.unity";
    const string MatDir = "Assets/Gallery/Materials/";
    const float RoomW = 10f, RoomL = 16f, RoomH = 4f;

    static Material Mat(string name, Color col, float smooth, Texture tex = null, Color? emission = null)
    {
        Directory.CreateDirectory(MatDir);
        string path = MatDir + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", col);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", 0f);
        if (tex != null) { m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white); }
        if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static Light Spot(string name, Transform parent, Vector3 pos, Vector3 target, float angle, float intensity, Color col, bool shadows)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        go.transform.position = pos; go.transform.LookAt(target);
        var l = go.AddComponent<Light>();
        l.type = LightType.Spot; l.spotAngle = angle; l.innerSpotAngle = angle * 0.6f;
        l.range = 12f; l.intensity = intensity; l.color = col;
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        l.shadowStrength = 1f; l.shadowBias = 0.02f; l.shadowNormalBias = 0.3f;
        return l;
    }

    public static void Build()
    {
        AssetDatabase.Refresh();
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var old = GameObject.Find("Gallery"); if (old) Object.DestroyImmediate(old);
        var root = new GameObject("Gallery").transform;

        // materials
        var wall = Mat("Gallery_Wall", new Color(0.82f, 0.80f, 0.76f), 0.05f);
        var floor = Mat("Gallery_Floor", new Color(0.30f, 0.27f, 0.25f), 0.45f);
        var ceil = Mat("Gallery_Ceiling", new Color(0.12f, 0.12f, 0.13f), 0.0f);
        var plinth = Mat("Gallery_Pedestal", new Color(0.92f, 0.92f, 0.90f), 0.15f);
        var frameM = Mat("Gallery_Frame", new Color(0.06f, 0.05f, 0.045f), 0.35f);
        var lampM = Mat("Gallery_Lamp", Color.white, 0.2f, null, new Color(5f, 3.8f, 2.4f));

        // room (closed so only the hand-placed lights count)
        Box("Floor", root, new Vector3(0, -0.05f, 0), new Vector3(RoomW, 0.1f, RoomL), floor);
        Box("Ceiling", root, new Vector3(0, RoomH + 0.05f, 0), new Vector3(RoomW + 0.2f, 0.1f, RoomL + 0.2f), ceil);
        Box("Wall_Left", root, new Vector3(-RoomW / 2 - 0.05f, RoomH / 2, 0), new Vector3(0.1f, RoomH, RoomL), wall);
        Box("Wall_Right", root, new Vector3(RoomW / 2 + 0.05f, RoomH / 2, 0), new Vector3(0.1f, RoomH, RoomL), wall);
        Box("Wall_Back", root, new Vector3(0, RoomH / 2, RoomL / 2 + 0.05f), new Vector3(RoomW + 0.2f, RoomH, 0.1f), wall);
        Box("Wall_Front", root, new Vector3(0, RoomH / 2, -RoomL / 2 - 0.05f), new Vector3(RoomW + 0.2f, RoomH, 0.1f), wall);

        // pictures: side walls, two each
        var art = new (string file, bool left, float z)[]
        {
            ("art_cats_sleigh", true, -1.0f), ("art_village", true, 4.5f),
            ("art_cubist_santa", false, -1.0f), ("art_robot_santa", false, 4.5f),
        };
        const float h = 2.2f, cy = 1.9f;
        foreach (var a in art)
        {
            string p = "Assets/Gallery/Art/" + a.file + ".jpg";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            float w = h * tex.width / tex.height;
            float sign = a.left ? 1f : -1f;                 // direction into the room
            float wallX = a.left ? -RoomW / 2 : RoomW / 2;
            var m = Mat("Art_" + a.file, Color.white, 0.12f, tex);
            Box("Frame_" + a.file, root, new Vector3(wallX + sign * 0.03f, cy, a.z), new Vector3(0.06f, h + 0.18f, w + 0.18f), frameM);
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Picture_" + a.file; q.transform.SetParent(root, false);
            q.transform.localPosition = new Vector3(wallX + sign * 0.062f, cy, a.z);
            q.transform.localRotation = Quaternion.Euler(0, a.left ? -90 : 90, 0);
            q.transform.localScale = new Vector3(w, h, 1);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.GetComponent<MeshRenderer>().sharedMaterial = m;
            // picture light from the ceiling, a bit in front of the wall
            Spot("PicLight_" + a.file, root, new Vector3(wallX + sign * 1.3f, RoomH - 0.1f, a.z), new Vector3(wallX, cy, a.z), 62f, 32f, new Color(1f, 0.93f, 0.82f), false);
        }

        // pedestals with Nisse
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Nisse/Nisse.obj");
        var nisseMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Nisse/Nisse.mat");
        Mesh mesh = model.GetComponentInChildren<MeshFilter>().sharedMesh;
        Bounds b = mesh.bounds;
        float s = 0.85f / b.size.y;
        var spots = new (Vector3 pos, float yaw)[] { (new Vector3(-2.3f, 0, 0.5f), 170f), (new Vector3(0f, 0, 3.0f), 180f), (new Vector3(2.3f, 0, 0.5f), 190f) };
        for (int i = 0; i < spots.Length; i++)
        {
            var sp = spots[i];
            Box("Pedestal_" + (i + 1), root, sp.pos + new Vector3(0, 0.5f, 0), new Vector3(0.9f, 1.0f, 0.9f), plinth);
            Box("PedestalTop_" + (i + 1), root, sp.pos + new Vector3(0, 1.03f, 0), new Vector3(1.0f, 0.06f, 1.0f), plinth);
            var wrap = new GameObject("Nisse_" + (i + 1)).transform; wrap.SetParent(root, false);
            wrap.position = sp.pos + new Vector3(0, 1.06f, 0); wrap.rotation = Quaternion.Euler(0, sp.yaw, 0);
            var child = (GameObject)PrefabUtility.InstantiatePrefab(model, wrap);
            child.name = "Mesh"; child.transform.localScale = Vector3.one * s;
            child.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z) * s;
            foreach (var r in child.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = nisseMat;
            // key spot, in front and above, with shadows
            Spot("NisseKey_" + (i + 1), root, sp.pos + new Vector3(i == 0 ? -1.2f : i == 2 ? 1.2f : 0f, 3.6f, -2.2f), sp.pos + new Vector3(0, 1.45f, 0), 40f, 55f, new Color(1f, 0.9f, 0.75f), true);
        }

        // cool fill from the back so shadows are not pitch black, and a lamp for bloom/flare later
        var fill = new GameObject("BackFill"); fill.transform.SetParent(root, false); fill.transform.position = new Vector3(0, 3.4f, 6.5f);
        var fl = fill.AddComponent<Light>(); fl.type = LightType.Point; fl.range = 14f; fl.intensity = 6f; fl.color = new Color(0.55f, 0.65f, 1f); fl.shadows = LightShadows.None;
        var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere); lamp.name = "Lamp"; lamp.transform.SetParent(root, false);
        lamp.transform.position = new Vector3(3.6f, 2.8f, 6.8f); lamp.transform.localScale = Vector3.one * 0.3f;
        Object.DestroyImmediate(lamp.GetComponent<Collider>()); lamp.GetComponent<MeshRenderer>().sharedMaterial = lampM;
        var ll = new GameObject("LampLight"); ll.transform.SetParent(lamp.transform, false);
        var lli = ll.AddComponent<Light>(); lli.type = LightType.Point; lli.range = 8f; lli.intensity = 8f; lli.color = new Color(1f, 0.82f, 0.55f); lli.shadows = LightShadows.None;

        // the empty-scene sun would shine through the ceiling (no shadows on it): switch it off, the room has its own lights
        foreach (var l in Object.FindObjectsByType<Light>())
            if (l.type == LightType.Directional) l.enabled = false;

        // ambient: dim flat, no sky reflections
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.05f, 0.05f, 0.06f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom; RenderSettings.reflectionIntensity = 0f;

        // camera at the entrance, looking down the hall
        var cam = Camera.main;
        if (cam)
        {
            cam.transform.position = new Vector3(0, 1.6f, -6.9f); cam.transform.LookAt(new Vector3(0, 1.3f, 2.5f));
            cam.fieldOfView = 62f; cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("EX4GALLERY built, nisse scale " + s + " mesh verts " + mesh.vertexCount);
    }

    public static void Capture()
    {
        EditorSceneManager.OpenScene(ScenePath);
        Directory.CreateDirectory("Captures/Ex04_Gallery");
        var cam = Camera.main;
        RtcgTools.WritePng(cam, "Captures/Ex04_Gallery/_warmup.png", 320, 180); // first render loads textures lazily
        RtcgTools.WritePng(cam, "Captures/Ex04_Gallery/gallery_entrance.png", 1600, 900);
        cam.transform.position = new Vector3(-3.2f, 1.7f, -2.5f); cam.transform.LookAt(new Vector3(1.0f, 1.2f, 2.5f));
        RtcgTools.WritePng(cam, "Captures/Ex04_Gallery/gallery_left.png", 1600, 900);
        cam.transform.position = new Vector3(0f, 1.5f, -0.5f); cam.transform.LookAt(new Vector3(-3.8f, 1.6f, 0.5f));
        RtcgTools.WritePng(cam, "Captures/Ex04_Gallery/gallery_art_left.png", 1600, 900);
        cam.transform.position = new Vector3(0f, 1.5f, -0.5f); cam.transform.LookAt(new Vector3(3.8f, 1.6f, 0.5f));
        RtcgTools.WritePng(cam, "Captures/Ex04_Gallery/gallery_art_right.png", 1600, 900);
    }
}
