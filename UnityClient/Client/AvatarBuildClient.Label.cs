#nullable enable
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace net.rs64.VRCAvatarBuildServerTool.Client
{
    public static partial class AvatarBuildClient
    {
        const string LABEL_WINDOWS = "VABST-Windows";
        const string LABEL_ANDROID = "VABST-Android";
        const string LABEL_IOS = "VABST-IOS";

        [MenuItem("Assets/VRCAvatarBuildServerTool/Others/BuildToServer-from-Label")]
        public static void DoWithLabel() { DoWithLabelImpl(); }
        [MenuItem("Assets/VRCAvatarBuildServerTool/Others/BuildToServer-from-Label (ClientSideNDMFExecution)")]
        public static void DoWithLabelWithNDMF() { DoWithLabelImpl(true); }
        public static async void DoWithLabelImpl(bool clientSideNDMFExecution = false)
        {
            var buildTargetsWin = AssetDatabase.FindAssets($"l:{LABEL_WINDOWS}");
            var buildTargetsAnd = AssetDatabase.FindAssets($"l:{LABEL_ANDROID}");
            var buildTargetsIos = AssetDatabase.FindAssets($"l:{LABEL_IOS}");

            var targets = buildTargetsWin
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(t => AssetDatabase.LoadAssetAtPath<GameObject>(t))
                    .Where(t => t != null)
                    .Select(t => new BuildTargetRecord(t, BuildTargetPlatform.Windows))
                .Concat(
                    buildTargetsAnd
                        .Select(AssetDatabase.GUIDToAssetPath)
                        .Select(t => AssetDatabase.LoadAssetAtPath<GameObject>(t))
                        .Where(t => t != null)
                        .Select(t => new BuildTargetRecord(t, BuildTargetPlatform.Android))
                )
                .Concat(
                    buildTargetsIos
                        .Select(AssetDatabase.GUIDToAssetPath)
                        .Select(t => AssetDatabase.LoadAssetAtPath<GameObject>(t))
                        .Where(t => t != null)
                        .Select(t => new BuildTargetRecord(t, BuildTargetPlatform.IOS))
                )
                .ToArray();

            await DoSendImpl(targets, clientSideNDMFExecution);
        }

        [MenuItem("Assets/VRCAvatarBuildServerTool/Others/Add-BuildTarget-Windows-Label")]
        public static void AddLabelWithWindows()
        {
            foreach (var a in Selection.objects)
            {
                if (AssetDatabase.Contains(a) is false) { continue; }
                if (a is not GameObject gameObject) { continue; }
                var label = AssetDatabase.GetLabels(gameObject).Append(LABEL_WINDOWS).Distinct().ToArray();
                AssetDatabase.SetLabels(gameObject, label);
            }
        }
        [MenuItem("Assets/VRCAvatarBuildServerTool/Others/Add-BuildTarget-Moblie-Label")]
        public static void AddLabelWithMoblie()
        {
            foreach (var a in Selection.objects)
            {
                if (AssetDatabase.Contains(a) is false) { continue; }
                if (a is not GameObject gameObject) { continue; }
                var label = AssetDatabase.GetLabels(gameObject).Append(LABEL_ANDROID).Append(LABEL_IOS).Distinct().ToArray();
                AssetDatabase.SetLabels(gameObject, label);
            }
        }



    }
}
