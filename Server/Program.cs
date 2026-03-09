using net.rs64.VRCAvatarBuildServerTool.Server;


string instancePath;
var configFileName = "config.json";

if (File.Exists(configFileName))
{
    // current directory
    Console.WriteLine("use current directory. in config  ./config.json");
    instancePath = Path.GetFullPath(".");
}
else if (File.Exists("Server.csproj") && File.Exists("../ServerInstance/config.json"))
{
    Console.WriteLine("use project directory found config in ../ServerInstance/config.json");
    instancePath = Path.GetFullPath("../ServerInstance");
}
else
{
    Console.WriteLine("config.json not found");
    Console.WriteLine("dotnet run in VRCAvatarBuildServerTool/Server and make VRCAvatarBuildServerTool/ServerInstance and VRCAvatarBuildServerTool/ServerInstance/config.json");
    Console.WriteLine("or create empty directory and then make config.json");
    return;
}

Console.WriteLine("---");
Console.WriteLine("instance path : " + instancePath);
Console.WriteLine("config path   : " + Path.Combine(instancePath, configFileName));
Console.WriteLine("---");

AvatarBuildServer.UnityBuildRunnerPath = Path.Combine(AppContext.BaseDirectory, "UnityBuildRunner");

Console.WriteLine("VRCAvatarBuildServer run !");
var abs = new AvatarBuildServer(instancePath);
await abs.Run();
