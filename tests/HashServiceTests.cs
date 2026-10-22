using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using FileChecksum;

internal static class HashServiceTests
{
    private const string EmptySha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string AbcSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private static string temporaryParent;
    private static string fixtureDirectory;
    private static int passed;
    private static int failed;

    private static int Main()
    {
        string testsDirectory = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName;
        temporaryParent = Path.GetFullPath(Path.Combine(testsDirectory, ".tmp"));
        fixtureDirectory = Path.GetFullPath(Path.Combine(temporaryParent, Guid.NewGuid().ToString("N")));
        RequireChildPath(fixtureDirectory, temporaryParent);
        Directory.CreateDirectory(fixtureDirectory);

        try
        {
            Run("NIST empty SHA-256", EmptyFile);
            Run("NIST abc SHA-256", AbcFile);
            Run("binary file spanning multiple read buffers", MultiBufferBinary);
            Run("Unicode filename and directory", UnicodePath);
            Run("input remains byte-for-byte unchanged and handle is released", PreservesInput);
            Run("byte progress is bounded, monotonic and complete", ByteProgress);
            Run("missing file fails without creating it", MissingFile);
            Run("exclusively locked file fails", LockedFile);
            Run("pre-cancelled operation fails and releases resources", PreCancelled);
            Run("cancellation from a synchronous progress callback", CancelDuringRead);
            Run("SHA-256 normalization validates exactly 64 ASCII hex digits", NormalizeHash);
            Run("checksum comparison validates both operands", CompareHashes);
            Run("manifest uses lowercase checksum, basename and LF", ManifestFormatting);
        }
        finally
        {
            // Never recursively delete a path outside this run's unique fixture directory.
            RequireChildPath(fixtureDirectory, temporaryParent);
            if (Directory.Exists(fixtureDirectory))
                Directory.Delete(fixtureDirectory, true);
        }

        Console.WriteLine("RESULT: {0} passed, {1} failed", passed, failed);
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            passed++;
            Console.WriteLine("PASS: " + name);
        }
        catch (Exception exception)
        {
            failed++;
            Console.WriteLine("FAIL: " + name);
            Console.WriteLine(exception.ToString());
        }
    }

    private static void EmptyFile()
    {
        string path = WriteFixture("empty.bin", new byte[0]);
        HashResult result = HashService.Compute(path, CancellationToken.None, null);
        Equal(EmptySha256, result.Sha256, "Empty file digest");
        Equal(0L, result.Length, "Empty file length");
        Equal(Path.GetFullPath(path), result.FilePath, "Resolved file path");
    }

    private static void AbcFile()
    {
        string path = WriteFixture("abc.txt", Encoding.ASCII.GetBytes("abc"));
        HashResult result = HashService.Compute(path, CancellationToken.None, null);
        Equal(AbcSha256, result.Sha256, "abc digest (no BOM or trailing newline)");
        Equal(3L, result.Length, "abc byte length");
    }

    private static void MultiBufferBinary()
    {
        byte[] content = new byte[7 * 1024 * 1024 + 177];
        new Random(76381).NextBytes(content);
        string path = WriteFixture("multi-buffer.bin", content);
        string independentlyComputed;
        using (SHA256 sha256 = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
            independentlyComputed = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();

        HashResult result = HashService.Compute(path, CancellationToken.None, null);
        Equal(independentlyComputed, result.Sha256, "Digest against direct .NET ComputeHash baseline");
        Equal((long)content.Length, result.Length, "Binary byte length including partial final buffer");
    }

    private static void UnicodePath()
    {
        string directory = Path.Combine(fixtureDirectory, "ภาษาไทย_日本語");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ทดสอบ checksum ✓.txt");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("abc"));
        HashResult result = HashService.Compute(path, CancellationToken.None, null);
        Equal(AbcSha256, result.Sha256, "Digest through Unicode path");
        Equal(Path.GetFullPath(path), result.FilePath, "Unicode path preserved");
    }

    private static void PreservesInput()
    {
        byte[] original = new byte[8193];
        new Random(9347).NextBytes(original);
        string path = WriteFixture("must-not-change.bin", original);
        DateTime lastWritten = File.GetLastWriteTimeUtc(path);
        HashService.Compute(path, CancellationToken.None, null);
        byte[] after = File.ReadAllBytes(path);
        Equal(original.Length, after.Length, "Input size after hashing");
        for (int index = 0; index < original.Length; index++)
            if (original[index] != after[index])
                throw new Exception("Input byte changed at offset " + index);
        Equal(lastWritten, File.GetLastWriteTimeUtc(path), "Input modification timestamp");
        AssertExclusiveOpen(path);
    }

    private static void ByteProgress()
    {
        byte[] content = new byte[2 * 1024 * 1024 + 19];
        string path = WriteFixture("progress.bin", content);
        List<long> reports = new List<long>();
        HashResult result = HashService.Compute(path, CancellationToken.None, new InlineProgress(reports.Add));
        True(reports.Count > 0, "At least the final byte count must be reported");
        long previous = 0;
        foreach (long value in reports)
        {
            True(value >= previous && value <= result.Length, "Progress must be monotonic and within file length");
            previous = value;
        }
        Equal(result.Length, reports[reports.Count - 1], "Final byte progress");

        reports.Clear();
        string empty = WriteFixture("empty-progress.bin", new byte[0]);
        HashService.Compute(empty, CancellationToken.None, new InlineProgress(reports.Add));
        True(reports.Count > 0, "Empty file must also report completion");
        Equal(0L, reports[reports.Count - 1], "Empty file progress");
    }

    private static void MissingFile()
    {
        string path = Path.Combine(fixtureDirectory, "does-not-exist.bin");
        Throws<FileNotFoundException>(delegate { HashService.Compute(path, CancellationToken.None, null); });
        True(!File.Exists(path), "Hashing must not create missing input");
    }

    private static void LockedFile()
    {
        string path = WriteFixture("locked.bin", Encoding.ASCII.GetBytes("abc"));
        using (FileStream exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Throws<IOException>(delegate { HashService.Compute(path, CancellationToken.None, null); });
        AssertExclusiveOpen(path);
    }

    private static void PreCancelled()
    {
        string path = WriteFixture("pre-cancelled.bin", Encoding.ASCII.GetBytes("abc"));
        using (CancellationTokenSource cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            Throws<OperationCanceledException>(delegate { HashService.Compute(path, cancellation.Token, null); });
        }
        AssertExclusiveOpen(path);
    }

    private static void CancelDuringRead()
    {
        // A large fixture ensures a throttled progress update occurs before EOF,
        // rather than only exercising cancellation after the final report.
        const long length = 256L * 1024 * 1024 + 31;
        string path = Path.Combine(fixtureDirectory, "cancel-during-read.bin");
        byte[] block = new byte[1024 * 1024];
        new Random(59251).NextBytes(block);
        using (FileStream output = File.Create(path))
        {
            long remaining = length;
            while (remaining > 0)
            {
                int count = (int)Math.Min(block.Length, remaining);
                output.Write(block, 0, count);
                remaining -= count;
            }
        }

        long cancellationOffset = -1;
        using (CancellationTokenSource cancellation = new CancellationTokenSource())
        {
            InlineProgress progress = new InlineProgress(delegate(long bytes)
            {
                if (bytes > 0 && bytes < length && cancellationOffset < 0)
                {
                    cancellationOffset = bytes;
                    cancellation.Cancel();
                }
            });
            Throws<OperationCanceledException>(delegate { HashService.Compute(path, cancellation.Token, progress); });
        }
        True(cancellationOffset > 0 && cancellationOffset < length, "Cancellation must be triggered before EOF");
        AssertExclusiveOpen(path);
    }

    private static void NormalizeHash()
    {
        string normalized;
        True(HashService.TryNormalizeHash(" \t" + AbcSha256.ToUpperInvariant() + "\r\n", out normalized), "Edge whitespace and uppercase are accepted");
        Equal(AbcSha256, normalized, "Normalized checksum is lowercase");
        string[] invalid = new string[]
        {
            null, "", " ", AbcSha256.Substring(1), AbcSha256 + "0",
            "g" + AbcSha256.Substring(1), "-" + AbcSha256.Substring(1),
            AbcSha256.Substring(0, 31) + " " + AbcSha256.Substring(32),
            "\uff42" + AbcSha256.Substring(1), "\u0661" + AbcSha256.Substring(1)
        };
        foreach (string input in invalid)
        {
            normalized = "sentinel";
            True(!HashService.TryNormalizeHash(input, out normalized), "Invalid hash rejected: " + (input ?? "<null>"));
            Equal<string>(null, normalized, "Failed normalization clears output");
        }
    }

    private static void CompareHashes()
    {
        True(HashService.Matches(AbcSha256, " \t" + AbcSha256.ToUpperInvariant() + "\n"), "Valid comparison ignores case and edge whitespace");
        True(!HashService.Matches(AbcSha256, EmptySha256), "Different valid digests mismatch");
        True(!HashService.Matches(null, AbcSha256), "Null actual rejects");
        True(!HashService.Matches(AbcSha256, null), "Null expected rejects");
        True(!HashService.Matches("invalid", "invalid"), "Equal invalid strings cannot match");
    }

    private static void ManifestFormatting()
    {
        string name = "ไฟล์ with spaces.txt";
        HashResult result = new HashResult(Path.Combine(fixtureDirectory, name), 3L, AbcSha256.ToUpperInvariant());
        Equal(AbcSha256 + "  " + name + "\n", HashService.FormatManifest(result), "Portable single-entry SHA-256 manifest");
        Throws<ArgumentNullException>(delegate { HashService.FormatManifest(null); });
        HashResult multiline = new HashResult(fixtureDirectory + Path.DirectorySeparatorChar + "bad\nfilename.txt", 0L, EmptySha256);
        Throws<ArgumentException>(delegate { HashService.FormatManifest(multiline); });
    }

    private static string WriteFixture(string name, byte[] content)
    {
        string path = Path.Combine(fixtureDirectory, name);
        RequireChildPath(path, fixtureDirectory);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static void AssertExclusiveOpen(string path)
    {
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            True(stream.CanRead, "Input handle must be released");
    }

    private static void RequireChildPath(string candidate, string parent)
    {
        string prefix = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(candidate).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fixture path is outside the permitted test directory.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name + " was not thrown.");
    }

    private static void True(bool value, string description)
    {
        if (!value) throw new Exception(description);
    }

    private static void Equal<T>(T expected, T actual, string description)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(description + ": expected <" + expected + ">, got <" + actual + ">.");
    }

    private sealed class InlineProgress : IProgress<long>
    {
        private readonly Action<long> callback;
        public InlineProgress(Action<long> callback) { this.callback = callback; }
        public void Report(long value) { callback(value); }
    }
}
