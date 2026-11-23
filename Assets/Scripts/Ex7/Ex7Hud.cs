// Ex 7. On-screen numbers (OnGUI): frustum counts, LOD counts, mip settings, frame rate and the engine's render counters.
// Key H hides and shows it. When the frustum is frozen the minimap is drawn in the top right corner.
// Triangles / draw calls come from ProfilerRecorder (Unity.Profiling). UnityStats would be simpler but only exists in the editor.
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

public class Ex7Hud : MonoBehaviour
{
    public FrustumDebug frustum;
    public LodDebug lod;
    public MipDebug mip;
    public int minimapPixels = 360;

    bool visible = true;
    float fps, fpsTimer; int fpsFrames;
    ProfilerRecorder trisRec, drawRec, setPassRec;
    GUIStyle style;

    void OnEnable()
    {
        trisRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        drawRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        setPassRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
    }

    void OnDisable() { trisRec.Dispose(); drawRec.Dispose(); setPassRec.Dispose(); }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.hKey.wasPressedThisFrame) visible = !visible;
        // frame rate averaged over half a second, so the number can be read
        fpsFrames++; fpsTimer += Time.unscaledDeltaTime;
        if (fpsTimer >= 0.5f) { fps = fpsFrames / fpsTimer; fpsFrames = 0; fpsTimer = 0f; }
    }

    static string Millions(long n) => n >= 1000000 ? (n / 1e6).ToString("F2") + " M" : (n / 1e3).ToString("F0") + " k";

    // The text is built in one place so the capture script can show exactly the same lines.
    public string BuildText()
    {
        var sb = new StringBuilder();
        string fpsText = Application.isPlaying ? $"{fps:F0} fps ({1000f / Mathf.Max(fps, 0.01f):F1} ms)" : "n/a (edit mode capture)";
        sb.AppendLine($"<b>Exercise 7: culling, LOD, mipmaps</b>    {fpsText}");
        sb.AppendLine("<b>Frustum culling</b>");
        sb.AppendLine($"  Objects in scene: {frustum.ObjectCount}");
        sb.AppendLine($"  Inside camera frustum (own 6-plane AABB test): {frustum.InsideLive}");
        sb.AppendLine($"  Drawn by the engine (Renderer.isVisible): {frustum.EngineVisible}" + (frustum.Frozen ? "  (minimap camera counts too)" : ""));
        sb.AppendLine(frustum.Frozen
            ? $"  <color=#ffd21a>C: frustum FROZEN</color>, inside: <color=#33dd44>{frustum.InsideFrozen}</color>, outside: <color=#ff4433>{frustum.ObjectCount - frustum.InsideFrozen}</color>"
            : "  C: freeze frustum (off)");
        string forced = lod.ForcedLod < 0 ? "automatic" : "forced LOD" + lod.ForcedLod;
        sb.AppendLine($"<b>LOD</b>   L: {forced}   T: tint {(lod.Tint ? "on" : "off")}");
        var c = lod.Counts; var v = lod.CountsInView;
        sb.AppendLine($"  all Nisse:  <color=#33dd44>LOD0 {c[0]}</color>  <color=#f2d91a>LOD1 {c[1]}</color>  <color=#ff800d>LOD2 {c[2]}</color>  <color=#ff3322>LOD3 {c[3]}</color>  culled {c[4]}");
        sb.AppendLine($"  in view:    <color=#33dd44>LOD0 {v[0]}</color>  <color=#f2d91a>LOD1 {v[1]}</color>  <color=#ff800d>LOD2 {v[2]}</color>  <color=#ff3322>LOD3 {v[3]}</color>  culled {v[4]}");
        sb.AppendLine($"  triangles of the drawn meshes: {Millions(frustum.DrawnTriangles)}");
        sb.AppendLine("<b>Mipmaps</b>");
        sb.AppendLine($"  M: mip level colours {(mip.ShowMipColours ? "on" : "off")}   B: {MipDebug.ModeNames[mip.Mode]}");
        if (mip.ShowMipColours)
        {
            sb.Append("  levels: ");
            for (int i = 0; i <= 10; i++) sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(MipDebug.LevelColors[i])}>{i}</color> ");
            sb.AppendLine();
        }
        if (trisRec.Valid && Application.isPlaying)
            sb.AppendLine($"<b>GPU work</b> (ProfilerRecorder): {Millions(trisRec.LastValue)} triangles, {drawRec.LastValue} draw calls, {setPassRec.LastValue} SetPass calls");
        else
            sb.AppendLine("<b>GPU work</b> (ProfilerRecorder): only counted in Play mode");
        sb.Append("WASD walk, Shift run, mouse look, Esc free cursor, 1/2/3 jump to area, H hide this");
        return sb.ToString();
    }

    void OnGUI()
    {
        if (!visible) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
            style.normal.textColor = Color.white;
        }
        string text = BuildText();
        var size = style.CalcSize(new GUIContent(text));
        GUI.Box(new Rect(8, 8, size.x + 16, size.y + 12), GUIContent.none);
        GUI.Box(new Rect(8, 8, size.x + 16, size.y + 12), GUIContent.none);   // twice for a darker box
        GUI.Label(new Rect(16, 14, size.x, size.y), text, style);

        if (frustum.Frozen && frustum.Minimap != null)
        {
            var r = new Rect(Screen.width - minimapPixels - 10, 10, minimapPixels, minimapPixels);
            GUI.Box(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), GUIContent.none);
            GUI.DrawTexture(r, frustum.Minimap);
            GUI.Label(new Rect(r.x + 6, r.yMax - 24, r.width, 22), "<b>top view, frozen frustum</b>", style);
        }
    }
}
