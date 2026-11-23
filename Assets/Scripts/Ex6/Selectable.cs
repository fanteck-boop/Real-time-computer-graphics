// Ex 6. Marker component: objects with this component can be clicked and outlined.
// Based on Ronja's Shader Tutorials, tutorial 052 Object Outline. The original marks selectable
// objects with the tag "Selectable"; here a component does the same job, so no project tag has to be added.
using UnityEngine;

public class Selectable : MonoBehaviour
{
    // The renderer that gets drawn into the selection buffer. Leave empty to use the first renderer in the children.
    public Renderer target;

    public Renderer GetRenderer()
    {
        if (target == null) target = GetComponentInChildren<Renderer>();
        return target;
    }
}
