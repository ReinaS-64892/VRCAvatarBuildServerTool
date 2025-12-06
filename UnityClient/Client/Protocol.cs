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

namespace net.rs64.VRCAvatarBuildServerTool.Client
{
    // Protocol
    [Serializable]
    public class BuildRequest
    {
        public string PrefabName = "";
        // Unity の GUID
        public string BuildTarget = "";

        public PathToHash[] Assets = Array.Empty<PathToHash>();
        public Package[] Packages = Array.Empty<Package>();
    }
    [Serializable]
    public class PathToHash
    {
        public string Path;
        public string Hash;
    }
    [Serializable]
    public class Package
    {
        public string PackageID;

        // パスは package 内の相対にしたいね
        public PathToHash[] Files = Array.Empty<PathToHash>();
    }

    [Serializable]
    public class BuildRequestResponse
    {
        public string ResultCode;
        public string[] MissingFiles;


        public const string MissingAssets = "MissingAssets";
        public const string BuildRequestAccept = "BuildRequestAccept";
    }

}
