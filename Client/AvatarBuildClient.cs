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
using net.rs64.VRCAvatarBuildServerTool.Transfer;
using System.IO.Compression;





#if CAU
using Anatawa12.ContinuousAvatarUploader.Editor;
#endif

namespace net.rs64.VRCAvatarBuildServerTool.Client
{

    public static class AvatarBuildClient
    {
        static HttpClient? _client;
        private static List<BuildServer>? _BuildTargetServers;

        [MenuItem("Assets/VRCAvatarBuildServerTool/BuildToServer")]
        [MenuItem("GameObject/VRCAvatarBuildServerTool/BuildToServer")]
        public static void Do() { DoImpl(); }

        [MenuItem("Assets/VRCAvatarBuildServerTool/BuildToServer(ClientSideNDMFExecution)")]
        [MenuItem("GameObject/VRCAvatarBuildServerTool/BuildToServer(ClientSideNDMFExecution)")]
        public static void ClientSideNDMFManualBakeToDo() { DoImpl(true); }
        public static async void DoImpl(bool clientSideNDMFExecution = false)
        {
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

                            Progress.Report(doID, 0.1f, "Post data search");

                            var sw = Stopwatch.StartNew();
                            var targetGUID = AssetDatabase.AssetPathToGUID(targetPath);
                            var transferTargetFiles = GetDependenciesWithFiltered(targetPath).SelectMany(p => new[] { p, p + ".meta" }).ToList();

                            sw.Stop();
                            Debug.Log("Find assets:" + sw.ElapsedMilliseconds + "ms");
                            Progress.Report(doID, 0.2f, "Build Sending");

                            try
                            {
                                sw.Restart();
                                _BuildTargetServers = AvatarBuildClientConfiguration.instance.BuildServers;
                                await Task.Run(() => SendBuildRun(new() { targetGUID }, transferTargetFiles));
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
                        // case AvatarUploadSettingOrGroup aus:
                        //     {
                        //         Progress.Report(doID, 0f, "GetPrefabs from cAU");
                        //         var targetAvatarRoots = GetPrefabFromCAU(aus);

                        //         Progress.Report(doID, 0.1f, "CloneAndBuildToAsset");
                        //         var targetPaths = targetAvatarRoots.Select(i => CloneAndBuildToAsset(i, clientSideNDMFExecution)).ToArray();

                        //         Progress.Report(doID, 0.7f, "Post data prepare");
                        //         var sw = Stopwatch.StartNew();

                        //         var targetGUIDs = targetPaths.Select(AssetDatabase.AssetPathToGUID);
                        //         var transferAssets = GetDependenciesWithFiltered(targetPaths);

                        //         sw.Stop();
                        //         Progress.Report(doID, 0.8f, "Encode Assets");
                        //         Debug.Log("Find assets:" + sw.ElapsedMilliseconds + "ms");
                        //         try
                        //         {
                        //             sw.Restart();
                        //             var internalBinary = await AssetTransferProtocol.EncodeAssetsAndTargetGUID(transferAssets, targetGUIDs);
                        //             sw.Stop();
                        //             Debug.Log("EncodeAssets:" + sw.ElapsedMilliseconds + "ms");
                        //             Progress.Report(doID, 0.95f, "POST");
                        //             await PostInternalBinary(internalBinary);
                        //         }
                        //         finally
                        //         {
                        //             foreach (var targetPath in targetPaths) AssetDatabase.DeleteAsset(targetPath);
                        //             Progress.Report(doID, 1f, "Exit");
                        //         }
                        //         Debug.Log("Exit Build transfer");
                        //         Progress.Finish(doID, Progress.Status.Succeeded);
                        //         return;
                        //     }
#endif
                }
            }
            catch (Exception e)
            {
                Progress.Finish(doID, Progress.Status.Failed);
                throw e;
            }
        }

        // private static List<string> FindPackages()
        // {
        //     return Directory.EnumerateDirectories("Packages")
        //     .SelectMany(p => Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
        //     .Where(p => p.Contains("/.git/") is false) // git フォルダを省きます。
        //     .ToList();
        // }

        public static async Task<string> GetHash(string filePath)
        {
            // SHA1 はスレッドセーフではない
            using var sha = SHA1.Create();
            var hash = sha.ComputeHash(await File.ReadAllBytesAsync(filePath));
            return Convert.ToBase64String(hash);
        }
        private static async Task SendBuildRun(List<string> targets, List<string> targetFiles)
        {
            try
            {
                await SendBuild(targets, targetFiles);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
        private static async Task SendBuild(List<string> targets, List<string> targetFiles)
        {
            var targetFileHashesKv = await Task.WhenAll(
                targetFiles.Select(
                    f => Task.Run(async () => new PathToHash()
                    {
                        Path = f,
                        Hash = await GetHash(f)
                    }
                    )
                )
            );
            var buildRequest = new BuildRequest() { BuildTargets = targets, Assets = targetFileHashesKv.ToList() };
            var buildRequestJson = JsonUtility.ToJson(buildRequest);
            var buildRequestBytes = Encoding.UTF8.GetBytes(buildRequestJson);
            using var buildRequestBinaryContent = new ByteArrayContent(buildRequestBytes);

            foreach (var server in _BuildTargetServers!)
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
        private static List<GameObject> GetPrefabFromCAU(AvatarUploadSettingOrGroup aus, List<GameObject>? avatarRoots = null)
        {
            avatarRoots ??= new();
            switch (aus)
            {
                default: break;
                case AvatarUploadSetting avatarUploadSetting:
                    {
                        if (avatarUploadSetting.GetCurrentPlatformInfo().enabled is false) { break; }
                        var avatarRoot = avatarUploadSetting.avatarDescriptor.asset as Component;
                        if (avatarRoot == null)
                        {
                            if (avatarUploadSetting.avatarDescriptor.asset != null)
                            { Debug.Log("on Scene Prefab is not supported"); }
                            break;
                        }

                        avatarRoots.Add(avatarRoot.gameObject);
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

        internal static async Task SyncPackages()
        {
            _BuildTargetServers = AvatarBuildClientConfiguration.instance.BuildServers;
            var ignorePackage = AvatarBuildClientConfiguration.instance.IgnorePackages;
            var packagesPathStringLen = "Packages/".Length;
            foreach (var server in _BuildTargetServers!)
            {
                var pID = Progress.Start("SyncPackages");
                Progress.Report(pID, 0);
                try
                {
                    if (server.Enable is false) { continue; }
                    PrepareHTTPClient(server);
                    if (_client is null) { throw new Exception(); }

                    var clearURI = new Uri(server.URL + "PackageClear");
                    var addURI = new Uri(server.URL + "AddPackage");

                    var clearReq = await _client.PostAsync(clearURI, new ByteArrayContent(Array.Empty<byte>()));
                    if (clearReq.IsSuccessStatusCode is false) { Debug.Log("Package Clear filed!"); continue; }
                    Debug.Log("Package Clear");

                    var task = Directory.GetDirectories("Packages")
                        .Where(p => ignorePackage.Any(i => p.Substring(packagesPathStringLen) == i) is false)
                        .Select(
                        async package =>
                        {
                            var cpID = Progress.Start("Sync " + package, "", Progress.Options.None, pID);
                            using var memStream = new MemoryStream();

                            using (var zip = new ZipArchive(memStream, ZipArchiveMode.Create))
                            {
                                var files = Directory.GetFiles(package, "*", SearchOption.AllDirectories);
                                for (var i = 0; files.Length > i; i += 1)
                                {
                                    Progress.Report(cpID, i / (float)files.Length);
                                    var f = files[i];

                                    try
                                    {
                                        var entry = zip.CreateEntry(f.Substring(packagesPathStringLen), System.IO.Compression.CompressionLevel.Optimal);
                                        using var entryWriter = entry.Open();
                                        using var fo = File.OpenRead(f);
                                        await fo.CopyToAsync(entryWriter);
                                    }
                                    catch (Exception e) { Debug.LogWarning(e); }// 握りつぶそうね ... ! めんどいから
                                }
                            }
                            Progress.Finish(cpID);
                            return memStream.ToArray();
                        }
                    );
                    var postCandidate = await Task.WhenAll(task);
                    for (var i = 0; postCandidate.Length > i; i += 1)
                    {
                        Progress.Report(pID, i / (float)postCandidate.Length);
                        var packageZip = postCandidate[i];
                        using var content = new ByteArrayContent(packageZip);
                        var addReq = await _client.PostAsync(addURI, content);
                        if (addReq.IsSuccessStatusCode is false) { Debug.Log("Add failed"); continue; }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    Progress.Finish(pID, Progress.Status.Failed);
                }
                Progress.Finish(pID);
                Debug.Log("Sync exit");
            }
        }
    }
}
