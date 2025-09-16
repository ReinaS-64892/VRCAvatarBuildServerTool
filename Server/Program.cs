using System.Text.Json;
using net.rs64.VRCAvatarBuildServerTool.Server;

var unityAssetBundleUploaderPackage = Path.GetFullPath("../UnityAssetBundleUploader");
var unityBuildRunnerPackage = Path.GetFullPath("../UnityBuildRunner");
if (args.Length != 1)
{
    // throw new Exception("invalid argument");
    // Reina_Sakiria の環境を前提とし
    Directory.SetCurrentDirectory("/home/Reina/Rs/Program/VRCAvatarBuildServerTool/ServerInstance");
    // Debug 用にキャッシュを破壊
    var cashDir = "/home/Reina/Rs/Program/VRCAvatarBuildServerTool/ServerInstance/Cash";
    if (Directory.Exists(cashDir)) Directory.Delete(cashDir, true);
    var buildRunnersDir = "/home/Reina/Rs/Program/VRCAvatarBuildServerTool/ServerInstance/BuildRunners";
    if (Directory.Exists(buildRunnersDir)) Directory.Delete(buildRunnersDir, true);
}
else { Directory.SetCurrentDirectory(args[0]); }

var currentDir = Directory.GetCurrentDirectory();
Console.WriteLine("ServerInstance directory : " + currentDir);


var configPath = Path.Combine(currentDir, "config.json");
if (File.Exists(configPath) is false) { throw new Exception("config.json is not found!"); }
var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath));
if (config is null) { throw new Exception("failed to load config!"); }

var abs = new AvatarBuildServer(currentDir, config);
await abs.LaunchEditors(unityAssetBundleUploaderPackage, unityBuildRunnerPackage);
await abs.Run();
