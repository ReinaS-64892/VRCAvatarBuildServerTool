#nullable enable
#if CAU
using System.Collections.Generic;
using UnityEngine;
using Debug = UnityEngine.Debug;

using Anatawa12.ContinuousAvatarUploader.Editor;

namespace net.rs64.VRCAvatarBuildServerTool.Client
{
    public static partial class AvatarBuildClient
    {
        private static List<BuildTargetRecord> GetPrefabFromCAU(AvatarUploadSettingOrGroup aus, List<BuildTargetRecord>? avatarRoots = null)
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

                            avatarRoots.Add(new(avatarRoot.gameObject, platform));
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
    }
}
#endif
