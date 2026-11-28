// Ex 4. Keys for the camera effects in the gallery, a small first person walk, and an OnGUI HUD.
//   1 bloom   2 depth of field   3 next focus target   4 lens flare   5 main lights off/on
//   6 auto exposure on/off   7 extra effects (vignette, chromatic aberration, white balance, motion blur)   H HUD
//   WASD walk, Shift run, mouse look, Esc frees the cursor, a click catches it again.
// The project only has the new Input System switched on, so this reads Keyboard.current and Mouse.current
// (Input.GetKey would throw). The walk follows Assets/Scripts/Ex7/FirstPersonWalker.cs, but the gallery has no
// colliders, so instead of a CharacterController the position is kept inside the room and outside the pedestals.
// Put it on the main camera, next to AutoExposure.
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class EffectsController : MonoBehaviour
{
    public Volume volume;
    public AutoExposure autoExposure;
    public LensFlareComponentSRP lensFlare;
    public Light[] mainLights;             // the dominant group switched by key 5
    public Transform[] focusTargets;       // cycled by key 3
    public string[] focusNames;

    [Header("Walk")]
    public float eyeHeight = 1.6f;
    public float walkSpeed = 2f, runSpeed = 5f;
    public float lookSensitivity = 0.1f;
    public Vector2 roomHalfSize = new Vector2(4.6f, 7.6f);   // x and z limits for the eye
    public Vector3[] pedestals;            // centres of the pedestals to walk around
    public float pedestalRadius = 0.85f;

    [Header("State")]
    public bool bloomOn = true, dofOn = true, flareOn = true, lightsOn = true, extrasOn = false, hudOn = true;
    public int focusIndex = 1;

    float yaw, pitch;
    Bloom bloom; DepthOfField dof; Vignette vignette; ChromaticAberration chroma; WhiteBalance whiteBalance; MotionBlur motionBlur;

    void Start()
    {
        Bind();
        Vector3 e = transform.eulerAngles; yaw = e.y; pitch = e.x > 180f ? e.x - 360f : e.x;
        LockCursor(true);
        ApplyAll();
    }

    void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // Used by the keys and by the editor capture script.
    public void ApplyAll()
    {
        if (bloom == null) Bind();
        bloom.active = bloomOn;
        dof.active = dofOn;
        if (lensFlare != null) lensFlare.enabled = flareOn;
        foreach (var l in mainLights) if (l != null) l.enabled = lightsOn;
        vignette.active = extrasOn; chroma.active = extrasOn; whiteBalance.active = extrasOn; motionBlur.active = extrasOn;
        UpdateFocus();
    }

    // Finds the overrides in the profile the volume uses (call again after the profile is swapped).
    public void Bind()
    {
        var p = AutoExposure.ProfileOf(volume);
        p.TryGet(out bloom); p.TryGet(out dof); p.TryGet(out vignette); p.TryGet(out chroma); p.TryGet(out whiteBalance); p.TryGet(out motionBlur);
    }

    // Bokeh depth of field works on view depth, so the focus distance is the target's depth along the view axis.
    public float FocusDistance()
    {
        if (focusTargets == null || focusTargets.Length == 0) return 10f;
        Transform t = focusTargets[Mathf.Clamp(focusIndex, 0, focusTargets.Length - 1)];
        return Mathf.Max(0.1f, Vector3.Dot(t.position - transform.position, transform.forward));
    }

    public void UpdateFocus() { if (dof != null) dof.focusDistance.Override(FocusDistance()); }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null) return;

        if (kb.digit1Key.wasPressedThisFrame) { bloomOn = !bloomOn; ApplyAll(); }
        if (kb.digit2Key.wasPressedThisFrame) { dofOn = !dofOn; ApplyAll(); }
        if (kb.digit3Key.wasPressedThisFrame) { focusIndex = (focusIndex + 1) % focusTargets.Length; ApplyAll(); }
        if (kb.digit4Key.wasPressedThisFrame) { flareOn = !flareOn; ApplyAll(); }
        if (kb.digit5Key.wasPressedThisFrame) { lightsOn = !lightsOn; ApplyAll(); }
        if (kb.digit6Key.wasPressedThisFrame && autoExposure != null) autoExposure.adapt = !autoExposure.adapt;
        if (kb.digit7Key.wasPressedThisFrame) { extrasOn = !extrasOn; ApplyAll(); }
        if (kb.hKey.wasPressedThisFrame) hudOn = !hudOn;

        // cursor: Esc frees it, a click catches it again
        if (kb.escapeKey.wasPressedThisFrame) LockCursor(false);
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);

        // mouse look
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 d = mouse.delta.ReadValue() * lookSensitivity;
            yaw += d.x;
            pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
        }
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        // walking on the floor plane
        Vector2 input = new Vector2(
            (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f),
            (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));
        input = Vector2.ClampMagnitude(input, 1f);
        float speed = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? runSpeed : walkSpeed;
        Quaternion body = Quaternion.Euler(0f, yaw, 0f);
        Vector3 pos = transform.position + (body * new Vector3(input.x, 0f, input.y)) * speed * Time.deltaTime;
        pos.x = Mathf.Clamp(pos.x, -roomHalfSize.x, roomHalfSize.x);
        pos.z = Mathf.Clamp(pos.z, -roomHalfSize.y, roomHalfSize.y);
        pos.y = eyeHeight;
        if (pedestals != null)
            foreach (var c in pedestals)
            {
                Vector2 d = new Vector2(pos.x - c.x, pos.z - c.z);
                if (d.magnitude < pedestalRadius) { d = d.normalized * pedestalRadius; pos.x = c.x + d.x; pos.z = c.z + d.y; }
            }
        transform.position = pos;

        UpdateFocus();   // the focus target keeps its distance while walking
    }

    void OnGUI()
    {
        if (!hudOn) return;
        string focus = focusNames != null && focusIndex < focusNames.Length ? focusNames[focusIndex] : focusIndex.ToString();
        string ae = autoExposure == null ? "none" :
            $"{(autoExposure.adapt ? "on" : "off")}\n" +
            $"  measured log-avg luminance {autoExposure.MeasuredLuminance:0.0000}\n" +
            $"  target luminance (all lights on) {autoExposure.targetLuminance:0.0000}\n" +
            $"  target EV {autoExposure.TargetEV:+0.00;-0.00;0.00}   applied post exposure {autoExposure.CurrentEV:+0.00;-0.00;0.00} EV";
        string text =
            "Ex 4 camera effects\n" +
            $"1 bloom: {(bloomOn ? "on" : "off")}\n" +
            $"2 depth of field (Bokeh): {(dofOn ? "on" : "off")}\n" +
            $"3 focus: {focus} at {FocusDistance():0.00} m\n" +
            $"4 lens flare: {(flareOn ? "on" : "off")}\n" +
            $"5 main lights (7 spots + back fill): {(lightsOn ? "on" : "off")}\n" +
            $"6 auto exposure: {ae}\n" +
            $"7 extra effects (vignette, chromatic aberration, white balance, motion blur): {(extrasOn ? "on" : "off")}\n" +
            "H hide this   WASD walk, Shift run, mouse look, Esc frees the cursor";
        var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = false };
        style.normal.textColor = Color.white;
        GUI.Box(new Rect(10, 10, 560, 230), text, style);
    }
}
