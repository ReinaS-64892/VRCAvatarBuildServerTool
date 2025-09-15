using UnityEditor;
using UnityEngine;

namespace net.rs64.VRCAvatarBuildServerTool.Uploader
{
    [EditorWindowTitle(title = "VRCAvatarBuildServer")]
    internal sealed class AvatarBuildServerEditor : EditorWindow
    {
        private SerializedObject sObj;

        [MenuItem("Tools/VRCAvatarBuildServerTool/UploadServer")]
        public static void ShowWindow()
        {
            GetWindow<AvatarBuildServerEditor>();
        }
        public void OnEnable()
        {
            if (AvatarUploadServerConfiguration.instance.ShowGUIToAutoStart)
            {
                AvatarUploader.ServerStart();
            }
        }

        public void OnGUI()
        {
            sObj ??= new SerializedObject(AvatarUploadServerConfiguration.instance);
            sObj.Update();
            using var ccs = new EditorGUI.ChangeCheckScope();

            EditorGUILayout.PropertyField(sObj.FindProperty(nameof(AvatarUploadServerConfiguration.UploadServerListenAddress)));
            EditorGUILayout.PropertyField(sObj.FindProperty(nameof(AvatarUploadServerConfiguration.ShowGUIToAutoStart)));
            if (AvatarUploader.IsServerStarted is false)
            {
                if (GUILayout.Button("server start"))
                {
                    AvatarUploader.ServerStart();
                }
            }
            else
            {
                if (GUILayout.Button("exit server"))
                {
                    AvatarUploader.ServerExit();
                }
            }

            if (ccs.changed)
            {
                sObj.ApplyModifiedProperties();
                var absc = sObj.targetObject as AvatarUploadServerConfiguration;
                absc?.Save();
                SaveChanges();
            }
        }
    }
}
