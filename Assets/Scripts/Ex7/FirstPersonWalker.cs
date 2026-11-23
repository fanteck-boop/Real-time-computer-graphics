// Ex 7. First person walking: WASD to move, Shift to run, mouse to look, Esc frees the cursor, click locks it again.
// Keys 1, 2 and 3 jump to the three areas (Nisse field, mip corridor, Box vs Kaiser lanes).
// The project only has the new Input System switched on, so this reads Keyboard.current and Mouse.current.
// (Input.GetKey would throw an exception here.)
// Put it on the Player object, which also has a CharacterController. The camera sits on a child object ("Head").
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonWalker : MonoBehaviour
{
    public Transform head;                 // child at eye height, holds the camera, only turns up and down
    public Camera cam;                     // the player camera (on the head)
    public float walkSpeed = 3f;           // metres per second
    public float runSpeed = 8f;            // with Shift
    public float lookSensitivity = 0.1f;   // degrees per pixel of mouse movement
    public float gravity = -9.81f;

    [Header("Teleport points (keys 1, 2, 3): feet position and yaw")]
    public Vector3[] teleportPoints = { Vector3.zero };
    public float[] teleportYaw = { 0f };

    CharacterController controller;
    float yaw, pitch, verticalSpeed;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        yaw = transform.eulerAngles.y;
        pitch = head.localEulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
        LockCursor(true);
    }

    void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null) return;

        // cursor: Esc frees it, a click catches it again
        if (kb.escapeKey.wasPressedThisFrame) LockCursor(false);
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);

        // mouse look: yaw turns the whole body, pitch only the head
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 d = mouse.delta.ReadValue() * lookSensitivity;
            yaw += d.x;
            pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
        }
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        head.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        // walking
        Vector2 input = new Vector2(
            (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f),
            (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));
        input = Vector2.ClampMagnitude(input, 1f);
        float speed = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? runSpeed : walkSpeed;
        Vector3 velocity = (transform.right * input.x + transform.forward * input.y) * speed;

        // gravity, so the controller stays on the floor
        if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = -1f;
        verticalSpeed += gravity * Time.deltaTime;
        velocity.y = verticalSpeed;
        controller.Move(velocity * Time.deltaTime);

        // teleports
        Key[] keys = { Key.Digit1, Key.Digit2, Key.Digit3 };
        for (int i = 0; i < keys.Length && i < teleportPoints.Length; i++)
            if (kb[keys[i]].wasPressedThisFrame) SetPose(teleportPoints[i], teleportYaw[i], 0f);
    }

    // Puts the player somewhere. Also used by the capture script, since keys and mouse cannot be simulated headless.
    public void SetPose(Vector3 feet, float newYaw, float newPitch)
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        controller.enabled = false;            // a CharacterController overrides transform changes while enabled
        transform.position = feet;
        controller.enabled = true;
        yaw = newYaw; pitch = newPitch; verticalSpeed = 0f;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }
}
