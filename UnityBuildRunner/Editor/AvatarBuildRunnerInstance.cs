#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mono.Cecil;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.Api;

namespace net.rs64.VRCAvatarBuildServerTool.BuildRunner
{
    internal class AvatarBuildRunnerInstance : IDisposable
    {
        SynchronizationContext _postCtx;
        BuildRunnerProjectConfig config;
        HttpListener _httpListener;

        CancellationTokenSource? _serverCancellationTokenSource;
        Task? _listenTask;

        public AvatarBuildRunnerInstance(SynchronizationContext post, BuildRunnerProjectConfig config)
        {
            _postCtx = post;
            this.config = config;
            _httpListener = new();
            _httpListener.Prefixes.Add($"http://127.0.0.1:{config.BuildRunnerServerPort}/");

            _serverCancellationTokenSource = new();
            _listenTask = Task.Run(Loop);
        }

        public void ServerListenEnable()
        {
            if (_listenTask is not null) { return; }
            _serverCancellationTokenSource = new();
            _listenTask = Task.Run(Loop);
        }
        public void ServerListenDisable()
        {
            _serverCancellationTokenSource?.Cancel();
            _listenTask = null;
        }
        async Task Loop()
        {
            _httpListener.Start();
            try
            {
                var cancellationToken = _serverCancellationTokenSource!.Token;
                while (cancellationToken.IsCancellationRequested is false)
                {
                    var ctxTask = _httpListener.GetContextAsync();
                    ctxTask.Wait(cancellationToken);

                    if (ctxTask.IsCompleted is false) { break; }

                    var ctx = await ctxTask;
                    var req = ctx.Request;

                    switch (req.HttpMethod)
                    {
                        default: { Debug.Log("Unknown Request"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
                        case "GET":
                            {
                                if (req.Url.AbsolutePath is "/Ping")
                                {
                                    Debug.Log("Ping Ok");
                                    ctx.Response.StatusCode = 200;
                                    ctx.Response.Close();
                                }
                                else
                                {
                                    Debug.Log("Unknown Request");
                                    ctx.Response.StatusCode = 400;
                                    ctx.Response.Close();
                                }
                                continue;
                            }
                        case "POST":
                            {
                                if (req.Url.AbsolutePath is not "/BuildRequest") { Debug.Log("Unknown Request"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
                                if (req.InputStream is null) { Debug.Log("POST data is not found"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }

                                var memStream = new MemoryStream((int)req.ContentLength64);
                                await req.InputStream.CopyToAsync(memStream);

                                ctx.Response.StatusCode = 200;
                                ctx.Response.Close();

                                _httpListener.Stop();
                                ServerListenDisable();

                                var request = JsonUtility.FromJson<BuildRequest>(Encoding.UTF8.GetString(memStream.ToArray()));
                                await DoBuildPrepare(request);

                                return;
                            }
                    }
                }
            }
            catch (Exception e)
            {
                if (e is not OperationCanceledException)
                    Debug.LogException(e);
            }
            finally
            {
                _httpListener.Stop();
            }
        }


        public void Dispose()
        {
            _serverCancellationTokenSource?.Cancel();
            _httpListener?.Close();
            _httpListener = null!;
            _listenTask = null!;
        }

        private async Task DoBuildPrepare(BuildRequest request)
        {
            var removePackage = request.Assets
                 .Select(a => a.Path)
                 .Where(p => p.StartsWith("Packages"))
                 .Select(p => p.Split("/")[1])
                 .Distinct();

            foreach (var p in removePackage)
            {
                var path = Path.Combine("Packages", p);
                if (Directory.Exists(path)) { Directory.Delete(path, true); }
            }

            foreach (var f in Directory.EnumerateFiles("Assets")) { File.Delete(f); }
            foreach (var f in Directory.EnumerateDirectories("Assets")) { Directory.Delete(f, true); }

            var getFileRequests = request.Assets
                .Select(async a =>
                    {
                        var bytes = await AvatarBuildRunner.GetFileFromInternalServer(a.Hash);

                        if (bytes is not null) await WriteFile(a.Path, bytes);
                        else Debug.LogWarning("なんか無いやつあるけど大丈夫かな : " + a.Path + " - " + a.Hash);
                    }
                ).ToArray();

            await Task.WhenAll(getFileRequests);

            _postCtx.Send(_ =>
            {
                SessionState.SetString(BuildTargetSessionKey, JsonUtility.ToJson(new BuildTarget() { BuildTargets = request.BuildTargets }));
                AssetDatabase.Refresh();
            }, null);

        }
        internal const string BuildTargetSessionKey = "net.rs64.vrc-avatar-build-server-tool.build-runner.build-targets";
        [Serializable]
        public class BuildTarget
        {
            public string[] BuildTargets = Array.Empty<string>();
        }

        private async Task WriteFile(string path, byte[] fileBytes)
        {
            var parentDirectory = new DirectoryInfo(path).Parent.FullName;
            try { if (Directory.Exists(parentDirectory) is false) Directory.CreateDirectory(parentDirectory); } catch (Exception e) { Debug.LogException(e); }

            await File.WriteAllBytesAsync(path, fileBytes);
        }
    }

}
