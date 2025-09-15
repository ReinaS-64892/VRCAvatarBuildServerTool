using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace net.rs64.VRCAvatarBuildServerTool.Server;

public class CashFileManager
{
    private readonly string _directory;
    private HashAlgorithm sha;

    public CashFileManager(string directory)
    {
        _directory = directory;
        sha = SHA1.Create();
        if (Directory.Exists(_directory) is false) { Directory.CreateDirectory(_directory); }
    }

    public bool HasFile(string hashBase64)
    {
        var path = Path.Combine(_directory, EscapeFileName(hashBase64));
        var exist = File.Exists(path);
        return exist;
    }
    public string GetFile(string hashBase64)
    {
        if (HasFile(hashBase64) is false) { throw new FileNotFoundException(hashBase64); }
        return Path.Combine(_directory, EscapeFileName(hashBase64));
    }
    public void AddFile(byte[] file)
    {
        var hash = sha.ComputeHash(file);
        var hashBase64 = Convert.ToBase64String(hash);

        var filePath = Path.Combine(_directory, EscapeFileName(hashBase64));

        if (File.Exists(filePath)) { return; }
        File.WriteAllBytes(filePath, file);
    }

    string EscapeFileName(string name)
    {
        return name.Replace("/", "\\");
    }
}
