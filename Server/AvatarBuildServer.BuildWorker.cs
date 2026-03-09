using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Threading.Tasks;
using net.rs64.VRCAvatarBuildServerTool.Transfer;

namespace net.rs64.VRCAvatarBuildServerTool.Server;

public partial class AvatarBuildServer
{
    private bool StatusRequested;

    class WorkerRun
    {
        public string PrefabName;
        Process _process;

        public WorkerRun(Config config, CacheFileManager cache, BuildRequest buildRequest, string projectPath, string logoutPath)
        {
            PrefabName = buildRequest.PrefabName;

            var assets = Path.Combine(projectPath, "Assets");
            foreach (var dir in Directory.EnumerateDirectories(assets)) { Directory.Delete(dir, true); }
            foreach (var path in Directory.EnumerateFiles(assets)) { File.Delete(path); }

            var packages = Path.Combine(projectPath, "Packages");
            foreach (var package in Directory.EnumerateDirectories(packages))
            {
                var jsonPath = Path.Combine(package, "package.json");
                if (File.Exists(jsonPath) is false) { Directory.Delete(package, true); continue; }

                static PackageJson? ReadPackageID(string path)
                {
                    using var jsonFile = File.OpenRead(path);
                    return JsonSerializer.Deserialize<PackageJson>(jsonFile);
                }

                var packageID = ReadPackageID(jsonPath)?.PackageID;
                if (packageID is null) { Directory.Delete(package, true); continue; }
                if (config.RetainPackageID.Contains(packageID) is false) { Directory.Delete(package, true); continue; }

                // it is retain package !!!
            }

            static string PathResolve(string path)
            {
                if (path.StartsWith("Assets/")) { return path.Substring("Assets/".Length); }
                if (path.StartsWith("Packages/nadena.dev.ndmf/__Generated")) { return path.Substring("Packages/nadena.dev.ndmf/__Generated".Length); }
                return path;
            }
            foreach (var assetFile in buildRequest.Assets)
            {
                var path = Path.Combine(assets, PathResolve(assetFile.Path));
                MaybeCreateFilePathDestinationDirectory(path);
                File.Copy(cache.GetFilePath(assetFile.Hash), path);
            }
            foreach (var package in buildRequest.Packages)
            {
                if (config.RetainPackageID.Contains(package.PackageID)) { continue; }

                foreach (var pf in package.Files)
                {
                    var extractPath = Path.Combine(projectPath, pf.Path);
                    MaybeCreateFilePathDestinationDirectory(extractPath);
                    File.Copy(cache.GetFilePath(pf.Hash), extractPath);
                }
            }
            CopyDirectory(UnityBuildRunnerPath, Path.Combine(packages, "AvatarBuildRunner"));

            var runnerReq = new RunnerRequest() { ParentServerURL = config.ListenAddress.First(), TargetGUID = buildRequest.BuildTarget };
            File.WriteAllText(Path.Combine(projectPath, "BuildTask"), JsonSerializer.Serialize(runnerReq));

            var buildTargetArgumentString = buildRequest.TargetPlatform switch
            {
                BuildTargetPlatform.Windows => "win",
                BuildTargetPlatform.Android => "android",
                BuildTargetPlatform.IOS => "ios",
                _ => "win",
            };

            _process = Process.Start(new ProcessStartInfo(
                config.UnityEditor,
                $"""
                -projectPath "{projectPath}" -batchmode -logFile {logoutPath} -buildTarget {buildTargetArgumentString}
                """
            )
            {
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            })!;

        }
        public bool HasExit() { return _process.HasExited; }
        public void MayKill()
        {
            if (_process.HasExited) { return; }
            _process.Kill();
        }
        public static bool IsReady(WorkerRun? worker)
        {
            if (worker is null) { return true; }
            return worker.HasExit();
        }


        static void MaybeCreateFilePathDestinationDirectory(string path)
        {
            var parentDirectory = new DirectoryInfo(path).Parent!.FullName;
            try { if (Directory.Exists(parentDirectory) is false) Directory.CreateDirectory(parentDirectory); }
            catch (Exception e) { Console.WriteLine(e); }
        }
    }
    class PackageJson
    {
        [JsonPropertyName("name")]
        public string PackageID { get; set; } = "";
    }
    async Task BuildWorkerManager(ChannelReader<BuildRequest> reader, CancellationToken token)
    {
        var workerProjects = new List<string>();
        var workers = new Dictionary<string, WorkerRun?>();
        try
        {
            var copyTask = new List<Task>();
            foreach (var index in Enumerable.Range(0, _config.MaxMultipleEditorCount))
            {
                var projectName = Path.GetFileName(_config.TemplateProject) + $"-{index}";
                var projectPath = Path.Combine(_instancePath, projectName);

                if (Directory.Exists(projectPath) is false)
                { copyTask.Add(Task.Run(() => CopyDirectory(_config.TemplateProject, projectPath))); }

                workerProjects.Add(projectPath);
            }
            await Task.WhenAll(copyTask);
            foreach (var p in workerProjects) { workers.Add(p, null); }


            var logDirectory = Path.Combine(_instancePath, "logs");
            if (Directory.Exists(logDirectory) is false) { Directory.CreateDirectory(logDirectory); }

            Console.WriteLine("BuildWorker Start!");
            var _requestQueue = new Queue<BuildRequest>();
            while (token.IsCancellationRequested is false)
            {
                var received = false;
                while (reader.TryRead(out var buildRequest))
                {
                    _requestQueue.Enqueue(buildRequest);
                    received = true;
                }
                if (received) ShowStatus();
                void ShowStatus()
                {
                    // show status
                    var str = new StringBuilder();
                    str.AppendLine("--- Status ---");
                    foreach (var q in _requestQueue) { str.AppendLine($"queuing : {q.PrefabName}"); }
                    foreach (var w in workers.Values.Where(v => v is not null).Where(v => WorkerRun.IsReady(v) is false))
                    { str.AppendLine($"building : {w!.PrefabName}"); }
                    str.AppendLine("--- end ---");
                    Console.WriteLine(str.ToString());
                }

                bool MayDoBuildRequest()
                {
                    foreach (var p in workerProjects)
                    {
                        if (WorkerRun.IsReady(workers[p]) && _requestQueue.TryDequeue(out var buildRequest))
                        {
                            workers[p] = new WorkerRun(_config, _cashManager, buildRequest, p, Path.Combine(_instancePath, "logs", DateTime.Now.ToString("yyyy.MM.dd.HH.mm.ss") + Guid.NewGuid() + ".txt"));
                            return true;
                        }
                    }
                    return false;
                }
                if (MayDoBuildRequest()) { ShowStatus(); }
                else { await Task.Delay(100); }
                if (StatusRequested) { StatusRequested = false; ShowStatus(); }
            }

        }
        catch (Exception e)
        {
            if (e is not OperationCanceledException)
                Console.WriteLine(e);
        }
        finally
        {
            foreach (var p in workers.Values) { p?.MayKill(); }
        }
    }

}
