// Ex 8: on-screen help in Play mode. H hides it.
using UnityEngine;
using UnityEngine.InputSystem;

public class IblHud : MonoBehaviour
{
    public IblController controller;
    public bool show = true;
    GUIStyle style;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.hKey.wasPressedThisFrame) show = !show;
    }

    void OnGUI()
    {
        if (!show || controller == null) return;
        if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = false };
        string text = "IMAGE-BASED LIGHTING (Ex 8)\n" + controller.Describe() +
                      "\nC alignment check    0 reset    H hide help\nRight mouse drag: orbit    scroll: zoom    arrows: orbit";
        var size = style.CalcSize(new GUIContent(text));
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(10, 10, size.x + 20, size.y + 16), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(20, 18, size.x + 10, size.y + 10), text, style);
    }
}
