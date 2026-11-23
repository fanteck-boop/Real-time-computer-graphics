using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Ex 7: frustum culling, LOD and mipmapping (Kaiser mip filter), in a scene you can walk around in.
// Run headless:
//   Unity.exe -batchmode -quit -projectPath <project> -executeMethod RtcgEx7Scene.BuildScene
//   Unity.exe -batchmode -quit -projectPath <project> -executeMethod RtcgEx7Scene.CaptureSteps
// BuildScene makes the textures (Kaiser), materials, the LOD prefab and the scene. CaptureSteps writes Captures/Ex07_Steps.
public static class RtcgEx7Scene
{
    const string ScenePath = "Assets/Scenes/Ex 7 Culling and Mips.unity";
    const string TexDir = "Assets/Textures/Ex7/";
    const string MatDir = "Assets/Materials/Ex7/";
    const string PrefabDir = "Assets/Prefabs/Ex7/";
    const string PrefabPath = PrefabDir + "Ex7_NisseLOD.prefab";
    const string NisseTex = "Assets/Nisse/Nisse_diffuse.jpeg";
    const string OutDir = "Captures/Ex07_Steps";

    // LOD thresholds (fraction of the screen height). With the PC quality level's LOD bias of 2 and a 60 degree
    // camera, a Nisse (LODGroup size 1.08 m) switches at about 6 m, 19 m, 47 m and 94 m.
    static readonly float[] LodHeights = { 0.30f, 0.10f, 0.04f, 0.02f };

    // area layout (metres)
    static readonly Vector3 CorridorStart = new Vector3(55f, 0f, -38f);
    static readonly Vector3 LanesStart = new Vector3(72f, 0f, -39f);
    const float StripZ0 = -36f, StripZ1 = 114f;   // corridor and lanes run along +Z between these

    // ---------------- textures ----------------
    static Texture2D MakeTexture(string name, int size, System.Func<int, int, Color> f, TextureImporterMipFilter filter, bool compressed, bool readable)
    {
        Directory.CreateDirectory(TexDir);
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++) px[y * size + x] = f(x, y);
        t.SetPixels(px); t.Apply();
        string path = TexDir + name + ".png";
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.filterMode = FilterMode.Trilinear;
        ti.mipmapEnabled = true;
        ti.mipmapFilter = filter;               // Box or Kaiser: how the smaller mip levels are made from level 0
        ti.maxTextureSize = 2048;
        ti.textureCompression = compressed ? TextureImporterCompression.Compressed : TextureImporterCompression.Uncompressed;
        ti.isReadable = readable;               // only so the builder can print mip statistics
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static float Checker(int x, int y, int cell) => ((x / cell) + (y / cell)) % 2 == 0 ? 1f : 0f;

    // Ground: light grey-green with thin darker lines, one texture covers 4 x 4 m, so the lines are 1 m apart.
    static Color GroundPx(int x, int y)
    {
        bool line = x % 128 < 2 || y % 128 < 2;
        float n = Mathf.PerlinNoise(x * 0.05f, y * 0.05f) * 0.06f;
        var c = new Color(0.50f + n, 0.53f + n, 0.47f + n);
        return line ? c * 0.75f : c;
    }

    // Corridor floor: hard black and white checker, 16 px cells. High contrast, so aliasing shows clearly without mips.
    static Color CorridorPx(int x, int y)
    {
        float v = Checker(x, y, 16) > 0.5f ? 0.92f : 0.08f;
        return new Color(v, v, v);
    }

    // Lanes: a black and white checker turned by about 23 degrees (cells about 16 px). Because it is turned, its edges
    // fall at every possible position inside the 2 x 2 texel blocks of the mip chain, which is where Box and Kaiser differ.
    // The wave numbers (60, 25) are whole numbers per 1024 px, so the texture still tiles without a seam.
    static Color LanePx(int x, int y)
    {
        const float a = 60f, b = 25f, n = 1024f;
        float u = Mathf.Cos(2f * Mathf.PI * (a * x + b * y) / n);
        float w = Mathf.Cos(2f * Mathf.PI * (-b * x + a * y) / n);
        float v = u * w > 0f ? 0.9f : 0.1f;
        return new Color(v, v, v);
    }

    // ---------------- materials ----------------
    static Material Mat(string name, string shader, Color color, Texture tex = null, Vector2? tiling = null)
    {
        Directory.CreateDirectory(MatDir);
        string path = MatDir + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find(shader)) { name = name };
        m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        if (tex != null)
        {
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", tiling ?? Vector2.one);
        }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    const string Lit = "Universal Render Pipeline/Lit", Unlit = "Universal Render Pipeline/Unlit";

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material m, bool collider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.transform.localScale = scale;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = m;
        go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    static Mesh LoadMesh(string path)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is Mesh m) return m;
        return null;
    }

    // ---------------- the LOD prefab ----------------
    static GameObject BuildPrefab(Material nisseMat, Material plinthMat)
    {
        Directory.CreateDirectory(PrefabDir);
        string[] paths = { "Assets/Nisse/Nisse.obj", "Assets/Nisse/LOD/Nisse_LOD1.obj", "Assets/Nisse/LOD/Nisse_LOD2.obj", "Assets/Nisse/LOD/Nisse_LOD3.obj" };
        var root = new GameObject("Ex7_NisseLOD");
        const float plinthTop = 0.06f;
        var plinth = Prim(PrimitiveType.Cylinder, "Plinth", root.transform, new Vector3(0, plinthTop / 2, 0), new Vector3(1.1f, plinthTop / 2, 1.1f), plinthMat, false);
        var lods = new LOD[paths.Length];
        Bounds b0 = default;
        for (int k = 0; k < paths.Length; k++)
        {
            var mesh = LoadMesh(paths[k]);
            // the LOD meshes were made from the same scan and share its coordinates, so all four get the transform that
            // makes LOD0 1 m tall with its base centre on the plinth
            if (k == 0) b0 = mesh.bounds;
            Bounds b = b0;
            float s = 1f / b.size.y;
            var go = new GameObject("LOD" + k);
            go.transform.SetParent(root.transform, false);
            go.transform.localScale = Vector3.one * s;
            go.transform.localPosition = new Vector3(-b.center.x * s, -b.min.y * s + plinthTop, -b.center.z * s);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = nisseMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            lods[k] = new LOD(LodHeights[k], new Renderer[] { r });
            Debug.Log($"EX7 LOD{k} {paths[k]}: verts={mesh.vertexCount} tris={mesh.triangles.Length / 3} bounds centre={mesh.bounds.center} size={mesh.bounds.size} scale={s}");
        }
        var group = root.AddComponent<LODGroup>();
        group.fadeMode = LODFadeMode.None;
        group.SetLODs(lods);
        group.RecalculateBounds();
        var col = root.AddComponent<CapsuleCollider>();   // so the player bumps into the Nisse instead of walking through
        col.center = new Vector3(0, 0.5f, 0); col.height = 1f; col.radius = 0.35f;
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Debug.Log($"EX7 prefab LODGroup size={group.size} ref={group.localReferencePoint}");
        Object.DestroyImmediate(root);
        return prefab;
    }

    // ---------------- scene ----------------
    public static void BuildScene()
    {
        AssetDatabase.Refresh();

        // 1. Kaiser mip filter on the Nisse texture: only this one field changes, max size and compression stay as they are
        var nti = (TextureImporter)AssetImporter.GetAtPath(NisseTex);
        Debug.Log($"EX7 Nisse texture before: mipmapFilter={nti.mipmapFilter} mipmapEnabled={nti.mipmapEnabled} maxSize={nti.maxTextureSize} compression={nti.textureCompression}");
        if (nti.mipmapFilter != TextureImporterMipFilter.KaiserFilter)
        {
            nti.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            nti.SaveAndReimport();
        }
        Debug.Log($"EX7 Nisse texture after: mipmapFilter={nti.mipmapFilter} mipmapEnabled={nti.mipmapEnabled} maxSize={nti.maxTextureSize} compression={nti.textureCompression}");

        // 2. generated textures: all Kaiser, except the Box copy of the lane texture
        var K = TextureImporterMipFilter.KaiserFilter;
        var texGround = MakeTexture("Ex7_Ground", 512, GroundPx, K, true, false);
        var texCorridor = MakeTexture("Ex7_Corridor", 1024, CorridorPx, K, false, false);
        var texLaneBox = MakeTexture("Ex7_Lane_Box", 1024, LanePx, TextureImporterMipFilter.BoxFilter, false, true);
        var texLaneKaiser = MakeTexture("Ex7_Lane_Kaiser", 1024, LanePx, K, false, true);
        LogMipStats(texLaneBox, texLaneKaiser);

        // 3. materials
        var mGround = Mat("Ex7_Ground", Lit, Color.white, texGround, new Vector2(140f / 4f, 170f / 4f));
        var mCorridor = Mat("Ex7_Corridor", Unlit, Color.white, texCorridor, new Vector2(6f / 2f, (StripZ1 - StripZ0) / 2f));
        var mLaneBox = Mat("Ex7_Lane_Box", Unlit, Color.white, texLaneBox, new Vector2(0.5f, (StripZ1 - StripZ0) / 4.8f));
        var mLaneKaiser = Mat("Ex7_Lane_Kaiser", Unlit, Color.white, texLaneKaiser, new Vector2(0.5f, (StripZ1 - StripZ0) / 4.8f));
        var mWall = Mat("Ex7_Wall", Lit, new Color(0.72f, 0.70f, 0.66f));
        var mBoundary = Mat("Ex7_Boundary", Lit, new Color(0.45f, 0.40f, 0.36f));
        var mPlinth = Mat("Ex7_Plinth", Lit, new Color(0.82f, 0.82f, 0.80f));
        var mDivider = Mat("Ex7_Divider", Unlit, new Color(0.2f, 0.2f, 0.22f));
        var mBody = Mat("Ex7_PlayerBody", Lit, new Color(0.2f, 0.45f, 1f));
        Color[] pillarCols = { new Color(0.85f, 0.75f, 0.55f), new Color(0.55f, 0.7f, 0.85f), new Color(0.75f, 0.6f, 0.8f), new Color(0.6f, 0.8f, 0.6f) };
        var mPillars = new Material[4];
        for (int i = 0; i < 4; i++) mPillars[i] = Mat("Ex7_Pillar" + i, Lit, pillarCols[i]);
        var nisseMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Nisse/Nisse.mat");

        // 4. LOD prefab
        var prefab = BuildPrefab(nisseMat, mPlinth);

        // 5. scene
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var world = new GameObject("World").transform;
        var ground = Prim(PrimitiveType.Plane, "Ground", world, new Vector3(20f, 0f, 37.5f), new Vector3(14f, 1f, 17f), mGround, true);
        // boundary walls, x from -50 to 90, z from -47.5 to 122.5
        Prim(PrimitiveType.Cube, "Boundary West", world, new Vector3(-50f, 0.75f, 37.5f), new Vector3(0.5f, 1.5f, 170f), mBoundary, true);
        Prim(PrimitiveType.Cube, "Boundary East", world, new Vector3(90f, 0.75f, 37.5f), new Vector3(0.5f, 1.5f, 170f), mBoundary, true);
        Prim(PrimitiveType.Cube, "Boundary South", world, new Vector3(20f, 0.75f, -47.5f), new Vector3(140f, 1.5f, 0.5f), mBoundary, true);
        Prim(PrimitiveType.Cube, "Boundary North", world, new Vector3(20f, 0.75f, 122.5f), new Vector3(140f, 1.5f, 0.5f), mBoundary, true);

        // mip corridor: 6 m wide, 150 m long, walls on both sides
        float len = StripZ1 - StripZ0, midZ = (StripZ0 + StripZ1) / 2f;
        var mipArea = new GameObject("Mip Area").transform; mipArea.SetParent(world, false);
        var corridor = Prim(PrimitiveType.Plane, "Corridor Floor (Kaiser checker)", mipArea, new Vector3(55f, 0.005f, midZ), new Vector3(0.6f, 1f, len / 10f), mCorridor, false);
        Prim(PrimitiveType.Cube, "Corridor Wall West", mipArea, new Vector3(51.8f, 1.25f, midZ), new Vector3(0.4f, 2.5f, len), mWall, true);
        Prim(PrimitiveType.Cube, "Corridor Wall East", mipArea, new Vector3(58.2f, 1.25f, midZ), new Vector3(0.4f, 2.5f, len), mWall, true);
        // two lanes side by side: same picture, Box mip filter on the left, Kaiser on the right
        var laneBox = Prim(PrimitiveType.Plane, "Lane Box", mipArea, new Vector3(70.7f, 0.005f, midZ), new Vector3(0.24f, 1f, len / 10f), mLaneBox, false);
        var laneKaiser = Prim(PrimitiveType.Plane, "Lane Kaiser", mipArea, new Vector3(73.3f, 0.005f, midZ), new Vector3(0.24f, 1f, len / 10f), mLaneKaiser, false);
        Prim(PrimitiveType.Cube, "Lane Divider", mipArea, new Vector3(72f, 0.006f, midZ), new Vector3(0.2f, 0.002f, len), mDivider, false);
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Label(mipArea, "BOX", new Vector3(70.7f, 0.02f, -37.2f), font);
        Label(mipArea, "KAISER", new Vector3(73.3f, 0.02f, -37.2f), font);

        // Nisse field and pillars: these are the objects the frustum test and the LOD counts look at
        var field = new GameObject("Nisse Field").transform; field.SetParent(world, false);
        var grid = field.gameObject.AddComponent<NisseGrid>();
        grid.prefab = prefab;
        grid.Spawn();
        var pillars = new GameObject("Pillars").transform; pillars.SetParent(world, false);
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.PI * 2f / 16f;
            var p = new Vector3(Mathf.Sin(a) * 42f, 2f, Mathf.Cos(a) * 42f);
            Prim(PrimitiveType.Cube, "Pillar " + i, pillars, p, new Vector3(1.2f, 4f, 1.2f), mPillars[i % 4], true);
        }

        // light: one sun, no shadows (keeps the frame cheap and keeps Renderer.isVisible about the camera only), flat ambient
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.2f; sun.color = new Color(1f, 0.96f, 0.9f);
        sun.shadows = LightShadows.None;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.52f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom; RenderSettings.reflectionIntensity = 0f;
        RenderSettings.fog = false;

        // player: CharacterController on the body, camera on a head child
        var player = new GameObject("Player");
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.9f, 0); cc.stepOffset = 0.3f; cc.slopeLimit = 45f;
        Prim(PrimitiveType.Capsule, "Body (seen from the minimap)", player.transform, new Vector3(0, 0.9f, 0), new Vector3(0.7f, 0.9f, 0.7f), mBody, false);
        var head = new GameObject("Head").transform; head.SetParent(player.transform, false); head.localPosition = new Vector3(0, 1.65f, 0);
        var camGO = new GameObject("Main Camera"); camGO.tag = "MainCamera"; camGO.transform.SetParent(head, false);
        var cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 60f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 120f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.74f, 0.88f);
        cam.allowHDR = false; cam.allowMSAA = false;
        camGO.AddComponent<AudioListener>();
        var acd = camGO.AddComponent<UniversalAdditionalCameraData>(); acd.renderPostProcessing = false; acd.antialiasing = AntialiasingMode.None;
        var walker = player.AddComponent<FirstPersonWalker>();
        walker.head = head; walker.cam = cam;
        walker.teleportPoints = new[] { Vector3.zero, CorridorStart, LanesStart };
        walker.teleportYaw = new[] { 0f, 0f, 0f };

        // spectator camera for the minimap (switched on by FrustumDebug when the frustum is frozen)
        var specGO = new GameObject("Spectator Camera (minimap)");
        var spec = specGO.AddComponent<Camera>();
        spec.orthographic = true; spec.orthographicSize = 40f; spec.nearClipPlane = 1f; spec.farClipPlane = 400f;
        spec.clearFlags = CameraClearFlags.SolidColor; spec.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
        spec.allowHDR = false; spec.allowMSAA = false; spec.depth = -2; spec.enabled = false;
        specGO.transform.position = new Vector3(0, 150, 0); specGO.transform.rotation = Quaternion.Euler(90, 0, 0);
        var sacd = specGO.AddComponent<UniversalAdditionalCameraData>(); sacd.renderPostProcessing = false;

        // debug components
        var dbg = new GameObject("Ex7 Debug");
        var fr = dbg.AddComponent<FrustumDebug>();
        var lod = dbg.AddComponent<LodDebug>();
        var mip = dbg.AddComponent<MipDebug>();
        var hud = dbg.AddComponent<Ex7Hud>();
        fr.playerCam = cam; fr.spectatorCam = spec; fr.objectRoots = new[] { field, pillars }; fr.lodDebug = lod;
        lod.cam = cam; lod.groupsRoot = field; lod.frustum = fr;
        mip.surfaces = new Renderer[] { corridor.GetComponent<Renderer>(), laneBox.GetComponent<Renderer>(), laneKaiser.GetComponent<Renderer>() };
        mip.textures = new[] { texCorridor, texLaneBox, texLaneKaiser };
        hud.frustum = fr; hud.lod = lod; hud.mip = mip;

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"EX7 scene built: {field.childCount} Nisse, {pillars.childCount} pillars, lodBias={QualitySettings.lodBias} aniso={QualitySettings.anisotropicFiltering} quality={QualitySettings.names[QualitySettings.GetQualityLevel()]}");
    }

    static void Label(Transform parent, string text, Vector3 pos, Font font)
    {
        var go = new GameObject("Label " + text);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // lying on the floor, readable from the south
        var tm = go.AddComponent<TextMesh>();
        tm.text = text; tm.font = font; tm.fontSize = 64; tm.characterSize = 0.06f;
        tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = new Color(0.1f, 0.1f, 0.1f);
        go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
    }

    // Prints, per mip level, how much contrast is left (standard deviation of the grey value) in the Box and Kaiser copies.
    static void LogMipStats(Texture2D box, Texture2D kaiser)
    {
        var sb = new StringBuilder("EX7 MIPSTATS level: std(box) std(kaiser) mean|box-kaiser|\n");
        for (int level = 0; level < box.mipmapCount; level++)
        {
            var a = box.GetPixels(level); var b = kaiser.GetPixels(level);
            double ma = 0, mb = 0, d = 0;
            for (int i = 0; i < a.Length; i++) { ma += a[i].grayscale; mb += b[i].grayscale; d += Mathf.Abs(a[i].grayscale - b[i].grayscale); }
            ma /= a.Length; mb /= b.Length;
            double va = 0, vb = 0;
            for (int i = 0; i < a.Length; i++) { va += (a[i].grayscale - ma) * (a[i].grayscale - ma); vb += (b[i].grayscale - mb) * (b[i].grayscale - mb); }
            sb.AppendLine($"  {level} ({box.width >> level}px): {System.Math.Sqrt(va / a.Length):F3} {System.Math.Sqrt(vb / b.Length):F3} {d / a.Length:F3}");
        }
        Debug.Log(sb.ToString());
    }

    // ---------------- captures ----------------
    static Texture2D Grab(Camera cam, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var oldTarget = cam.targetTexture;
        cam.targetTexture = rt; cam.aspect = (float)w / h; cam.Render();
        RenderTexture.active = rt;
        var t = new Texture2D(w, h, TextureFormat.RGB24, false);
        t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
        RenderTexture.active = null; cam.targetTexture = oldTarget;
        Object.DestroyImmediate(rt);
        return t;
    }

    static void Save(Texture2D t, string name)
    {
        File.WriteAllBytes(Path.Combine(OutDir, name), t.EncodeToPNG());
        Debug.Log("WROTE " + name);
    }

    static void Shot(Camera cam, string name)
    {
        var t = Grab(cam, 1280, 720); Save(t, name); Object.DestroyImmediate(t);
    }

    // Copies src into dst at (x, y) (bottom left origin), with a white frame of the given width.
    static void Paste(Texture2D dst, Texture2D src, int x, int y, int frame)
    {
        for (int j = -frame; j < src.height + frame; j++)
            for (int i = -frame; i < src.width + frame; i++)
            {
                bool inside = i >= 0 && j >= 0 && i < src.width && j < src.height;
                dst.SetPixel(x + i, y + j, inside ? src.GetPixel(i, j) : Color.white);
            }
        dst.Apply();
    }

    // OnGUI does not run in a headless editor, so the capture puts the HUD's own text (Ex7Hud.BuildText) on a
    // TextMesh in front of the camera. In Play mode the same text is drawn by OnGUI.
    static GameObject HudOverlay(Camera cam, string text)
    {
        var root = new GameObject("HUD capture overlay") { hideFlags = HideFlags.DontSave };
        root.transform.SetParent(cam.transform, false);
        float d = 1f;
        float viewH = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), viewW = viewH * 16f / 9f;
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var tgo = new GameObject("text") { hideFlags = HideFlags.DontSave };
        tgo.transform.SetParent(root.transform, false);
        tgo.transform.localPosition = new Vector3(-viewW / 2f + viewW * 0.018f, viewH / 2f - viewH * 0.025f, d - 0.01f);
        var tm = tgo.AddComponent<TextMesh>();
        tm.font = font; tm.fontSize = 40; tm.characterSize = viewH / 720f * 0.42f; tm.lineSpacing = 1.05f;
        tm.anchor = TextAnchor.UpperLeft; tm.richText = true; tm.color = Color.white; tm.text = text;
        tgo.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        // scale the characters so one line is about 21 screen pixels high (OnGUI uses a 15 pt font).
        // A TextMesh only builds its mesh when it is drawn, so render once before reading its bounds.
        var mr = tgo.GetComponent<MeshRenderer>();
        int lines = text.Split('\n').Length;
        Object.DestroyImmediate(Grab(cam, 320, 180));
        // localBounds is in the text object's own axes, which are the camera's axes (renderer.bounds is a world box)
        float hNow = mr.localBounds.size.y;
        tm.characterSize *= lines * 21f / 720f * viewH / hNow;
        Object.DestroyImmediate(Grab(cam, 320, 180));
        Bounds b = mr.localBounds;
        // dark box behind the text, sized from the text's bounds
        Vector3 lc = tgo.transform.localPosition + b.center;
        Vector3 ls = b.size;
        var box = GameObject.CreatePrimitive(PrimitiveType.Quad);
        box.hideFlags = HideFlags.DontSave; Object.DestroyImmediate(box.GetComponent<Collider>());
        box.transform.SetParent(root.transform, false);
        float pad = viewH * 0.012f;
        box.transform.localPosition = new Vector3(lc.x, lc.y, d);
        box.transform.localScale = new Vector3(Mathf.Abs(ls.x) + 2 * pad, Mathf.Abs(ls.y) + 2 * pad, 1f);
        var bm = new Material(Shader.Find(Unlit)) { hideFlags = HideFlags.DontSave };
        bm.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.1f));
        box.GetComponent<MeshRenderer>().sharedMaterial = bm;
        Debug.Log($"EX7 HUD overlay text bounds (camera space) centre={lc} size={ls}");
        return root;
    }

    public static void CaptureSteps()
    {
        EditorSceneManager.OpenScene(ScenePath);
        Directory.CreateDirectory(OutDir);
        var walker = Object.FindAnyObjectByType<FirstPersonWalker>();
        var fr = Object.FindAnyObjectByType<FrustumDebug>();
        var lod = Object.FindAnyObjectByType<LodDebug>();
        var mip = Object.FindAnyObjectByType<MipDebug>();
        var hud = Object.FindAnyObjectByType<Ex7Hud>();
        var cam = walker.cam;
        cam.aspect = 16f / 9f;
        fr.Init(); lod.Init(); mip.Init();
        Debug.Log($"EX7 capture: lodBias={QualitySettings.lodBias} aniso={QualitySettings.anisotropicFiltering} quality={QualitySettings.names[QualitySettings.GetQualityLevel()]} GRD={((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).gpuResidentDrawerMode}");

        void Report(string step)
        {
            fr.Recount(); lod.Recount();
            var c = lod.Counts; var v = lod.CountsInView;
            Debug.Log($"EX7 {step}: objects={fr.ObjectCount} insideLive={fr.InsideLive} engineVisible={fr.EngineVisible} frozen={fr.Frozen} insideFrozen={fr.InsideFrozen} " +
                      $"tris={fr.DrawnTriangles} lodAll=[{c[0]},{c[1]},{c[2]},{c[3]},{c[4]}] lodView=[{v[0]},{v[1]},{v[2]},{v[3]},{v[4]}] forced={lod.ForcedLod}");
        }

        // In edit mode no frames pass between these renders, so Renderer.isVisible is never reset: it collects every
        // render since the start. The HUD shot therefore comes first, from the pose of the warm-up renders.
        var hudPose = new Vector3(-6f, 0f, -14f);
        walker.SetPose(hudPose, 35f, 4f);
        // first renders of a session come out untextured
        for (int i = 0; i < 3; i++) { var w = Grab(cam, 640, 360); Object.DestroyImmediate(w); }
        Report("step4 player camera only");

        // step 4: HUD with all counters, frozen frustum and the minimap in the corner
        var spec = fr.spectatorCam;
        fr.Freeze();
        var mini = Grab(spec, 360, 360);
        var main = Grab(cam, 1280, 720); Object.DestroyImmediate(main);
        Report("step4 player + minimap");
        var overlay = HudOverlay(cam, hud.BuildText());
        var withHud = Grab(cam, 1280, 720);
        Object.DestroyImmediate(overlay);
        Paste(withHud, mini, 1280 - 360 - 12, 720 - 360 - 12, 3);
        Save(withHud, "step4_hud_overview.png");
        Debug.Log("EX7 HUD TEXT\n" + hud.BuildText());

        // step 1: the same frozen frustum seen from above (zoomed out so the far plane fits), and from the player
        float ahead = fr.minimapAhead, half = fr.minimapHalfSize;
        fr.minimapAhead = 56f; fr.minimapHalfSize = 68f;
        fr.Freeze();
        Shot(spec, "step1a_frustum_minimap.png");
        Shot(cam, "step1b_frustum_from_player.png"); Report("step1 frozen");
        fr.minimapAhead = ahead; fr.minimapHalfSize = half;
        fr.Unfreeze();

        // step 0: normal first person view from the spawn point
        walker.SetPose(Vector3.zero, 0f, 6f);
        Shot(cam, "step0_walk_view.png"); Report("step0");

        // step 2: LOD tint (rings around the player), then forced LOD0 and LOD3 close up
        walker.SetPose(Vector3.zero, 0f, 10f);
        lod.SetTint(true);
        Shot(cam, "step2a_lod_tint.png"); Report("step2a");
        lod.SetTint(false);
        walker.SetPose(new Vector3(2.75f, 0f, 2.75f), 45f, 30f);
        lod.SetForcedLod(0);
        var lod0 = Grab(cam, 1280, 720); Save(lod0, "step2b_lod_forced0.png"); Report("step2b forced 0");
        lod.SetForcedLod(3);
        var lod3 = Grab(cam, 1280, 720); Save(lod3, "step2c_lod_forced3.png"); Report("step2c forced 3");
        lod.SetForcedLod(-1);
        // extra: the nearest Nisse from both pictures side by side, 2x nearest neighbour
        var pair = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        for (int y = 0; y < 720; y++)
            for (int x = 0; x < 1280; x++)
            {
                var src = x < 640 ? lod0 : lod3;
                int sx = 480 + (x % 640) / 2, sy = 160 + y / 2;   // a 320 x 360 window around the near Nisse
                pair.SetPixel(x, y, x % 640 < 2 ? Color.white : src.GetPixel(sx, sy));
            }
        pair.Apply();
        Save(pair, "extra_step2_lod0_left_lod3_right.png");

        // step 3: mip corridor, plain and with mip level colours
        walker.SetPose(CorridorStart, 0f, 8f);
        Shot(cam, "step3a_mip_floor_plain.png");
        mip.SetMipColours(true);
        Shot(cam, "step3b_mip_floor_colours.png");
        mip.SetMipColours(false);

        // step 3c/3d: Box and Kaiser lanes side by side, far view, and a 4x crop of the middle distance
        walker.SetPose(LanesStart + new Vector3(0, 0, -1f), 0f, 7f);
        var lanes = Grab(cam, 1280, 720); Save(lanes, "step3c_box_vs_kaiser.png");
        Crop4x(lanes, "step3d_box_vs_kaiser_close.png");
        mip.SetMode(1);
        var lanesNoAniso = Grab(cam, 1280, 720); Save(lanesNoAniso, "extra_step3c_box_vs_kaiser_aniso_off.png");
        Crop4x(lanesNoAniso, "extra_step3d_box_vs_kaiser_close_aniso_off.png");
        mip.SetMipColours(true);
        Shot(cam, "extra_step3c_lanes_mip_colours_aniso_off.png");
        mip.SetMode(0);
        Shot(cam, "extra_step3c_lanes_mip_colours.png");
        mip.SetMipColours(false);

        // put everything back (nothing in the scene is saved)
        fr.Unfreeze(); mip.SetMode(0); lod.SetForcedLod(-1); lod.SetTint(false);
        Debug.Log("EX7 captures done");
    }

    // Crops a 320 x 180 window from the middle distance of the lanes and scales it up 4x with nearest neighbour,
    // so every screen pixel becomes a 4 x 4 block and nothing is smoothed.
    static void Crop4x(Texture2D src, string name)
    {
        int cw = 320, ch = 180, x0 = 640 - cw / 2, y0 = 200;   // y counts from the bottom of the picture
        var t = new Texture2D(cw * 4, ch * 4, TextureFormat.RGB24, false);
        for (int y = 0; y < ch * 4; y++)
            for (int x = 0; x < cw * 4; x++) t.SetPixel(x, y, src.GetPixel(x0 + x / 4, y0 + y / 4));
        t.Apply();
        Save(t, name);
        Object.DestroyImmediate(t);
    }
}
