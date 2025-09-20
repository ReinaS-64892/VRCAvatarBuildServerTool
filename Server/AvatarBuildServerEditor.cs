using System.IO;
using System.Linq;
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
                AvatarBuildServer.ServerStart();
            }
        }

        public void OnGUI()
        {
            sObj ??= new SerializedObject(AvatarBuildServerConfiguration.instance);
            sObj.Update();
            using var ccs = new EditorGUI.ChangeCheckScope();

            var isEnable = sObj.FindProperty(nameof(AvatarBuildServerConfiguration.EnableServer));
            EditorGUILayout.PropertyField(isEnable);
            if (isEnable.boolValue)
            {
                EditorGUILayout.PropertyField(sObj.FindProperty(nameof(AvatarBuildServerConfiguration.BuildServerListenAddress)));
                EditorGUILayout.PropertyField(sObj.FindProperty(nameof(AvatarBuildServerConfiguration.ServerPasscode)));
                EditorGUILayout.PropertyField(sObj.FindProperty(nameof(AvatarBuildServerConfiguration.ShowGUIToAutoStart)));
                if (AvatarBuildServer.IsServerStarted is false)
                {
                    if (GUILayout.Button("server start"))
                    {
                        AvatarBuildServer.ServerStart();
                    }
                }
                else
                {
                    if (GUILayout.Button("exit server"))
                    {
                        AvatarBuildServer.ServerExit();
                    }
                }
                EditorGUILayout.PropertyField(sObj.FindProperty(nameof(AvatarBuildServerConfiguration.PresavePackageFolderName)));
                if (GUILayout.Button("set now packages"))
                {
                    AvatarBuildServer.ServerExit();
                    AvatarBuildServerConfiguration.instance.PresavePackageFolderName = new(Directory.EnumerateDirectories("Packages").Select(p => p.Substring("Packages/".Length)));
                    AvatarBuildServerConfiguration.instance.Save();
                }
                if (GUILayout.Button("Clear package"))
                {
                    AvatarBuildServer.ClearPackageRequest();
                }
            }
            else
            {
                if (AvatarBuildServer.IsServerStarted) { AvatarBuildServer.ServerExit(); }
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
