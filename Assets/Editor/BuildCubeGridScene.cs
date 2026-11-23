using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class BuildCubeGridScene
{
    // Tunables as constants so the doc and the scene agree.
    public const float LightIntensity = 3000f;
    public static readonly Color Ambient = new Color(0.0f, 0.14f, 0.03f); // sRGB, dim green

    [MenuItem("RTCG/Build Cube Grid Scene")]
    public static void Build()
    {
        // ponytail: scene is built by this static method instead of hand-clicked, so CI/CLI can
        // reproduce it. Rebuild in the editor (RTCG > Build Cube Grid Scene) if you tweak inspector values.
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Camera
        GameObject camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        Camera cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGO.AddComponent<AudioListener>();
        camGO.transform.position = new Vector3(0, 0, -30);
        camGO.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;

        // Point light
        GameObject lightGO = new GameObject("Point Light");
        Light light = lightGO.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 50f;
        light.intensity = LightIntensity;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 1f;
        // ponytail: shadow resolution tier High (1024 per cube-map face) and tighter biases; default tier
        // Medium (512) over a 50 range blurs the shadows of 0.5-unit cubes.
        light.shadowBias = 0.02f; light.shadowNormalBias = 0.1f;
        var lightData = lightGO.AddComponent<UniversalAdditionalLightData>();
        var lso = new SerializedObject(lightData);
        lso.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = 2;
        lso.FindProperty("m_UsePipelineSettings").boolValue = false;
        lso.ApplyModifiedPropertiesWithoutUndo();

        // Manager, holding every script from the exercise
        GameObject manager = new GameObject("Manager");

        Transform prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Cube.prefab").transform;
        if (prefab == null)
            Debug.LogError("BuildCubeGridScene: Cube prefab not found at Assets/Prefabs/Cube.prefab");

        CreateGrid createGrid = manager.AddComponent<CreateGrid>();
        createGrid.prefab = prefab;
        createGrid.gridResolution = 10;
        createGrid.colourByPosition = false; // white albedo: cube colour comes from the light only

        var tr = manager.AddComponent<Transformations>();
        tr.cosPulse = true; tr.moveAmplitude = 0f; tr.scaleFrequency = 1.5f;
        tr.pulseScale = 0.45f; tr.pulseAmount = 1f; tr.pulsePhasePerCube = 0.004f; // size 0..1, grid collapses to zero and regrows, slow wave through it
        tr.waves = true; // custom tweak 4 and 5: distortion waves from random cubes + click shockwave
        // ponytail: default orbit distance (10) sits inside the 9-unit grid, filling the frame
        // with one close-up cube. 30 matches the reference gif's smaller, more distant look.
        CameraControl camControl = manager.AddComponent<CameraControl>();
        camControl.distance = 12f;

        LightManipulation lightManip = manager.AddComponent<LightManipulation>();
        lightManip.pointLight = lightGO;
        lightManip.figureEight = true; lightManip.amplitude = 9f; lightManip.frequency = 0.8f;

        manager.AddComponent<RayCastManipulator>();

        // ponytail: a metallic/smooth material only looks shiny when there is something for it to
        // reflect. Deleting the skybox (as the tutorial literally says) kills that, so the cubes
        // read as flat grey plastic instead of metal. Keep the default skybox as the reflection
        // source, and rely on the camera's SolidColor clear flag above to still show pure black on
        // screen — the skybox stays invisible to the viewer but URP still samples it for specular.
        // Root cause of the earlier "ambient has no effect": the default skybox was both the ambient (SH) and
        // reflection source, and the metallic 0.7 material mirrored it, so unlit faces stayed grey.
        // Flat dark-green ambient + zero reflection intensity gives hard, dark green unlit faces.
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Ambient;
        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
        RenderSettings.reflectionIntensity = 0f;

        // Post processing is OFF on the camera. The default URP volume (grading mode HDR) squashed every
        // pixel to a maximum of 209/255, so lit faces could never reach the fully saturated light colour of the
        // reference. (The old Volume/Tonemapping asset also never serialised its component, so it did nothing.)

        string path = "Assets/Scenes/CubeGrid.unity";
        EditorSceneManager.SaveScene(scene, path);
        Debug.Log("BuildCubeGridScene: saved " + path);
    }
}
