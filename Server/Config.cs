namespace net.rs64.VRCAvatarBuildServerTool.Server;


public class Config
{
    public List<string> ListenAddress { get; set; } = new() { };
    public uint MaxMultipleEditorCount { get; set; } = 2;
    public uint BuildRunnerPortStart { get; set; } = 10000;
    public string UnityEditor { get; set; } = "";
    public string UploaderURL { get; set; } = "";
    public string ServerPasscodeHeader { get; set; } = "";
}
