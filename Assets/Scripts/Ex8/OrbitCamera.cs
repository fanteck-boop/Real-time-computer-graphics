// Ex 8: orbit camera. Hold the right mouse button and drag to orbit, scroll to zoom, arrow keys also orbit.
// Uses the new Input System (Mouse.current, Keyboard.current).
using UnityEngine;
using UnityEngine.InputSystem;

public class OrbitCamera : MonoBehaviour
{
    public Vector3 target = new Vector3(0f, 0.75f, 0f);
    public float distance = 4.2f;
    public float yaw = 0f;          // degrees around Y
    public float pitch = 8f;        // degrees, positive looks down
    public float minDistance = 0.8f, maxDistance = 14f;
    public float mouseSpeed = 0.25f; // degrees per pixel
    public float keySpeed = 60f;     // degrees per second

    void LateUpdate()
    {
        var m = Mouse.current;
        var kb = Keyboard.current;
        if (m != null)
        {
            if (m.rightButton.isPressed)
            {
                Vector2 d = m.delta.ReadValue() * mouseSpeed;
                yaw += d.x;
                pitch -= d.y;
            }
            float s = m.scroll.ReadValue().y;
            if (s != 0f) distance *= s > 0f ? 0.9f : 1.1f;   // one step per scroll event, whatever the units
        }
        if (kb != null)
        {
            float dt = Time.deltaTime * keySpeed;
            if (kb.leftArrowKey.isPressed) yaw += dt;
            if (kb.rightArrowKey.isPressed) yaw -= dt;
            if (kb.upArrowKey.isPressed) pitch += dt;
            if (kb.downArrowKey.isPressed) pitch -= dt;
        }
        Apply();
    }

    public void Apply()
    {
        pitch = Mathf.Clamp(pitch, -20f, 85f);
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        transform.SetPositionAndRotation(target - rot * Vector3.forward * distance, rot);
    }

    // used by the capture script, since the mouse cannot be moved headless
    public void SetView(Vector3 newTarget, float newYaw, float newPitch, float newDistance)
    {
        target = newTarget; yaw = newYaw; pitch = newPitch; distance = newDistance;
        Apply();
    }
}
