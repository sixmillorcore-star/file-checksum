using System;
using System.IO;
using System.Text;

namespace FileChecksum
{
    public static class ChecksumWriter
    {
        // Holding the original open without write/delete sharing also protects
        // against saving through another hard-link path to the same input.
        public static void Write(HashResult result, string destination)
        {
            if (result == null) throw new ArgumentNullException("result");
            string source = Path.GetFullPath(result.FilePath);
            string output = Path.GetFullPath(destination);
            if (String.Equals(source, output, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The checksum cannot overwrite the input file.");
            string manifest = HashService.FormatManifest(result);
            using (FileStream guard = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream stream = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                writer.Write(manifest);
        }
    }
}
