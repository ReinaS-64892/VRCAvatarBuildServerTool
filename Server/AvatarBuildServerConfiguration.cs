using System.Collections.Generic;
using UnityEditor;

namespace net.rs64.VRCAvatarBuildServerTool.Server
{
    [FilePath("ProjectSettings/VRCAvatarBuildServerToolConfiguration.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class AvatarBuildServerConfiguration : ScriptableSingleton<AvatarBuildServerConfiguration>
    {
        public string BuildServerListenAddress = "http://127.0.0.1:8080/";
        public string ServerPasscode = "適当な値の入力を要求します。";
        public bool ShowGUIToAutoStart = true;
        public List<string> PresavePackageFolderName;
        internal void Save()
        {
            Save(true);
        }
    }
}
