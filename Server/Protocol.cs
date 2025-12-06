using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace net.rs64.VRCAvatarBuildServerTool.Transfer;

public class BuildRequest
{
    public string PrefabName { get; set; } = "";
    public string BuildTarget { get; set; } = "";

    public List<PathToHash> Assets { get; set; } = new();
    public List<Package> Packages { get; set; } = new();
}
public class PathToHash
{
    public string Path { get; set; } = "";
    public string Hash { get; set; } = "";
}
public class Package
{
    public string PackageID { get; set; } = "";
    public List<PathToHash> Files { get; set; } = new();
}
public class BuildRequestResponse
{
    public string ResultCode { get; set; } = "";
    public string[] MissingFiles { get; set; } = [];


    public const string MissingAssets = "MissingAssets";
    public const string BuildRequestAccept = "BuildRequestAccept";
}


public class RunnerRequest
{
    public string ParentServerURL { get; set; } = "";
    public string TargetGUID { get; set; } = "";
}
