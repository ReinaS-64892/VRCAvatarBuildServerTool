using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace net.rs64.VRCAvatarBuildServerTool.Client
{
    [EditorWindowTitle(title = "AvatarBuildClientEditor")]
    internal sealed class AvatarBuildClientEditor : EditorWindow
    {
        private SerializedObject sObj;

        [MenuItem("Tools/VRCAvatarBuildServerTool/Client")]
        public static void ShowWindow()
        {
            GetWindow<AvatarBuildClientEditor>();
        }

        public void OnGUI()
        {
            sObj ??= new SerializedObject(AvatarBuildClientConfiguration.instance);
            sObj.Update();
            using var ccs = new EditorGUI.ChangeCheckScope();

            EditorGUILayout.PropertyField(sObj.FindProperty(nameof(AvatarBuildClientConfiguration.BuildServers)));
            EditorGUILayout.PropertyField(sObj.FindProperty(nameof(AvatarBuildClientConfiguration.IgnorePackages)));

            if (GUILayout.Button("Sync Packages"))
            {
                Task.Run(async () => await AvatarBuildClient.SyncPackages());
            }

            if (ccs.changed)
            {
                sObj.ApplyModifiedProperties();
                var abcc = sObj.targetObject as AvatarBuildClientConfiguration;
                abcc?.Save();
                SaveChanges();
            }
        }
    }
}
