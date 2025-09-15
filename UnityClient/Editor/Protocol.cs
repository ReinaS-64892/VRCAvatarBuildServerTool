using System;
using System.Collections.Generic;

namespace net.rs64.VRCAvatarBuildServerTool.UnityClient
{
    [Serializable]
    public class BuildRequest
    {
        public List<string> BuildTargets = new();
        public PathToHash[] Assets ;
    }
    [Serializable]
    public class PathToHash
    {
        public string Path = "";
        public string Hash = "";
    }
    [Serializable]
    public class BuildRequestResponse
    {
        public string ResultCode;
        public string[] MissingFiles;
    }
}
