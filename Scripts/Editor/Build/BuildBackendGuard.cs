#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Keeps the Windows build from failing over a missing C++ toolchain.
// IL2CPP (fast native code) needs the Windows SDK (10.0.19041 or newer, part of Visual Studio's "Desktop development
// with C++"). Without it the build stops with "Could not set up a toolchain for Architecture x64".
//  - SDK missing and the project set to IL2CPP -> the project switches to Mono (builds work right away, a little slower
//    scripts in the game), and remembers it did.
//  - SDK installed later -> it switches back to IL2CPP by itself.
// Checked when the editor loads and again right before every build. Only on Windows; your own choice of Mono is left alone.
[InitializeOnLoad]
static class BuildBackendGuard
{
    const string SwitchedKey = "Mossblood.BuildGuard.SwitchedToMono";
    static readonly Version MinSdk = new Version(10, 0, 19041, 0);

    static BuildBackendGuard() => EditorApplication.delayCall += () => Check(false);

    // True when a Windows SDK good enough for IL2CPP is installed (headers and x64 libraries)
    public static bool WindowsSdkReady(out string found)
    {
        found = null;
        try
        {
            string kits = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10");
            string include = Path.Combine(kits, "Include");
            if (!Directory.Exists(include)) return false;
            Version best = null;
            foreach (string dir in Directory.GetDirectories(include))
            {
                if (!Version.TryParse(Path.GetFileName(dir), out Version v) || v < MinSdk) continue;
                if (!Directory.Exists(Path.Combine(dir, "um")) || !Directory.Exists(Path.Combine(kits, "Lib", Path.GetFileName(dir), "um", "x64"))) continue;
                if (best == null || v > best) best = v;
            }
            if (best == null) return false;
            found = best.ToString();
            return true;
        }
        catch { return false; }
    }

    public static void Check(bool beforeBuild)
    {
        if (Application.platform != RuntimePlatform.WindowsEditor) return;
        if (!beforeBuild && (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating))
        {
            EditorApplication.delayCall += () => Check(false);
            return;
        }
        var target = NamedBuildTarget.Standalone;
        ScriptingImplementation backend = PlayerSettings.GetScriptingBackend(target);
        bool ready = WindowsSdkReady(out string sdk);
        if (backend == ScriptingImplementation.IL2CPP && !ready)
        {
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.Mono2x);
            EditorPrefs.SetBool(SwitchedKey, true);
            AssetDatabase.SaveAssets();
            Debug.LogWarning("[Build] The Windows SDK for IL2CPP is not installed, so the game is built with Mono for now (it builds fine, scripts a little slower). "
                + "To get IL2CPP back: Visual Studio Installer > Modify > Individual components > tick 'Windows 11 SDK' (10.0.22621 or newer) > Modify. "
                + "This project switches back to IL2CPP by itself once it is installed.");
        }
        else if (backend == ScriptingImplementation.Mono2x && ready && EditorPrefs.GetBool(SwitchedKey, false))
        {
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
            EditorPrefs.DeleteKey(SwitchedKey);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Build] Windows SDK {sdk} found: the game is built with IL2CPP again.");
        }
    }

    // Right before every build: the same check (and the build continues with the right backend)
    sealed class BeforeBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platformGroup != BuildTargetGroup.Standalone) return;
            Check(true);
        }
    }

    [InitializeOnLoadMethod]
    static void HookBuildButton()
    {
        // The Build button: settle the backend first, then build as usual
        BuildPlayerWindow.RegisterBuildPlayerHandler(options =>
        {
            Check(true);
            BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
        });
    }
}
#endif
