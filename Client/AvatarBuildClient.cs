using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using net.rs64.VRCAvatarBuildServerTool.Transfer;
using UnityEditor;
using UnityEngine;

namespace net.rs64.VRCAvatarBuildServerTool.Client
{

    public static class AvatarBuildClient
    {
        static HttpClient _client;

        [MenuItem("Assets/VRCAvatarBuildServerTool/BuildToServer")]
        [MenuItem("GameObject/VRCAvatarBuildServerTool/BuildToServer")]
        public static async Task Do()
        {
            var prefab = Selection.activeGameObject;
            if (PrefabUtility.IsAnyPrefabInstanceRoot(prefab) is false) { return; }
            Debug.Log("That is InstanceRoot");

            var isAsset = AssetDatabase.Contains(prefab);
            Debug.Log("That Contains-AssetDataBase is" + isAsset);

            var builded = UnityEngine.Object.Instantiate(prefab);
#if NDMF
            try
            {
                if (AvatarBuildClientConfiguration.instance.ClientSideNDMFExecution) nadena.dev.ndmf.AvatarProcessor.ProcessAvatar(builded);
            }
            catch (Exception e) { Debug.LogException(e); }
#endif
            var targetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/" + builded.name + ".prefab");

            byte[] internalBinary = null;
            try
            {
                PrefabUtility.SaveAsPrefabAsset(builded, targetPath);
                var targetGUID = AssetDatabase.AssetPathToGUID(targetPath);

                //ここで一部の依存関係が正しく include されない Shader とかの類を その AvatarRoot にコンポーネントをつけさせる形で追加できるといいね

                var transferAssets = GetDependenciesWithFiltered(targetPath);

                internalBinary = AssetTransferProtocol.EncodeAssetsAndTargetGUID(transferAssets, new string[] { targetGUID });
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(builded);
                AssetDatabase.DeleteAsset(targetPath);
            }

            try
            {
                _client ??= new HttpClient() { Timeout = TimeSpan.FromSeconds(5) };
                var response = await _client.PostAsync(AvatarBuildClientConfiguration.instance.BuildServerURL, new ByteArrayContent(internalBinary));
                Debug.Log("POST Response :" + response.StatusCode);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            Debug.Log("Exit");
        }

        private static string[] GetDependenciesWithFiltered(string targetPrefabPath)
        {
            return AssetDatabase.GetDependencies(targetPrefabPath)
            .Where(path => path.StartsWith("Packages") is false || path.StartsWith("Packages/nadena.dev.ndmf/__Generated"))
            .ToArray();
        }
    }
}
