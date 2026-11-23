using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Build-up images for the Ex 5 Shadow picture guide. Nothing is saved: values are set through
// MaterialPropertyBlock overrides and the scene is reopened without saving at the end.
public static class RtcgEx5Steps
{
    const string ShadowScene = "Assets/Scenes/Ex 5 Shadow.unity";
    const string Out = "Captures/Ex05_Steps";
    const int W = 1280, H = 720;

    struct Over { public float d; public bool edgeBlack, rimBlack, wispZero, shadowGrey; }

    static void SetProps(NisseShadowDriver drv, Over o)
    {
        var mpb = new MaterialPropertyBlock();
        foreach (var r in drv.targets)
        {
            if (r == null) continue;
            mpb.Clear();
            mpb.SetFloat("_Dissolve", o.d);
            if (o.edgeBlack) mpb.SetColor("_EdgeColor", Color.black);
            if (o.rimBlack) mpb.SetColor("_RimColor", Color.black);
            if (o.wispZero) mpb.SetFloat("_WispAmount", 0f);
            if (o.shadowGrey) mpb.SetColor("_ShadowColor", new Color(0.5f, 0.5f, 0.5f, 1f));
            r.SetPropertyBlock(mpb);
        }
    }

    static void ClearProps(NisseShadowDriver drv)
    {
        foreach (var r in drv.targets) if (r != null) r.SetPropertyBlock(null);
    }

    static void Particles(NisseShadowDriver drv, bool smoke, bool specks)
    {
        drv.smoke.GetComponent<ParticleSystemRenderer>().enabled = smoke;
        drv.specks.GetComponent<ParticleSystemRenderer>().enabled = specks;
    }

    static void Snap(Camera cam, string name)
    {
        RtcgTools.WritePng(cam, $"{Out}/{name}", W, H);
    }

    static void ResetSystems(NisseShadowDriver drv)
    {
        foreach (var ps in new[] { drv.smoke, drv.specks })
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false; ps.randomSeed = 1234;
            ps.Simulate(0f, true, true, false);
        }
    }

    public static void CaptureSteps()
    {
        EditorSceneManager.OpenScene(ShadowScene);
        var drv = Object.FindAnyObjectByType<NisseShadowDriver>();
        var cam = Camera.main;
        Directory.CreateDirectory(Out);
        foreach (var f in Directory.GetFiles(Out, "*.png")) File.Delete(f);

        drv.Apply(0f);
        for (int i = 0; i < 4; i++) { RtcgTools.WritePng(cam, "Captures/_warm_ex5s.png", 160, 90); }
        File.Delete("Captures/_warm_ex5s.png");

        ResetSystems(drv);
        var all = new Over { d = 0.5f };
        const float dt = 1f / 30f;
        float t = 0f;
        bool did25 = false, did43 = false;
        while (!did43)
        {
            t += dt;
            drv.Apply(t);
            drv.smoke.Simulate(dt, true, false, false);
            drv.specks.Simulate(dt, true, false, false);

            if (!did25 && t >= 2.5f - 1e-4f)
            {
                did25 = true;
                Debug.Log($"EX5S t={t:0.000} d={drv.DissolveAt(t, out _):0.000} smoke={drv.smoke.particleCount} specks={drv.specks.particleCount}");
                Particles(drv, false, false);
                SetProps(drv, new Over { d = 0f }); Snap(cam, "step0_solid.png");
                SetProps(drv, new Over { d = 0.5f, edgeBlack = true, rimBlack = true, wispZero = true, shadowGrey = true });
                Snap(cam, "step3a_clip_only.png");
                SetProps(drv, new Over { d = 0.5f, edgeBlack = true, rimBlack = true, wispZero = true });
                Snap(cam, "step3b_plus_colour.png");
                SetProps(drv, new Over { d = 0.5f, rimBlack = true, wispZero = true });
                Snap(cam, "step3c_plus_edge.png");
                SetProps(drv, new Over { d = 0.5f, wispZero = true });
                Snap(cam, "step3d_plus_rim.png");
                SetProps(drv, new Over { d = 0.5f });
                Snap(cam, "step3e_plus_wisps.png");
                Particles(drv, true, false); Snap(cam, "step7a_plus_smoke.png");
                Particles(drv, true, true); Snap(cam, "step7b_plus_specks.png");
                ClearProps(drv); drv.Apply(t);
            }
            if (t >= 4.3f - 1e-4f)
            {
                did43 = true;
                Debug.Log($"EX5S t={t:0.000} d={drv.DissolveAt(t, out _):0.000} smoke={drv.smoke.particleCount} specks={drv.specks.particleCount}");
                Particles(drv, true, true); Snap(cam, "step7c_gone_specks.png");
                Particles(drv, false, true); Snap(cam, "step7d_specks_only.png");
            }
        }
        Particles(drv, true, true);

        // step 6: emitter points. Specks only, body hidden, high rate, first 0.15 s.
        ResetSystems(drv);
        foreach (var r in drv.targets) if (r != null) r.enabled = false;
        Particles(drv, false, true);
        {
            var e = drv.specks.emission; e.rateOverTime = 70000f;
            var m = drv.specks.main; m.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.02f);
            drv.smoke.Clear();
            float tt = 0f;
            while (tt < 0.15f - 1e-4f) { drv.specks.Simulate(dt, true, false, false); tt += dt; }
            Debug.Log($"EX5S emitter points specks={drv.specks.particleCount}");
            Snap(cam, "step6_emitter_points.png");
        }
        foreach (var r in drv.targets) if (r != null) r.enabled = true;

        ExportPuff();
        Debug.Log("EX5S done");
        EditorSceneManager.OpenScene(ShadowScene);
    }

    // Left half: alpha as grayscale. Right half: RGBA composited over a dark grey checker.
    static void ExportPuff()
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Shaders/Ex05/Ex5_SmokePuff.asset");
        const int S = 512;
        var rt = RenderTexture.GetTemporary(S, S, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var prev = RenderTexture.active;
        tex.filterMode = FilterMode.Bilinear;
        Graphics.Blit(tex, rt);
        RenderTexture.active = rt;
        var src = new Texture2D(S, S, TextureFormat.RGBA32, false, true);
        src.ReadPixels(new Rect(0, 0, S, S), 0, 0); src.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        var o = new Texture2D(S * 2, S, TextureFormat.RGB24, false, true);
        var px = src.GetPixels();
        var res = new Color[S * 2 * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var c = px[y * S + x];
                res[y * S * 2 + x] = new Color(c.a, c.a, c.a, 1f);
                float chk = (((x / 32) + (y / 32)) & 1) == 0 ? 0.16f : 0.22f;
                var bg = new Color(chk, chk, chk);
                res[y * S * 2 + S + x] = new Color(c.r * c.a + bg.r * (1 - c.a), c.g * c.a + bg.g * (1 - c.a), c.b * c.a + bg.b * (1 - c.a), 1f);
            }
        o.SetPixels(res); o.Apply();
        File.WriteAllBytes($"{Out}/step5_puff_texture.png", o.EncodeToPNG());
        Object.DestroyImmediate(src); Object.DestroyImmediate(o);
        Debug.Log($"EX5S puff {tex.width}x{tex.height} readable={tex.isReadable}");
    }
}
