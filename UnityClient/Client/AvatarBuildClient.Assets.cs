#nullable enable
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using System.IO;
using System;
using System.Security.Cryptography;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace net.rs64.VRCAvatarBuildServerTool.Client
{
    public static partial class AvatarBuildClient
    {

        private static string[] GetDependenciesWithFiltered(params string[] targetPrefabPath)
        {
            return AssetDatabase.GetDependencies(targetPrefabPath)
            .Where(path => path.StartsWith("Packages") is false || path.StartsWith("Packages/nadena.dev.ndmf/__Generated"))
            .ToArray();
        }
        private static async Task<PathToHash[]> FindingDependencyPathToHash(string prefabPath)
        {
            var transferTargetFiles = GetDependenciesWithFiltered(prefabPath).SelectMany(p => new[] { p, p + ".meta" }).ToList();
            var targetFileHashesKv = await Task.WhenAll(transferTargetFiles.Select(p => Task.Run(async () => new PathToHash() { Path = p, Hash = await GetHash(p) })));
            return targetFileHashesKv;
        }
        public static async Task<string> GetHash(string filePath)
        {
            // SHA1 はスレッドセーフではない
            using var sha = SHA1.Create();
            var hash = sha.ComputeHash(await File.ReadAllBytesAsync(filePath));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }


        private static string CloneAndBuildToAsset(GameObject avatarRoot, bool doNDMFManualBake)
        {
            var builded = UnityEngine.Object.Instantiate(avatarRoot);

            MaybeDoNDMF(doNDMFManualBake, builded);

            var targetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/" + builded.name + ".prefab");
            PrefabUtility.SaveAsPrefabAsset(builded, targetPath);
            UnityEngine.Object.DestroyImmediate(builded);

            UnityForwardCompatibilityBreakingFix(targetPath);

            return targetPath;
        }

        [Conditional("NDMF")]
        private static void MaybeDoNDMF(bool doNDMFManualBake, GameObject builded)
        {
            try
            {
                if (doNDMFManualBake) nadena.dev.ndmf.AvatarProcessor.ProcessAvatar(builded);
            }
            catch (Exception e) { Debug.LogException(e); }
        }


        private static void UnityForwardCompatibilityBreakingFix(string prefabPath)
        {
#if UNITY_6000_4_OR_NEWER

            // 少なくとも Unity 6.4 では、 AudioSource の m_audioClip が m_Resource に移動したので、それを書き戻すパッチを当てる
            //AudioSource:
            //  m_audioClip: {fileID: 0}
            //  m_Resource: {fileID: 8300000, guid: eb14eab34389d79728894b2d6a4bf289, type: 3}

            var patchedPath = prefabPath + ".patched";
            {
                using var prefabStream = File.OpenText(prefabPath);
                using var patched = File.CreateText(patchedPath);
                while (prefabStream.EndOfStream is false)
                {
                    var line = prefabStream.ReadLine();
                    patched.WriteLine(line);
                    if (line is not "AudioSource:") { continue; }

                    while (true)
                    {
                        var audioSourcePropertyLine = prefabStream.ReadLine();

                        if (audioSourcePropertyLine.StartsWith("  m_Resource:"))
                        {
                            patched.WriteLine(audioSourcePropertyLine);
                            patched.WriteLine(audioSourcePropertyLine.Replace("  m_Resource:", "  m_audioClip:"));
                        }
                        else if (audioSourcePropertyLine.StartsWith("  m_audioClip:"))
                        {
                            // ignore
                        }
                        else if (audioSourcePropertyLine.StartsWith("--- "))
                        {
                            patched.WriteLine(audioSourcePropertyLine);
                            break;
                        }
                        else
                        {
                            patched.WriteLine(audioSourcePropertyLine);
                        }

                    }
                }
            }
            // File.Copy(prefabPath, prefabPath + ".origin", true);
            File.Copy(patchedPath, prefabPath, true);
            File.Delete(patchedPath);
#endif
        }

    }
}
