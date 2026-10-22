using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Choreography of the Nisse wave. Pose() is sampled every 0.1 s by the editor to write the AnimationClip keys.
public static class Ex09Motion
{
    public const float Len = 6f;
    static float Sm(float a, float b, float t) { t = Mathf.Clamp01((t - a) / (b - a)); return t * t * (3 - 2 * t); }

    // returns euler (x,y,z) degrees per bone name, and hips height offset, at time t (loops at Len)
    public static void Pose(float t, Dictionary<string, Vector3> rot, out float hipY)
    {
        float W = 2 * Mathf.PI;
        float raise = Sm(0.6f, 1.4f, t) * (1 - Sm(4.4f, 5.2f, t));         // right arm up, then down
        float waveAmp = Sm(1.2f, 1.7f, t) * (1 - Sm(4.2f, 4.6f, t));
        float wave = Mathf.Sin(W * 2.2f * (t - 1.4f)) * waveAmp;           // 2.2 waves per second
        float sway = Mathf.Sin(W * t / Len * 3f);                          // 3 sways per loop
        float bob = Mathf.Sin(W * t / Len * 6f);
        hipY = 0.012f * bob;
        rot["Hips"] = new Vector3(0, 6f * Mathf.Sin(W * t / Len), -4f * sway);
        rot["Spine"] = new Vector3(2f * bob, 0, -3f * Mathf.Sin(W * t / Len * 3f - 0.6f));
        float nod = Sm(4.9f, 5.2f, t) * (1 - Sm(5.3f, 5.7f, t));
        rot["Head"] = new Vector3(14f * nod, 14f * Mathf.Sin(W * t / Len * 2f), 5f * raise + 3f * sway);
        for (int i = 1; i <= 4; i++)
        {
            string n = i == 4 ? "HatTip" : "Hat" + i; float lag = 0.12f * i;
            rot[n] = new Vector3(2f * Mathf.Sin(W * (t - lag) / Len * 6f), 0, 5f * Mathf.Sin(W * (t - lag) / Len * 3f - 0.8f) - 3f * raise * Mathf.Sin(W * (t - lag) / Len * 3f));
        }
        rot["Shoulder_R"] = new Vector3(0, 0, 118f * raise + 2f * bob);
        rot["Forearm_R"] = new Vector3(0, 0, 28f * wave + 25f * raise);
        rot["Shoulder_L"] = new Vector3(0, 0, -8f * Mathf.Sin(W * t / Len * 3f + 1f) - 3f);
        rot["Forearm_L"] = new Vector3(0, 0, -6f * Mathf.Sin(W * t / Len * 3f + 1.5f));
        rot["Leg_R"] = new Vector3(0, 0, 1.5f * sway); rot["Leg_L"] = new Vector3(0, 0, 1.5f * sway);
        rot["Foot_R"] = Vector3.zero; rot["Foot_L"] = Vector3.zero;
        rot["Root"] = Vector3.zero;
    }

}

// Play-mode test: with -ex09pose <dir> it poses the rig by hand and saves images (used to judge the skin weights).
public class Ex09PoseTest : MonoBehaviour
{
    public Camera cam; public Animator animator; public GameObject stage; public SkinnedMeshRenderer heat;
    static string Arg(string n, string d) { var a = Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == n) return a[i + 1]; return d; }

    IEnumerator Start()
    {
        string dir = Arg("-ex09pose", "");
        if (dir == "") yield break;
        var cap = GetComponent<RtcgPlayCapture>(); if (cap) cap.enabled = false;
        animator.enabled = false;
        Directory.CreateDirectory(dir);
        const int W = 640, H = 800;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        cam.targetTexture = rt; cam.aspect = (float)W / H;
        var tex = new Texture2D(W, H, TextureFormat.RGBAHalf, false);
        var bones = new Dictionary<string, Transform>();
        foreach (var t in stage.GetComponentsInChildren<Transform>()) bones[t.name] = t;
        var hipBase = bones["Hips"].localPosition;
        var body = stage.GetComponentInChildren<SkinnedMeshRenderer>();
        void Snap(string name)
        {
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = null;
            RtcgPlayCapture.WritePngFromHalf(tex, Path.Combine(dir, name + ".png"));
        }
        void Reset() { foreach (var kv in bones) if (kv.Key != "NisseRig" && kv.Key != "Body" && kv.Key != "Heat") kv.Value.localRotation = Quaternion.identity; bones["Hips"].localPosition = hipBase; }
        var rot = new Dictionary<string, Vector3>();
        foreach (float t in new[] { 0f, 1.45f, 2.0f, 3.1f, 5.0f })
        {
            Ex09Motion.Pose(t, rot, out float hy); Reset();
            foreach (var kv in rot) bones[kv.Key].localRotation = Quaternion.Euler(kv.Value);
            bones["Hips"].localPosition = hipBase + Vector3.up * hy;
            yield return null; yield return null; Snap($"pose_t{t:F2}");
        }
        Reset();
        bones["Shoulder_R"].localRotation = Quaternion.Euler(0, 0, 130); bones["Shoulder_L"].localRotation = Quaternion.Euler(0, 0, -60);
        bones["Head"].localRotation = Quaternion.Euler(20, 35, 15); bones["Hips"].localRotation = Quaternion.Euler(0, 20, -10);
        bones["Leg_R"].localRotation = Quaternion.Euler(-25, 0, 0); bones["Hat1"].localRotation = Quaternion.Euler(0, 0, 25);
        yield return null; yield return null; Snap("extreme");
        yield return null; Snap("extreme_b");
        if (heat != null)
        {
            body.enabled = false; heat.enabled = true;
            yield return null; yield return null; Snap("extreme_heat");
            Reset(); yield return null; yield return null; Snap("rest_heat");
            cam.transform.position = new Vector3(2.4f, 0.62f, 0.2f); cam.transform.LookAt(new Vector3(0, 0.5f, 0));
            yield return null; yield return null; Snap("rest_heat_side");
        }
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }
}
