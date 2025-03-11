using UnityEditor;

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

            EditorGUILayout.PropertyField(sObj.FindProperty(nameof(AvatarBuildClientConfiguration.BuildServerURL)));
            EditorGUILayout.PropertyField(sObj.FindProperty(nameof(AvatarBuildClientConfiguration.ClientSideNDMFExecution)));

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
