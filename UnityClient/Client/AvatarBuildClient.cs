using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using net.rs64.VRCAvatarBuildServerTool.Transfer;
using UnityEditor;
using UnityEngine;
using System.Diagnostics;
using Debug = UnityEngine.Debug;



#if CAU
using Anatawa12.ContinuousAvatarUploader.Editor;
#endif

namespace net.rs64.VRCAvatarBuildServerTool.Client
{

    public static class AvatarBuildClient
    {
        static HttpClient _client;

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
                            var transferAssets = GetDependenciesWithFiltered(targetPath);

                            sw.Stop();
                            Debug.Log("Find assets:" + sw.ElapsedMilliseconds + "ms");
                            Progress.Report(doID, 0.2f, "Encode Assets");

                            try
                            {
                                sw.Restart();
                                var internalBinary = await AssetTransferProtocol.EncodeAssetsAndTargetGUID(transferAssets, new string[] { targetGUID });
                                sw.Stop();
                                Debug.Log("EncodeAssets:" + sw.ElapsedMilliseconds + "ms");
                                Progress.Report(doID, 0.95f, "POST");
                                await PostInternalBinary(internalBinary);
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
                            var targetPaths = targetAvatarRoots.Select(i => CloneAndBuildToAsset(i, clientSideNDMFExecution)).ToArray();

                            Progress.Report(doID, 0.7f, "Post data prepare");
                            var sw = Stopwatch.StartNew();

                            var targetGUIDs = targetPaths.Select(AssetDatabase.AssetPathToGUID);
                            var transferAssets = GetDependenciesWithFiltered(targetPaths);

                            sw.Stop();
                            Progress.Report(doID, 0.8f, "Encode Assets");
                            Debug.Log("Find assets:" + sw.ElapsedMilliseconds + "ms");
                            try
                            {
                                sw.Restart();
                                var internalBinary = await AssetTransferProtocol.EncodeAssetsAndTargetGUID(transferAssets, targetGUIDs);
                                sw.Stop();
                                Debug.Log("EncodeAssets:" + sw.ElapsedMilliseconds + "ms");
                                Progress.Report(doID, 0.95f, "POST");
                                await PostInternalBinary(internalBinary);
                            }
                            finally
                            {
                                foreach (var targetPath in targetPaths) AssetDatabase.DeleteAsset(targetPath);
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

#if CAU
        private static List<GameObject> GetPrefabFromCAU(AvatarUploadSettingOrGroup aus, List<GameObject> avatarRoots = null)
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

        private static async Task PostInternalBinary(byte[] internalBinary)
        {
            try
            {
                Debug.Log("internal binary size for:" + internalBinary.LongLength / (1024.0 * 1024.0) + "mb");
                _client ??= new HttpClient() { Timeout = TimeSpan.FromSeconds(360) };
                using var binaryContent = new ByteArrayContent(internalBinary);

                // PostAsync を使うとでかいバイナリを投げる時に壊れることがある、しかしなぜ？ 古い API は信用してはならないのかもしれない。
                // var response = await _client.PostAsync(AvatarBuildClientConfiguration.instance.BuildServerURL, binaryContent);

                var targetServers = AvatarBuildClientConfiguration.instance.BuildServers;
                var postRequests = targetServers.Where(server => server.Enable).Select(server =>
                {
                    var req = new HttpRequestMessage();
                    req.Content = binaryContent;
                    req.Method = HttpMethod.Post;
                    req.RequestUri = new Uri(server.URL);
                    return (req, server.URL);
                }).Select(req => { return (_client.SendAsync(req.req), req.URL); }).ToArray();

                foreach (var postRequest in postRequests)
                {
                    try
                    {
                        var result = await postRequest.Item1;
                        Debug.Log("POST Response :" + result.StatusCode + " from " + postRequest.URL);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("failed URL : " + postRequest.URL);
                        Debug.LogException(e);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

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
