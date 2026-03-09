#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using System.IO;
using System.Text;
using System.Threading;

namespace net.rs64.VRCAvatarBuildServerTool.Client
{
    public static partial class AvatarBuildClient
    {
        static HttpClient? _client;

        [MenuItem("Assets/VRCAvatarBuildServerTool/BuildToServer")]
        public static void DoAny()
        {
            var doLabel = Selection.objects.Any() is false;
            doLabel |= Selection.objects.Length is 1 && Selection.objects.First() is UnityEditor.DefaultAsset;
            if (doLabel) { DoWithLabel(); }
            else { DoWithSelection(); }
        }


        [MenuItem("Assets/VRCAvatarBuildServerTool/Others/BuildToServer-from-Selection")]
        [MenuItem("GameObject/VRCAvatarBuildServerTool/BuildToServer")]
        [MenuItem("GameObject/VRCAvatarBuildServerTool/Others/BuildToServer-from-Selection")]
        public static void Do() { DoWithSelection(); }

        [MenuItem("Assets/VRCAvatarBuildServerTool/Others/BuildToServer-from-Selection (ClientSideNDMFExecution)")]
        [MenuItem("GameObject/VRCAvatarBuildServerTool/Others/BuildToServer-from-Selection (ClientSideNDMFExecution)")]
        public static void ClientSideNDMFManualBakeToDo() { DoWithSelection(true); }

        public static async void DoWithSelection(bool clientSideNDMFExecution = false)
        {
            await DoSendImpl(Selection.objects.SelectMany(Correcting).ToArray(), clientSideNDMFExecution);

            IEnumerable<BuildTargetRecord> Correcting(UnityEngine.Object o)
            {
                switch (o)
                {
                    default: { Debug.Log("Unknown Target"); break; }
                    case GameObject gameObject:
                        {
                            var avatarRoot = gameObject;
                            // VRCSDK の参照するのをサボっている。
                            if (avatarRoot.GetComponent<Animator>() == null) { break; }

                            var platform = EditorUserBuildSettings.selectedBuildTargetGroup switch
                            {
                                BuildTargetGroup.Standalone => BuildTargetPlatform.Windows,
                                BuildTargetGroup.Android => BuildTargetPlatform.Android,
                                BuildTargetGroup.iOS => BuildTargetPlatform.IOS,
                                _ => throw new NotImplementedException(),
                            };

                            yield return new(avatarRoot, platform);
                            break;
                        }
#if CAU
                    case Anatawa12.ContinuousAvatarUploader.Editor.AvatarUploadSettingOrGroup aus:
                        {
                            foreach (var t in GetPrefabFromCAU(aus))
                                yield return t;

                            break;
                        }
#endif
                }
            }
        }

        static async Task DoSendImpl(IEnumerable<BuildTargetRecord> buildTargetRecords, bool clientSideNDMFExecution = false)
        {
            if (buildTargetRecords.Any() is false) { Debug.Log("target not found"); return; }

            var targetServer = AvatarBuildClientConfiguration.instance.BuildServers.FirstOrDefault();
            var ignorePackageIDs = AvatarBuildClientConfiguration.instance.IgnorePackageIDs;

            var doID = Progress.Start("AvatarBuildClient-SentToBuild", "VRCAvatarBuildServerTool-BuildToServer");
            Progress.Report(doID, 0.0f, "prepiae ... ");

            var postRemoveAssetPath = new List<string>();
            try
            {
                Progress.Report(doID, 0.1f, "CloneAndBuildToAsset");
                var targetNameAndPathAndPlatforms = buildTargetRecords
                    .Select(i =>
                        new BuildTargetClonedPrefabRecord(i.prefab.name, CloneAndBuildToAsset(i.prefab, clientSideNDMFExecution), i.target)
                    ).ToArray();

                postRemoveAssetPath.AddRange(targetNameAndPathAndPlatforms.Select(i => i.PrefabPath));

                Progress.Report(doID, 0.7f, "Post data prepare");
                var sw = Stopwatch.StartNew();

                var ignorePackageID = (ignorePackageIDs ?? Enumerable.Empty<string>())
                    .Append("net.rs64.vrc-avatar-build-server-tool.unity-client")
                    .Append("net.rs64.vrc-avatar-build-server-tool.unity-build-runner")
                    .ToList();

                var packagesHash = await GetCorrectingPackageToHash(ignorePackageID);

                var requests = await Task.WhenAll(targetNameAndPathAndPlatforms.Select(CreateRequest));
                async Task<BuildRequest> CreateRequest(BuildTargetClonedPrefabRecord t)
                {
                    var targetFileHashesKv = await FindingDependencyPathToHash(t.PrefabPath);

                    var targetGUID = AssetDatabase.AssetPathToGUID(t.PrefabPath);
                    var req = new BuildRequest
                    {
                        PrefabName = t.PrefabName,
                        TargetPlatform = t.TargetPlatform,
                        BuildTarget = targetGUID,
                        Assets = targetFileHashesKv,
                        Packages = packagesHash,
                    };

                    return req;
                }

                sw.Stop();
                Progress.Report(doID, 0.8f, "Encode Assets");
                Debug.Log("Find assets:" + sw.ElapsedMilliseconds + "ms");
                sw.Restart();

                PrepareHTTPClient(targetServer.ServerPasscodeHeader);
                await Task.WhenAll(requests.Select(br => SendBuild(targetServer.URL, br)));

                sw.Stop();
                Debug.Log("Build Sending :" + sw.ElapsedMilliseconds + "ms");
                Progress.Report(doID, 0.95f, "Exiting");
            }
            catch
            {
                Progress.Report(doID, 1f, "Error!!!");
                Debug.Log("Exit Build transfer (Error !!!)");
                Progress.Finish(doID, Progress.Status.Failed);

                foreach (var targetPath in postRemoveAssetPath) AssetDatabase.DeleteAsset(targetPath);
                Progress.Report(doID, 1f, "Exit");
                postRemoveAssetPath.Clear();

                throw;
            }
            finally
            {
                foreach (var targetPath in postRemoveAssetPath) AssetDatabase.DeleteAsset(targetPath);
                postRemoveAssetPath.Clear();

                Progress.Report(doID, 1f, "Exit");
            }

            Debug.Log("Exit Build transfer");
            Progress.Finish(doID, Progress.Status.Succeeded);
            return;
        }

        private static async Task SendBuild(string uri, BuildRequest buildRequest)
        {
            if (_client is null) { throw new Exception(); }

            var buildRequestJson = JsonUtility.ToJson(buildRequest);
            var buildRequestBytes = Encoding.UTF8.GetBytes(buildRequestJson);
            using var buildRequestBinaryContent = new ByteArrayContent(buildRequestBytes);


            var buildURI = new Uri(uri + "Build");
            var fileURI = new Uri(uri + "File");

            var transferBytes = 0L;

            try
            {
                for (var i = 0; 4 > i; i += 1)//とりあえず safety として 4回
                {
                    var response = await _client.PostAsync(buildURI, buildRequestBinaryContent);
                    if (response.StatusCode is not System.Net.HttpStatusCode.OK) { Debug.LogError("Unknown Error"); break; }
                    if (response.Content is null) { Debug.LogError("Unknown Error"); break; }

                    var responseJson = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());
                    var requestResponse = JsonUtility.FromJson<BuildRequestResponse>(responseJson);

                    switch (requestResponse.ResultCode)
                    {
                        default: { break; }
                        case "BuildRequestAccept":
                            {
                                Debug.Log("BuildRequestAccept!");
                                return;
                            }
                        case "MissingAssets":
                            {
                                Debug.Log("MissingAssets!");
                                var filePostTask = requestResponse.MissingFiles.Select(async f =>
                                {
                                    var bytes = await File.ReadAllBytesAsync(f);
                                    using var fileBytesContents = new ByteArrayContent(bytes);
                                    var res = await _client.PostAsync(fileURI, fileBytesContents);
                                    Debug.Log("File POST:" + res.StatusCode + " - " + f);
                                    Interlocked.Add(ref transferBytes, bytes.LongLength);
                                }
                                ).ToArray();
                                await Task.WhenAll(filePostTask);
                                break;
                            }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                Debug.Log("file transferer size for:" + transferBytes / (1024.0 * 1024.0) + "mb");
            }
        }

        private static void PrepareHTTPClient(string serverPasscodeHeader)
        {
            _client ??= new HttpClient() { Timeout = TimeSpan.FromSeconds(360) };
            if (_client.DefaultRequestHeaders.Contains("Authorization")) _client.DefaultRequestHeaders.Remove("Authorization");
            _client.DefaultRequestHeaders.Add("Authorization", serverPasscodeHeader);
        }
    }

    internal record BuildTargetRecord
    {
        public GameObject prefab;
        public BuildTargetPlatform target;

        public BuildTargetRecord(GameObject prefab, BuildTargetPlatform target)
        {
            this.prefab = prefab;
            this.target = target;
        }
    }
    internal record BuildTargetClonedPrefabRecord
    {
        public string PrefabName;
        public string PrefabPath;
        public BuildTargetPlatform TargetPlatform;

        public BuildTargetClonedPrefabRecord(string prefabName, string prefabPath, BuildTargetPlatform target)
        {
            this.PrefabName = prefabName;
            this.PrefabPath = prefabPath;
            this.TargetPlatform = target;
        }
    }
}
