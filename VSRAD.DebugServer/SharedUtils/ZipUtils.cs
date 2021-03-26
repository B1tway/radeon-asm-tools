﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace VSRAD.DebugServer.SharedUtils
{
    public static class ZipUtils
    {
        public static void UnpackToDirectory(string path, byte[] zipData, bool preserveTimestamps)
        {
            var destination = Directory.CreateDirectory(path);

            using (var stream = new MemoryStream(zipData))
            using (var archive = new ZipArchive(stream))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    var entryDestPath = Path.Combine(destination.FullName, entry.FullName);
                    if (!entryDestPath.StartsWith(destination.FullName, StringComparison.Ordinal))
                        throw new IOException("Zip archive contains absolute paths");

                    if (entryDestPath.EndsWith("/", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(entryDestPath);
                        if (preserveTimestamps)
                            Directory.SetLastWriteTimeUtc(entryDestPath, entry.LastWriteTime.UtcDateTime);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(entryDestPath));
                        entry.ExtractToFile(entryDestPath, overwrite: true);
                        // ExtractToFile applies the entry's last write time by default
                        if (!preserveTimestamps)
                            File.SetLastWriteTimeUtc(entryDestPath, DateTime.UtcNow);
                    }
                }
            }
        }

        #region Test utils
        public static byte[] CreateZipArchive(IEnumerable<(byte[] Data, string Name, DateTime LastWriteTime)> items)
        {
            using (var memStream = new MemoryStream())
            {
                using (var archive = new ZipArchive(memStream, ZipArchiveMode.Create, false))
                {
                    foreach (var (data, name, lastWriteTime) in items)
                    {
                        var entry = archive.CreateEntry(name);
                        entry.LastWriteTime = lastWriteTime;
                        using (var entryStream = entry.Open())
                            entryStream.Write(data, 0, data.Length);
                    }
                }
                return memStream.ToArray();
            }
        }

        public static IEnumerable<(string Path, byte[] Data, DateTime LastWriteTimeUtc)> ReadZipItems(byte[] zipBytes)
        {
            using (var stream = new MemoryStream(zipBytes))
            using (var archive = new ZipArchive(stream))
            {
                foreach (var e in archive.Entries)
                {
                    using (var s = new MemoryStream())
                    using (var dataStream = e.Open())
                    {
                        dataStream.CopyTo(s);
                        yield return (e.FullName, s.ToArray(), e.LastWriteTime.UtcDateTime);
                    }
                }
            }
        }
        #endregion
    }
}
