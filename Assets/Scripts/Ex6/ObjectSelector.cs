// Ex 6. Click selection for the object outline.
// Based on Ronja's Shader Tutorials, tutorial 052 Object Outline (ObjectOutline.cs, Update part).
// Changes for this project:
//  - the new Input System (Mouse.current) instead of Input.GetMouseButtonDown, because the project only has the new input handler on
//  - a Selectable component instead of the "Selectable" tag
//  - the drawing part (OnRenderImage) moved to ObjectOutlineFeature, because URP never calls OnRenderImage
// Put this component on the camera. ObjectOutlineFeature looks for it on the camera that is rendering.
using UnityEngine;
using UnityEngine.InputSystem;

public class ObjectSelector : MonoBehaviour
{
    public Renderer OutlinedObject;

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

        var ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
        Selectable selectable = null;
        if (Physics.Raycast(ray, out var hit)) selectable = hit.transform.GetComponentInParent<Selectable>();

        if (selectable != null) Select(selectable.GetRenderer());
        else Select(null);
    }

    // Lets other scripts (and the capture script) select an object without a mouse click. Pass null to clear.
    public void Select(Renderer r)
    {
        OutlinedObject = r;
    }
}
