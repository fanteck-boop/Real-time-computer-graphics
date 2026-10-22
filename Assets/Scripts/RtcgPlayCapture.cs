using System;
using System.IO;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Headless play-mode frame grabber for the RTCG evidence (gifs, motion blur). Does nothing unless the editor was
// started with "-rtcgCapture <outDir>". Optional: -rtcgFrames N, -rtcgFps N, -rtcgW, -rtcgH, -rtcgMB 0|1 (motion blur).
public class RtcgPlayCapture : MonoBehaviour
{
    public Camera cam;
    public Transform orbitPivot;        // camera orbits this point, if set
    public float orbitDegPerSec = 0;
    public Transform spinTarget;        // object spun around Y, if set
    public float spinDegPerSec = 0;
    public Volume volume;               // for the motion blur toggle

    static string Arg(string n, string d)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == n) return a[i + 1];
        return d;
    }

    // RGBAHalf linear display-referred pixels -> 8-bit sRGB PNG
    public static void WritePngFromHalf(Texture2D half, string path)
    {
        var px = half.GetPixels();
        var o = new Color32[px.Length];
        for (int i = 0; i < px.Length; i++)
            o[i] = new Color32(Enc(px[i].r), Enc(px[i].g), Enc(px[i].b), 255);
        var t = new Texture2D(half.width, half.height, TextureFormat.RGBA32, false);
        t.SetPixels32(o);
        File.WriteAllBytes(path, t.EncodeToPNG());
        if (Application.isPlaying) Destroy(t); else DestroyImmediate(t);
    }
    static byte Enc(float v) { v = Mathf.Clamp01(v); return (byte)Mathf.RoundToInt(Mathf.LinearToGammaSpace(v) * 255f); }

    IEnumerator Start()
    {
        string dir = Arg("-rtcgCapture", "");
        if (dir == "") yield break;
        int frames = int.Parse(Arg("-rtcgFrames", "48")), fps = int.Parse(Arg("-rtcgFps", "24"));
        int w = int.Parse(Arg("-rtcgW", "960")), h = int.Parse(Arg("-rtcgH", "540"));
        string mb = Arg("-rtcgMB", "");
        if (cam == null) cam = Camera.main;
        if (volume != null && volume.profile.TryGet(out MotionBlur m) && mb != "")
        {
            m.active = mb == "1"; m.intensity.Override(1f); m.mode.Override(MotionBlurMode.CameraAndObjects);
            m.quality.Override(MotionBlurQuality.High); m.clamp.Override(0.2f);
        }
        Directory.CreateDirectory(dir);
        Time.captureFramerate = fps;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        cam.targetTexture = rt; cam.aspect = (float)w / h;
        var tex = new Texture2D(w, h, TextureFormat.RGBAHalf, false);
        Vector3 off = orbitPivot != null ? cam.transform.position - orbitPivot.position : Vector3.zero;
        const int warm = 6;
        for (int f = -warm; f < frames; f++)
        {
            float t = Mathf.Max(f, 0) / (float)fps;
            if (orbitPivot != null)
            {
                cam.transform.position = orbitPivot.position + Quaternion.Euler(0, orbitDegPerSec * t, 0) * off;
                cam.transform.LookAt(orbitPivot.position);
            }
            if (spinTarget != null) spinTarget.rotation = Quaternion.Euler(0, spinDegPerSec * t, 0);
            yield return null;   // WaitForEndOfFrame never fires in batchmode; the RT holds the frame rendered after this Update
            if (f < 0) continue;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            WritePngFromHalf(tex, Path.Combine(dir, $"f_{f:000}.png"));
            RenderTexture.active = null;
        }
        Debug.Log("RTCG play capture done " + dir);
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }
}
