using UnityEditor;
using UnityEngine;

namespace net.rs64.VRCAvatarBuildServerTool.Client
{
    [FilePath("ProjectSettings/AvatarBuildClientConfiguration.asset", FilePathAttribute.Location.PreferencesFolder)]
    internal sealed class AvatarBuildClientConfiguration : ScriptableSingleton<AvatarBuildClientConfiguration>
    {
        public string BuildServerURL = "http://127.0.0.1:8080";
        public bool ClientSideNDMFExecution = true;
        internal void Save()
        {
            Save(true);
        }
    }
}
