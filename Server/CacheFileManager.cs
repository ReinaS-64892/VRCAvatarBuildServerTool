using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace net.rs64.VRCAvatarBuildServerTool.Server
{

    public class CacheFileManager
    {
        private string _directory;

        public CacheFileManager(string directory)
        {
            _directory = directory;
            if (Directory.Exists(_directory) is false) { Directory.CreateDirectory(_directory); }
        }

        private static SHA1 GetSha()
        {
            return SHA1.Create();
        }

        public bool HasFile(string hash)
        {
            return File.Exists(GetPath(hash));
        }
        public string GetFilePath(string hash)
        {
            if (HasFile(hash) is false) { throw new FileNotFoundException(hash); }
            return GetPath(hash);
        }
        public void AddFile(byte[] file)
        {
            var hash = GetSha().ComputeHash(file);
            var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

            var filePath = GetPath(hashString);

            if (File.Exists(filePath)) { return; }
            File.WriteAllBytes(filePath, file);
        }

        private string GetPath(string hash)
        {
            return Path.Combine(_directory, hash);
        }

        public Task<byte[]> GetFile(string hash)
        {
            return File.ReadAllBytesAsync(GetFilePath(hash));
        }
    }
}
