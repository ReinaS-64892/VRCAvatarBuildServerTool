using UnityEditor;

namespace net.rs64.VRCAvatarBuildServerTool.Uploader
{
    [FilePath("ProjectSettings/VRCAvatarBuildServerToolConfiguration.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class AvatarUploadServerConfiguration : ScriptableSingleton<AvatarUploadServerConfiguration>
    {
        // ここに ローカル ではないマシンからアクセスできる URL にはしないこと
        public string UploadServerListenAddress = "http://127.0.0.1:9506/";
        public bool ShowGUIToAutoStart = true;
        internal void Save()
        {
            Save(true);
        }
    }
}
