using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

/// <summary>Streaming ZIP preparation. No Unity or native Vosk calls.</summary>
internal static class VoskModelPreparation
{
    internal const long GiB = 1024L * 1024 * 1024;
    internal const long DiskReserve = 2 * GiB;
    internal const long MaxExpandedBytes = 8 * GiB;
    const int MaxEntries = 10000;

    internal static string Resolve(string configuredPath, string streamingAssetsPath, string persistentDataPath,
        CancellationToken cancellation, Action<string> report, Func<string, long> availableSpace = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(configuredPath)) throw new InvalidDataException("Model path is empty.");
        string sourcePath = Path.GetFullPath(Path.Combine(streamingAssetsPath, configuredPath));
        if (Directory.Exists(sourcePath))
        {
            Validate(sourcePath);
            report("Using model folder: " + sourcePath);
            return sourcePath;
        }
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Model folder or ZIP was not found.", sourcePath);
        if (!string.Equals(Path.GetExtension(sourcePath), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Model archive must use the .zip extension.");

        Directory.CreateDirectory(persistentDataPath);
        string target = Path.GetFullPath(Path.Combine(persistentDataPath, Path.GetFileNameWithoutExtension(sourcePath)));
        // File sharing guards preparation across Editor/player processes as well as components.
        using (var preparationLock = new FileStream(target + ".prepare.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            if (Directory.Exists(target))
            {
                Validate(target);
                report("Reusing extracted model (ZIP extraction skipped): " + target);
                return target;
            }
            // Do not accumulate a new multi-GB extraction after an abnormal exit.
            foreach (string stale in Directory.GetDirectories(persistentDataPath, Path.GetFileName(target) + ".extracting-*"))
                throw new IOException("An unfinished extraction exists. Inspect/remove it before retrying: " + stale);

            using (var archiveFile = File.OpenRead(sourcePath))
            using (var archive = new ZipArchive(archiveFile, ZipArchiveMode.Read, false))
            {
                if (archive.Entries.Count > MaxEntries) throw new InvalidDataException("ZIP contains too many entries.");
                long expandedBytes = 0;
                foreach (var entry in archive.Entries)
                {
                    cancellation.ThrowIfCancellationRequested();
                    expandedBytes = checked(expandedBytes + entry.Length);
                    if (expandedBytes > MaxExpandedBytes) throw new InvalidDataException("ZIP expanded size exceeds the 8 GiB limit.");
                }
                EnsureDiskSpace(target, checked(expandedBytes + DiskReserve), availableSpace);
                string temporary = target + ".extracting-" + Guid.NewGuid().ToString("N");
                string root = Path.GetFullPath(temporary) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(temporary);
                bool committed = false;
                try
                {
                    report("Extracting ZIP once: " + expandedBytes + " bytes to " + temporary);
                    string commonRoot = FindCommonRoot(archive);
                    var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    byte[] buffer = new byte[64 * 1024];
                    long sinceSpaceCheck = 0;
                    foreach (var entry in archive.Entries)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        string relative = entry.FullName.Replace('\\', '/');
                        if (Path.IsPathRooted(relative) || relative.Contains(":"))
                            throw new InvalidDataException("ZIP contains a rooted or alternate-stream path.");
                        if (!string.IsNullOrEmpty(commonRoot) && relative.StartsWith(commonRoot + "/", StringComparison.Ordinal))
                            relative = relative.Substring(commonRoot.Length + 1);
                        if (string.IsNullOrEmpty(relative)) continue;
                        string output = Path.GetFullPath(Path.Combine(temporary, relative.Replace('/', Path.DirectorySeparatorChar)));
                        if (!output.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("ZIP contains an escaping path: " + entry.FullName);
                        // Never create symlinks from archive entries.
                        if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                            throw new InvalidDataException("ZIP symlinks are not supported.");
                        if (relative.EndsWith("/", StringComparison.Ordinal))
                        {
                            Directory.CreateDirectory(output);
                            continue;
                        }
                        if (!destinations.Add(output)) throw new InvalidDataException("ZIP contains duplicate paths.");
                        Directory.CreateDirectory(Path.GetDirectoryName(output));
                        long copied = 0;
                        using (var input = entry.Open())
                        using (var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            int read;
                            while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                            {
                                cancellation.ThrowIfCancellationRequested();
                                copied = checked(copied + read);
                                if (copied > entry.Length) throw new InvalidDataException("ZIP entry exceeds its declared length.");
                                sinceSpaceCheck += read;
                                if (sinceSpaceCheck >= 16 * 1024 * 1024)
                                {
                                    EnsureDiskSpace(target, DiskReserve, availableSpace);
                                    sinceSpaceCheck = 0;
                                }
                                destination.Write(buffer, 0, read);
                            }
                        }
                        if (copied != entry.Length) throw new InvalidDataException("ZIP entry is truncated.");
                    }
                    cancellation.ThrowIfCancellationRequested();
                    Validate(temporary);
                    EnsureDiskSpace(target, DiskReserve, availableSpace);
                    Directory.Move(temporary, target);
                    committed = true;
                    report("ZIP extraction completed: " + target);
                    return target;
                }
                finally
                {
                    // Delete only this attempt's verified child directory; never an existing model.
                    if (!committed && Directory.Exists(temporary))
                    {
                        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(temporary)),
                            Path.GetFullPath(persistentDataPath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                            throw new IOException("Refusing cleanup outside the model cache.");
                        Directory.Delete(temporary, true);
                    }
                }
            }
        }
    }

    static string FindCommonRoot(ZipArchive archive)
    {
        string root = null;
        foreach (var entry in archive.Entries)
        {
            string path = entry.FullName.Replace('\\', '/');
            int slash = path.IndexOf('/');
            if (slash <= 0) return string.Empty;
            string candidate = path.Substring(0, slash);
            if (root != null && root != candidate) return string.Empty;
            root = candidate;
        }
        return root ?? string.Empty;
    }

    internal static void Validate(string path)
    {
        if (!Nonempty(Path.Combine(path, "am", "final.mdl")) ||
            !Nonempty(Path.Combine(path, "conf", "mfcc.conf")) ||
            !Nonempty(Path.Combine(path, "conf", "model.conf")) ||
            !(Nonempty(Path.Combine(path, "graph", "HCLG.fst")) ||
              (Nonempty(Path.Combine(path, "graph", "HCLr.fst")) && Nonempty(Path.Combine(path, "graph", "Gr.fst")))))
            throw new InvalidDataException("Model files are missing or empty. Existing cache will not be overwritten: " + path);
    }

    static bool Nonempty(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

    internal static long ModelBytes(string path, CancellationToken cancellation)
    {
        long size = 0;
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Model symlink directories are not supported.");
            foreach (string file in Directory.GetFiles(directory)) size = checked(size + new FileInfo(file).Length);
            if (size > MaxExpandedBytes) throw new IOException("Model exceeds the 8 GiB limit.");
            foreach (string child in Directory.GetDirectories(directory)) pending.Push(child);
        }
        return size;
    }

    internal static void EnsureDiskSpace(string path, long required, Func<string, long> availableSpace = null)
    {
        long available = availableSpace != null ? availableSpace(path) : new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))).AvailableFreeSpace;
        if (available < required)
            throw new IOException("Insufficient disk space. Required " + required + " bytes, available " + available + ". Vosk stopped before further allocation.");
    }
}
