using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace FileChecksum
{
    public sealed class HashResult
    {
        public string FilePath { get; private set; }
        public long Length { get; private set; }
        public string Sha256 { get; private set; }

        public HashResult(string filePath, long length, string sha256)
        {
            if (filePath == null)
                throw new ArgumentNullException("filePath");
            if (length < 0)
                throw new ArgumentOutOfRangeException("length");

            string normalized;
            if (!HashService.TryNormalizeHash(sha256, out normalized))
                throw new ArgumentException("A SHA-256 checksum must contain exactly 64 hexadecimal characters.", "sha256");

            FilePath = filePath;
            Length = length;
            Sha256 = normalized;
        }
    }

    public static class HashService
    {
        private const int ChunkSize = 1024 * 1024;
        private const long ProgressIntervalMilliseconds = 100;

        public static HashResult Compute(string path, CancellationToken cancellationToken, IProgress<long> progress)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path == null)
                throw new ArgumentNullException("path");

            string fullPath = Path.GetFullPath(path);
            cancellationToken.ThrowIfCancellationRequested();

            long length;
            long bytesRead = 0;
            string digest;
            byte[] buffer = new byte[ChunkSize];
            Stopwatch progressTimer = Stopwatch.StartNew();

            // Deny write and delete sharing while the checksum is being calculated.
            // A writer which already has the file open also makes this open fail.
            using (FileStream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, FileOptions.SequentialScan))
            using (SHA256 sha256 = SHA256.Create())
            {
                length = stream.Length;
                DateTime lastWriteTime = File.GetLastWriteTimeUtc(fullPath);

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = stream.Read(buffer, 0, buffer.Length);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (count == 0)
                        break;

                    sha256.TransformBlock(buffer, 0, count, buffer, 0);
                    bytesRead = checked(bytesRead + count);

                    if (progress != null && progressTimer.ElapsedMilliseconds >= ProgressIntervalMilliseconds)
                    {
                        progress.Report(bytesRead);
                        progressTimer.Restart();
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                sha256.TransformFinalBlock(new byte[0], 0, 0);

                // File sharing blocks ordinary concurrent writers on Windows. These
                // checks also detect a changed length or timestamp where observable.
                if (bytesRead != length || stream.Length != length ||
                    File.GetLastWriteTimeUtc(fullPath) != lastWriteTime)
                    throw new IOException("The file changed while its checksum was being calculated. Please try again.");

                digest = ToLowerHex(sha256.Hash);
                cancellationToken.ThrowIfCancellationRequested();
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (progress != null)
                progress.Report(bytesRead);
            cancellationToken.ThrowIfCancellationRequested();

            return new HashResult(fullPath, length, digest);
        }

        public static bool TryNormalizeHash(string input, out string normalized)
        {
            normalized = null;
            if (input == null)
                return false;

            string candidate = input.Trim();
            if (candidate.Length != 64)
                return false;

            for (int index = 0; index < candidate.Length; index++)
            {
                char character = candidate[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f') ||
                      (character >= 'A' && character <= 'F')))
                    return false;
            }

            normalized = candidate.ToLowerInvariant();
            return true;
        }

        public static bool Matches(string actual, string expected)
        {
            string normalizedActual;
            string normalizedExpected;
            return TryNormalizeHash(actual, out normalizedActual) &&
                   TryNormalizeHash(expected, out normalizedExpected) &&
                   String.Equals(normalizedActual, normalizedExpected, StringComparison.Ordinal);
        }

        public static string FormatManifest(HashResult result)
        {
            if (result == null)
                throw new ArgumentNullException("result");

            string fileName = Path.GetFileName(result.FilePath);
            if (String.IsNullOrEmpty(fileName) || fileName.IndexOfAny(new char[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("The filename cannot be represented in a single-line checksum manifest.", "result");

            return result.Sha256 + "  " + fileName + "\n";
        }

        private static string ToLowerHex(byte[] bytes)
        {
            const string alphabet = "0123456789abcdef";
            StringBuilder text = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++)
            {
                text.Append(alphabet[bytes[index] >> 4]);
                text.Append(alphabet[bytes[index] & 15]);
            }
            return text.ToString();
        }
    }
}
