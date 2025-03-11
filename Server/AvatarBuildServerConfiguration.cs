using UnityEditor;

namespace net.rs64.VRCAvatarBuildServerTool.Server
{
    [FilePath("ProjectSettings/VRCAvatarBuildServerToolConfiguration.asset", FilePathAttribute.Location.PreferencesFolder)]
    internal sealed class AvatarBuildServerConfiguration : ScriptableSingleton<AvatarBuildServerConfiguration>
    {
        public string BuildServerListenAddress = "http://127.0.0.1:8080/";
        public bool ShowGUIToAutoStart = true;
        internal void Save()
        {
            Save(true);
        }
    }
}
