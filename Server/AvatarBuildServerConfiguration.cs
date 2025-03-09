using UnityEditor;
using UnityEngine;

namespace net.rs64.VRCAvatarBuildServerTool.Server
{
    [FilePath("ProjectSettings/VRCAvatarBuildServerToolConfiguration.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class AvatarBuildServerConfiguration : ScriptableSingleton<AvatarBuildServerConfiguration>
    {
        public string BuildServerListenAddress = "http://127.0.0.1:8080/";
        public bool ShowGUIToAutoStart = true;
    }

    [EditorWindowTitle(title = "VRCAvatarBuildServer")]
    internal sealed class AvatarBuildServerEditor : EditorWindow
    {
        private SerializedObject sObj;

        [MenuItem("Tools/VRCAvatarBuildServerTool/Server")]
        public static void ShowWindow()
        {
            GetWindow<AvatarBuildServerEditor>();
        }
        public void OnEnable()
        {
            if (AvatarBuildServerConfiguration.instance.ShowGUIToAutoStart)
            {
                BuildServer.ServerStart();
            }
        }

        public void OnGUI()
        {
            sObj ??= new SerializedObject(AvatarBuildServerConfiguration.instance);
            sObj.Update();

            EditorGUILayout.PropertyField(sObj.FindProperty(nameof(AvatarBuildServerConfiguration.BuildServerListenAddress)));
            EditorGUILayout.PropertyField(sObj.FindProperty(nameof(AvatarBuildServerConfiguration.ShowGUIToAutoStart)));
            if (BuildServer.IsServerStarted is false)
            {
                if (GUILayout.Button("server start"))
                {
                    BuildServer.ServerStart();
                }
            }
            else
            {
                if (GUILayout.Button("exit server"))
                {
                    BuildServer.ServerExit();
                }
            }
            sObj.ApplyModifiedProperties();
        }
    }
}
