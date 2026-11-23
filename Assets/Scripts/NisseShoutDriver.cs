using UnityEngine;

// Drives the NisseShout shader graph (and the optional distortion shell) for exercise 5.
// Every `period` seconds a new shout starts at `origin`. Values go through a MaterialPropertyBlock,
// so the material asset on disk is never changed at runtime.
public class NisseShoutDriver : MonoBehaviour
{
    public Renderer[] targets;          // renderers using NisseShout
    public Renderer shell;              // sphere using NisseShoutShell (optional)
    public Transform origin;            // world-space start point of the shout
    public float speed = 1.0f;          // ring speed, units per second
    public float life = 2.2f;           // seconds until the ring has faded out
    public float period = 3.2f;         // seconds between shouts
    public float delay = 0.4f;          // quiet time at the start of each loop

    MaterialPropertyBlock mpb;

    void Update()
    {
        float t = Mathf.Repeat(Time.time, period) - delay;
        Apply(t);
    }

    // t = seconds since the shout started. Negative means "no shout yet".
    public void Apply(float t)
    {
        if (mpb == null) mpb = new MaterialPropertyBlock();
        if (t < 0f) t = 100f;           // idle: ring far away and fully faded
        Vector3 o = origin != null ? origin.position : transform.position;
        foreach (var r in targets)
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetVector("_ShoutOrigin", o);
            mpb.SetFloat("_ShoutTime", t);
            mpb.SetFloat("_ShoutSpeed", speed);
            mpb.SetFloat("_ShoutLife", life);
            r.SetPropertyBlock(mpb);
        }
        if (shell != null)
        {
            bool on = t > 0f && t < life;
            shell.enabled = on;
            float radius = Mathf.Max(0.001f, t * speed);
            shell.transform.position = o;
            shell.transform.localScale = Vector3.one * radius * 2f;   // Unity sphere mesh has radius 0.5
            shell.GetPropertyBlock(mpb);
            mpb.SetFloat("_ShoutTime", t);
            mpb.SetFloat("_ShoutLife", life);
            shell.SetPropertyBlock(mpb);
        }
    }
}
