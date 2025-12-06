using net.rs64.VRCAvatarBuildServerTool.Server;

Console.WriteLine("VRCAvatarBuildServer run !");
var VABSToolDirectory = Path.Combine(Directory.GetCurrentDirectory(), "../");
var instancePath = Path.Combine(VABSToolDirectory, "ServerInstance");
AvatarBuildServer.UnityBuildRunnerPath = Path.Combine(VABSToolDirectory, "UnityBuildRunner");
var abs = new AvatarBuildServer(instancePath);
await abs.Run();
