#nullable enable
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BestHTTP.JSON;
using UnityEditor;
using UnityEngine;

namespace net.rs64.VRCAvatarBuildServerTool.BuildRunner
{
    public static class AvatarBuildRunner
    {
        static AvatarBuildRunnerInstance? _serverInstance;
        private static BuildRunnerProjectConfig _config = new();
        static Task? _observeServerTask;
        internal static HttpClient _httpClient = new();
        [InitializeOnLoadMethod]
        public static void DoUploaderServer()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "BuildRunnerConfig.json");
            if (File.Exists(path) is false) { return; }

            _config = JsonUtility.FromJson<BuildRunnerProjectConfig>(File.ReadAllText(path));
            _httpClient.DefaultRequestHeaders.Add("Authorization", _config.AuthorizationCode);

            ServerStart();
            EditorApplication.update += VRCSDKController.BuilderLoop;
            FetchTarget();
        }

        internal static void FetchTarget()
        {
            var buildTargets = SessionState.GetString(AvatarBuildRunnerInstance.BuildTargetSessionKey, "");
            if (string.IsNullOrWhiteSpace(buildTargets)) { return; }
            foreach (var guid in JsonUtility.FromJson<AvatarBuildRunnerInstance.BuildTarget>(buildTargets).BuildTargets) VRCSDKController.EnQueue(guid);
        }

        public static void ServerStart()
        {
            if (_serverInstance != null) { Debug.Log("Server is already started"); return; }
            var mainThreadContext = SynchronizationContext.Current;

            _serverInstance = new(mainThreadContext, _config);
            _observeServerTask = Task.Run(() => ObserveInternalServer(mainThreadContext));
        }
        public static void ServerListenEnable() { _serverInstance?.ServerListenEnable(); }
        public static void ServerListenDisable() { _serverInstance?.ServerListenDisable(); }

        static async Task ObserveInternalServer(SynchronizationContext mainThreadContext)
        {
            await Task.Delay(1000);

            var pingURL = _config.InternalServerURL + "Ping";
            var alive = true;
            while (alive)
            {
                await Task.Delay(500);
                try
                {
                    var response = await _httpClient.GetAsync(pingURL);
                    alive = response.IsSuccessStatusCode;
                }
                catch { alive = false; }
            }
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }


        internal static async Task<byte[]?> GetFileFromInternalServer(string hash)
        {
            var response = await _httpClient.GetAsync(new Uri(_config.InternalServerURL + "File" + $"?FileHash={Uri.EscapeDataString(hash)}") { });

            if (response.IsSuccessStatusCode is false) { return null; }
            return await response.Content.ReadAsByteArrayAsync();
        }
        internal static async Task<string?> GetNewBlueprintID(string avatarName)
        {
            var response = await _httpClient.GetAsync(new Uri(_config.InternalServerURL + "GetBlueprintID" + $"?AvatarName={Uri.EscapeDataString(avatarName)}") { });

            if (response.IsSuccessStatusCode is false) { return null; }
            return Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());
        }
        internal static async Task PostUploadRequest(UploadRequest uploadRequest)
        {
            var requestStr = JsonUtility.ToJson(uploadRequest);
            await _httpClient.PostAsync(new Uri(_config.InternalServerURL + "Upload") { }, new ByteArrayContent(Encoding.UTF8.GetBytes(requestStr)));
        }
    }

}
