// Ex 8: image-based lighting, switched on one ingredient at a time.
// Keys (new Input System, Input.GetKey would throw in this project):
//   1  background: the environment map behind everything, or a plain colour
//   2  diffuse ambient: from the map (spherical harmonics) -> flat colour -> none
//   3  specular reflections from the map on or off
//   4  the sun as a real directional light (the map without the sun disc is used for lighting then)
//   5  spin the environment slowly
//   R  rotate the environment 45 degrees (Shift+R: back)
//   E  exposure 1 -> 2 -> 0.5 -> 1
//   C  alignment check: keep the sun in the map while the light is on, so its reflection and the light's highlight overlap
//   0  reset
// Put it on any object. The editor script RtcgEx8Scene fills in the references and the measured sun direction.
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class IblController : MonoBehaviour
{
    public enum Ambient { Skybox, Flat, None }

    [Header("References")]
    public Camera cam;
    public Skybox cameraSkybox;          // the background the camera draws (always the full map, so the sun is visible)
    public Material skyFull;             // Skybox/Cubemap with the full map (sun disc included)
    public Material skyNoSun;            // the same map with the sun disc removed
    public Light sun;
    public ReflectionProbe skyProbe;     // custom probe holding a small copy of the map, turned with the sky
    public Cubemap reflectionFull;       // small (256 face) copies of the maps; their blurrier mips serve rough surfaces
    public Cubemap reflectionNoSun;
    public Cubemap reflectionBlack;      // "reflections off": the probe gets a black cubemap

    [Header("Measured from the map (by RtcgEx8Scene)")]
    public Vector3 sunDirection = new Vector3(0.5f, 0.2f, 0.8f); // world direction towards the sun at rotation 0
    public float sunIntensity = 0.95f;   // sun irradiance / pi, so it matches the map's own units
    public Color flatAmbient = new Color(0.3f, 0.3f, 0.35f); // linear, the average of the map's ambient light
    public Color backgroundColour = new Color(0.16f, 0.17f, 0.20f);

    [Header("State")]
    public bool background = true;
    public Ambient ambient = Ambient.Skybox;
    public bool reflections = true;
    public bool sunLight = false;
    public bool keepSunInMap = false;
    public bool spin = false;
    [Range(0, 360)] public float rotation = 0f;
    public float exposure = 1f;
    public float spinSpeed = 10f;        // degrees per second

    void Start() { Apply(); }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        bool changed = false;
        if (kb.digit1Key.wasPressedThisFrame) { background = !background; changed = true; }
        if (kb.digit2Key.wasPressedThisFrame) { ambient = (Ambient)(((int)ambient + 1) % 3); changed = true; }
        if (kb.digit3Key.wasPressedThisFrame) { reflections = !reflections; changed = true; }
        if (kb.digit4Key.wasPressedThisFrame) { sunLight = !sunLight; changed = true; }
        if (kb.digit5Key.wasPressedThisFrame) { spin = !spin; }
        if (kb.cKey.wasPressedThisFrame) { keepSunInMap = !keepSunInMap; changed = true; }
        if (kb.rKey.wasPressedThisFrame) { rotation = Mathf.Repeat(rotation + (kb.shiftKey.isPressed ? -45f : 45f), 360f); changed = true; }
        if (kb.eKey.wasPressedThisFrame) { exposure = exposure == 1f ? 2f : exposure == 2f ? 0.5f : 1f; changed = true; }
        if (kb.digit0Key.wasPressedThisFrame) { ResetAll(); changed = true; }
        if (spin) { rotation = Mathf.Repeat(rotation + spinSpeed * Time.deltaTime, 360f); changed = true; }
        if (changed) Apply();
    }

    public void ResetAll()
    {
        background = true; ambient = Ambient.Skybox; reflections = true; sunLight = false;
        keepSunInMap = false; spin = false; rotation = 0f; exposure = 1f;
    }

    // The sun direction after the environment is turned. Skybox/Cubemap turns the sky by -rotation about Y
    // in Unity's left-handed terms, so the light has to turn the same way.
    public Vector3 RotatedSunDirection() => Quaternion.Euler(0f, -rotation, 0f) * sunDirection.normalized;

    public void Apply()
    {
        // the two sky materials always share rotation and exposure
        foreach (var m in new[] { skyFull, skyNoSun })
        {
            if (m == null) continue;
            m.SetFloat("_Rotation", rotation);
            m.SetFloat("_Exposure", exposure);
        }

        // 1. background
        if (cam != null)
        {
            cam.clearFlags = background ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = backgroundColour;
        }
        if (cameraSkybox != null) cameraSkybox.material = skyFull;

        // 4. the sun: either inside the map (no light), or taken out of the map and added back as a light
        bool lightOn = sunLight;
        RenderSettings.skybox = (lightOn && !keepSunInMap) ? skyNoSun : skyFull;
        if (sun != null)
        {
            sun.enabled = lightOn;
            sun.intensity = sunIntensity * exposure;
            sun.transform.rotation = Quaternion.LookRotation(-RotatedSunDirection(), Vector3.up);
        }
        RenderSettings.sun = sun;

        // 2. diffuse ambient
        switch (ambient)
        {
            case Ambient.Skybox:
                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.ambientIntensity = 1f;
                break;
            case Ambient.Flat:
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = (flatAmbient * exposure).gamma; // the colour field is gamma, the value is linear
                break;
            case Ambient.None:
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;
                break;
        }

        // 3. specular reflections. The probe holds a small copy of the map: its mip 0 is sharp for smooth surfaces,
        // the smaller mips are blurrier and are read by rough surfaces.
        // It uses the same map as the lighting (with or without the sun disc), turns with the sky and follows the exposure.
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = reflections ? 1f : 0f;
        if (skyProbe != null)
        {
            // Swapping the texture is used to switch reflections off. Disabling the probe or setting its intensity to 0
            // made URP stop using it in the editor captures, even after switching it back on.
            skyProbe.customBakedTexture = !reflections ? reflectionBlack
                                        : RenderSettings.skybox == skyNoSun ? reflectionNoSun : reflectionFull;
            skyProbe.intensity = exposure;
            skyProbe.transform.rotation = Quaternion.Euler(0f, -rotation, 0f);
        }

        // re-project the sky into spherical harmonics (the diffuse ambient)
        DynamicGI.UpdateEnvironment();
    }

    public string Describe()
    {
        return $"1 Background: {(background ? "environment map" : "plain colour")}\n" +
               $"2 Diffuse ambient: {(ambient == Ambient.Skybox ? "from the map (SH)" : ambient == Ambient.Flat ? "flat colour" : "none")}\n" +
               $"3 Reflections: {(reflections ? "on" : "off")}\n" +
               $"4 Sun light: {(sunLight ? "on (sun taken out of the map)" : "off (sun only inside the map)")}" +
               (sunLight && keepSunInMap ? "  [C: sun kept in the map]" : "") + "\n" +
               $"5 Spin: {(spin ? "on" : "off")}    R rotation: {rotation:0} deg    E exposure: {exposure:0.0}";
    }
}
