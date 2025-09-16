#nullable enable
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace net.rs64.VRCAvatarBuildServerTool.Uploader
{
    public static class AvatarUploader
    {
        static AvatarUploadServerInstance? _serverInstance;
        private static UploaderProjectConfig _config = new();
        static Task? _observeServerTask;
        static HttpClient _httpClient = new();
        [InitializeOnLoadMethod]
        public static void DoUploaderServer()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "UploaderConfig.json");
            if (File.Exists(path) is false) { return; }

            _config = JsonUtility.FromJson<UploaderProjectConfig>(File.ReadAllText(path));
            _httpClient.DefaultRequestHeaders.Add("Authorization", _config.AuthorizationCode);
            ServerStart();
        }
        public static void ServerStart()
        {
            if (_serverInstance != null) { Debug.Log("Server is already started"); return; }
            var mainThreadContext = SynchronizationContext.Current;

            EditorApplication.update += VRCSDKController.BuilderLoop;
            _serverInstance = new(mainThreadContext, _config);
            _observeServerTask = Task.Run(() => ObserveInternalServer(mainThreadContext));
        }

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
    }

}
