using System;

namespace net.rs64.VRCAvatarBuildServerTool.BuildRunner
{
    [Serializable]
    public class BuildRunnerProjectConfig
    {
        public string BuildRunnerServerPort;
        public string InternalServerURL;
        public string AuthorizationCode;
    }
    [Serializable]
    public class BuildRequest
    {
        public string[] BuildTargets;
        public PathToHash[] Assets;
    }
    [Serializable]
    public class PathToHash
    {
        public string Path;
        public string Hash;
    }
[Serializable]
    public class UploadRequest
    {
        public bool IsNewAvatar;
        public string BlueprintID;
        public string AssetBundleBase64;
    }

}
