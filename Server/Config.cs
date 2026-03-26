namespace net.rs64.VRCAvatarBuildServerTool.Server;


public class Config
{
    public List<string> ListenAddress { get; set; } = new() { };
    public int MaxMultipleEditorCount { get; set; } = 1;
    public string UnityEditor { get; set; } = "";
    public string ServerPasscodeHeader { get; set; } = "";

    public string TemplateProject { get; set; } = "";
    public List<string> RetainPackageID { get; set; } = [];

    public List<string> LaunchArguments  { get; set;} = [];
}
