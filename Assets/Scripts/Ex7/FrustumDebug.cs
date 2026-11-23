// Ex 7. Frustum culling made visible.
// Every frame: the 6 planes of the player camera's frustum are tested against the bounding box (AABB) of every object,
// and the result is counted next to the engine's own number (Renderer.isVisible).
// Key C freezes the frustum: the planes stop following the camera, the frustum is drawn as yellow lines,
// a top-down camera renders a minimap, and every object turns green (inside the frozen frustum) or red (outside).
// You can then keep walking and look at the frozen frustum from outside.
// This script also owns the materials of the tested objects, so the LOD tint (LodDebug) goes through ApplyMaterials too.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class FrustumDebug : MonoBehaviour
{
    [Header("Cameras")]
    public Camera playerCam;
    public Camera spectatorCam;            // orthographic, looks straight down, renders into the minimap texture
    [Header("Objects: every direct child of these roots counts as one object")]
    public Transform[] objectRoots;
    public LodDebug lodDebug;
    [Header("Look")]
    public Color insideColor = new Color(0.15f, 0.8f, 0.2f);
    public Color outsideColor = new Color(0.9f, 0.12f, 0.1f);
    public Color lineColor = new Color(1f, 0.85f, 0.05f);
    public float lineWidth = 0.35f;        // metres
    public float minimapAhead = 22f;       // the minimap is centred this far in front of the frozen camera
    public float minimapHalfSize = 40f;    // orthographic size: half the height of the minimap in metres

    public bool Frozen { get; private set; }
    public int ObjectCount => items.Count;
    public int InsideLive { get; private set; }      // own AABB test against the live player frustum
    public int InsideFrozen { get; private set; }    // own AABB test against the frozen frustum
    public int EngineVisible { get; private set; }   // objects with at least one renderer that has isVisible == true
    public long DrawnTriangles { get; private set; } // triangles of the meshes the engine reports as visible
    public RenderTexture Minimap { get; private set; }

    class Item
    {
        public Bounds bounds;          // world space axis aligned box around the whole object
        public Renderer[] renderers;   // every renderer of the object (gets tinted)
        public Material[] original;
        public Renderer[] counted;     // the renderers that count for "drawn": the LOD meshes (not the plinth)
        public int[] triangles;
        public bool insideFrozen;
    }

    readonly List<Item> items = new List<Item>();
    readonly Plane[] livePlanes = new Plane[6];
    readonly Plane[] frozenPlanes = new Plane[6];
    Material insideMat, outsideMat;
    LineRenderer[] edges;
    bool ready;

    void Start() { Init(); }

    public void Init()
    {
        if (ready) return;
        ready = true;
        insideMat = MakeLit("Frustum inside", insideColor);
        outsideMat = MakeLit("Frustum outside", outsideColor);

        foreach (var root in objectRoots)
            foreach (Transform child in root) items.Add(MakeItem(child));

        // 12 edges of the frustum: near rectangle, far rectangle, 4 side edges
        var lineMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Frustum lines", hideFlags = HideFlags.DontSave };
        lineMat.SetColor("_BaseColor", lineColor);
        var holder = new GameObject("Frozen frustum lines") { hideFlags = HideFlags.DontSave };
        edges = new LineRenderer[12];
        for (int i = 0; i < 12; i++)
        {
            var go = new GameObject("Edge " + i) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(holder.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.positionCount = 2; lr.widthMultiplier = lineWidth;
            lr.sharedMaterial = lineMat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            lr.enabled = false;
            edges[i] = lr;
        }

        Minimap = new RenderTexture(512, 512, 24) { name = "Minimap", hideFlags = HideFlags.DontSave };
        spectatorCam.targetTexture = Minimap;
        spectatorCam.enabled = false;
    }

    static Material MakeLit(string name, Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, hideFlags = HideFlags.DontSave };
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", 0.2f);
        return m;
    }

    Item MakeItem(Transform t)
    {
        var it = new Item();
        it.renderers = t.GetComponentsInChildren<Renderer>(true);
        it.original = new Material[it.renderers.Length];
        it.bounds = it.renderers[0].bounds;
        for (int i = 0; i < it.renderers.Length; i++)
        {
            it.original[i] = it.renderers[i].sharedMaterial;
            it.bounds.Encapsulate(it.renderers[i].bounds);
        }
        var group = t.GetComponent<LODGroup>();
        if (group != null)
        {
            var list = new List<Renderer>();
            foreach (var lod in group.GetLODs()) list.AddRange(lod.renderers);
            it.counted = list.ToArray();
        }
        else it.counted = it.renderers;
        it.triangles = new int[it.counted.Length];
        for (int i = 0; i < it.counted.Length; i++)
        {
            var mf = it.counted[i].GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) it.triangles[i] += (int)(mf.sharedMesh.GetIndexCount(s) / 3);
        }
        return it;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.cKey.wasPressedThisFrame)
        {
            if (Frozen) Unfreeze(); else Freeze();
        }
        Recount();
    }

    // The test itself. CalculateFrustumPlanes gives 6 planes (left, right, bottom, top, near, far) with normals pointing
    // into the frustum. TestPlanesAABB returns false as soon as the box lies completely on the outside of one plane.
    public void Recount()
    {
        GeometryUtility.CalculateFrustumPlanes(playerCam, livePlanes);
        int inside = 0, visible = 0; long tris = 0;
        foreach (var it in items)
        {
            if (GeometryUtility.TestPlanesAABB(livePlanes, it.bounds)) inside++;
            bool any = false;
            for (int i = 0; i < it.counted.Length; i++)
                if (it.counted[i].isVisible) { any = true; tris += it.triangles[i]; }
            if (any) visible++;
        }
        InsideLive = inside; EngineVisible = visible; DrawnTriangles = tris;
    }

    public void Freeze()
    {
        Init();
        Frozen = true;
        GeometryUtility.CalculateFrustumPlanes(playerCam, frozenPlanes);
        int inside = 0;
        foreach (var it in items)
        {
            it.insideFrozen = GeometryUtility.TestPlanesAABB(frozenPlanes, it.bounds);
            if (it.insideFrozen) inside++;
        }
        InsideFrozen = inside;

        // frustum corners in world space
        var n = new Vector3[4]; var f = new Vector3[4];
        var full = new Rect(0, 0, 1, 1);
        playerCam.CalculateFrustumCorners(full, playerCam.nearClipPlane, Camera.MonoOrStereoscopicEye.Mono, n);
        playerCam.CalculateFrustumCorners(full, playerCam.farClipPlane, Camera.MonoOrStereoscopicEye.Mono, f);
        var ct = playerCam.transform;
        for (int i = 0; i < 4; i++) { n[i] = ct.TransformPoint(n[i]); f[i] = ct.TransformPoint(f[i]); }
        for (int i = 0; i < 4; i++)
        {
            SetEdge(i, n[i], n[(i + 1) % 4]);       // near rectangle (tiny, the near plane is only 0.1 m away)
            SetEdge(4 + i, f[i], f[(i + 1) % 4]);   // far rectangle
            SetEdge(8 + i, n[i], f[i]);             // side edges
        }

        // minimap camera: high above, looking down, "up" in the picture is the direction the frozen camera looked
        Vector3 fwd = Vector3.ProjectOnPlane(ct.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        spectatorCam.transform.position = ct.position + fwd * minimapAhead + Vector3.up * 150f;
        spectatorCam.transform.rotation = Quaternion.LookRotation(Vector3.down, fwd);
        spectatorCam.orthographic = true;
        spectatorCam.orthographicSize = minimapHalfSize;
        spectatorCam.enabled = true;
        ApplyMaterials();
    }

    public void Unfreeze()
    {
        Init();
        Frozen = false;
        foreach (var e in edges) e.enabled = false;
        spectatorCam.enabled = false;
        ApplyMaterials();
    }

    void SetEdge(int i, Vector3 a, Vector3 b)
    {
        edges[i].SetPosition(0, a); edges[i].SetPosition(1, b); edges[i].enabled = true;
    }

    // One place decides the material of every tested renderer: frozen tint first, then LOD tint, else the original.
    public void ApplyMaterials()
    {
        foreach (var it in items)
            for (int i = 0; i < it.renderers.Length; i++)
            {
                var r = it.renderers[i];
                Material m = it.original[i];
                if (Frozen) m = it.insideFrozen ? insideMat : outsideMat;
                else if (lodDebug != null)
                {
                    var lm = lodDebug.TintFor(r);
                    if (lm != null) m = lm;
                }
                if (r.sharedMaterial != m) r.sharedMaterial = m;
            }
    }

    void OnDisable()
    {
        // put the real materials back
        foreach (var it in items)
            for (int i = 0; i < it.renderers.Length; i++)
                if (it.renderers[i] != null) it.renderers[i].sharedMaterial = it.original[i];
    }
}
