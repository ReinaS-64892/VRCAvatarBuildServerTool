using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using net.rs64.VRCAvatarBuildServerTool.Transfer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Core;
using VRC.SDK3A.Editor;
using VRC.SDKBase.Editor.Api;

namespace net.rs64.VRCAvatarBuildServerTool.Server
{
    public static class BuildServer
    {
        static BuildServerInstance _serverInstance;
        public static bool IsServerStarted => _serverInstance != null;
        public static void ServerStart()
        {
            if (_serverInstance != null) { Debug.Log("Server is already started"); return; }
            var port = AvatarBuildServerConfiguration.instance.BuildServerReceivePort;
            var mainThreadContext = SynchronizationContext.Current;

            _serverInstance = new(port, mainThreadContext);
        }
        public static void ServerExit()
        {
            if (_serverInstance == null) { Debug.Log("Server is not started"); return; }
            _serverInstance.Dispose(); _serverInstance = null;
        }
        class BuildServerInstance : IDisposable
        {
            int _port;
            SynchronizationContext _postCtx;

            HttpListener _httpListener;

            CancellationTokenSource _serverCancellationTokenSource;

            Task _listenTask;

            public BuildServerInstance(int port, SynchronizationContext post)
            {
                _port = port;
                _postCtx = post;
                _httpListener = new();
                _httpListener.Prefixes.Add("http://127.0.0.1:" + port + "/");

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
                        
                        _postCtx.Post(static async byteArray => await GetSDKAndUploadFromTransferred(byteArray as byte[]).ConfigureAwait(false), memStream.ToArray());
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


        const string TemporaryThumbnailGUID = "bf34225cf0fe6be64b94e281fb3a55ce";
        public static async Task GetSDKAndUploadFromTransferred(byte[] bytes)
        {
            if (VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out var sdk) is false) { Debug.Log("filed to get builder "); return; }
            await UploadFromTransferred(sdk, bytes);
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

                foreach (var guid in guids)
                {
                    await BuildToUploadFromGUID(sdk, guid);
                }
            }
            finally
            {
                Directory.Delete(destPath, true);
                File.Delete(destPath + ".meta");
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
                pipelineManager.AssignId();
                EditorUtility.SetDirty(pipelineManager);
                PrefabUtility.SavePrefabAsset(prefab);

                vrcAvatar = new()
                {
                    Name = prefab.name,
                    Description = "",
                    Tags = new List<string>(),
                    ReleaseStatus = "private"
                };
            }
            else vrcAvatar = await VRCApi.GetAvatar(pipelineManager.blueprintId);
            var thumbnailPath = isNewAvatar ? AssetDatabase.GUIDToAssetPath(TemporaryThumbnailGUID) : null;

            await sdk.BuildAndUpload(prefab, vrcAvatar, thumbnailPath);
        }
    }
}
