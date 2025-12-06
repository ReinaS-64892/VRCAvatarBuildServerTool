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

    async Task HttpServerLoop(ChannelWriter<BuildRequest> channel, CancellationToken token)
    {
        _httpServer.Start();
        try
        {
            while (token.IsCancellationRequested is false)
            {
                var ctxTask = _httpServer.GetContextAsync();
                ctxTask.Wait(token);
                if (ctxTask.IsCompleted is false) { break; }
                await Response(channel, await ctxTask);
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

    private async Task Response(ChannelWriter<BuildRequest> channel, HttpListenerContext ctx)
    {
        var req = ctx.Request;

        // 返す code はもう少し何とかするべきではあると思う
        if (req.Headers.Get("Authorization") != _config.ServerPasscodeHeader)
        {
            var path = req?.Url?.AbsolutePath ?? ""; // ここ ランナー が死活監視するために穴を開ける。
            if (path == "/Ping") { ctx.Response.StatusCode = 200; ctx.Response.Close(); return; }

            Console.WriteLine("Authorization failed"); ctx.Response.StatusCode = 400; ctx.Response.Close(); return;
        }
        if (req.Url is null) { Console.WriteLine("URI not found"); ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }
        if (req.HttpMethod != "POST") { Console.WriteLine("Unknown Request"); ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }
        if (req.InputStream is null) { Console.WriteLine("POST data is not found"); ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }

        switch (req.Url.AbsolutePath)
        {
            default: { Console.WriteLine("Unknown Request"); ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }
            case "/Build":
                {
                    var memStream = new MemoryStream((int)req.ContentLength64);
                    await req.InputStream.CopyToAsync(memStream);
                    var jsonString = System.Text.Encoding.UTF8.GetString(memStream.ToArray());

                    var result = BuildRequestRun(channel, jsonString);

                    switch (result)
                    {
                        default: { Console.WriteLine("Unknown Error"); ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }
                        case BuildRequestResult.CanNotReadJson:
                            { Console.WriteLine("CanNotReadJson"); ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }
                        case BuildRequestResult.BuildTargetNotFound:
                            { Console.WriteLine("BuildTargetNotFound"); ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }
                        case BuildRequestResult.MissingAssets missingAssets:
                            {
                                Console.WriteLine("MissingAssets");
                                var responseSource = new BuildRequestResponse() { ResultCode = BuildRequestResponse.MissingAssets, MissingFiles = missingAssets.MissingFiles };
                                var response = JsonSerializer.Serialize(responseSource);
                                var responseBytes = System.Text.Encoding.UTF8.GetBytes(response);

                                ctx.Response.OutputStream.Write(responseBytes);
                                ctx.Response.Close();
                                return;
                            }
                        case BuildRequestResult.BuildRequestAccept:
                            {
                                Console.WriteLine("BuildRequestAccept");
                                var responseSource = new BuildRequestResponse() { ResultCode = BuildRequestResponse.BuildRequestAccept, MissingFiles = Array.Empty<string>() };
                                var response = JsonSerializer.Serialize(responseSource);
                                var responseBytes = System.Text.Encoding.UTF8.GetBytes(response);

                                ctx.Response.OutputStream.Write(responseBytes);
                                ctx.Response.Close();
                                return;
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
                    return;
                }
        }
    }

    private BuildRequestResult BuildRequestRun(ChannelWriter<BuildRequest> channel, string jsonString)
    {
        var buildRequest = JsonSerializer.Deserialize<BuildRequest>(jsonString);

        if (buildRequest is null) { return new BuildRequestResult.CanNotReadJson(); }
        if (string.IsNullOrWhiteSpace(buildRequest.BuildTarget)) { return new BuildRequestResult.BuildTargetNotFound(); }

        var missing = buildRequest.Assets.Concat(buildRequest.Packages.SelectMany(p => p.Files)).Where(a => _cashManager.HasFile(a.Hash) is false).Select(a => { Console.WriteLine("not cash :" + a.Path + "-" + a.Hash); return a.Path; }).ToArray();
        if (missing.Length is not 0) { return new BuildRequestResult.MissingAssets(missing); }

        // 投げっぱなし、多分大丈夫！
        _ = Task.Run(() => channel.WriteAsync(buildRequest));
        return new BuildRequestResult.BuildRequestAccept();
    }
    public abstract record BuildRequestResult
    {
        public record CanNotReadJson : BuildRequestResult { }
        public record BuildTargetNotFound : BuildRequestResult { }

        public record MissingAssets : BuildRequestResult
        {
            public string[] MissingFiles;
            public MissingAssets(string[] missingFiles)
            {
                MissingFiles = missingFiles;
            }
        }
        public record BuildRequestAccept : BuildRequestResult { }
    }


}
