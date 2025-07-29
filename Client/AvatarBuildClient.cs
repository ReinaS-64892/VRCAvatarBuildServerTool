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
        public static async void Do()
        {
            switch (Selection.activeObject)
            {
                default: { Debug.Log("Unknown Target"); return; }
                case GameObject gameObject:
                    {
                        var avatarRoot = gameObject;
                        // VRCSDK の参照するのをサボっている。
                        if (avatarRoot.GetComponent<Animator>() == null) { return; }
                        EditorUtility.DisplayProgressBar("AvatarBuildClient-SentToBuild", "CloneAndBuildToAsset", 0f);

                        var targetPath = CloneAndBuildToAsset(avatarRoot);

                        EditorUtility.DisplayProgressBar("AvatarBuildClient-SentToBuild", "Post data search", 0.1f);

                        var sw = Stopwatch.StartNew();
                        var targetGUID = AssetDatabase.AssetPathToGUID(targetPath);
                        var transferAssets = GetDependenciesWithFiltered(targetPath);

                        sw.Stop();
                        Debug.Log("Find assets:" + sw.ElapsedMilliseconds + "ms");
                        EditorUtility.DisplayProgressBar("AvatarBuildClient-SentToBuild", "Post data prepare", 0.2f);

                        try
                        {
                            sw.Restart();
                            var internalBinary = await AssetTransferProtocol.EncodeAssetsAndTargetGUID(transferAssets, new string[] { targetGUID });
                            sw.Stop();
                            Debug.Log("EncodeAssets:" + sw.ElapsedMilliseconds + "ms");
                            EditorUtility.DisplayProgressBar("AvatarBuildClient-SentToBuild", "POST", 0.95f);
                            await PostInternalBinary(internalBinary);
                        }
                        finally
                        {
                            AssetDatabase.DeleteAsset(targetPath);
                            EditorUtility.DisplayProgressBar("AvatarBuildClient-SentToBuild", "Exit", 1f);
                        }
                        Debug.Log("Exit Build transfer");
                        EditorUtility.ClearProgressBar();
                        return;
                    }
#if CAU
                case AvatarUploadSettingOrGroup aus:
                    {
                        EditorUtility.DisplayProgressBar("AvatarBuildClient-SentToBuild", "GetPrefabs from cAU", 0f);
                        var targetAvatarRoots = GetPrefabFromCAU(aus);

                        EditorUtility.DisplayProgressBar("AvatarBuildClient-SentToBuild", "CloneAndBuildToAsset", 0.1f);
                        var targetPaths = targetAvatarRoots.Select(CloneAndBuildToAsset).ToArray();

                        EditorUtility.DisplayProgressBar("AvatarBuildClient-SentToBuild", "Post data prepare", 0.7f);
                        var sw = Stopwatch.StartNew();

                        var targetGUIDs = targetPaths.Select(AssetDatabase.AssetPathToGUID);
                        var transferAssets = GetDependenciesWithFiltered(targetPaths);

                        sw.Stop();
                        Debug.Log("Find assets:" + sw.ElapsedMilliseconds + "ms");
                        try
                        {
                            sw.Restart();
                            var internalBinary = await AssetTransferProtocol.EncodeAssetsAndTargetGUID(transferAssets, targetGUIDs);
                            EditorUtility.DisplayProgressBar("AvatarBuildClient-SentToBuild", "POST", 0.95f);
                            sw.Stop();
                            Debug.Log("EncodeAssets:" + sw.ElapsedMilliseconds + "ms");
                            await PostInternalBinary(internalBinary);
                        }
                        finally
                        {
                            foreach (var targetPath in targetPaths) AssetDatabase.DeleteAsset(targetPath);
                            EditorUtility.DisplayProgressBar("AvatarBuildClient-SentToBuild", "Exit", 1f);
                        }
                        Debug.Log("Exit Build transfer");
                        EditorUtility.ClearProgressBar();
                        return;
                    }
#endif
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

        private static string CloneAndBuildToAsset(GameObject avatarRoot)
        {
            var builded = UnityEngine.Object.Instantiate(avatarRoot);
#if NDMF
            try
            {
                if (AvatarBuildClientConfiguration.instance.ClientSideNDMFExecution) nadena.dev.ndmf.AvatarProcessor.ProcessAvatar(builded);
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
