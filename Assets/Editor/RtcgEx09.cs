using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Ex9: a code-built skeleton for the Nisse scan, computed bone weights, a scripted AnimationClip and the animation scene.
// Space: Nisse is 1 unit tall, pivot at the base, front (beard) faces +Z. Joints were placed from per-slice mesh extents and
// front/side/back renders (Captures/Ex09/analysis_*.png). The character's right arm is on world +X.
public static class RtcgEx09
{
    public class BoneDef
    {
        public string name, parent; public Vector3 head, tail; public float radius, gain = 1f;
        public BoneDef(string n, string p, Vector3 h, Vector3 t, float r) { name = n; parent = p; head = h; tail = t; radius = r; }
    }

    static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }

    public static BoneDef[] Bones()
    {
        var b = new List<BoneDef>
        {
            new BoneDef("Root", null, V(0, 0, 0), V(0, 0.07f, 0), 0.25f),
            new BoneDef("Hips", "Root", V(0, 0.27f, 0), V(0, 0.34f, 0), 0.17f),
            new BoneDef("Spine", "Hips", V(0, 0.34f, 0), V(0, 0.47f, 0.01f), 0.17f),
            new BoneDef("Head", "Spine", V(0, 0.47f, 0.02f), V(-0.01f, 0.55f, 0.02f), 0.14f),
            new BoneDef("Hat1", "Head", V(-0.01f, 0.52f, 0.02f), V(-0.02f, 0.66f, 0f), 0.12f),
            new BoneDef("Hat2", "Hat1", V(-0.02f, 0.66f, 0f), V(-0.03f, 0.78f, -0.02f), 0.09f),
            new BoneDef("Hat3", "Hat2", V(-0.03f, 0.78f, -0.02f), V(-0.07f, 0.88f, -0.05f), 0.06f),
            new BoneDef("HatTip", "Hat3", V(-0.07f, 0.88f, -0.05f), V(-0.18f, 1.0f, -0.065f), 0.05f),
        };
        foreach (var s in new[] { 1, -1 })
        {
            string k = s > 0 ? "R" : "L";
            b.Add(new BoneDef("Shoulder_" + k, "Spine", V(0.12f * s, 0.44f, 0), V(0.17f * s, 0.39f, 0.01f), 0.075f) { gain = 4f });
            b.Add(new BoneDef("Forearm_" + k, "Shoulder_" + k, V(0.17f * s, 0.39f, 0.01f), V(0.228f * s, 0.33f, 0.02f), 0.07f) { gain = 4f });
            b.Add(new BoneDef("Leg_" + k, "Hips", V(0.055f * s, 0.27f, 0), V(0.055f * s, 0.13f, 0), 0.065f));
            b.Add(new BoneDef("Foot_" + k, "Leg_" + k, V(0.055f * s, 0.13f, 0), V(0.055f * s, 0.10f, 0.10f), 0.07f));
        }
        return b.ToArray();
    }

    // bone GameObjects at their joint position with identity rotation
    public static Transform[] BuildBones(BoneDef[] defs, Transform parent)
    {
        var t = new Transform[defs.Length]; var map = new Dictionary<string, Transform>();
        for (int i = 0; i < defs.Length; i++)
        {
            var g = new GameObject(defs[i].name);
            g.transform.SetParent(defs[i].parent == null ? parent : map[defs[i].parent], false);
            g.transform.position = defs[i].head;
            t[i] = g.transform; map[defs[i].name] = g.transform;
        }
        return t;
    }

    public static string PathOf(Transform t, Transform root)
    {
        string p = t.name;
        while (t.parent != null && t.parent != root) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    static float SegDist(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
        return (p - (a + ab * t)).magnitude;
    }

    // Weight of bone i at vertex p = 1 / ((distance to bone segment / bone radius)^pow + eps); the 4 strongest bones are kept and
    // normalised. A function of POSITION only, so duplicated vertices on UV seams get identical weights and cannot tear apart.
    public static BoneWeight[] ComputeWeights(Vector3[] verts, BoneDef[] bones, float pow, float eps = 0.05f)
    {
        var res = new BoneWeight[verts.Length];
        var w = new float[bones.Length];
        for (int vi = 0; vi < verts.Length; vi++)
        {
            var p = verts[vi];
            if (p.y < 0.072f) { res[vi].boneIndex0 = 0; res[vi].weight0 = 1; continue; }   // base plate stays rigid
            for (int i = 0; i < bones.Length; i++)
                w[i] = bones[i].gain / (Mathf.Pow(SegDist(p, bones[i].head, bones[i].tail) / bones[i].radius, pow) + eps);
            int i0 = -1, i1 = -1, i2 = -1, i3 = -1;
            for (int i = 0; i < bones.Length; i++)
            {
                if (i0 < 0 || w[i] > w[i0]) { i3 = i2; i2 = i1; i1 = i0; i0 = i; }
                else if (i1 < 0 || w[i] > w[i1]) { i3 = i2; i2 = i1; i1 = i; }
                else if (i2 < 0 || w[i] > w[i2]) { i3 = i2; i2 = i; }
                else if (i3 < 0 || w[i] > w[i3]) { i3 = i; }
            }
            float sum = w[i0] + w[i1] + w[i2] + w[i3];
            var bw = new BoneWeight();
            bw.boneIndex0 = i0; bw.weight0 = w[i0] / sum; bw.boneIndex1 = i1; bw.weight1 = w[i1] / sum;
            bw.boneIndex2 = i2; bw.weight2 = w[i2] / sum; bw.boneIndex3 = i3; bw.weight3 = w[i3] / sum;
            res[vi] = bw;
        }
        return res;
    }

    public static Mesh BakedScan()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Nisse/Nisse.prefab");
        var mf = prefab.GetComponentInChildren<MeshFilter>();
        var src = mf.sharedMesh; var m = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
        var v = src.vertices; var n = src.normals;
        for (int i = 0; i < v.Length; i++) { v[i] = m.MultiplyPoint3x4(v[i]); n[i] = m.MultiplyVector(n[i]).normalized; }
        var mesh = new Mesh { name = "Nisse_skinned", indexFormat = IndexFormat.UInt32 };
        mesh.vertices = v; mesh.normals = n; mesh.uv = src.uv; mesh.triangles = src.triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    public static Mesh BuildSkinnedMesh(BoneDef[] defs, Transform[] bones, Transform rigRoot, float pow, out Mesh debugMesh)
    {
        var mesh = BakedScan();
        var bw = ComputeWeights(mesh.vertices, defs, pow);
        mesh.boneWeights = bw;
        var bind = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++) bind[i] = bones[i].worldToLocalMatrix * rigRoot.localToWorldMatrix;
        mesh.bindposes = bind;
        debugMesh = UnityEngine.Object.Instantiate(mesh);
        var cols = new Color[bones.Length];
        for (int i = 0; i < cols.Length; i++) cols[i] = Color.HSVToRGB((i * 0.61803f) % 1f, 0.85f, 0.95f);
        var c = new Color[bw.Length];
        for (int i = 0; i < c.Length; i++)
        {
            var b = bw[i];
            c[i] = cols[b.boneIndex0] * b.weight0 + cols[b.boneIndex1] * b.weight1 + cols[b.boneIndex2] * b.weight2 + cols[b.boneIndex3] * b.weight3;
        }
        debugMesh.colors = c;
        return mesh;
    }

    public static AnimationClip BuildClip(Transform rigRoot, Transform[] bones)
    {
        var clip = new AnimationClip { name = "Nisse_Wave", frameRate = 30 };
        int keys = (int)(Ex09Motion.Len * 10) + 1;   // a key every 0.1 s
        var rot = new Dictionary<string, Vector3>();
        var curves = new Dictionary<string, AnimationCurve[]>();
        foreach (var b in bones) curves[b.name] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
        var hip = new AnimationCurve(); float baseY = 0;
        foreach (var b in bones) if (b.name == "Hips") baseY = b.localPosition.y;
        for (int k = 0; k < keys; k++)
        {
            float t = k * 0.1f; float tt = k == keys - 1 ? 0f : t;   // last key equals first so the loop is seamless
            Ex09Motion.Pose(tt, rot, out float hy);
            foreach (var b in bones) { var e = rot[b.name]; for (int a = 0; a < 3; a++) curves[b.name][a].AddKey(new Keyframe(t, e[a])); }
            hip.AddKey(new Keyframe(t, baseY + hy));
        }
        void Smooth(AnimationCurve c) { for (int i = 0; i < c.length; i++) { AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto); } }
        foreach (var b in bones)
        {
            string path = PathOf(b, rigRoot);
            for (int a = 0; a < 3; a++)
            {
                Smooth(curves[b.name][a]);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "localEulerAnglesRaw." + "xyz"[a]), curves[b.name][a]);
            }
        }
        Smooth(hip);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Hips", typeof(Transform), "m_LocalPosition.y"), hip);
        var st = AnimationUtility.GetAnimationClipSettings(clip); st.loopTime = true; AnimationUtility.SetAnimationClipSettings(clip, st);
        return clip;
    }

    static Camera MakeCam(Vector3 pos, Vector3 target)
    {
        var go = new GameObject("Main Camera"); go.tag = "MainCamera";
        var cam = go.AddComponent<Camera>(); cam.fieldOfView = 30; cam.nearClipPlane = 0.05f; cam.farClipPlane = 50; cam.allowHDR = true;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.72f, 0.78f, 0.86f);
        go.AddComponent<AudioListener>(); go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        go.transform.position = pos; go.transform.LookAt(target);
        return cam;
    }

    static void Stage(out Camera cam)
    {
        var l = new GameObject("Sun").AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.4f; l.shadows = LightShadows.Soft;
        l.transform.rotation = Quaternion.Euler(45, -35, 0);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.62f);
        var g = GameObject.CreatePrimitive(PrimitiveType.Plane); g.name = "Floor"; UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.localScale = new Vector3(3, 1, 3);
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.SetColor("_BaseColor", new Color(0.82f, 0.8f, 0.76f)); m.SetFloat("_Smoothness", 0.1f);
        AssetDatabase.DeleteAsset("Assets/Nisse/Ex09_floor.mat"); AssetDatabase.CreateAsset(m, "Assets/Nisse/Ex09_floor.mat");
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        cam = MakeCam(new Vector3(0.25f, 0.62f, 2.6f), new Vector3(0, 0.5f, 0));
    }

    public static void OpenAndPlay()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Ex09_Animation.unity");
        EditorApplication.isPlaying = true;
    }

    // main build: rig, skinned mesh, clip, controller, prefab and the Ex09 scene
    public static void Build()
    {
        float pow = float.Parse(Arg("-pow", "3"));
        Directory.CreateDirectory("Captures/Ex09");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var rig = new GameObject("NisseRig");
        var defs = Bones(); var bones = BuildBones(defs, rig.transform);
        var mesh = BuildSkinnedMesh(defs, bones, rig.transform, pow, out var dbg2);
        AssetDatabase.DeleteAsset("Assets/Nisse/Nisse_skinned.asset"); AssetDatabase.CreateAsset(mesh, "Assets/Nisse/Nisse_skinned.asset");
        if (Arg("-debug", "0") == "1") { AssetDatabase.DeleteAsset("Assets/Nisse/Nisse_skinned_debug.asset"); AssetDatabase.CreateAsset(dbg2, "Assets/Nisse/Nisse_skinned_debug.asset"); }
        var body = new GameObject("Body"); body.transform.SetParent(rig.transform, false);
        var smr = body.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = mesh; smr.bones = bones; smr.rootBone = bones[0];
        smr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Nisse/Nisse.mat");
        smr.localBounds = new Bounds(new Vector3(0, 0.5f, 0), new Vector3(1.2f, 1.2f, 1.2f));
        var clip = BuildClip(rig.transform, bones);
        AssetDatabase.DeleteAsset("Assets/Animations/Nisse_Wave.anim"); AssetDatabase.CreateAsset(clip, "Assets/Animations/Nisse_Wave.anim");
        AssetDatabase.DeleteAsset("Assets/Animations/Nisse.controller");
        var ctrl = AnimatorController.CreateAnimatorControllerAtPathWithClip("Assets/Animations/Nisse.controller", clip);
        var anim = rig.AddComponent<Animator>(); anim.runtimeAnimatorController = ctrl;
        PrefabUtility.SaveAsPrefabAsset(rig, "Assets/Nisse/Nisse_Rigged.prefab");
        UnityEngine.Object.DestroyImmediate(rig);

        scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Stage(out var cam);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Nisse/Nisse_Rigged.prefab"));
        var cap = cam.gameObject.AddComponent<RtcgPlayCapture>(); cap.cam = cam;
        var pt = cam.gameObject.AddComponent<Ex09PoseTest>(); pt.cam = cam; pt.animator = inst.GetComponent<Animator>();
        pt.stage = inst;
        if (Arg("-debug", "0") == "1")
        {
            var heat = new GameObject("Heat"); heat.transform.SetParent(inst.transform, false);
            var h = heat.AddComponent<SkinnedMeshRenderer>(); h.sharedMesh = dbg2;
            h.bones = inst.GetComponentInChildren<SkinnedMeshRenderer>().bones; h.rootBone = h.bones[0];
            h.sharedMaterial = new Material(Shader.Find("RTCG/Ex09_VertexColor")); h.localBounds = new Bounds(new Vector3(0, 0.5f, 0), Vector3.one * 1.2f); h.enabled = false;
            pt.heat = h;
        }
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Ex09_Animation.unity");
        AssetDatabase.SaveAssets();
        Debug.Log($"EX09 built bones={bones.Length} verts={mesh.vertexCount} tris={mesh.triangles.Length / 3} clipLen={clip.length}");
    }

    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return def;
    }
}
