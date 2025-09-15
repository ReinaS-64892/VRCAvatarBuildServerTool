#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using VRC.Core;
using VRC.SDK3A.Editor;
using VRC.SDKBase.Editor.Api;

namespace net.rs64.VRCAvatarBuildServerTool.Uploader
{
    public static class AvatarUploader
    {
        static UploadServerInstance? _serverInstance;
        public static bool IsServerStarted => _serverInstance != null;
        public static void ServerStart()
        {
            if (_serverInstance != null) { Debug.Log("Server is already started"); return; }
            var mainThreadContext = SynchronizationContext.Current;

            EditorApplication.update += BuilderLoop;

            _serverInstance = new(mainThreadContext);
        }
        public static void ServerExit()
        {
            if (_serverInstance == null) { Debug.Log("Server is not started"); return; }
            EditorApplication.update -= BuilderLoop;
            _serverInstance.Dispose(); _serverInstance = null;
        }
        class UploadServerInstance : IDisposable
        {
            SynchronizationContext _postCtx;

            HttpListener _httpListener;
            CancellationTokenSource _serverCancellationTokenSource;

            Task _listenTask;

            public UploadServerInstance(SynchronizationContext post)
            {
                _postCtx = post;

                _httpListener = new();
                _httpListener.Prefixes.Add(AvatarUploadServerConfiguration.instance.UploadServerListenAddress);

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

                        if (ctxTask.IsCompleted is false) { break; }

                        var ctx = await ctxTask;
                        var req = ctx.Request;

                        switch (req.HttpMethod)
                        {
                            default: { Debug.Log("Unknown Request"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
                            case "GET":
                                {
                                    var avatarName = req.QueryString.Get("AvatarName");
                                    if (string.IsNullOrWhiteSpace(avatarName)) { Debug.Log("AvatarName not found"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }

                                    _postCtx.Send(async (_) =>
                                    {
                                        var avatar = await GetNewBlueprintID(avatarName);

                                        ctx.Response.StatusCode = 200;
                                        ctx.Response.OutputStream.Write(Encoding.UTF8.GetBytes(avatar.ID));
                                        ctx.Response.Close();
                                    }, null);

                                    break;
                                }
                            case "POST":
                                {
                                    if (req.InputStream is null) { Debug.Log("POST data is not found"); ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }

                                    var memStream = new MemoryStream((int)req.ContentLength64);
                                    await req.InputStream.CopyToAsync(memStream);

                                    ctx.Response.StatusCode = 200;
                                    ctx.Response.Close();

                                    var request = JsonUtility.FromJson<UploadRequest>(Encoding.UTF8.GetString(memStream.ToArray()));
                                    var uploadTask = new UploadTask(request.IsNewAvatar, request.BlueprintID, Convert.FromBase64String(request.AssetBundleBase64));
                                    _postCtx.Post(task => EnQueue((UploadTask)task), uploadTask);

                                    break;
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

            private static async Task<VRCAvatar> GetNewBlueprintID(string avatarName)
            {
                var vrcAvatar = new VRCAvatar()
                {
                    Name = avatarName,
                    Description = "",
                    Tags = new List<string>(),
                    ReleaseStatus = "private"
                };
                vrcAvatar = await VRCApi.CreateAvatarRecord(vrcAvatar);
                return vrcAvatar;
            }

            public void Dispose()
            {
                _serverCancellationTokenSource.Cancel();
                _httpListener?.Close();
                _httpListener = null!;
                _listenTask = null!;
            }
        }

        static volatile bool s_isTaskDoing;//しぶわいねぇ ... うーん
        static Queue<UploadTask> UploadTaskQueue = new();
        const string TemporaryThumbnailGUID = "bf34225cf0fe6be64b94e281fb3a55ce";
        public static void EnQueue(UploadTask task)
        {
            lock (UploadTaskQueue)
            {
                UploadTaskQueue.Enqueue(task);
            }
        }
        static void BuilderLoop()
        {
            if (s_isTaskDoing) { return; }
            UploadTask task;
            lock (UploadTaskQueue) { UploadTaskQueue.TryDequeue(out task); }

            if (task is not null) CallAsyncVoid(task);

            static async void CallAsyncVoid(UploadTask task)
            {
                try
                {
                    s_isTaskDoing = true;
                    var sdk = default(IVRCSdkAvatarBuilderApi);

                    if (VRCSdkControlPanel.window == null) { EditorApplication.ExecuteMenuItem("VRChat SDK/Show Control Panel"); }
                    while (VRCSdkControlPanel.TryGetBuilder(out sdk) is false) { await Task.Delay(100); }
                    if (sdk is null) { Debug.LogError("sdk がない !!! なぜ !!!"); return; }
                    if (await TryLogin() is false) { Debug.LogError("何らかの原因でログインできなかったよ〜！"); return; }

                    await UploadFromTransferred(task);
                }
                catch (Exception e) { Debug.LogException(e); }
                finally
                {
                    s_isTaskDoing = false;
                }
            }
        }

        static async Task UploadFromTransferred(UploadTask task)
        {
            var bundlePath = task.BlueprintID + ".vrca";
            File.WriteAllBytes(bundlePath, task.AssetBundle);

            try
            {
                var vrcAvatar = await VRCApi.GetAvatar(task.BlueprintID);
                if (task.IsNewAvatar) { _ = await VRCApi.CreateNewAvatar(task.BlueprintID, vrcAvatar, bundlePath, AssetDatabase.GUIDToAssetPath(TemporaryThumbnailGUID)); }
                else { _ = await VRCApi.UpdateAvatarBundle(task.BlueprintID, vrcAvatar, bundlePath); }
            }
            finally
            {
                File.Delete(bundlePath);
            }
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
        public class UploadTask
        {
            public bool IsNewAvatar;
            public string BlueprintID;
            public byte[] AssetBundle;

            public UploadTask(bool isNewAvatar, string blueprintID, byte[] assetBundle)
            {
                IsNewAvatar = isNewAvatar;
                BlueprintID = blueprintID;
                AssetBundle = assetBundle;
            }
        }
    }

}
