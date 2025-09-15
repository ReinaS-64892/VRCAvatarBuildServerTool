namespace net.rs64.VRCAvatarBuildServerTool.Server;

public class BuildRequest
{
    // Unity の GUID
    public List<string> BuildTargets { get; set; } = new();

    // .meta も packages も関係なくねじ込む想定
    public List<PathToHash> Assets { get; set; } = new();
}
public class PathToHash
{
    public string Path { get; set; } = "";
    public string Hash { get; set; } = "";
}

public class BuildRequestResponse
{
    public string ResultCode { get; set; } = "";
    public string[] MissingFiles { get; set; } = [];
}
