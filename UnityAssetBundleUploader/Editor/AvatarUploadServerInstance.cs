#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VRC.SDKBase.Editor.Api;

namespace net.rs64.VRCAvatarBuildServerTool.Uploader
{
    class AvatarUploadServerInstance : IDisposable
    {
        SynchronizationContext _postCtx;
        UploaderProjectConfig config;
        HttpListener _httpListener;
        CancellationTokenSource _serverCancellationTokenSource;

        Task _listenTask;

        public AvatarUploadServerInstance(SynchronizationContext post, UploaderProjectConfig config)
        {
            _postCtx = post;
            this.config = config;
            _httpListener = new();
            _httpListener.Prefixes.Add($"http://127.0.0.1:{config.UploaderServerPort}/");

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
                                var uploadTask = new VRCSDKController.UploadTask(request.IsNewAvatar, request.BlueprintID, Convert.FromBase64String(request.AssetBundleBase64));
                                _postCtx.Post(task => VRCSDKController.EnQueue((VRCSDKController.UploadTask)task), uploadTask);

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

}
