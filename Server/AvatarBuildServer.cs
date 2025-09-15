using System.Net;
using System.Net.Http.Json;
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

    public AvatarBuildServer(string instancePath, Config config)
    {
        this._instancePath = instancePath;
        this._config = config;
        _httpServer = new HttpListener();
        _cancellationTokenSource = new CancellationTokenSource();


        foreach (var uri in _config.ListenAddress)
        {
            Console.WriteLine("ListenAddress : " + uri);
            _httpServer.Prefixes.Add(uri);
        }
        _cashPath = Path.Combine(_instancePath, "Cash");
        _cashManager = new CashFileManager(_cashPath);
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
                                        var responseSource = new BuildRequestResponse() { ResultCode = "MissingAssets", MissingFiles = missingAssets.MissingFiles };
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
                                        var responseSource = new BuildRequestResponse() { ResultCode = "BuildRequestAccept", MissingFiles = [] };
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

        // TODO : post to Build Worker Manager

        return new BuildRequestResult.BuildRequestAccept();
    }
    public abstract record BuildRequestResult
    {
        public record CanNotReadJson : BuildRequestResult { }
        public record BuildTargetNotFound : BuildRequestResult { }

        public record MissingAssets(string[] MissingFiles) : BuildRequestResult { }
        public record BuildRequestAccept : BuildRequestResult { }
    }
}

