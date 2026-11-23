using UnityEditor;
using UnityEngine;

// Re-imports the updated Nisse scan (Assets/Nisse/Nisse.obj, 178,672 triangles, base plate removed)
// and rebuilds the normalised prefab. Keeps the existing Nisse.mat and every GUID, so scenes that use them follow.
public static class RtcgNisseV2
{
    const string Dir = "Assets/Nisse/";

    public static void Rebuild()
    {
        AssetDatabase.ImportAsset(Dir + "Nisse.obj", ImportAssetOptions.ForceUpdate);
        var mi = (ModelImporter)AssetImporter.GetAtPath(Dir + "Nisse.obj");
        mi.indexFormat = ModelImporterIndexFormat.UInt32;
        mi.meshCompression = ModelImporterMeshCompression.Off;
        mi.importNormals = ModelImporterNormals.Calculate;
        mi.SaveAndReimport();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "Nisse.obj");
        Mesh mesh = model.GetComponentInChildren<MeshFilter>().sharedMesh;
        Bounds b = mesh.bounds;
        Debug.Log($"NISSE v2 mesh verts={mesh.vertexCount} tris={mesh.triangles.Length / 3} bounds={b}");

        var mat = AssetDatabase.LoadAssetAtPath<Material>(Dir + "Nisse.mat");
        float s = 1f / b.size.y;
        var root = new GameObject("Nisse");
        var child = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        child.name = "Mesh";
        child.transform.localScale = Vector3.one * s;
        child.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z) * s;
        foreach (var r in child.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = mat;
        PrefabUtility.SaveAsPrefabAsset(root, Dir + "Nisse.prefab");
        Object.DestroyImmediate(root);
        Debug.Log("NISSE v2 prefab saved, scale factor " + s);
        AssetDatabase.SaveAssets();
    }

    // Full 8192 diffuse (source resolution), high quality compression, anisotropic filtering 8.
    public static void TextureFull()
    {
        string p = Dir + "Nisse_diffuse.jpeg";
        var ti = (TextureImporter)AssetImporter.GetAtPath(p);
        ti.maxTextureSize = 8192;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.anisoLevel = 8;
        ti.mipmapEnabled = true;
        foreach (var plat in new[] { "Standalone", "Windows" })
        {
            var ps = ti.GetPlatformTextureSettings(plat);
            if (ps.overridden) { ps.maxTextureSize = 8192; ps.textureCompression = TextureImporterCompression.CompressedHQ; ti.SetPlatformTextureSettings(ps); }
        }
        ti.SaveAndReimport();
        Debug.Log($"NISSE texture max={ti.maxTextureSize} aniso={ti.anisoLevel}");
    }
}
