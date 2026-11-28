// Ex 7 benchmark launcher. Opens the Ex 7 scene (without changing or saving it) and enters Play mode;
// Ex7Bench (Assets/Scripts/Ex7/Ex7Bench.cs) then runs because of the -ex7bench argument and exits the editor.
//   Unity.exe -batchmode -projectPath <project> -executeMethod RtcgEx7Bench.Run -ex7bench Captures/Ex07_Bench
// (no -quit: Play mode has to keep running). A watchdog exits the editor if the run takes longer than 15 minutes.
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class RtcgEx7Bench
{
    const string ScenePath = "Assets/Scenes/Ex 7 Culling and Mips.unity";
    const double TimeoutSeconds = 900;

    static RtcgEx7Bench()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-ex7bench") < 0) return;
        EditorApplication.update += () =>
        {
            if (EditorApplication.timeSinceStartup > TimeoutSeconds)
            {
                Debug.LogError("EX7B watchdog: timeout, exiting");
                EditorApplication.Exit(3);
            }
        };
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath);
        EditorApplication.isPlaying = true;
    }
}
