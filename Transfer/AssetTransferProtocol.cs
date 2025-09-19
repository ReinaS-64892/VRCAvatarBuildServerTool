using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace net.rs64.VRCAvatarBuildServerTool.Transfer
{
    // Protocol
    [Serializable]
    public class BuildRequest
    {
        // Unity の GUID
        public List<string> BuildTargets;

        // .meta も packages も関係なくねじ込む想定
        public List<PathToHash> Assets;
    }
    public class PathToHash
    {
        public string Path;
        public string Hash;
    }

    public class BuildRequestResponse
    {
        public string ResultCode;
        public string[] MissingFiles;


        public const string MissingAssets = "MissingAssets";
        public const string BuildRequestAccept = "BuildRequestAccept";
    }

}
