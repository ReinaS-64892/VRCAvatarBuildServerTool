using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
            var address = AvatarBuildServerConfiguration.instance.BuildServerListenAddress;
            var mainThreadContext = SynchronizationContext.Current;

            EditorApplication.update += BuilderLoop;

            _serverInstance = new(address, mainThreadContext);
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

            HttpListener _httpListener;

            CancellationTokenSource _serverCancellationTokenSource;

            Task _listenTask;

            public BuildServerInstance(string address, SynchronizationContext post)
            {
                _address = address;
                _postCtx = post;
                _httpListener = new();
                _httpListener.Prefixes.Add(address);

                _serverCancellationTokenSource = new();

                _listenTask = Task.Run(Loop);
            }

            async void Loop()
            {
                _httpListener.Start();
                try
                {
                    var cancellationToken = _serverCancellationTokenSource.Token;
                    while (cancellationToken.IsCancellationRequested is false)
                    {
                        var ctxTask = _httpListener.GetContextAsync();
                        ctxTask.Wait(cancellationToken);

                        if (ctxTask.IsCompleted is false) { return; }

                        var ctx = await ctxTask;
                        var req = ctx.Request;
                        if (req.HttpMethod != "POST") { Debug.Log("Unknown Request"); ctx.Response.StatusCode = 400; ctx.Response.Close(); }
                        if (req.InputStream is null) { Debug.Log("POST data is not found"); ctx.Response.StatusCode = 400; ctx.Response.Close(); }

                        var memStream = new MemoryStream((int)req.ContentLength64);
                        await req.InputStream.CopyToAsync(memStream);

                        ctx.Response.StatusCode = 200;
                        ctx.Response.Close();

                        _postCtx.Post(byteArray => EnQueue(byteArray as byte[]), memStream.ToArray());
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
                _serverCancellationTokenSource.Cancel();
                _httpListener?.Close();
                _httpListener = null;
                _listenTask = null;
            }
        }

        static volatile bool s_isTaskDoing;//しぶわいねぇ ... うーん
        static Queue<byte[]> buildTaskQueue = new();
        const string TemporaryThumbnailGUID = "bf34225cf0fe6be64b94e281fb3a55ce";
        public static void EnQueue(byte[] bytes)
        {
            lock (buildTaskQueue)
            {
                buildTaskQueue.Enqueue(bytes);
            }
        }
        static void BuilderLoop()
        {
            if (s_isTaskDoing) { return; }
            byte[] task;
            lock (buildTaskQueue) { buildTaskQueue.TryDequeue(out task); }

            if (task is not null) CallAsyncVoid(task);

            static async void CallAsyncVoid(byte[] bytes)
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

        static async Task UploadFromTransferred(IVRCSdkAvatarBuilderApi sdk, byte[] bytes)
        {
            var (zipMemStream, guids) = AssetTransferProtocol.DecodeAssetsAndTargetGUID(bytes);
            var zip = new ZipArchive(zipMemStream, ZipArchiveMode.Read);

            var destPath = "Assets/ZZZ___TTransferredAssets";
            if (Directory.Exists(destPath)) { Directory.Delete(destPath, true); }
            Directory.CreateDirectory(destPath);

            try
            {
                zip.ExtractToDirectory(destPath);
                AssetDatabase.ImportAsset(destPath, ImportAssetOptions.ImportRecursive);

                var count = guids.Count;
                var i = 0;
                EditorUtility.DisplayProgressBar("upload avatar(s)", "", 0.0f);

                foreach (var guid in guids)
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
