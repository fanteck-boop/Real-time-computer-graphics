using UnityEditor;

// Rebuild the cube grid scene, then capture it in play mode (used for the Ex01 match iterations).
public static class RtcgEx01Match
{
    public static void BuildAndCapture()
    {
        BuildCubeGridScene.Build();
        RtcgTools.CaptureCubeGrid();
    }
}

