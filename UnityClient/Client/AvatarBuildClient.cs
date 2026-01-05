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
using System.Security.Cryptography;
using System.IO;
using System.Text;
using System.Threading;
using System.IO.Compression;





#if CAU
using Anatawa12.ContinuousAvatarUploader.Editor;
#endif

namespace net.rs64.VRCAvatarBuildServerTool.Client
{

    public static class AvatarBuildClient
    {
        static HttpClient? _client;
        private static AvatarBuildClientConfiguration? _config;

        [MenuItem("Assets/VRCAvatarBuildServerTool/BuildToServer")]
        [MenuItem("GameObject/VRCAvatarBuildServerTool/BuildToServer")]
        public static void Do() { DoImpl(); }

        [MenuItem("Assets/VRCAvatarBuildServerTool/BuildToServer(ClientSideNDMFExecution)")]
        [MenuItem("GameObject/VRCAvatarBuildServerTool/BuildToServer(ClientSideNDMFExecution)")]
        public static void ClientSideNDMFManualBakeToDo() { DoImpl(true); }
        public static async void DoImpl(bool clientSideNDMFExecution = false)
        {
            _config = AvatarBuildClientConfiguration.instance;
            var doID = Progress.Start("AvatarBuildClient-SentToBuild", "VRCAvatarBuildServerTool-BuildToServer");
            try
            {
                switch (Selection.activeObject)
                {
                    default: { Debug.Log("Unknown Target"); return; }
                    case GameObject gameObject:
                        {
                            var avatarRoot = gameObject;
                            // VRCSDK の参照するのをサボっている。
                            if (avatarRoot.GetComponent<Animator>() == null) { return; }
                            Progress.Report(doID, 0f, "CloneAndBuildToAsset");

                            var targetPath = CloneAndBuildToAsset(avatarRoot, clientSideNDMFExecution);
                            try
                            {
                                Progress.Report(doID, 0.1f, "make build request");
                                var sw = Stopwatch.StartNew();
                                var buildRequest = await CreateBuildRequest(avatarRoot.name, targetPath);
                                sw.Stop();
                                Debug.Log("Find assets:" + sw.ElapsedMilliseconds + "ms");
                                Progress.Report(doID, 0.2f, "Build Sending");

                                sw.Restart();
                                await Task.Run(() => SendBuild(buildRequest));
                                sw.Stop();
                                Debug.Log("Build Sending :" + sw.ElapsedMilliseconds + "ms");
                                Progress.Report(doID, 0.95f, "Exiting");
                            }
                            finally
                            {
                                AssetDatabase.DeleteAsset(targetPath);
                                Progress.Report(doID, 1f, "Exit");
                            }
                            Debug.Log("Exit Build transfer");
                            Progress.Finish(doID, Progress.Status.Succeeded);
                            return;
                        }
#if CAU
                    case AvatarUploadSettingOrGroup aus:
                        {
                            Progress.Report(doID, 0f, "GetPrefabs from cAU");
                            var targetAvatarRoots = GetPrefabFromCAU(aus);

                            Progress.Report(doID, 0.1f, "CloneAndBuildToAsset");
                            var targetNameAndPathAndPlatforms = targetAvatarRoots.Select(i => (i.prefab.name, CloneAndBuildToAsset(i.prefab, clientSideNDMFExecution), i.target)).ToArray();
                            try
                            {
                                Progress.Report(doID, 0.7f, "Post data prepare");
                                var sw = Stopwatch.StartNew();
                                var requests = new List<BuildRequest>();
                                foreach (var targetNPP in targetNameAndPathAndPlatforms)
                                {
                                    requests.Add(await CreateBuildRequest(targetNPP.name, targetNPP.Item2, targetNPP.target));
                                }
                                sw.Stop();
                                Progress.Report(doID, 0.8f, "Encode Assets");
                                Debug.Log("Find assets:" + sw.ElapsedMilliseconds + "ms");
                                sw.Restart();
                                foreach (var buildRequest in requests)
                                {
                                    await Task.Run(() => SendBuild(buildRequest));
                                }
                                sw.Stop();
                                Debug.Log("Build Sending :" + sw.ElapsedMilliseconds + "ms");
                                Progress.Report(doID, 0.95f, "Exiting");
                            }
                            finally
                            {
                                foreach (var targetPath in targetNameAndPathAndPlatforms) AssetDatabase.DeleteAsset(targetPath.Item2);
                                Progress.Report(doID, 1f, "Exit");
                            }
                            Debug.Log("Exit Build transfer");
                            Progress.Finish(doID, Progress.Status.Succeeded);
                            return;
                        }
#endif
                }
            }
            catch (Exception e)
            {
                Progress.Finish(doID, Progress.Status.Failed);
                throw e;
            }
        }

        public static async Task<BuildRequest> CreateBuildRequest(string prefabName, string prefabPath, BuildTargetPlatform buildTargetPlatform = BuildTargetPlatform.Windows)
        {
            var targetGUID = AssetDatabase.AssetPathToGUID(prefabPath);
            var transferTargetFiles = GetDependenciesWithFiltered(prefabPath).SelectMany(p => new[] { p, p + ".meta" }).ToList();

            var req = new BuildRequest();
            req.PrefabName = prefabName;
            req.BuildTarget = targetGUID;
            req.TargetPlatform = buildTargetPlatform;

            var targetFileHashesKv = await Task.WhenAll(transferTargetFiles.Select(p => Task.Run(async () => new PathToHash() { Path = p, Hash = await GetHash(p) })));
            req.Assets = targetFileHashesKv;

            var ingorePackageID = (_config?.IgnorePackageIDs ?? Enumerable.Empty<string>())
                .Append("net.rs64.vrc-avatar-build-server-tool.unity-client")
                .Append("net.rs64.vrc-avatar-build-server-tool.unity-build-runner")
                .ToList();

            if (PackageCache is null)
            {

                var task = Directory.GetDirectories("Packages")
                    .Select(pkg => Task.Run(async () =>
                    {
                        var parentDir = pkg;// exsample "Packages/TexTransTool"

                        var pkjJson = Path.Combine(parentDir, "package.json");
                        if (File.Exists(pkjJson) is false) { return null; }
                        var pkjNameID = JsonUtility.FromJson<PackageJson>(File.ReadAllText(pkjJson)).name;
                        if (string.IsNullOrWhiteSpace(pkjNameID)) { return null; }
                        if (ingorePackageID.Contains(pkjNameID)) { return null; }

                        var fileTask = Directory.GetFiles(pkg, "*", SearchOption.AllDirectories)
                            .Select(p =>
                            {
                                // exsample p "Packages/TexTransTool/package.json"
                                return Task.Run(async () =>
                                {
                                    if (p.Contains("/.git/")) { return null; }// .git は特別に無視します。
                                    try
                                    {
                                        return new PathToHash() { Path = p, Hash = await GetHash(p) };
                                    }
                                    catch // 壊れた symlink などを無視するために握りつぶし！！！
                                    {
                                        return null;
                                    }
                                }
                                );
                            });
                        var files = (await Task.WhenAll(fileTask)).OfType<PathToHash>().ToArray();
                        return new Package() { PackageID = pkjNameID, Files = files };
                    }
                    )
                );
                req.Packages = PackageCache = (await Task.WhenAll(task)).OfType<Package>().ToArray();
            }
            else { req.Packages = PackageCache; }

            return req;
        }
        // くそざつキャッシング 何もしてないのでドメインリロードで破棄されるから問題なし。
        static Package[]? PackageCache = null;
        class PackageJson
        {
            public string name = "";
        }
        public static async Task<string> GetHash(string filePath)
        {
            // SHA1 はスレッドセーフではない
            using var sha = SHA1.Create();
            var hash = sha.ComputeHash(await File.ReadAllBytesAsync(filePath));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        private static async Task SendBuild(BuildRequest buildRequest)
        {
            var buildRequestJson = JsonUtility.ToJson(buildRequest);
            var buildRequestBytes = Encoding.UTF8.GetBytes(buildRequestJson);
            using var buildRequestBinaryContent = new ByteArrayContent(buildRequestBytes);

            foreach (var server in _config!.BuildServers)
            {
                if (server.Enable is false) { continue; }

                var buildURI = new Uri(server.URL + "Build");
                var fileURI = new Uri(server.URL + "File");

                PrepareHTTPClient(server); if (_client is null) { throw new Exception(); }

                var transferBytes = 0L;
                for (var i = 0; 4 > i; i += 1)//とりあえず safety として 4回
                {
                    try
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
                                    goto LoopExit;// 渋い気持ち
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
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }

            LoopExit:

                Debug.Log("file transferer size for:" + transferBytes / (1024.0 * 1024.0) + "mb");
            }
        }

        private static void PrepareHTTPClient(BuildServer server)
        {
            _client ??= new HttpClient() { Timeout = TimeSpan.FromSeconds(360) };
            if (_client.DefaultRequestHeaders.Contains("Authorization")) _client.DefaultRequestHeaders.Remove("Authorization");
            _client.DefaultRequestHeaders.Add("Authorization", server.ServerPasscodeHeader);
        }

#if CAU
        private static List<(GameObject prefab, BuildTargetPlatform target)> GetPrefabFromCAU(AvatarUploadSettingOrGroup aus, List<(GameObject, BuildTargetPlatform)>? avatarRoots = null)
        {
            avatarRoots ??= new();
            switch (aus)
            {
                default: break;
                case AvatarUploadSetting avatarUploadSetting:
                    {
                        void Add(PlatformSpecificInfo info, BuildTargetPlatform platform)
                        {
                            if (info.enabled is false) { return; }
                            var avatarRoot = avatarUploadSetting.avatarDescriptor.asset as Component;
                            if (avatarRoot == null)
                            {
                                if (avatarUploadSetting.avatarDescriptor.asset != null)
                                { Debug.Log("on Scene Prefab is not supported"); }
                                return;
                            }

                            avatarRoots.Add((avatarRoot.gameObject, platform));
                        }
                        Add(avatarUploadSetting.windows, BuildTargetPlatform.Windows);
                        Add(avatarUploadSetting.quest, BuildTargetPlatform.Android);
                        Add(avatarUploadSetting.ios, BuildTargetPlatform.IOS);
                        break;
                    }
                case AvatarUploadSettingGroup group:
                    {
                        foreach (var a in group.avatars)
                        {
                            GetPrefabFromCAU(a, avatarRoots);
                        }
                        break;
                    }
                case AvatarUploadSettingGroupGroup groupGroup:
                    {
                        foreach (var g in groupGroup.groups)
                        {
                            GetPrefabFromCAU(g, avatarRoots);
                        }
                        break;
                    }
            }
            return avatarRoots;
        }

#endif

        private static string CloneAndBuildToAsset(GameObject avatarRoot, bool doNDMFManualBake)
        {
            var builded = UnityEngine.Object.Instantiate(avatarRoot);
#if NDMF
            try
            {
                if (doNDMFManualBake) nadena.dev.ndmf.AvatarProcessor.ProcessAvatar(builded);
            }
            catch (Exception e) { Debug.LogException(e); }
#endif
            var targetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/" + builded.name + ".prefab");
            PrefabUtility.SaveAsPrefabAsset(builded, targetPath);
            UnityEngine.Object.DestroyImmediate(builded);
            return targetPath;
        }

        private static string[] GetDependenciesWithFiltered(params string[] targetPrefabPath)
        {
            return AssetDatabase.GetDependencies(targetPrefabPath)
            .Where(path => path.StartsWith("Packages") is false || path.StartsWith("Packages/nadena.dev.ndmf/__Generated"))
            .ToArray();
        }
    }
}
