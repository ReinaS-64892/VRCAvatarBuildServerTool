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

        public bool HasFile(string hashBase64)
        {
            var path = Path.Combine(_directory, EscapeFileName(hashBase64));
            var exist = File.Exists(path);
            return exist;
        }
        public string GetFilePath(string hashBase64)
        {
            if (HasFile(hashBase64) is false) { throw new FileNotFoundException(hashBase64); }
            return Path.Combine(_directory, EscapeFileName(hashBase64));
        }
        public void AddFile(byte[] file)
        {
            var hash = GetSha().ComputeHash(file);
            var hashBase64 = Convert.ToBase64String(hash);

            var filePath = Path.Combine(_directory, EscapeFileName(hashBase64));

            if (File.Exists(filePath)) { return; }
            File.WriteAllBytes(filePath, file);
        }

        string EscapeFileName(string name)
        {
            return name.Replace("/", "\\");
        }

        public Task<byte[]> GetFile(string hash)
        {
            return File.ReadAllBytesAsync(GetFilePath(hash));
        }
    }
}
