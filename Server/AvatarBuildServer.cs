using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using net.rs64.VRCAvatarBuildServerTool.Transfer;
using UnityEditor;
using UnityEngine;
using VRC.Core;
using VRC.SDK3A.Editor;
using VRC.SDKBase.Editor.Api;

namespace net.rs64.VRCAvatarBuildServerTool.Server
{
    public static class AvatarBuildServer
    {
        static BuildServerInstance _serverInstance;
        public static bool IsServerStarted => _serverInstance != null;
        public static void ServerStart()
        {
            if (_serverInstance != null) { Debug.Log("Server is already started"); return; }
            var mainThreadContext = SynchronizationContext.Current;

            EditorApplication.update += BuilderLoop;

            _serverInstance = new(AvatarBuildServerConfiguration.instance, mainThreadContext);
        }
        public static void ServerExit()
        {
            if (_serverInstance == null) { Debug.Log("Server is not started"); return; }
            EditorApplication.update -= BuilderLoop;
            _serverInstance.Dispose(); _serverInstance = null;
        }
        class BuildServerInstance : IDisposable
        {
            string _address;
            SynchronizationContext _postCtx;

            HttpListener _httpServer;
            private string _passCode;
            private List<string> _presave;
            CancellationTokenSource _serverCancellationTokenSource;

            Task _listenTask;
            internal CacheFileManager _cashManager;

            public BuildServerInstance(AvatarBuildServerConfiguration config, SynchronizationContext post)
            {
                _address = config.BuildServerListenAddress;
                _passCode = config.ServerPasscode;
                _presave = config.PresavePackageFolderName;

                _postCtx = post;
                _httpServer = new();
                _httpServer.Prefixes.Add(_address);

                _serverCancellationTokenSource = new();

                _listenTask = Task.Run(() => Loop(_serverCancellationTokenSource.Token));
                _cashManager = new CacheFileManager(Path.GetFullPath("Library/VRCAvatarBuildServerCash"));
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
                        await Response(await ctxTask);
                    }
                }
                catch (Exception e)
                {
                    if (e is not OperationCanceledException)
                        Debug.Log(e);
                }
                finally
                {
                    _httpServer.Stop();
                }
            }

            private async Task Response(HttpListenerContext ctx)
            {
                var req = ctx.Request;

                // 返す code はもう少し何とかするべきではあると思う
                if (req.Headers.Get("Authorization") != _passCode) { Debug.Log("Authorization failed"); ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }
                if (req.Url is null) { Debug.Log("URI not found"); ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }
                if (req.HttpMethod != "POST") { Debug.Log("Unknown Request"); ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }
                if (req.InputStream is null) { Debug.Log("POST data is not found"); ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }

                switch (req.Url.AbsolutePath)
                {
                    default: { Debug.Log("Unknown Request"); ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }
                    case "/Build":
                        {
                            var memStream = new MemoryStream((int)req.ContentLength64);
                            await req.InputStream.CopyToAsync(memStream);
                            var jsonString = System.Text.Encoding.UTF8.GetString(memStream.ToArray());

                            var result = BuildRequestRun(jsonString);

                            switch (result)
                            {
                                default: { Debug.Log("Unknown Error"); ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }
                                case BuildRequestResult.CanNotReadJson:
                                    { Debug.Log("CanNotReadJson"); ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }
                                case BuildRequestResult.BuildTargetNotFound:
                                    { Debug.Log("BuildTargetNotFound"); ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }
                                case BuildRequestResult.MissingAssets missingAssets:
                                    {
                                        Debug.Log("MissingAssets");
                                        var responseSource = new BuildRequestResponse() { ResultCode = BuildRequestResponse.MissingAssets, MissingFiles = missingAssets.MissingFiles };
                                        var response = JsonUtility.ToJson(responseSource);
                                        var responseBytes = System.Text.Encoding.UTF8.GetBytes(response);

                                        ctx.Response.OutputStream.Write(responseBytes);
                                        ctx.Response.Close();
                                        return;
                                    }
                                case BuildRequestResult.BuildRequestAccept:
                                    {
                                        Debug.Log("BuildRequestAccept");
                                        var responseSource = new BuildRequestResponse() { ResultCode = BuildRequestResponse.BuildRequestAccept, MissingFiles = Array.Empty<string>() };
                                        var response = JsonUtility.ToJson(responseSource);
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
                    case "/PackageClear":
                        {
                            ctx.Response.StatusCode = 200;
                            ctx.Response.Close();

                            foreach (var p in Directory.EnumerateDirectories("Packages").Select(p => Path.GetFileName(p)).Where(p => _presave.Contains(p)))
                                Directory.Delete(Path.Combine("Packages", p), true);

                            return;
                        }
                    case "/AddPackage":
                        {
                            var memStream = new MemoryStream((int)req.ContentLength64);
                            await req.InputStream.CopyToAsync(memStream);

                            using var zip = new ZipArchive(memStream, ZipArchiveMode.Read);
                            if (zip.Entries.Any() is false) { return; }

                            var e = zip.Entries.First();
                            if (_presave.Any(p => e.FullName.StartsWith(p))) { return; }

                            zip.ExtractToDirectory("Packages", true);

                            return;
                        }

                }
            }

            private BuildRequestResult BuildRequestRun(string jsonString)
            {
                var buildRequest = JsonUtility.FromJson<BuildRequest>(jsonString);

                if (buildRequest is null) { return new BuildRequestResult.CanNotReadJson(); }
                if (buildRequest.BuildTargets.Any() is false) { return new BuildRequestResult.BuildTargetNotFound(); }

                var missing = buildRequest.Assets.Where(a => _cashManager.HasFile(a.Hash) is false).Select(a => { Debug.Log("not cash :" + a.Path + "-" + a.Hash); return a.Path; }).ToArray();
                if (missing.Length is not 0) { return new BuildRequestResult.MissingAssets(missing); }

                EnQueue(buildRequest);
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

            public void Dispose()
            {
                _serverCancellationTokenSource.Cancel();
                _httpServer?.Close();
                _httpServer = null;
                _listenTask = null;
            }
        }

        static volatile bool s_isTaskDoing;//しぶわいねぇ ... うーん
        static Queue<BuildRequest> buildTaskQueue = new();
        const string TemporaryThumbnailGUID = "bf34225cf0fe6be64b94e281fb3a55ce";
        public static void EnQueue(BuildRequest req)
        {
            lock (buildTaskQueue)
            {
                buildTaskQueue.Enqueue(req);
            }
        }
        static void BuilderLoop()
        {
            if (s_isTaskDoing) { return; }
            BuildRequest task;
            lock (buildTaskQueue) { buildTaskQueue.TryDequeue(out task); }

            if (task is not null) CallAsyncVoid(task);

            static async void CallAsyncVoid(BuildRequest bytes)
            {
                try
                {
                    s_isTaskDoing = true;
                    var sdk = default(IVRCSdkAvatarBuilderApi);

                    if (VRCSdkControlPanel.window == null) { EditorApplication.ExecuteMenuItem("VRChat SDK/Show Control Panel"); }
                    while (VRCSdkControlPanel.TryGetBuilder(out sdk) is false)
                    { await Task.Delay(100); }
                    if (await TryLogin() is false) { Debug.LogError("何らかの原因でログインできなかったよ〜！"); return; }

                    await UploadFromTransferred(sdk, bytes);
                }
                catch (Exception e) { Debug.LogException(e); }
                finally
                {
                    s_isTaskDoing = false;
                }
            }
        }

        static async Task UploadFromTransferred(IVRCSdkAvatarBuilderApi sdk, BuildRequest bytes)
        {

            var destPath = "Assets/ZZZ___TTransferredAssets";
            if (Directory.Exists(destPath)) { Directory.Delete(destPath, true); }
            Directory.CreateDirectory(destPath);

            try
            {
                await LoadFiles(destPath, bytes.Assets);
                AssetDatabase.ImportAsset(destPath, ImportAssetOptions.ImportRecursive);

                var count = bytes.BuildTargets.Count;
                var i = 0;
                EditorUtility.DisplayProgressBar("upload avatar(s)", "", 0.0f);

                foreach (var guid in bytes.BuildTargets)
                {
                    await BuildToUploadFromGUID(sdk, guid);

                    i += 1;
                    EditorUtility.DisplayProgressBar("upload avatar(s)", guid, i / (float)count);
                }
            }
            finally
            {
                Directory.Delete(destPath, true);
                File.Delete(destPath + ".meta");

                EditorUtility.ClearProgressBar();
            }
        }

        private static async Task LoadFiles(string destPath, List<PathToHash> assets)
        {
            await Task.WhenAll(assets.Where(a =>
                            {
                                if (_serverInstance._cashManager.HasFile(a.Hash) is false)
                                {
                                    Debug.LogWarning("not fount " + a.Hash + ":" + a.Path); return false;
                                }
                                return true;
                            }
                        ).Select(async a =>
                          await WriteFile(Path.Combine(destPath, a.Path), await _serverInstance._cashManager.GetFile(a.Hash))
                        )
            );
        }
        private static async Task WriteFile(string path, byte[] fileBytes)
        {
            var parentDirectory = new DirectoryInfo(path).Parent!.FullName;
            try { if (Directory.Exists(parentDirectory) is false) Directory.CreateDirectory(parentDirectory); }
            catch (Exception e) { Console.WriteLine(e); }

            Console.WriteLine("write ! : " + path);
            await File.WriteAllBytesAsync(path, fileBytes);
        }

        private static async Task BuildToUploadFromGUID(IVRCSdkAvatarBuilderApi sdk, string guid)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            var pipelineManager = prefab.GetComponent<PipelineManager>();

            var isNewAvatar = string.IsNullOrEmpty(pipelineManager.blueprintId);

            VRCAvatar vrcAvatar;
            if (isNewAvatar)
            {
                vrcAvatar = new()
                {
                    Name = prefab.name,
                    Description = "",
                    Tags = new List<string>(),
                    ReleaseStatus = "private"
                };
                vrcAvatar = await VRCApi.CreateAvatarRecord(vrcAvatar);
                if (string.IsNullOrEmpty(vrcAvatar.ID)) throw new Exception("Failed to reserve an avatar ID");
                pipelineManager.blueprintId = vrcAvatar.ID;
                EditorUtility.SetDirty(pipelineManager);
                PrefabUtility.SavePrefabAsset(prefab);
            }
            else vrcAvatar = await VRCApi.GetAvatar(pipelineManager.blueprintId);
            var thumbnailPath = isNewAvatar ? AssetDatabase.GUIDToAssetPath(TemporaryThumbnailGUID) : null;

            await sdk.BuildAndUpload(prefab, vrcAvatar, thumbnailPath);
            Debug.Log("upload:" + prefab.name);
        }

        // MIT LICENSE https://github.com/anatawa12/ContinuousAvatarUploader/blob/d6b8fd82fac6c4734664d57914d02a8092cb5dc7/LICENSE
        // Copyright (c) 2023 anatawa12
        // copy from CAU Uploader https://github.com/anatawa12/ContinuousAvatarUploader/blob/d6b8fd82fac6c4734664d57914d02a8092cb5dc7/Editor/Uploader.cs#L89-L114
        public static async Task<bool> TryLogin()
        {
            if (!ConfigManager.RemoteConfig.IsInitialized())
            {
                API.SetOnlineMode(true);
                ConfigManager.RemoteConfig.Init();
            }
            if (!APIUser.IsLoggedIn && ApiCredentials.Load())
            {
                var task = new TaskCompletionSource<bool>();
                APIUser.InitialFetchCurrentUser(c =>
                {
                    AnalyticsSDK.LoggedInUserChanged(c.Model as APIUser);
                    task.TrySetResult(true);
                }, e =>
                {
                    task.TrySetException(new Exception(e?.Error ?? "Unknown error"));
                });
                await task.Task;
            }
            return APIUser.IsLoggedIn;
        }
    }
}
