using UnityEditor;
using UnityEngine;

namespace net.rs64.VRCAvatarBuildServerTool.Server
{
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
            using var ccs = new EditorGUI.ChangeCheckScope();

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

            if (ccs.changed)
            {
                sObj.ApplyModifiedProperties();
                var absc = sObj.targetObject as AvatarBuildServerConfiguration;
                absc?.Save();
                SaveChanges();
            }
        }
    }
}
