// Ex 7. LOD (level of detail) made visible.
// Key L cycles a forced LOD level on every LODGroup: automatic, 0, 1, 2, 3 (LODGroup.ForceLOD, -1 means automatic).
// Key T tints each LOD level in its own colour: LOD0 green, LOD1 yellow, LOD2 orange, LOD3 red.
// The counts per level are worked out here with the same rule Unity uses: the object's size divided by the height
// of the view at the object's distance, times QualitySettings.lodBias, compared with the LOD thresholds.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class LodDebug : MonoBehaviour
{
    public Camera cam;
    public Transform groupsRoot;           // parent of all Nisse with a LODGroup
    public FrustumDebug frustum;           // owns the materials, see FrustumDebug.ApplyMaterials
    public Color[] lodColors =
    {
        new Color(0.15f, 0.8f, 0.2f),      // LOD0 green
        new Color(0.95f, 0.85f, 0.1f),     // LOD1 yellow
        new Color(1f, 0.5f, 0.05f),        // LOD2 orange
        new Color(0.9f, 0.12f, 0.1f),      // LOD3 red
    };

    public bool Tint { get; private set; }
    public int ForcedLod { get; private set; } = -1;
    public int[] Counts { get; } = new int[5];       // LOD0..LOD3, and [4] = too small, culled by the LODGroup
    public int[] CountsInView { get; } = new int[5]; // the same, only for Nisse inside the camera frustum

    LODGroup[] groups;
    readonly Dictionary<Renderer, int> levelOf = new Dictionary<Renderer, int>();
    Material[] tintMats;
    readonly Plane[] planes = new Plane[6];
    bool ready;

    void Start() { Init(); }

    public void Init()
    {
        if (ready) return;
        ready = true;
        groups = groupsRoot.GetComponentsInChildren<LODGroup>();
        foreach (var g in groups)
        {
            var lods = g.GetLODs();
            for (int k = 0; k < lods.Length; k++)
                foreach (var r in lods[k].renderers) levelOf[r] = k;
        }
        tintMats = new Material[lodColors.Length];
        for (int k = 0; k < lodColors.Length; k++)
        {
            tintMats[k] = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "LOD tint " + k, hideFlags = HideFlags.DontSave };
            tintMats[k].SetColor("_BaseColor", lodColors[k]);
            tintMats[k].SetFloat("_Smoothness", 0.2f);
        }
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.tKey.wasPressedThisFrame) SetTint(!Tint);
            if (kb.lKey.wasPressedThisFrame) SetForcedLod(ForcedLod >= 3 ? -1 : ForcedLod + 1);
        }
        Recount();
    }

    public void SetTint(bool on)
    {
        Init();
        Tint = on;
        frustum.ApplyMaterials();
    }

    public void SetForcedLod(int level)
    {
        Init();
        ForcedLod = level;
        foreach (var g in groups) g.ForceLOD(level);
    }

    // Material for a renderer when the tint is on: the colour of the LOD level that renderer belongs to.
    public Material TintFor(Renderer r)
    {
        if (!Tint || tintMats == null) return null;
        return levelOf.TryGetValue(r, out int k) ? tintMats[k] : null;
    }

    public void Recount()
    {
        Init();
        System.Array.Clear(Counts, 0, Counts.Length);
        System.Array.Clear(CountsInView, 0, CountsInView.Length);
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        foreach (var g in groups)
        {
            int level = ForcedLod >= 0 ? ForcedLod : LevelFor(g, cam, out _);
            Counts[level]++;
            var b = new Bounds(g.transform.TransformPoint(g.localReferencePoint), Vector3.one * g.size);
            if (GeometryUtility.TestPlanesAABB(planes, b)) CountsInView[level]++;
        }
    }

    // Which LOD the group picks for this camera. Returns lods.Length when the object is too small (culled).
    public static int LevelFor(LODGroup g, Camera cam, out float screenHeight)
    {
        var lods = g.GetLODs();
        Vector3 s = g.transform.lossyScale;
        float size = g.size * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        if (cam.orthographic)
            screenHeight = size / (2f * cam.orthographicSize);
        else
        {
            float distance = Vector3.Distance(cam.transform.position, g.transform.TransformPoint(g.localReferencePoint));
            screenHeight = size / (2f * distance * Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad));
        }
        screenHeight *= QualitySettings.lodBias;
        for (int k = 0; k < lods.Length; k++)
            if (screenHeight >= lods[k].screenRelativeTransitionHeight) return k;
        return lods.Length;
    }

    void OnDisable()
    {
        if (groups != null) foreach (var g in groups) if (g != null) g.ForceLOD(-1);
    }
}
