using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace net.rs64.VRCAvatarBuildServerTool.Transfer
{
    public static class AssetTransferProtocol
    {
        readonly static byte[] Signature = Encoding.ASCII.GetBytes("net.rs64.vrc-avatar-build-server-tool.internal-binary");
        readonly static uint ProtocolVersion = 0;
        public static Task<byte[]> EncodeAssetsAndTargetGUID(IEnumerable<string> includeAssetsPath, IEnumerable<string> buildTargetGUID)
        {
            var outMemStream = new MemoryStream();

            outMemStream.Write(Signature);

            Span<byte> protocolVersion = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(protocolVersion, ProtocolVersion);
            outMemStream.Write(protocolVersion);

            var jsonStr = JsonUtility.ToJson(new PacketDescription() { BuildTargetGUID = buildTargetGUID.ToList() });
            var jsonBytes = Encoding.UTF8.GetBytes(jsonStr);

            Span<byte> jsonStrLength = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(jsonStrLength, jsonBytes.Length);
            outMemStream.Write(jsonStrLength);
            outMemStream.Write(jsonBytes);

            return ReadFromFiles(includeAssetsPath, outMemStream);
        }

        private static async Task<byte[]> ReadFromFiles(IEnumerable<string> includeAssetsPath, MemoryStream ownedWriteTarget)
        {
            using var outMemStream = ownedWriteTarget;

            var readTask = includeAssetsPath
                                .SelectMany(path => new string[] { path, path + ".meta" })
                                .Where(File.Exists)
                                .Select(path => (path, File.ReadAllBytesAsync(path)))
                                .ToArray();

            using var memStream = new MemoryStream();
            using (var zipArchiver = new ZipArchive(memStream, ZipArchiveMode.Create, true))
                foreach (var readBytesTask in readTask)
                {
                    var entry = zipArchiver.CreateEntry(readBytesTask.path, System.IO.Compression.CompressionLevel.Optimal);

                    using var entryWriter = entry.Open();
                    await entryWriter.WriteAsync(await readBytesTask.Item2);
                }

            memStream.Seek(0, SeekOrigin.Begin);
            await memStream.CopyToAsync(outMemStream);// 直接 ZipArchive outMemStream を食わせてよいのかわかんなかったので暫定的な実装。
            return outMemStream.ToArray();
        }

        public static (MemoryStream zipStream, List<string> BuildTargetGUID) DecodeAssetsAndTargetGUID(byte[] bytes)
        {
            var i = 0;
            if (bytes.AsSpan(i, Signature.Length).SequenceEqual(Signature) is false) { throw new Exception(); }
            i += Signature.Length;

            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i, 4)) != ProtocolVersion) { throw new Exception(); }
            i += 4;

            var jsonStrLen = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i, 4));
            if (jsonStrLen == 0) { throw new Exception(); }
            i += 4;

            var jsonString = Encoding.UTF8.GetString(bytes.AsSpan(i, jsonStrLen));
            var packetDescription = JsonUtility.FromJson<PacketDescription>(jsonString);
            i += jsonStrLen;

            var memoryStream = new MemoryStream(bytes, i, bytes.Length - i, false);

            return (memoryStream, packetDescription.BuildTargetGUID);
        }
    }
    [Serializable]
    struct PacketDescription
    {
        public List<string> BuildTargetGUID;
    }
}
