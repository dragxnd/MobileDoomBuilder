using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace MobileUDB.Editor
{
    // Original user-supplied pixels remain in headd1.png, without regeneration.
    [InitializeOnLoad]
    public static class MobileAppBranding
    {
        public const string ProductName="Mobile Doom Builder";
        public const string IconPath="Assets/MobileUDB/AppIcon/headd1.png";
        static MobileAppBranding(){EditorApplication.delayCall+=Configure;}
        public static void Configure()
        {
            var icon=AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);if(icon==null)return;
            PlayerSettings.productName=ProductName;
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown,new[]{icon},IconKind.Any);
            // Android's adaptive resource is composed from the original bitmap by
            // the Gradle hook below, retaining aspect ratio and mask-safe margins.
            int count=PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Android,IconKind.Any).Length;
            if(count>0)PlayerSettings.SetIcons(NamedBuildTarget.Android,Enumerable.Repeat(icon,count).ToArray(),IconKind.Any);
        }
        public static void WriteAndroidIcons(string unityLibrary)
        {
            string launcher=Path.Combine(Directory.GetParent(unityLibrary).FullName,"launcher");
            if(!Directory.Exists(launcher))throw new InvalidOperationException("Android launcher module is missing.");
            string resources=Path.Combine(launcher,"src/main/res");
            string bitmap=Path.Combine(resources,"drawable-nodpi");Directory.CreateDirectory(bitmap);
            File.Copy(Path.Combine(Directory.GetParent(Application.dataPath).FullName,IconPath),Path.Combine(bitmap,"mobileudb_sprite.png"),true);
            string drawable=Path.Combine(resources,"drawable");Directory.CreateDirectory(drawable);
            const string header="<?xml version=\"1.0\" encoding=\"utf-8\"?>\n";
            File.WriteAllText(Path.Combine(drawable,"mobileudb_icon_foreground.xml"),header+
                "<layer-list xmlns:android=\"http://schemas.android.com/apk/res/android\"><item android:width=\"50dp\" android:height=\"56dp\" android:gravity=\"center\"><bitmap android:src=\"@drawable/mobileudb_sprite\" android:gravity=\"fill\" android:filter=\"false\" /></item></layer-list>");
            File.WriteAllText(Path.Combine(drawable,"mobileudb_icon_background.xml"),header+
                "<shape xmlns:android=\"http://schemas.android.com/apk/res/android\" android:shape=\"rectangle\"><solid android:color=\"#162130\" /></shape>");
            string adaptive=Path.Combine(resources,"mipmap-anydpi-v26");Directory.CreateDirectory(adaptive);
            string xml=header+"<adaptive-icon xmlns:android=\"http://schemas.android.com/apk/res/android\"><background android:drawable=\"@drawable/mobileudb_icon_background\"/><foreground android:drawable=\"@drawable/mobileudb_icon_foreground\"/></adaptive-icon>";
            File.WriteAllText(Path.Combine(adaptive,"app_icon.xml"),xml);File.WriteAllText(Path.Combine(adaptive,"app_icon_round.xml"),xml);
        }
    }
    public sealed class MobileAppBrandingImporter:AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if(assetPath!=MobileAppBranding.IconPath)return;var importer=(TextureImporter)assetImporter;
            importer.textureType=TextureImporterType.Default;importer.mipmapEnabled=false;importer.filterMode=FilterMode.Point;
            importer.wrapMode=TextureWrapMode.Clamp;importer.npotScale=TextureImporterNPOTScale.None;
            importer.alphaIsTransparency=true;importer.textureCompression=TextureImporterCompression.Uncompressed;
        }
    }
    public sealed class MobileAppBrandingBuild:IPreprocessBuildWithReport
    {
        public int callbackOrder {get{return 100;}}
        public void OnPreprocessBuild(BuildReport report){if(report.summary.platform==BuildTarget.Android)MobileAppBranding.Configure();}
    }
#if UNITY_ANDROID
    public sealed class MobileAppBrandingGradle:UnityEditor.Android.IPostGenerateGradleAndroidProject
    {
        public int callbackOrder {get{return 100;}}
        public void OnPostGenerateGradleAndroidProject(string path){MobileAppBranding.WriteAndroidIcons(path);}
    }
#endif
}

