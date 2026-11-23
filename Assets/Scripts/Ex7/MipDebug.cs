// Ex 7. Mipmaps made visible.
// Key M swaps the texture on the mip test floors for a "mip level colour" texture: a texture of the same size whose
// mip levels are each filled with one flat colour. The GPU picks mip levels for it exactly as for the real texture,
// so the colour you see is the mip level the real texture is read from at that spot.
// Key B cycles the sampling mode of the test textures (trilinear, no anisotropic, bilinear, point, mip bias +1 / -1).
using UnityEngine;
using UnityEngine.InputSystem;

public class MipDebug : MonoBehaviour
{
    public Renderer[] surfaces;            // corridor floor and the two lanes
    public Texture2D[] textures;           // the textures that B changes (the ones on the surfaces)

    // one colour per mip level, level 0 first (a 1024 texture has 11 levels: 1024, 512, ... 1)
    public static readonly Color[] LevelColors =
    {
        new Color(1f, 0f, 0f),       // 0 red
        new Color(1f, 0.5f, 0f),     // 1 orange
        new Color(1f, 1f, 0f),       // 2 yellow
        new Color(0f, 0.85f, 0f),    // 3 green
        new Color(0f, 0.9f, 0.9f),   // 4 cyan
        new Color(0.1f, 0.3f, 1f),   // 5 blue
        new Color(0.6f, 0.2f, 1f),   // 6 purple
        new Color(1f, 0.2f, 0.8f),   // 7 pink
        new Color(1f, 1f, 1f),       // 8 white
        new Color(0.5f, 0.5f, 0.5f), // 9 grey
        new Color(0.1f, 0.1f, 0.1f), // 10 black
        new Color(0.1f, 0.1f, 0.1f), // 11 (only for textures larger than 1024)
    };

    public static readonly string[] ModeNames =
    {
        "Trilinear + anisotropic (project default)",
        "Trilinear, anisotropic off",
        "Bilinear (hard step between mips), anisotropic off",
        "Point (no filtering), anisotropic off",
        "Trilinear + aniso, mip bias +1 (blurrier)",
        "Trilinear + aniso, mip bias -1 (sharper, more shimmer)",
    };

    public bool ShowMipColours { get; private set; }
    public int Mode { get; private set; }

    Material[] originals, colourMats;
    FilterMode[] origFilter; float[] origBias;
    AnisotropicFiltering origAniso;
    bool ready;

    void Start() { Init(); }

    public void Init()
    {
        if (ready) return;
        ready = true;
        origAniso = QualitySettings.anisotropicFiltering;
        origFilter = new FilterMode[textures.Length]; origBias = new float[textures.Length];
        for (int i = 0; i < textures.Length; i++) { origFilter[i] = textures[i].filterMode; origBias[i] = textures[i].mipMapBias; }

        originals = new Material[surfaces.Length];
        colourMats = new Material[surfaces.Length];
        for (int i = 0; i < surfaces.Length; i++)
        {
            originals[i] = surfaces[i].sharedMaterial;
            var tex = originals[i].GetTexture("_BaseMap");
            colourMats[i] = new Material(originals[i]) { name = originals[i].name + " (mip colours)", hideFlags = HideFlags.DontSave };
            colourMats[i].SetTexture("_BaseMap", MakeMipColourTexture(tex.width, tex.height, tex.anisoLevel));
        }
    }

    // A texture with a full mip chain where every level is one flat colour. Same size as the real texture,
    // so the same level gets picked. SetPixels(colors, level) fills one level; Apply(false) uploads without
    // recalculating the mips (true would overwrite our colours with averages of level 0).
    public static Texture2D MakeMipColourTexture(int width, int height, int aniso)
    {
        var t = new Texture2D(width, height, TextureFormat.RGBA32, true)
        {
            name = "Mip level colours", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear,
            anisoLevel = aniso, hideFlags = HideFlags.DontSave
        };
        for (int level = 0; level < t.mipmapCount; level++)
        {
            int w = Mathf.Max(1, width >> level), h = Mathf.Max(1, height >> level);
            var c = new Color[w * h];
            for (int i = 0; i < c.Length; i++) c[i] = LevelColors[Mathf.Min(level, LevelColors.Length - 1)];
            t.SetPixels(c, level);
        }
        t.Apply(false);
        return t;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.mKey.wasPressedThisFrame) SetMipColours(!ShowMipColours);
        if (kb.bKey.wasPressedThisFrame) SetMode((Mode + 1) % ModeNames.Length);
    }

    public void SetMipColours(bool on)
    {
        Init();
        ShowMipColours = on;
        for (int i = 0; i < surfaces.Length; i++) surfaces[i].sharedMaterial = on ? colourMats[i] : originals[i];
    }

    public void SetMode(int mode)
    {
        Init();
        Mode = mode;
        bool anisoOff = mode == 1 || mode == 2 || mode == 3;
        QualitySettings.anisotropicFiltering = anisoOff ? AnisotropicFiltering.Disable : origAniso;
        var all = new System.Collections.Generic.List<Texture>(textures);
        foreach (var m in colourMats) all.Add(m.GetTexture("_BaseMap"));
        foreach (var t in all)
        {
            t.filterMode = mode == 2 ? FilterMode.Bilinear : mode == 3 ? FilterMode.Point : FilterMode.Trilinear;
            t.mipMapBias = mode == 4 ? 1f : mode == 5 ? -1f : 0f;
        }
    }

    void OnDisable()
    {
        if (!ready) return;
        // the textures are project assets: put their settings back so nothing leaks out of Play mode
        QualitySettings.anisotropicFiltering = origAniso;
        for (int i = 0; i < textures.Length; i++)
            if (textures[i] != null) { textures[i].filterMode = origFilter[i]; textures[i].mipMapBias = origBias[i]; }
        for (int i = 0; i < surfaces.Length; i++) if (surfaces[i] != null) surfaces[i].sharedMaterial = originals[i];
    }
}
