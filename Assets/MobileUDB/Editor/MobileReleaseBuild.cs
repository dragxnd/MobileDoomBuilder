using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MobileUDB.Editor
{
    public static class MobileReleaseBuild
    {
        // Command line: -buildTarget Android -executeMethod
        // MobileUDB.Editor.MobileReleaseBuild.Android -mobileUdbApk <output.apk>
        public static void Android()
        {
            string[] args = Environment.GetCommandLineArgs();
            int option = Array.IndexOf(args, "-mobileUdbApk");
            if (option >= 0 && option + 1 >= args.Length)
                throw new ArgumentException("Specify a path after -mobileUdbApk.");
            string output = Path.GetFullPath(option < 0
                ? "Builds/Android/MobileDoomBuilder-0.5-arm64.apk" : args[option + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            PlayerSettings.productName = "Mobile Doom Builder";
            PlayerSettings.bundleVersion = "0.5";
            PlayerSettings.Android.bundleVersionCode = 5;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.development = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/MobileUDB/Scenes/MobileEditor.unity" },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });
            Debug.Log("MOBILE_DOOM_BUILDER_RELEASE: " + report.summary.result +
                ", errors=" + report.summary.totalErrors + ", bytes=" + report.summary.totalSize);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Mobile Doom Builder APK build failed.");
        }
    }
}
