using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace net.rs64.VRCAvatarBuildServerTool.Server;

public class AvatarBuildServer
{
    private string _instancePath;
    private Config _config;


    private HttpListener _httpServer;
    private CancellationTokenSource _cancellationTokenSource;


    private string _cashPath;
    private CashFileManager _cashManager;


    private HttpListener _httpInternalServer;
    Task? _internalServerLoop;
    private HttpClient _httpInternalClient;
    private string _internalServerAuthorizationCode = "";

    Process? UploaderProcess;
    List<Process> BuildRunnerProcess = new();

    public AvatarBuildServer(string instancePath, Config config)
    {
        this._instancePath = instancePath;
        this._config = config;
        _httpServer = new() { };
        _httpInternalServer = new();
        _httpInternalClient = new();
        _cancellationTokenSource = new();


        foreach (var uri in _config.ListenAddress)
        {
            Console.WriteLine("ListenAddress : " + uri);
            _httpServer.Prefixes.Add(uri);
        }
        _cashPath = Path.Combine(_instancePath, "Cash");
        _cashManager = new CashFileManager(_cashPath);
    }
    internal async Task LaunchEditors(string unityAssetBundleUploaderPackage, string unityBuildRunnerPackage)
    {
        _internalServerAuthorizationCode = string.Join("", Enumerable.Range(0, 4).Select(i => Random.Shared.NextInt64().ToString()));
        var currentPort = _config.InternalServerPortStart;
        var internalServerPort = $"http://127.0.0.1:{currentPort}/";
        _httpInternalServer.Prefixes.Clear();
        _httpInternalServer.Prefixes.Add(internalServerPort);
        currentPort += 1;

        var uploaderProjectPath = Path.Combine(_instancePath, "UploadServer");
        var uploaderProjectConfig = new UploaderProjectConfig()
        {
            UploaderServerPort = currentPort.ToString(),
            InternalServerURL = internalServerPort,
            AuthorizationCode = _internalServerAuthorizationCode
        };
        currentPort += 1;
        var uploaderProjectConfigPath = Path.Combine(uploaderProjectPath, "UploaderConfig.json");
        File.WriteAllText(uploaderProjectConfigPath, JsonSerializer.Serialize(uploaderProjectConfig));
        var uploaderPackagePath = Path.Combine(uploaderProjectPath, "Packages", new DirectoryInfo(unityAssetBundleUploaderPackage).Name);
        if (Directory.Exists(uploaderPackagePath)) { Directory.Delete(uploaderPackagePath, true); }
        CopyDirectory(unityAssetBundleUploaderPackage, uploaderPackagePath);
        var uploaderTemp = Path.Combine(uploaderProjectPath, "Temp");
        if (Directory.Exists(uploaderTemp)) { Directory.Delete(uploaderTemp, true); }

        UploaderProcess = Process.Start(new ProcessStartInfo(_config.UnityEditor, "--projectpath " + uploaderProjectPath));
        if (UploaderProcess is null) { throw new Exception("Unity Editor ほんとにあるのこれ ... ? なんか起動できてないよ ... ?"); }



        var buildRunnerTemplateDirectory = Path.Combine(_instancePath, "BuildServerTemplate");
        var relativeBuildRunnerPackage = Path.Combine("Packages", new DirectoryInfo(unityBuildRunnerPackage).Name);

        var buildRunnersDirectory = Path.Combine(_instancePath, "BuildRunners");
        if (Directory.Exists(buildRunnersDirectory) is false) Directory.CreateDirectory(buildRunnersDirectory);
        for (var i = 0; _config.MaxMultipleEditorCount > i; i += 1)
        {
            var buildRunnerDirectory = Path.Combine(buildRunnersDirectory, i.ToString());
            if (Directory.Exists(buildRunnerDirectory) is false) { CopyDirectory(buildRunnerTemplateDirectory, buildRunnerDirectory); }
            var buildRunnerTemp = Path.Combine(buildRunnerDirectory, "Temp");
            if (Directory.Exists(buildRunnerTemp)) { Directory.Delete(buildRunnerTemp, true); }

            var buildRunnerPackage = Path.Combine(buildRunnerDirectory, relativeBuildRunnerPackage);
            if (Directory.Exists(buildRunnerPackage)) { Directory.Delete(buildRunnerPackage, true); }
            CopyDirectory(unityBuildRunnerPackage, buildRunnerPackage);

            var config = new BuildRunnerProjectConfig()
            {
                BuildRunnerServerPort = currentPort.ToString(),
                InternalServerURL = internalServerPort,
                AuthorizationCode = _internalServerAuthorizationCode
            };
            var buildRunnerProjectConfigPath = Path.Combine(buildRunnerDirectory, "BuildRunnerConfig.json");
            if (File.Exists(buildRunnerProjectConfigPath)) { File.Delete(buildRunnerProjectConfigPath); }
            File.WriteAllText(buildRunnerProjectConfigPath, JsonSerializer.Serialize(config));
            currentPort += 1;

            var runnerStartInfo = new ProcessStartInfo(_config.UnityEditor, "--projectpath " + buildRunnerDirectory);
            var runner = Process.Start(runnerStartInfo);
            if (runner is null) { throw new Exception(currentPort + ":Unity Editor ほんとにあるのこれ ... ? なんか起動できてないよ ... ?"); }
            BuildRunnerProcess.Add(runner);
        }

        _internalServerLoop = Task.Run(InternalServerRun);
        await Task.Delay(10);
    }
    async Task InternalServerRun()
    {
        var token = _cancellationTokenSource.Token;
        _httpInternalServer.Start();
        Console.WriteLine("internal server run!");
        try
        {
            while (token.IsCancellationRequested is false)
            {
                var ctxTask = _httpInternalServer.GetContextAsync();
                ctxTask.Wait(token);
                if (ctxTask.IsCompleted is false) { break; }

                var ctx = await ctxTask;
                var req = ctx.Request;

                if (req.Headers.Get("Authorization") != _internalServerAuthorizationCode) { Console.WriteLine("Authorization failed"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
                if (req.Url is null) { Console.WriteLine("URI not found"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }

                // Console.WriteLine(req.Url.AbsolutePath);
                switch (req.HttpMethod)
                {
                    default: { Console.WriteLine("Unknown Request"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
                    case "GET":
                        {
                            switch (req.Url.AbsolutePath)
                            {
                                default: { Console.WriteLine("Unknown Request"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
                                case "/Ping":
                                    {
                                        // Console.WriteLine("ping from: " + ctx.Request.RemoteEndPoint.ToString());
                                        ctx.Response.StatusCode = 200;
                                        ctx.Response.Close();
                                        continue;
                                    }
                                case "/File":
                                    {
                                        var quayFileHash = ctx.Request.QueryString.Get("FileHash");
                                        Console.WriteLine("Get File Request : " + quayFileHash);
                                        if (string.IsNullOrWhiteSpace(quayFileHash) is false && _cashManager.HasFile(quayFileHash))
                                        {
                                            var file = File.ReadAllBytes(_cashManager.GetFile(quayFileHash));
                                            ctx.Response.OutputStream.Write(file);
                                            ctx.Response.StatusCode = 200;
                                        }
                                        else { ctx.Response.StatusCode = 400; }
                                        ctx.Response.Close();
                                        continue;
                                    }
                                case "/GetBlueprintID":
                                    {
                                        Console.WriteLine("Get new blueprintID Request");
                                        var avatarName = ctx.Request.QueryString.Get("AvatarName");
                                        if (string.IsNullOrWhiteSpace(avatarName) is false)
                                        {
                                            var newID = await GetNewBlueprintID(avatarName);
                                            if (newID is not null)
                                            {
                                                ctx.Response.OutputStream.Write(Encoding.UTF8.GetBytes(newID));
                                                ctx.Response.StatusCode = 200;
                                            }
                                            else { ctx.Response.StatusCode = 400; }
                                        }
                                        else { ctx.Response.StatusCode = 400; }
                                        ctx.Response.Close();
                                        continue;
                                    }
                            }
                        }
                    case "POST":
                        {
                            switch (req.Url.AbsolutePath)
                            {
                                default: { Console.WriteLine("Unknown Request"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
                                case "/Upload":
                                    {
                                        Console.WriteLine("receive upload request");
                                        var memStream = new MemoryStream((int)req.ContentLength64);
                                        await req.InputStream.CopyToAsync(memStream);
                                        var jsonString = System.Text.Encoding.UTF8.GetString(memStream.ToArray());
                                        var uploadReq = JsonSerializer.Deserialize<UploadRequest>(jsonString);
                                        if (uploadReq is null) { Console.WriteLine("json parse error?"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }

                                        await PostUploadRequest(uploadReq);

                                        ctx.Response.StatusCode = 200;
                                        ctx.Response.Close();
                                        continue;
                                    }
                            }
                        }
                }


            }
        }
        catch (Exception e)
        {
            if (e is not OperationCanceledException)
                Console.WriteLine(e);
        }
        finally
        {
            _httpInternalServer.Stop();
        }
    }
    async Task<string?> GetNewBlueprintID(string avatarName)
    {
        var response = await _httpInternalClient.GetAsync($"http://127.0.0.1:{_config.InternalServerPortStart + 1}/?AvatarName={Uri.EscapeDataString(avatarName)}");
        if (response.IsSuccessStatusCode is false) { return null; }
        return Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());
    }
    async Task PostUploadRequest(UploadRequest uploadRequest)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(uploadRequest)));
        await _httpInternalClient.PostAsync($"http://127.0.0.1:{_config.InternalServerPortStart + 1}/", content);
    }
    public async Task Run()
    {
        await Loop(_cancellationTokenSource.Token);
    }

    async Task Loop(CancellationToken token)
    {
        _httpServer.Start();
        try
        {
            while (token.IsCancellationRequested is false)
            {
                var ctxTask = _httpServer.GetContextAsync();
                ctxTask.Wait(token);
                if (ctxTask.IsCompleted is false) { break; }

                var ctx = await ctxTask;
                var req = ctx.Request;

                // 返す code はもう少し何とかするべきではあると思う
                if (req.Headers.Get("Authorization") != _config.ServerPasscodeHeader) { Console.WriteLine("Authorization failed"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
                if (req.Url is null) { Console.WriteLine("URI not found"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
                if (req.HttpMethod != "POST") { Console.WriteLine("Unknown Request"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
                if (req.InputStream is null) { Console.WriteLine("POST data is not found"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }

                switch (req.Url.AbsolutePath)
                {
                    default: { Console.WriteLine("Unknown Request"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
                    case "/Build":
                        {
                            var memStream = new MemoryStream((int)req.ContentLength64);
                            await req.InputStream.CopyToAsync(memStream);
                            var jsonString = System.Text.Encoding.UTF8.GetString(memStream.ToArray());

                            var result = BuildRequestRun(jsonString);

                            switch (result)
                            {
                                default: { Console.WriteLine("Unknown Error"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
                                case BuildRequestResult.CanNotReadJson:
                                    { Console.WriteLine("CanNotReadJson"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
                                case BuildRequestResult.BuildTargetNotFound:
                                    { Console.WriteLine("BuildTargetNotFound"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
                                case BuildRequestResult.MissingAssets missingAssets:
                                    {
                                        Console.WriteLine("MissingAssets");
                                        var responseSource = new BuildRequestResponse() { ResultCode = BuildRequestResponse.MissingAssets, MissingFiles = missingAssets.MissingFiles };
                                        var response = JsonSerializer.Serialize(responseSource);
                                        var responseBytes = System.Text.Encoding.UTF8.GetBytes(response);

                                        ctx.Response.OutputStream.Write(responseBytes);
                                        // ctx.Response.StatusCode = 400; // これを書き込んでも強制的に 200 にさせられる様子 ... は?
                                        ctx.Response.StatusCode = 200;
                                        ctx.Response.Close();
                                        continue;
                                    }
                                case BuildRequestResult.BuildRequestAccept:
                                    {
                                        Console.WriteLine("BuildRequestAccept");
                                        var responseSource = new BuildRequestResponse() { ResultCode = BuildRequestResponse.BuildRequestAccept, MissingFiles = [] };
                                        var response = JsonSerializer.Serialize(responseSource);
                                        var responseBytes = System.Text.Encoding.UTF8.GetBytes(response);

                                        ctx.Response.OutputStream.Write(responseBytes);
                                        ctx.Response.StatusCode = 200;
                                        ctx.Response.Close();
                                        continue;
                                    }
                            }
                        }
                    case "/File":
                        {
                            var memStream = new MemoryStream((int)req.ContentLength64);
                            await req.InputStream.CopyToAsync(memStream);

                            _cashManager.AddFile(memStream.ToArray());

                            ctx.Response.StatusCode = 200;
                            ctx.Response.Close();
                            continue;
                        }

                }
            }
        }
        catch (Exception e)
        {
            if (e is not OperationCanceledException)
                Console.WriteLine(e);
        }
        finally
        {
            _httpServer.Stop();
        }
    }

    private BuildRequestResult BuildRequestRun(string jsonString)
    {
        var buildRequest = JsonSerializer.Deserialize<BuildRequest>(jsonString);

        if (buildRequest is null) { return new BuildRequestResult.CanNotReadJson(); }
        if (buildRequest.BuildTargets.Any() is false) { return new BuildRequestResult.BuildTargetNotFound(); }

        var missing = buildRequest.Assets.Where(a => _cashManager.HasFile(a.Hash) is false).Select(a => { Console.WriteLine(a.Path + "-" + a.Hash); return a.Path; }).ToArray();
        if (missing.Length is not 0) { return new BuildRequestResult.MissingAssets(missing); }

        // Task.Run(() => PostRunners(buildRequest));
        PostRunnersDoAsyncVoid(buildRequest);

        return new BuildRequestResult.BuildRequestAccept();
    }

    private async void PostRunnersDoAsyncVoid(BuildRequest buildRequest)
    {
        await PostRunners(buildRequest);
    }
    private async Task PostRunners(BuildRequest buildRequest)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(buildRequest)));
        // ... ここでキューを作る必要があるが ... 一旦後
        var protMax = _config.InternalServerPortStart + 2 + _config.MaxMultipleEditorCount;
        for (var i = _config.InternalServerPortStart + 2; protMax > i; i += 1)
        {
            var runnerURL = $"http://127.0.0.1:{i}/";
            try
            {
                var response = await _httpInternalClient.GetAsync(runnerURL + "Ping");
                if (response.IsSuccessStatusCode is false) { Console.WriteLine(runnerURL + "  is busy or dead"); continue; }
            }
            catch { Console.WriteLine(runnerURL + "is busy or dead"); continue; }
            var postRes = await _httpInternalClient.PostAsync(runnerURL + "BuildRequest", content);
            if (postRes.IsSuccessStatusCode) { Console.WriteLine("post sucrose : " + runnerURL); }
            else { Console.WriteLine("Unknown Error"); }
            return;
        }
    }

    public abstract record BuildRequestResult
    {
        public record CanNotReadJson : BuildRequestResult { }
        public record BuildTargetNotFound : BuildRequestResult { }

        public record MissingAssets(string[] MissingFiles) : BuildRequestResult { }
        public record BuildRequestAccept : BuildRequestResult { }
    }


    static bool CopyDirectory(string source, string destination)
    {
        var dir = new DirectoryInfo(source);
        if (dir.Exists is false) return false;

        Directory.CreateDirectory(destination);

        var dirs = dir.GetDirectories();
        foreach (var file in dir.GetFiles())
            file.CopyTo(Path.Combine(destination, file.Name));
        foreach (var subDir in dirs)
            CopyDirectory(subDir.FullName, Path.Combine(destination, subDir.Name));

        return true;
    }
}

