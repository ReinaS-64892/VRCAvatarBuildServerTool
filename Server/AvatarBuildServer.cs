using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Threading.Tasks;
using net.rs64.VRCAvatarBuildServerTool.Transfer;

namespace net.rs64.VRCAvatarBuildServerTool.Server;

public partial class AvatarBuildServer
{
    private string _instancePath;
    private Config _config;


    private HttpListener _httpServer;
    private CancellationTokenSource _cancellationTokenSource;


    private string _cashPath;
    private CacheFileManager _cashManager;

    // 渋い気持ち
    internal static string UnityBuildRunnerPath = "";

    public AvatarBuildServer(string instancePath)
    {
        this._instancePath = instancePath;
        using var configFile = File.OpenRead(Path.Combine(instancePath, "config.json"));
        this._config = JsonSerializer.Deserialize<Config>(configFile) ?? throw new Exception();
        _httpServer = new() { };
        _cancellationTokenSource = new();


        foreach (var uri in _config.ListenAddress)
        {
            Console.WriteLine("ListenAddress : " + uri);
            _httpServer.Prefixes.Add(uri);
        }
        _cashPath = Path.Combine(_instancePath, "Cash");
        _cashManager = new CacheFileManager(_cashPath);
    }
    public async Task Run()
    {
        var channel = Channel.CreateUnbounded<BuildRequest>();

        var buildWorkerManageTask = Task.Run(async () => await BuildWorkerManager(channel.Reader, _cancellationTokenSource.Token));
        var httpServerTask = Task.Run(async () => await HttpServerLoop(channel.Writer, _cancellationTokenSource.Token));
        while (true)
        {
            var k = Console.ReadKey();
            switch (k.KeyChar)
            {
                default: break;
                case 's':
                    {
                        StatusRequested = true;
                        break;
                    }
                case 'q':
                    {
                        _cancellationTokenSource.Cancel();
                        return;
                    }
            }
        }
    }
    static bool CopyDirectory(string source, string destination)
    {
        var dir = new DirectoryInfo(source);
        if (dir.Exists is false) return false;

        Directory.CreateDirectory(destination);

        var dirs = dir.GetDirectories();
        foreach (var file in dir.GetFiles())
            try { file.CopyTo(Path.Combine(destination, file.Name)); }
            catch
            {
                // よくわからんので握りつぶします。
            }
        foreach (var subDir in dirs)
            CopyDirectory(subDir.FullName, Path.Combine(destination, subDir.Name));

        return true;
    }
}
