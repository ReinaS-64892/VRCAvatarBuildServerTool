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

namespace net.rs64.VRCAvatarBuildServerTool.BuildRunner
{
    static class VRCSDKController
    {

        static volatile bool s_isTaskDoing;//しぶわいねぇ ... うーん
        static Queue<string> _buildTaskQueue = new();
        const string TemporaryThumbnailGUID = "bf34225cf0fe6be64b94e281fb3a55ce";
        public static void EnQueue(string task)
        {
            lock (_buildTaskQueue)
            {
                _buildTaskQueue.Enqueue(task);
            }
        }
        public static void BuilderLoop()
        {
            if (s_isTaskDoing) { return; }

            string task;
            lock (_buildTaskQueue) { _buildTaskQueue.TryDequeue(out task); }

            if (task is not null) CallAsyncVoid(task);

            static async void CallAsyncVoid(string task)
            {
                try
                {
                    s_isTaskDoing = true;
                    AvatarBuildRunner.ServerListenDisable();
                    var sdk = default(IVRCSdkAvatarBuilderApi);

                    if (VRCSdkControlPanel.window == null) { EditorApplication.ExecuteMenuItem("VRChat SDK/Show Control Panel"); }
                    while (VRCSdkControlPanel.TryGetBuilder(out sdk) is false) { await Task.Delay(100); }
                    if (sdk is null) { Debug.LogError("sdk がない !!! なぜ !!!"); return; }

                    await BuildFromTransferred(sdk, task);
                }
                catch (Exception e) { Debug.LogException(e); }
                finally
                {
                    s_isTaskDoing = false;

                    lock (_buildTaskQueue)
                    {
                        if (_buildTaskQueue.Count is 0)
                        {
                            AvatarBuildRunner.ServerListenEnable();
                        }
                    }
                }
            }
        }

        static async Task BuildFromTransferred(IVRCSdkAvatarBuilderApi sdk, string task)
        {
            var buildTargetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(task));

            var vrcPipelineManager = buildTargetPrefab.GetComponent<PipelineManager>();
            if (string.IsNullOrWhiteSpace(vrcPipelineManager.blueprintId))
            {
                var newID = await AvatarBuildRunner.GetNewBlueprintID(buildTargetPrefab.name);
                if (newID is null) { throw new Exception("filed get new id"); }
                vrcPipelineManager.blueprintId = newID;

                EditorUtility.SetDirty(vrcPipelineManager);
                PrefabUtility.SavePrefabAsset(buildTargetPrefab);

                var bundlePath = await sdk.Build(buildTargetPrefab);

                await AvatarBuildRunner.PostUploadRequest(new UploadRequest()
                {
                    IsNewAvatar = true,
                    BlueprintID = vrcPipelineManager.blueprintId,
                    AssetBundleBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(bundlePath))
                });
            }
            else
            {
                var bundlePath = await sdk.Build(buildTargetPrefab);

                await AvatarBuildRunner.PostUploadRequest(new UploadRequest()
                {
                    IsNewAvatar = false,
                    BlueprintID = vrcPipelineManager.blueprintId,
                    AssetBundleBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(bundlePath))
                });
            }
        }
    }

}
