// Ex 7. Places copies of the LOD Nisse prefab in a grid around the spawn point, with a free circle in the middle
// so the player does not start inside a Nisse. Right click the component header and pick "Spawn Grid".
// The editor builder (RtcgEx7Scene) calls the same method, so the scene is built the same way you would build it by hand.
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class NisseGrid : MonoBehaviour
{
    public GameObject prefab;          // Ex7_NisseLOD prefab (has the LODGroup)
    public int countX = 19, countZ = 19;
    public float spacing = 4f;         // metres between Nisse
    public float clearRadius = 4.5f;   // no Nisse closer than this to the centre
    public int seed = 7;               // random turn of each Nisse, fixed seed so every build looks the same

    [ContextMenu("Spawn Grid")]
    public void Spawn()
    {
        for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);
        var rng = new System.Random(seed);
        for (int ix = 0; ix < countX; ix++)
            for (int iz = 0; iz < countZ; iz++)
            {
                var pos = new Vector3((ix - (countX - 1) * 0.5f) * spacing, 0f, (iz - (countZ - 1) * 0.5f) * spacing);
                float yaw = (float)rng.NextDouble() * 360f;
                if (pos.magnitude < clearRadius) continue;
                GameObject go;
#if UNITY_EDITOR
                if (!Application.isPlaying) go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, transform);
                else
#endif
                go = Instantiate(prefab, transform);
                go.name = $"Nisse_{ix:00}_{iz:00}";
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
    }
}
