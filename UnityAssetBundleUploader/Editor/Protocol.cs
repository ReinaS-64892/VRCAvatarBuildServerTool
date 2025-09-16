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

namespace net.rs64.VRCAvatarBuildServerTool.Uploader
{
    [Serializable]
    public class UploaderProjectConfig
    {
        public string UploaderServerPort;
        public string InternalServerURL;
        public string AuthorizationCode;
    }
    [Serializable]
    public class UploadRequest
    {
        public bool IsNewAvatar;
        public string BlueprintID;
        public string AssetBundleBase64;
    }

}
