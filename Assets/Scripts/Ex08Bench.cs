using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Debug = UnityEngine.Debug;

// Ex08: thousands of Nisse copies, compared with and without GPU instancing and LOD.
// Interactive: press 1..5 to switch variant, the overlay shows live numbers.
// Benchmark: start the editor with  -ex08bench <outDir> [-ex08scenario main|small]  (see RtcgPart3.RunEx08).
public class Ex08Bench : MonoBehaviour
{
    public enum Variant { Baseline, SrpBatcher, Instancing, Lod, LodInstancing, Grd, GrdLod }

    public Mesh lod0, lod1, lod2, lod3;                // 252k, 25k, 5k triangles
    public Material matPlain, matInstanced;       // identical URP/Lit + Nisse texture, only "Enable GPU Instancing" differs
    public Vector3 meshLocalPos; public float meshLocalScale = 1f;   // wrapper offset taken from Nisse.prefab
    public Camera cam;
    public int count = 2500;
    public float spacing = 1.2f;
    public Variant variant = Variant.Baseline;
    public string scenario = "main";            // main: full scan + LODs | small: 5k mesh x10 000 | tiny: 1.3k mesh x20 000
    bool single => scenario != "main";

    GameObject root;
    ProfilerRecorder rDraw, rBatch, rSetPass, rTris, rVerts;
    readonly string[] names = { "1 Baseline (no batching)", "2 SRP Batcher", "3 GPU instancing", "4 LOD", "5 LOD + GPU instancing", "6 GPU Resident Drawer (project default)", "7 GPU Resident Drawer + LOD" };

    static string Arg(string n, string d)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == n) return a[i + 1];
        return d;
    }

    void OnEnable()
    {
        rDraw = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        rBatch = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        rSetPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        rTris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        rVerts = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
    }
    void OnDisable() { rDraw.Dispose(); rBatch.Dispose(); rSetPass.Dispose(); rTris.Dispose(); rVerts.Dispose(); }

    IEnumerator Start()
    {
        string dir = Arg("-ex08bench", "");
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        QualitySettings.lodBias = 1f; QualitySettings.maximumLODLevel = 0;
        if (dir == "") { Build(variant); yield break; }
        scenario = Arg("-ex08scenario", "main");
        if (scenario == "small") { count = 10000; spacing = 0.8f; }
        if (scenario == "tiny") { count = 20000; spacing = 0.6f; }
        yield return Benchmark(dir);
    }

    // ---------- scene construction ----------
    public void Build(Variant v, bool lodTint = false)
    {
        variant = v;
        if (root != null) Destroy(root);
        root = new GameObject("Crowd");
        // the SRP Batcher takes precedence over GPU instancing for compatible shaders, so it is only on for variant 2
        bool grd = v == Variant.Grd || v == Variant.GrdLod;
        SetGrd(grd);
        GraphicsSettings.useScriptableRenderPipelineBatching = v == Variant.SrpBatcher || grd;   // the GPU Resident Drawer needs the SRP Batcher on
        bool inst = v == Variant.Instancing || v == Variant.LodInstancing;
        bool lod = (v == Variant.Lod || v == Variant.LodInstancing || v == Variant.GrdLod) && !single;
        Material m = inst ? matInstanced : matPlain;
        Material[] tint = null;
        if (lodTint)
        {
            tint = new Material[3];
            Color[] c = { new Color(0.9f, 0.25f, 0.2f), new Color(0.3f, 0.8f, 0.3f), new Color(0.25f, 0.45f, 0.95f) };
            for (int i = 0; i < 3; i++) { tint[i] = new Material(m); tint[i].SetTexture("_BaseMap", null); tint[i].SetColor("_BaseColor", c[i]); }
        }
        int side = Mathf.CeilToInt(Mathf.Sqrt(count));
        var rng = new System.Random(1234);
        for (int i = 0; i < count; i++)
        {
            float x = (i % side - side * 0.5f) * spacing + (float)(rng.NextDouble() - 0.5) * 0.3f;
            float z = (i / side - side * 0.5f) * spacing + (float)(rng.NextDouble() - 0.5) * 0.3f;
            float yaw = (float)rng.NextDouble() * 360f, sc = 0.9f + (float)rng.NextDouble() * 0.2f;
            var g = new GameObject("n" + i);
            g.transform.SetParent(root.transform, false);
            g.transform.localPosition = new Vector3(x, 0, z); g.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            g.transform.localScale = Vector3.one * sc;
            if (!lod) { AddMesh(g, scenario == "small" ? lod2 : scenario == "tiny" ? lod3 : lod0, tint != null ? tint[0] : m); continue; }
            var lg = g.AddComponent<LODGroup>();
            var lods = new LOD[3];
            Mesh[] ms = { lod0, lod1, lod2 };
            float[] h = { 0.30f, 0.10f, 0.02f };   // LOD0 above 30% of screen height, LOD1 above 10%, LOD2 above 2%, culled below
            for (int k = 0; k < 3; k++)
            {
                var c = new GameObject("LOD" + k); c.transform.SetParent(g.transform, false);
                var r = AddMesh(c, ms[k], tint != null ? tint[k] : m);
                lods[k] = new LOD(h[k], new Renderer[] { r });
            }
            lg.SetLODs(lods); lg.RecalculateBounds();
        }
    }

    GPUResidentDrawerMode grdOriginal; bool grdSaved;
    // The project's pipeline asset has the GPU Resident Drawer on (it batches plain MeshRenderers through BatchRendererGroup).
    // The asset is only changed in memory for the run and restored in RestoreGrd; it is never saved.
    void SetGrd(bool on)
    {
        var a = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (a == null) return;
        if (!grdSaved) { grdOriginal = a.gpuResidentDrawerMode; grdSaved = true; }
        a.gpuResidentDrawerMode = on ? grdOriginal : GPUResidentDrawerMode.Disabled;
    }
    void ApplySrpb() { GraphicsSettings.useScriptableRenderPipelineBatching = variant == Variant.SrpBatcher || variant == Variant.Grd || variant == Variant.GrdLod; }
    void RestoreGrd() { if (grdSaved) SetGrd(true); GraphicsSettings.useScriptableRenderPipelineBatching = true; }
    void OnApplicationQuit() { RestoreGrd(); }

    MeshRenderer AddMesh(GameObject g, Mesh mesh, Material m)
    {
        var holder = new GameObject("mesh"); holder.transform.SetParent(g.transform, false);
        holder.transform.localPosition = meshLocalPos; holder.transform.localScale = Vector3.one * meshLocalScale;
        holder.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = holder.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return r;
    }

    // deterministic camera path, a function of the frame index only (identical for every variant)
    public void PlaceCamera(float u)
    {
        float span = Mathf.CeilToInt(Mathf.Sqrt(count)) * spacing;
        float a = u * Mathf.PI * 2f;
        float R = span * 0.30f;
        Vector3 p = new Vector3(Mathf.Cos(a) * R, 2.2f + Mathf.Sin(a * 2f) * 0.6f, Mathf.Sin(a) * R);
        Vector3 look = new Vector3(Mathf.Cos(a + 2.4f) * R * 0.5f, 0.6f, Mathf.Sin(a + 2.4f) * R * 0.5f);
        cam.transform.position = p; cam.transform.LookAt(look);
    }

    // ---------- benchmark ----------
    IEnumerator Benchmark(string dir)
    {
        const int W = 1920, H = 1080, WARM = 90, SAMPLE = 240;
        Directory.CreateDirectory(dir);
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        cam.targetTexture = rt; cam.aspect = (float)W / H;
        var tex = new Texture2D(W, H, TextureFormat.RGBAHalf, false);
        var one = new Texture2D(1, 1, TextureFormat.RGBAHalf, false);
        string scen = scenario;
        string csv = Path.Combine(dir, $"results_{scen}.csv");
        File.WriteAllText(csv, "scenario,variant,count,frames,avg_ms,fps,p95_ms,max_ms,draw_calls,instances,setpass,triangles_M,vertices_M,cpu_frame_ms,cpu_render_ms\n");
        var combos = single
            ? new[] { Variant.Baseline, Variant.SrpBatcher, Variant.Instancing, Variant.Grd }
            : new[] { Variant.Baseline, Variant.SrpBatcher, Variant.Instancing, Variant.Lod, Variant.LodInstancing, Variant.Grd, Variant.GrdLod };
        Debug.Log($"EX08 start scenario={scen} count={count} GPU={SystemInfo.graphicsDeviceName} api={SystemInfo.graphicsDeviceType} unity={Application.unityVersion} res={W}x{H} recordersValid={rDraw.Valid}/{rBatch.Valid}/{rSetPass.Valid}/{rTris.Valid}");
        foreach (var v in combos)
        {
            Build(v);
            for (int f = 0; f < WARM; f++) { PlaceCamera(0); ApplySrpb(); yield return null; }   // changing the GRD state recreates the pipeline, which resets the SRP Batcher flag, so it is re-applied here
            foreach (float u in new[] { 0f, 0.5f }) { PlaceCamera(u); yield return null; yield return null; Snap(rt, tex, Path.Combine(dir, $"{scen}_{v}_u{(int)(u * 100)}.png")); }
            for (int f = 0; f < 10; f++) { PlaceCamera(0); yield return null; }
            Sync(rt, one);
            var dt = new List<float>(SAMPLE); double sS = 0, tS = 0, vS = 0; int n = 0; double uD = 0, uI = 0, uS = 0, uT = 0, uF = 0, uR = 0;
            var sw = Stopwatch.StartNew();
            for (int f = 0; f < SAMPLE; f++)
            {
                PlaceCamera(f / (float)SAMPLE);
                yield return null;
                dt.Add(Time.unscaledDeltaTime * 1000f);
                sS += rSetPass.LastValue; tS += rTris.LastValue; vS += rVerts.LastValue; n++;
#if UNITY_EDITOR
                uD += UnityStats.drawCalls; uI += UnityStats.instances; uS += UnityStats.setPassCalls; uT += UnityStats.triangles; uF += UnityStats.frameTime; uR += UnityStats.renderTime;
#endif
            }
            Sync(rt, one);
            sw.Stop();
            double avg = sw.Elapsed.TotalMilliseconds / SAMPLE;
            dt.Sort();
            string line = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4:F2},{5:F1},{6:F2},{7:F2},{8:F0},{9:F0},{10:F0},{11:F3},{12:F3},{13:F2},{14:F2}\n",
                scen, v, count, SAMPLE, avg, 1000.0 / avg, dt[(int)(dt.Count * 0.95)], dt[dt.Count - 1],
                uD / n, uI / n, uS / n, uT / n / 1e6, vS / n / 1e6, uF / n * 1000, uR / n * 1000);
            File.AppendAllText(csv, line);
#if UNITY_EDITOR
            { var sb = new StringBuilder(); foreach (var pr in typeof(UnityStats).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)) { try { sb.Append(pr.Name + "=" + pr.GetValue(null) + " "); } catch { } } Debug.Log("EX08 UnityStatsAll " + v + ": " + sb); }
#endif
            Debug.Log($"EX08 recorders {v}: setpass={sS / n:F0} tris={tS / n / 1e6:F2}M verts={vS / n / 1e6:F2}M");
            Debug.Log("EX08 " + line.Trim());
        }
        if (!single)
        {   // LOD visualisation: same path point, LOD levels tinted red / green / blue
            Build(Variant.Lod, true);
            for (int f = 0; f < 4; f++) { PlaceCamera(0); yield return null; }
            Snap(rt, tex, Path.Combine(dir, "main_lod_tint.png"));
            PlaceCamera(0.5f); yield return null; yield return null;
            Snap(rt, tex, Path.Combine(dir, "main_lod_tint_b.png"));
        }
        RestoreGrd();
        Debug.Log("EX08 done " + csv);
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    void Sync(RenderTexture rt, Texture2D one)
    {   // blocking 1-pixel read-back: returns only when the GPU has finished everything queued so far
        RenderTexture.active = rt; one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); RenderTexture.active = null;
    }

    void Snap(RenderTexture rt, Texture2D tex, string path)
    {
        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = null;
        RtcgPlayCapture.WritePngFromHalf(tex, path);
    }

    // ---------- interactive overlay ----------
    float fpsAcc; int fpsN; float fpsShown; float tAcc;
    void Update()
    {
        if (Arg("-ex08bench", "") != "") return;
        var kb = Keyboard.current;
        if (kb != null) { Key[] ks = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7 }; for (int i = 0; i < 7; i++) if (kb[ks[i]].wasPressedThisFrame) Build((Variant)i); }
        PlaceCamera((Time.time * 0.02f) % 1f);
        fpsAcc += Time.unscaledDeltaTime; fpsN++; tAcc += Time.unscaledDeltaTime;
        if (tAcc > 0.5f) { fpsShown = fpsN / fpsAcc; fpsAcc = 0; fpsN = 0; tAcc = 0; }
    }
    void OnGUI()
    {
        if (Arg("-ex08bench", "") != "") return;
        GUI.Label(new Rect(10, 10, 700, 100),
            $"{names[(int)variant]}   keys 1-7\nFPS {fpsShown:F0}   batches {rBatch.LastValue}   draw calls {rDraw.LastValue}   SetPass {rSetPass.LastValue}   tris {rTris.LastValue / 1e6f:F1} M");
    }
}
