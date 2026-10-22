using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileChecksum;

internal static class UiSmokeTests
{
    private const string AbcSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private const string EmptySha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private static MainForm form;
    private static string repositoryDirectory;
    private static string temporaryParent;
    private static string fixtureDirectory;
    private static string inputPath;
    private static int passed;
    private static int failed;

    [STAThread]
    private static int Main()
    {
        string testsDirectory = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName;
        repositoryDirectory = Directory.GetParent(testsDirectory).FullName;
        temporaryParent = Path.GetFullPath(Path.Combine(testsDirectory, ".tmp"));
        fixtureDirectory = Path.GetFullPath(Path.Combine(temporaryParent, "ui-" + Guid.NewGuid().ToString("N")));
        RequireChildPath(fixtureDirectory, temporaryParent);
        Directory.CreateDirectory(fixtureDirectory);
        inputPath = Path.Combine(fixtureDirectory, "example.txt");
        File.WriteAllText(inputPath, "abc", new UTF8Encoding(false));

        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (form = new MainForm())
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.Shown += async delegate
                {
                    try { await RunSuiteAsync(); }
                    catch (Exception exception)
                    {
                        failed++;
                        Console.WriteLine("FAIL: UI test runner\n" + exception);
                    }
                    finally { form.Close(); }
                };
                Application.Run(form);
            }
        }
        finally
        {
            // Cleanup is confined to the unique directory created by this run.
            RequireChildPath(fixtureDirectory, temporaryParent);
            if (Directory.Exists(fixtureDirectory)) Directory.Delete(fixtureDirectory, true);
        }

        Console.WriteLine("RESULT: {0} UI smoke tests passed, {1} failed", passed, failed);
        Console.WriteLine("LIMITS: Native dialogs, real drag/drop, clipboard actions and high-DPI displays were not exercised.");
        return failed == 0 ? 0 : 1;
    }

    private static async Task RunSuiteAsync()
    {
        form.SetLanguage(false);
        Run("initial action state", delegate
        {
            Equal("", Find<TextBox>("Hash").Text, "Initial hash");
            AssertNoResult();
            True(Find<Button>("Browse").Enabled, "Browse initially enabled");
            True(!Find<Button>("CancelHash").Enabled, "Cancel initially disabled");
        });

        await RunAsync("load UTF-8 abc through the UI event loop", async delegate
        {
            await form.LoadFileAsync(inputPath);
            Equal(AbcSha256, Find<TextBox>("Hash").Text, "Loaded SHA-256");
            True(Find<Button>("CopyHash").Enabled && Find<Button>("SaveChecksum").Enabled, "Result actions enabled");
            Equal("SHA-256 calculated.", Find<Label>("Status").Text, "Completed status");
            Equal(1000, Find<ProgressBar>("Progress").Value, "Completed progress");
        });

        Run("expected hash match, mismatch, invalid and clear", delegate
        {
            TextBox expected = Find<TextBox>("ExpectedHash");
            expected.Text = "  " + AbcSha256.ToUpperInvariant() + " ";
            StartsWith("MATCH", Find<Label>("Comparison").Text, "Matching uppercase expected hash");
            expected.Text = EmptySha256;
            StartsWith("DOES NOT MATCH", Find<Label>("Comparison").Text, "Mismatching expected hash");
            expected.Text = new String('g', 64);
            StartsWith("Use exactly 64", Find<Label>("Comparison").Text, "Invalid expected hash");
            expected.Clear();
            StartsWith("Paste an expected hash", Find<Label>("Comparison").Text, "Cleared expected hash");
        });

        Run("language changes preserve results and comparison", delegate
        {
            Find<TextBox>("ExpectedHash").Text = AbcSha256;
            form.SetLanguage(true);
            Equal("คำนวณ SHA-256 แล้ว", Find<Label>("Status").Text, "Thai ready status");
            StartsWith("ตรงกัน", Find<Label>("Comparison").Text, "Thai match label");
            Equal(AbcSha256, Find<TextBox>("Hash").Text, "Hash preserved when changing language");
            True(Find<Button>("CopyHash").Enabled && Find<Button>("SaveChecksum").Enabled, "Actions remain enabled in Thai");
            form.SetLanguage(false);
            StartsWith("MATCH", Find<Label>("Comparison").Text, "English match label restored");
        });

        await RunAsync("failed load clears the previous result and actions", async delegate
        {
            await form.LoadFileAsync(Path.Combine(fixtureDirectory, "missing.txt"));
            AssertNoResult();
            StartsWith("Could not read this file:", Find<Label>("Status").Text, "Failure status");
            StartsWith("Choose a file", Find<Label>("Comparison").Text, "Previous result cannot still compare");
            True(Find<Button>("Browse").Enabled, "Browse recovers after failure");
        });

        await RunAsync("cancel button and language switching while busy", async delegate
        {
            string largePath = Path.Combine(fixtureDirectory, "large-cancel.bin");
            using (FileStream file = File.Create(largePath)) file.SetLength(256L * 1024 * 1024 + 31);
            Task pending = form.LoadFileAsync(largePath);
            True(!pending.IsCompleted, "Large file operation remains pending for immediate cancellation");
            True(!Find<Button>("Browse").Enabled, "Browse disabled while working");
            AssertNoResult();
            Button cancel = Find<Button>("CancelHash");
            True(cancel.Visible && cancel.Enabled, "Cancel available while working");
            form.SetLanguage(true);
            StartsWith("กำลังอ่านไฟล์", Find<Label>("Status").Text, "Busy state survives language switch");
            True(!Find<Button>("Browse").Enabled && cancel.Enabled, "Language change preserves busy action state");
            cancel.PerformClick();
            await pending;
            AssertNoResult();
            StartsWith("ยกเลิกแล้ว", Find<Label>("Status").Text, "Cancellation status");
            True(Find<Button>("Browse").Enabled, "Browse recovers after cancellation");
            True(!cancel.Enabled && !cancel.Visible, "Cancel hidden and disabled after cancellation");
            form.SetLanguage(false);
            await form.LoadFileAsync(inputPath);
            Equal(AbcSha256, Find<TextBox>("Hash").Text, "A new operation succeeds after cancellation");
        });

        Run("default and minimum layouts fit in English and Thai", delegate
        {
            Size originalSize = form.Size;
            foreach (bool thai in new bool[] { false, true })
            {
                form.SetLanguage(thai);
                form.Size = originalSize;
                AssertLayout();
                form.Size = form.MinimumSize;
                AssertLayout();
            }
            form.Size = originalSize;
            form.SetLanguage(false);
        });

        Run("render English and Thai sample previews", delegate
        {
            string imageDirectory = Path.Combine(repositoryDirectory, "docs", "images");
            Directory.CreateDirectory(imageDirectory);
            // Display a sample filename instead of exposing this machine's fixture path.
            Find<TextBox>("FilePath").Text = "example.txt";
            Find<TextBox>("ExpectedHash").Text = AbcSha256;
            foreach (bool thai in new bool[] { false, true })
            {
                form.SetLanguage(thai);
                form.PerformLayout();
                form.Update();
                string imagePath = Path.Combine(imageDirectory, thai ? "file-checksum-th.png" : "file-checksum.png");
                using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(imagePath, ImageFormat.Png);
                }
                True(new FileInfo(imagePath).Length > 1000, "Preview PNG contains rendered image data");
                Console.WriteLine("PREVIEW: " + imagePath);
            }
            form.SetLanguage(false);
        });

        Run("export writes UTF-8 without BOM and may replace an unrelated output", delegate
        {
            string source = Path.Combine(fixtureDirectory, "ตัวอย่าง.txt");
            File.WriteAllText(source, "abc", new UTF8Encoding(false));
            HashResult result = new HashResult(source, 3L, AbcSha256);
            string output = source + ".sha256";
            File.WriteAllText(output, "unrelated old output", new UTF8Encoding(false));
            ChecksumWriter.Write(result, output);
            byte[] actual = File.ReadAllBytes(output);
            byte[] expected = new UTF8Encoding(false).GetBytes(AbcSha256 + "  ตัวอย่าง.txt\n");
            Equal(expected.Length, actual.Length, "UTF-8 output byte length with no BOM");
            for (int index = 0; index < expected.Length; index++)
                Equal(expected[index], actual[index], "Manifest byte at offset " + index);
            Equal("abc", File.ReadAllText(source, Encoding.UTF8), "Source unchanged after export");
        });

        Run("export rejects the input path without modifying input", delegate
        {
            HashResult result = new HashResult(inputPath, 3L, AbcSha256);
            Throws<IOException>(delegate { ChecksumWriter.Write(result, inputPath); });
            Equal("abc", File.ReadAllText(inputPath, Encoding.UTF8), "Source unchanged after same-path rejection");
        });

        Run("export rejects a hard-link alias without modifying input", delegate
        {
            string alias = Path.Combine(fixtureDirectory, "input-hardlink.txt");
            if (!CreateHardLink(alias, inputPath, IntPtr.Zero))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the NTFS hard-link test fixture.");
            HashResult result = new HashResult(inputPath, 3L, AbcSha256);
            Throws<IOException>(delegate { ChecksumWriter.Write(result, alias); });
            Equal("abc", File.ReadAllText(inputPath, Encoding.UTF8), "Source unchanged after alias rejection");
            Equal("abc", File.ReadAllText(alias, Encoding.UTF8), "Alias unchanged after rejection");
        });
    }

    private static void AssertLayout()
    {
        form.PerformLayout();
        form.Update();
        AssertControlBounds(form);
        string[] labels = { "Summary", "FileLabel", "HashLabel", "ExpectedLabel", "Comparison", "Footer" };
        foreach (string name in labels)
        {
            Label label = Find<Label>(name);
            Size measured = TextRenderer.MeasureText(label.Text, label.Font, new Size(Int32.MaxValue, Int32.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            True(measured.Width <= label.ClientSize.Width && measured.Height <= label.ClientSize.Height,
                name + " text fits at client size " + form.ClientSize + ": measured=" + measured + ", available=" + label.ClientSize);
        }
        foreach (string name in new string[] { "Browse", "CopyHash", "SaveChecksum" })
        {
            Button button = Find<Button>(name);
            Size measured = TextRenderer.MeasureText(button.Text, button.Font, new Size(Int32.MaxValue, Int32.MaxValue), TextFormatFlags.SingleLine);
            True(measured.Width + 10 <= button.ClientSize.Width && measured.Height + 4 <= button.ClientSize.Height,
                name + " button caption fits at client size " + form.ClientSize);
        }
    }

    private static void AssertControlBounds(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (!child.Visible) continue;
            Point clientLocation = form.PointToClient(parent.PointToScreen(child.Location));
            Rectangle absolute = new Rectangle(clientLocation, child.Size);
            True(form.ClientRectangle.Contains(absolute), "Visible control is inside client bounds: " + child.Name + " " + absolute);
            AssertControlBounds(child);
        }
    }

    private static void AssertNoResult()
    {
        Equal("", Find<TextBox>("Hash").Text, "No stale calculated hash");
        True(!Find<Button>("CopyHash").Enabled && !Find<Button>("SaveChecksum").Enabled, "No result actions without a result");
    }

    private static T Find<T>(string name) where T : Control
    {
        Control[] matches = form.Controls.Find(name, true);
        if (matches.Length != 1 || !(matches[0] is T)) throw new Exception("Expected one " + typeof(T).Name + " named " + name);
        return (T)matches[0];
    }

    private static void Run(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS: " + name); }
        catch (Exception exception) { failed++; Console.WriteLine("FAIL: " + name + "\n" + exception); }
    }

    private static async Task RunAsync(string name, Func<Task> action)
    {
        try { await action(); passed++; Console.WriteLine("PASS: " + name); }
        catch (Exception exception) { failed++; Console.WriteLine("FAIL: " + name + "\n" + exception); }
    }

    private static void RequireChildPath(string candidate, string parent)
    {
        string prefix = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(candidate).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture cleanup path.");
    }

    private static void StartsWith(string expectedPrefix, string actual, string description)
    {
        True(actual.StartsWith(expectedPrefix, StringComparison.Ordinal), description + ": " + actual);
    }

    private static void True(bool value, string description)
    {
        if (!value) throw new Exception(description);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name + " was not thrown.");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newFileName, string existingFileName, IntPtr securityAttributes);

    private static void Equal<T>(T expected, T actual, string description)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception(description + ": expected <" + expected + ">, got <" + actual + ">.");
    }
}
