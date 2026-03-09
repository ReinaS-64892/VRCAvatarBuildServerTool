#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using System.IO;

namespace net.rs64.VRCAvatarBuildServerTool.Client
{
    public static partial class AvatarBuildClient
    {
        // くそざつキャッシング 何もしてないのでドメインリロードで破棄されるから問題なし。
        static Package[]? PackageCache = null;
        class PackageJson
        {
            public string name = "";
        }
        private static async Task<Package[]> GetCorectingPackageToHash(List<string> ingorePackageID)
        {
            if (PackageCache is null)
            {
                var packages = await CorectingPackageToHashImpl(ingorePackageID);
                PackageCache = packages;
            }
            return PackageCache;
        }
        private static async Task<Package[]> CorectingPackageToHashImpl(List<string> ingorePackageID)
        {
            var task = Directory.GetDirectories("Packages")
                .Select(pkg => Task.Run(async () =>
                {
                    var parentDir = pkg;// exsample "Packages/TexTransTool"

                    var pkjJson = Path.Combine(parentDir, "package.json");
                    if (File.Exists(pkjJson) is false) { return null; }
                    var pkjNameID = JsonUtility.FromJson<PackageJson>(File.ReadAllText(pkjJson)).name;
                    if (string.IsNullOrWhiteSpace(pkjNameID)) { return null; }
                    if (ingorePackageID.Contains(pkjNameID)) { return null; }

                    var fileTask = Directory.GetFiles(pkg, "*", SearchOption.AllDirectories)
                        .Select(p =>
                        {
                            // exsample p "Packages/TexTransTool/package.json"
                            return Task.Run(async () =>
                            {
                                if (p.Contains("/.git/")) { return null; }// .git は特別に無視します。
                                try
                                {
                                    return new PathToHash() { Path = p, Hash = await GetHash(p) };
                                }
                                catch // 壊れた symlink などを無視するために握りつぶし！！！
                                {
                                    return null;
                                }
                            }
                            );
                        });
                    var files = (await Task.WhenAll(fileTask)).OfType<PathToHash>().ToArray();
                    return new Package() { PackageID = pkjNameID, Files = files };
                }
                )
            );
            return (await Task.WhenAll(task)).OfType<Package>().ToArray();
        }

    }
}
