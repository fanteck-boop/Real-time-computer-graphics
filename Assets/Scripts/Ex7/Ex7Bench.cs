// Ex 7 benchmark: frustum culling and LOD, each on and off, measured on the same camera path.
// Also mipmaps on vs off on the corridor floor (a runtime copy of the texture with only level 0).
// Does nothing in normal play. It only starts when the editor was launched with  -ex7bench <outDir>
// (see Assets/Editor/RtcgEx7Bench.cs). The scene is never saved; every change here is runtime only.
//
// How culling is switched off: Camera.cullingMatrix is set to a fixed orthographic box around the whole level,
// so the engine's frustum test passes for every object. The camera still renders with its normal projection,
// and LOD selection still uses the real camera position and field of view.
// How LOD is switched off: LODGroup.ForceLOD(0) on every LODGroup (the full 178k triangle scan everywhere).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Debug = UnityEngine.Debug;

public class Ex7Bench : MonoBehaviour
{
    enum Variant { Neither, CullingOnly, LodOnly, Both }

    const int W = 1920, H = 1080, WARM = 60, SAMPLE = 360, RUNS = 3;
    const float Pitch = 6f;                                     // degrees down, eye height 1.65 m (the scene's head)
    static readonly Vector3 Spawn = Vector3.zero;               // centre of the Nisse field
    static readonly Vector3 CorridorStart = new Vector3(55f, 0f, -38f);

    string dir;
    Camera cam; Transform player, head;
    LODGroup[] groups;
    List<Renderer[]> objects = new List<Renderer[]>();          // one entry per Nisse / pillar: all its renderers
    RenderTexture rt; Texture2D tex, one;
    Matrix4x4 everything;                                       // culling matrix that contains the whole level

    static string Arg(string n, string d)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == n) return a[i + 1];
        return d;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Arg("-ex7bench", "") == "") return;
        new GameObject("Ex7Bench").AddComponent<Ex7Bench>();
    }

    IEnumerator Start()
    {
        dir = Arg("-ex7bench", "");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "raw"));
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;

        // switch off the interactive scripts so they neither move the camera nor add per-frame work
        var walker = FindAnyObjectByType<FirstPersonWalker>();
        cam = walker.cam; player = walker.transform; head = walker.head;
        walker.enabled = false;
        var cc = player.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
        var fr = FindAnyObjectByType<FrustumDebug>();
        var mip = FindAnyObjectByType<MipDebug>();
        yield return null;                                      // let their Start/Init run once first
        foreach (var mb in new MonoBehaviour[] { fr, FindAnyObjectByType<LodDebug>(), mip, FindAnyObjectByType<Ex7Hud>() })
            if (mb) mb.enabled = false;
        if (fr && fr.spectatorCam) fr.spectatorCam.enabled = false;

        groups = FindObjectsByType<LODGroup>(FindObjectsSortMode.None);
        foreach (var rootName in new[] { "Nisse Field", "Pillars" })
        {
            var root = GameObject.Find(rootName);
            if (root == null) { Debug.LogError("EX7B missing root " + rootName); continue; }
            foreach (Transform c in root.transform) objects.Add(c.GetComponentsInChildren<Renderer>(true));
        }
        // fixed orthographic box, 300 m on each side, around the centre of the level (ground spans x -50..90, z -47..122)
        everything = Matrix4x4.Ortho(-150, 150, -150, 150, -150, 150) * Matrix4x4.Translate(-new Vector3(20, 0, 37));

        rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        cam.targetTexture = rt; cam.aspect = (float)W / H;
        tex = new Texture2D(W, H, TextureFormat.RGBAHalf, false);
        one = new Texture2D(1, 1, TextureFormat.RGBAHalf, false);

        var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        Debug.Log($"EX7B start GPU={SystemInfo.graphicsDeviceName} api={SystemInfo.graphicsDeviceType} cpu={SystemInfo.processorType} unity={Application.unityVersion} " +
                  $"res={W}x{H} lodBias={QualitySettings.lodBias} quality={QualitySettings.names[QualitySettings.GetQualityLevel()]} GRD={(urp ? urp.gpuResidentDrawerMode.ToString() : "?")} " +
                  $"groups={groups.Length} objects={objects.Count} batchmode={Application.isBatchMode}");

        string csv = Path.Combine(dir, "results.csv");
        File.WriteAllText(csv, "run,variant,frames,mean_ms,fps,draw_calls,instances,setpass,triangles_M,vertices_M\n");

        // ---- reference: how many of the 372 objects are inside the real frustum, own 6-plane AABB test, over the path ----
        // (Renderer.isVisible cannot be used: with the GPU Resident Drawer it reported 372 of 372 in every variant.)
        {
            float sum = 0; int min = int.MaxValue, max = 0;
            for (int k = 0; k < 360; k++)
            {
                Place(k / 360f);
                int c = CountInFrustum(); sum += c; min = Mathf.Min(min, c); max = Mathf.Max(max, c);
            }
            Debug.Log($"EX7B inFrustum mean={sum / 360f:F1} min={min} max={max} of {objects.Count}");
        }

        // ---- timing: 3 runs, the 4 variants interleaved inside each run ----
        for (int run = 1; run <= RUNS; run++)
            foreach (Variant v in Enum.GetValues(typeof(Variant)))
            {
                Apply(v);
                for (int f = 0; f < WARM; f++) { Place(f / (float)WARM); yield return null; }
                Place(0); yield return null;
                Sync();
                double sD = 0, sB = 0, sS = 0, sT = 0, sV = 0; int n = 0;
                var sw = Stopwatch.StartNew();
                for (int f = 0; f < SAMPLE; f++)
                {
                    Place(f / (float)SAMPLE);
                    yield return null;
#if UNITY_EDITOR
                    sD += UnityStats.drawCalls; sB += UnityStats.instances; sS += UnityStats.setPassCalls; sT += UnityStats.triangles; sV += UnityStats.vertices;
#endif
                    n++;
                }
                Sync();
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds / SAMPLE;
                string line = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3:F3},{4:F1},{5:F0},{6:F0},{7:F0},{8:F3},{9:F3}\n",
                    run, v, SAMPLE, ms, 1000.0 / ms, sD / n, sB / n, sS / n, sT / n / 1e6, sV / n / 1e6);
                File.AppendAllText(csv, line);
                Debug.Log("EX7B " + line.Trim());
            }

        // ---- images: culling (player view + top view of what was drawn), LOD forced 0 vs automatic ----
        yield return CullingImages(30f);
        yield return LodImages(0f);

        // ---- mipmaps on vs off: corridor floor ----
        yield return Mips(mip, csv);

        Apply(Variant.Both);
        Debug.Log("EX7B done " + csv);
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    void Apply(Variant v)
    {
        bool cull = v == Variant.CullingOnly || v == Variant.Both;
        bool lod = v == Variant.LodOnly || v == Variant.Both;
        if (cull) cam.ResetCullingMatrix(); else cam.cullingMatrix = everything;
        foreach (var g in groups) g.ForceLOD(lod ? -1 : 0);
    }

    // the camera path: standing at the centre of the field, one slow full turn. Only depends on u (0..1).
    void Place(float u) { Pose(Spawn, u * 360f, Pitch); }
    void Pose(Vector3 feet, float yaw, float pitch)
    {
        player.SetPositionAndRotation(feet, Quaternion.Euler(0, yaw, 0));
        head.localRotation = Quaternion.Euler(pitch, 0, 0);
    }

    readonly Plane[] planes = new Plane[6];
    int CountInFrustum()
    {
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        int c = 0;
        foreach (var rs in objects)
        {
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (GeometryUtility.TestPlanesAABB(planes, b)) c++;
        }
        return c;
    }

    void OnEnable() { Application.logMessageReceived += OnLog; }
    void OnDisable() { Application.logMessageReceived -= OnLog; }
    void OnLog(string msg, string stack, LogType type)
    {   // a crash inside the coroutine would otherwise leave the editor running
        if (type != LogType.Exception) return;
        Debug.Log("EX7B aborting after exception: " + msg);
#if UNITY_EDITOR
        EditorApplication.Exit(4);
#endif
    }

    void Sync()
    {   // blocking 1 pixel read-back: returns only when the GPU has finished all queued work
        RenderTexture.active = rt; one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); RenderTexture.active = null;
    }

    void Snap(RenderTexture src, Texture2D dst, string name)
    {
        RenderTexture.active = src; dst.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0); dst.Apply(); RenderTexture.active = null;
        RtcgPlayCapture.WritePngFromHalf(dst, Path.Combine(dir, "raw", name));
    }

    string Stats()
    {
#if UNITY_EDITOR
        return string.Format(CultureInfo.InvariantCulture, "draw_calls={0} instances={1} setpass={2} triangles={3} vertices={4}",
            UnityStats.drawCalls, UnityStats.instances, UnityStats.setPassCalls, UnityStats.triangles, UnityStats.vertices);
#else
        return "";
#endif
    }

    IEnumerator CullingImages(float yaw)
    {
        // top camera: orthographic, straight down over the field (field +-36 m, pillars at 42 m)
        var topGO = new GameObject("Bench top camera");
        var top = topGO.AddComponent<Camera>();
        top.orthographic = true; top.orthographicSize = 46f; top.nearClipPlane = 1f; top.farClipPlane = 300f;
        top.clearFlags = CameraClearFlags.SolidColor; top.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
        top.allowHDR = false; top.allowMSAA = false; top.enabled = false;
        topGO.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        topGO.transform.SetPositionAndRotation(new Vector3(0, 150, 0), Quaternion.Euler(90, 0, 0));
        var trt = new RenderTexture(1080, 1080, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        top.targetTexture = trt; top.aspect = 1f;
        var ttex = new Texture2D(1080, 1080, TextureFormat.RGBAHalf, false);

        var green = new Material(Shader.Find("Universal Render Pipeline/Unlit")); green.SetColor("_BaseColor", new Color(0.1f, 0.85f, 0.2f));
        
        // The top view shows what the engine's culling lets through: the top camera is given the PLAYER's culling
        // matrix, so the engine culls the top view against the player's frustum (or against the whole level when
        // culling is defeated). Objects that are culled are simply missing from the top view. Nisse and pillars are
        // tinted green so they stand out from above.
        foreach (var v in new[] { Variant.LodOnly, Variant.Both })   // culling off / on, LOD automatic in both
        {
            Apply(v);
            for (int f = 0; f < 6; f++) { Pose(Spawn, yaw, Pitch); yield return null; }
            string tag = v == Variant.Both ? "cull_on" : "cull_off";
            string st = Stats();
            Snap(rt, tex, $"{tag}_player.png");
            Debug.Log($"EX7B image {tag}: yaw={yaw} inFrustum={CountInFrustum()} of {objects.Count} {st}");

            var saved = new List<(Renderer, Material[])>();
            foreach (var rs in objects)
                foreach (var r in rs) { saved.Add((r, r.sharedMaterials)); var ms = new Material[r.sharedMaterials.Length]; for (int k = 0; k < ms.Length; k++) ms[k] = green; r.sharedMaterials = ms; }
            foreach (var g in groups) g.ForceLOD(3);                  // top view: same small mesh for all, so every Nisse shows
            top.cullingMatrix = v == Variant.Both ? cam.projectionMatrix * cam.worldToCameraMatrix : everything;
            cam.enabled = false; top.enabled = true;
            yield return null; yield return null;
            string tst = Stats();
            Snap(trt, ttex, $"{tag}_top.png");
            Debug.Log($"EX7B image {tag}_top: {tst}");
            top.enabled = false; cam.enabled = true; top.ResetCullingMatrix();
            foreach (var (r, ms) in saved) r.sharedMaterials = ms;
            Apply(v);
        }
        File.WriteAllText(Path.Combine(dir, "raw", "top_camera.txt"), string.Format(CultureInfo.InvariantCulture,
            "centre_x=0\ncentre_z=0\northo_size={0}\npixels=1080\nplayer_x={1}\nplayer_z={2}\nyaw={3}\nhfov_deg={4}\nfar={5}\n",
            top.orthographicSize, Spawn.x, Spawn.z, yaw, Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, (float)W / H), cam.farClipPlane));
        Destroy(topGO);
    }

    IEnumerator LodImages(float yaw)
    {
        foreach (var v in new[] { Variant.CullingOnly, Variant.Both })   // LOD forced 0 / automatic, culling on in both
        {
            Apply(v);
            for (int f = 0; f < 6; f++) { Pose(Spawn, yaw, Pitch); yield return null; }
            string st = Stats();
            Snap(rt, tex, v == Variant.Both ? "lod_auto.png" : "lod_forced0.png");
            Debug.Log($"EX7B image {(v == Variant.Both ? "lod_auto" : "lod_forced0")}: yaw={yaw} {st}");
        }
    }

    IEnumerator Mips(MipDebug mip, string csv)
    {
        Apply(Variant.Both);
        var floor = mip.surfaces[0];
        var original = floor.sharedMaterial;
        var src = (Texture2D)original.GetTexture("_BaseMap");
        // runtime copy that has only mip level 0, made by a 1:1 blit into a render texture without mips.
        // The imported texture and its import settings are not touched.
        var noMip = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        { name = "corridor level 0 only", useMipMap = false, wrapMode = src.wrapMode, filterMode = FilterMode.Bilinear, anisoLevel = src.anisoLevel };
        noMip.Create();
        Graphics.Blit(src, noMip);
        var noMipMat = new Material(original) { name = "corridor no mips" };
        noMipMat.SetTexture("_BaseMap", noMip);
        Debug.Log($"EX7B mips: src={src.name} {src.width}x{src.height} fmt={src.graphicsFormat} mips={src.mipmapCount} filter={src.filterMode} aniso={src.anisoLevel} globalAniso={QualitySettings.anisotropicFiltering}; copy mips={noMip.mipmapCount} copyFmt={noMip.graphicsFormat}");

        for (int run = 1; run <= RUNS; run++)
            foreach (bool on in new[] { true, false })
            {
                floor.sharedMaterial = on ? original : noMipMat;
                for (int f = 0; f < WARM; f++) { Pose(CorridorStart, 0f, 8f); yield return null; }
                if (run == 1) Snap(rt, tex, on ? "mips_on.png" : "mips_off.png");
                Sync();
                double sD = 0, sB = 0, sS = 0, sT = 0, sV = 0; int n = 0;
                var sw = Stopwatch.StartNew();
                for (int f = 0; f < SAMPLE; f++)
                {
                    yield return null;
#if UNITY_EDITOR
                    sD += UnityStats.drawCalls; sB += UnityStats.instances; sS += UnityStats.setPassCalls; sT += UnityStats.triangles; sV += UnityStats.vertices;
#endif
                    n++;
                }
                Sync(); sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds / SAMPLE;
                string line = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3:F3},{4:F1},{5:F0},{6:F0},{7:F0},{8:F3},{9:F3},\n",
                    run, on ? "MipsOn_corridor" : "MipsOff_corridor", SAMPLE, ms, 1000.0 / ms, sD / n, sB / n, sS / n, sT / n / 1e6, sV / n / 1e6);
                File.AppendAllText(csv, line);
                Debug.Log("EX7B " + line.Trim());
            }
        floor.sharedMaterial = original;
    }
}
