using System.IO;
using MobileUDB.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MobileUDB.Editor
{
    public static class MobileEditorSetup
    {
        public const string ScenePath = "Assets/MobileUDB/Scenes/MobileEditor.unity";
        [MenuItem("Mobile Doom Builder/Create editor scene")]
        public static void CreateScene()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play Mode first.");
            Directory.CreateDirectory("Assets/MobileUDB/Scenes");
            if (File.Exists(ScenePath)) { Debug.Log("Scene already exists: " + ScenePath); return; }
            // Additively create the artifact, preserving the user's current scene and unsaved work.
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            new GameObject("Mobile Doom Builder").AddComponent<MobileEditor>();
            var camera = new GameObject("Main Camera", typeof(Camera)); camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 0, -10); var component = camera.GetComponent<Camera>(); component.orthographic = true; component.clearFlags = CameraClearFlags.SolidColor; component.backgroundColor = new Color32(15, 23, 35, 255);
            var sun = new GameObject("Directional Light", typeof(Light)); sun.GetComponent<Light>().type = LightType.Directional;
            EditorSceneManager.SaveScene(scene, ScenePath); EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            AssetDatabase.Refresh(); Debug.Log("Mobile Doom Builder scene created: " + ScenePath);
        }
        [MenuItem("Mobile Doom Builder/Open editor scene")]
        public static void OpenScene()
        {
            CreateScene();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }
        [MenuItem("Mobile Doom Builder/Build Android APK")]
        public static void BuildAndroid()
        {
            CreateScene();
            MobileReleaseBuild.Android();
        }
    }
}
