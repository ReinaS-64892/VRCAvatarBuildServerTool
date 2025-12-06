using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using VRC.Core;
using VRC.SDK3A.Editor;
using VRC.SDKBase.Editor.Api;

namespace net.rs64.VRCAvatarBuildServerTool.Server
{
    public static class AvatarBuildServer
    {
        [InitializeOnLoadMethod]
        public static async void DoUploaderServer()
        {
            var buildTargetGUIDFilePath = "BuildTask";
            if (File.Exists(buildTargetGUIDFilePath) is false) { return; }

            try
            {
                var guid = File.ReadAllText(buildTargetGUIDFilePath);

                var sdk = default(IVRCSdkAvatarBuilderApi);

                if (VRCSdkControlPanel.window == null)
                {
                    // EditorApplication.ExecuteMenuItem("VRChat SDK/Show Control Panel");
                    // どうやら無理やり作ってあげれば大丈夫らしい ... 本当にこれで良いのか ... ???
                    VRCSdkControlPanel.window = ScriptableObject.CreateInstance<VRCSdkControlPanel>();
                    VRCSdkControlPanel.InitAccount();
                }
                while (VRCSdkControlPanel.TryGetBuilder(out sdk) is false)
                { await Task.Delay(100); }
                if (await TryLogin() is false) { Debug.LogError("何らかの原因でログインできなかったよ〜！"); return; }

                await BuildToUploadFromGUID(sdk, guid);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                File.Delete(buildTargetGUIDFilePath);
                EditorApplication.Exit(0);
            }
        }

        const string TemporaryThumbnailGUID = "bf34225cf0fe6be64b94e281fb3a55ce";

        private static async Task BuildToUploadFromGUID(IVRCSdkAvatarBuilderApi sdk, string guid)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            var pipelineManager = prefab.GetComponent<PipelineManager>();

            var isNewAvatar = string.IsNullOrEmpty(pipelineManager.blueprintId);

            VRCAvatar vrcAvatar;
            if (isNewAvatar)
            {
                vrcAvatar = new()
                {
                    Name = prefab.name,
                    Description = "",
                    Tags = new List<string>(),
                    ReleaseStatus = "private"
                };
                vrcAvatar = await VRCApi.CreateAvatarRecord(vrcAvatar);
                if (string.IsNullOrEmpty(vrcAvatar.ID)) throw new Exception("Failed to reserve an avatar ID");
                pipelineManager.blueprintId = vrcAvatar.ID;
                EditorUtility.SetDirty(pipelineManager);
                PrefabUtility.SavePrefabAsset(prefab);
            }
            else vrcAvatar = await VRCApi.GetAvatar(pipelineManager.blueprintId);
            var thumbnailPath = isNewAvatar ? AssetDatabase.GUIDToAssetPath(TemporaryThumbnailGUID) : null;

            await sdk.BuildAndUpload(prefab, vrcAvatar, thumbnailPath);
            Debug.Log("upload:" + prefab.name);
        }

        // MIT LICENSE https://github.com/anatawa12/ContinuousAvatarUploader/blob/d6b8fd82fac6c4734664d57914d02a8092cb5dc7/LICENSE
        // Copyright (c) 2023 anatawa12
        // copy from CAU Uploader https://github.com/anatawa12/ContinuousAvatarUploader/blob/d6b8fd82fac6c4734664d57914d02a8092cb5dc7/Editor/Uploader.cs#L89-L114
        public static async Task<bool> TryLogin()
        {
            if (!ConfigManager.RemoteConfig.IsInitialized())
            {
                API.SetOnlineMode(true);
                ConfigManager.RemoteConfig.Init();
            }
            if (!APIUser.IsLoggedIn && ApiCredentials.Load())
            {
                var task = new TaskCompletionSource<bool>();
                APIUser.InitialFetchCurrentUser(c =>
                {
                    AnalyticsSDK.LoggedInUserChanged(c.Model as APIUser);
                    task.TrySetResult(true);
                }, e =>
                {
                    task.TrySetException(new Exception(e?.Error ?? "Unknown error"));
                });
                await task.Task;
            }
            return APIUser.IsLoggedIn;
        }
    }
}
