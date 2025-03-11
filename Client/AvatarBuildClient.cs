using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using net.rs64.VRCAvatarBuildServerTool.Transfer;
using UnityEditor;
using UnityEngine;
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

                        EditorUtility.DisplayProgressBar("AvatarBuildClient-SentToBuild", "Post data prepare", 0.7f);

                        var targetGUID = AssetDatabase.AssetPathToGUID(targetPath);
                        var transferAssets = GetDependenciesWithFiltered(targetPath);

                        try
                        {
                            var internalBinary = AssetTransferProtocol.EncodeAssetsAndTargetGUID(transferAssets, new string[] { targetGUID });
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
                        var targetGUIDs = targetPaths.Select(AssetDatabase.AssetPathToGUID);
                        var transferAssets = GetDependenciesWithFiltered(targetPaths);
                        try
                        {
                            var internalBinary = AssetTransferProtocol.EncodeAssetsAndTargetGUID(transferAssets, targetGUIDs);
                            EditorUtility.DisplayProgressBar("AvatarBuildClient-SentToBuild", "POST", 0.95f);
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
                _client ??= new HttpClient() { Timeout = TimeSpan.FromSeconds(5) };
                using var binaryContent = new ByteArrayContent(internalBinary);
                var response = await _client.PostAsync(AvatarBuildClientConfiguration.instance.BuildServerURL, binaryContent).ConfigureAwait(false);
                Debug.Log("POST Response :" + response.StatusCode);
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
