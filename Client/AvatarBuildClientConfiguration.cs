using UnityEditor;
using UnityEngine;

namespace net.rs64.VRCAvatarBuildServerTool.Client
{
    [FilePath("ProjectSettings/AvatarBuildClientConfiguration.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class AvatarBuildClientConfiguration : ScriptableSingleton<AvatarBuildClientConfiguration>
    {
        public string BuildServerURL = "http://127.0.0.1:8080";
        public bool ClientSideNDMFExecution = true;
    }

    [EditorWindowTitle(title = "AvatarBuildClientConfigurationEditor")]
    internal sealed class AvatarBuildClientConfigurationEditor : EditorWindow
    {
        private SerializedObject sObj;

        [MenuItem("Tools/VRCAvatarBuildServerTool/ClientConfiguration")]
        public static void ShowWindow()
        {
            GetWindow<AvatarBuildClientConfigurationEditor>();
        }

        public void OnGUI()
        {
            sObj ??= new SerializedObject(AvatarBuildClientConfiguration.instance);
            sObj.Update();

            EditorGUILayout.PropertyField(sObj.FindProperty(nameof(AvatarBuildClientConfiguration.BuildServerURL)));
            EditorGUILayout.PropertyField(sObj.FindProperty(nameof(AvatarBuildClientConfiguration.ClientSideNDMFExecution)));

            sObj.ApplyModifiedProperties();
        }
    }
}
