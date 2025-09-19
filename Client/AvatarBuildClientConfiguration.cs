using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace net.rs64.VRCAvatarBuildServerTool.Client
{
    [FilePath("ProjectSettings/AvatarBuildClientConfiguration.asset", FilePathAttribute.Location.PreferencesFolder)]
    internal sealed class AvatarBuildClientConfiguration : ScriptableSingleton<AvatarBuildClientConfiguration>
    {
        public List<BuildServer> BuildServers = new List<BuildServer>() { new() };
        internal void Save()
        {
            Save(true);
        }
    }
    [Serializable]
    internal class BuildServer
    {
        public bool Enable = true;
        public string URL = "http://127.0.0.1:9505/";
        public string ServerPasscodeHeader = "";
    }
}
