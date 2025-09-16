#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using VRC.Core;
using VRC.SDK3A.Editor;
using VRC.SDKBase.Editor.Api;

namespace net.rs64.VRCAvatarBuildServerTool.Uploader
{
    static class VRCSDKController
    {

        static volatile bool s_isTaskDoing;//しぶわいねぇ ... うーん
        static Queue<UploadTask> UploadTaskQueue = new();
        const string TemporaryThumbnailGUID = "bf34225cf0fe6be64b94e281fb3a55ce";
        public static void EnQueue(UploadTask task)
        {
            lock (UploadTaskQueue)
            {
                UploadTaskQueue.Enqueue(task);
            }
        }
        public static void BuilderLoop()
        {
            if (s_isTaskDoing) { return; }
            UploadTask task;
            lock (UploadTaskQueue) { UploadTaskQueue.TryDequeue(out task); }

            if (task is not null) CallAsyncVoid(task);

            static async void CallAsyncVoid(UploadTask task)
            {
                try
                {
                    s_isTaskDoing = true;
                    var sdk = default(IVRCSdkAvatarBuilderApi);

                    if (VRCSdkControlPanel.window == null) { EditorApplication.ExecuteMenuItem("VRChat SDK/Show Control Panel"); }
                    while (VRCSdkControlPanel.TryGetBuilder(out sdk) is false) { await Task.Delay(100); }
                    if (sdk is null) { Debug.LogError("sdk がない !!! なぜ !!!"); return; }
                    if (await TryLogin() is false) { Debug.LogError("何らかの原因でログインできなかったよ〜！"); return; }

                    await UploadFromTransferred(task);
                }
                catch (Exception e) { Debug.LogException(e); }
                finally
                {
                    s_isTaskDoing = false;
                }
            }
        }

        static async Task UploadFromTransferred(UploadTask task)
        {
            var bundlePath = task.BlueprintID + ".vrca";
            File.WriteAllBytes(bundlePath, task.AssetBundle);

            try
            {
                var vrcAvatar = await VRCApi.GetAvatar(task.BlueprintID);
                if (task.IsNewAvatar) { _ = await VRCApi.CreateNewAvatar(task.BlueprintID, vrcAvatar, bundlePath, AssetDatabase.GUIDToAssetPath(TemporaryThumbnailGUID)); }
                else { _ = await VRCApi.UpdateAvatarBundle(task.BlueprintID, vrcAvatar, bundlePath); }
            }
            finally
            {
                File.Delete(bundlePath);
            }
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
        public class UploadTask
        {
            public bool IsNewAvatar;
            public string BlueprintID;
            public byte[] AssetBundle;

            public UploadTask(bool isNewAvatar, string blueprintID, byte[] assetBundle)
            {
                IsNewAvatar = isNewAvatar;
                BlueprintID = blueprintID;
                AssetBundle = assetBundle;
            }
        }
    }

}
