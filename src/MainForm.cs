using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FileChecksum
{
    public sealed class MainForm : Form
    {
        private readonly Label heading = new Label();
        private readonly Label summary = new Label();
        private readonly Label fileLabel = new Label();
        private readonly TextBox filePath = new TextBox();
        private readonly Button browse = new Button();
        private readonly Label details = new Label();
        private readonly ProgressBar progressBar = new ProgressBar();
        private readonly Label status = new Label();
        private readonly Label hashLabel = new Label();
        private readonly TextBox hash = new TextBox();
        private readonly Button copy = new Button();
        private readonly Button save = new Button();
        private readonly Button cancel = new Button();
        private readonly Label expectedLabel = new Label();
        private readonly TextBox expected = new TextBox();
        private readonly Label comparison = new Label();
        private readonly Label footer = new Label();
        private readonly ComboBox language = new ComboBox();
        private CancellationTokenSource cancellation;
        private HashResult result;
        private string state = "idle";
        private string error = "";
        private bool thai;
        private bool closing;
        private long operation;
        private long processed;
        private long length;

        public MainForm()
        {
            Text = "File Checksum";
            Name = "MainForm";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Segoe UI", 10F);
            BackColor = Color.FromArgb(250, 250, 250);
            ForeColor = Color.FromArgb(25, 25, 25);
            ClientSize = new Size(760, 556);
            MinimumSize = new Size(680, 600);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (ArgumentException) { }

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Name = "Layout";
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(24, 20, 24, 18);
            layout.ColumnCount = 1;
            layout.RowCount = 15;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int[] heights = { 40, 34, 23, 34, 25, 10, 32, 23, 34, 43, 23, 34, 36 };
            foreach (int height in heights) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));

            TableLayoutPanel header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.Margin = Padding.Empty;
            header.ColumnCount = 2;
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            heading.Font = new Font(Font.FontFamily, 21F, FontStyle.Bold);
            heading.Dock = DockStyle.Fill;
            heading.Margin = Padding.Empty;
            heading.Text = "File Checksum";
            language.Name = "Language";
            language.AccessibleName = "Interface language";
            language.DropDownStyle = ComboBoxStyle.DropDownList;
            language.Items.AddRange(new object[] { "English", "ไทย" });
            language.Dock = DockStyle.Top;
            language.Margin = new Padding(8, 5, 0, 0);
            language.SelectedIndex = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "th" ? 1 : 0;
            thai = language.SelectedIndex == 1;
            language.SelectedIndexChanged += delegate { SetLanguage(language.SelectedIndex == 1); };
            header.Controls.Add(heading, 0, 0);
            header.Controls.Add(language, 1, 0);

            SetLabel(summary, "Summary", Color.DimGray);
            SetLabel(fileLabel, "FileLabel", ForeColor);
            SetLabel(details, "FileDetails", Color.DimGray);
            SetLabel(status, "Status", Color.DimGray);
            SetLabel(hashLabel, "HashLabel", ForeColor);
            SetLabel(expectedLabel, "ExpectedLabel", ForeColor);
            SetLabel(comparison, "Comparison", Color.DimGray);
            SetLabel(footer, "Footer", Color.DimGray);
            footer.Font = new Font(Font.FontFamily, 9F);

            TableLayoutPanel fileRow = new TableLayoutPanel();
            fileRow.Dock = DockStyle.Fill;
            fileRow.Margin = Padding.Empty;
            fileRow.ColumnCount = 2;
            fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            filePath.Name = "FilePath";
            filePath.AccessibleName = "Selected file";
            filePath.ReadOnly = true;
            filePath.BackColor = Color.White;
            filePath.Dock = DockStyle.Top;
            filePath.Margin = new Padding(0, 2, 8, 0);
            ConfigureButton(browse, "Browse", 110);
            browse.Margin = Padding.Empty;
            browse.Dock = DockStyle.Fill;
            browse.Click += async delegate { await BrowseAsync(); };
            fileRow.Controls.Add(filePath, 0, 0);
            fileRow.Controls.Add(browse, 1, 0);

            progressBar.Name = "Progress";
            progressBar.Maximum = 1000;
            progressBar.Dock = DockStyle.Fill;
            progressBar.Margin = new Padding(0, 1, 0, 1);
            hash.Name = "Hash";
            hash.AccessibleName = "Calculated SHA-256";
            hash.ReadOnly = true;
            hash.BackColor = Color.White;
            hash.Font = new Font("Consolas", 10F);
            hash.Dock = DockStyle.Top;
            hash.Margin = Padding.Empty;
            expected.Name = "ExpectedHash";
            expected.AccessibleName = "Expected SHA-256";
            expected.Font = new Font("Consolas", 10F);
            expected.Dock = DockStyle.Top;
            expected.Margin = Padding.Empty;
            expected.MaxLength = 256;
            expected.TextChanged += delegate { UpdateComparison(); };

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.Margin = Padding.Empty;
            actions.WrapContents = false;
            ConfigureButton(copy, "CopyHash", 124);
            ConfigureButton(save, "SaveChecksum", 156);
            ConfigureButton(cancel, "CancelHash", 100);
            copy.Click += delegate { CopyHash(); };
            save.Click += delegate { SaveChecksum(); };
            cancel.Click += delegate { if (cancellation != null) { cancellation.Cancel(); state = "cancelling"; UpdateStatus(); } };
            actions.Controls.AddRange(new Control[] { copy, save, cancel });

            Control[] rows = { header, summary, fileLabel, fileRow, details, progressBar, status, hashLabel, hash, actions, expectedLabel, expected, comparison };
            for (int row = 0; row < rows.Length; row++) layout.Controls.Add(rows[row], 0, row);
            layout.Controls.Add(footer, 0, 14);
            Controls.Add(layout);
            AttachDrop(this);
            SetLanguage(thai);
            SetBusy(false);
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (cancellation != null)
                {
                    args.Cancel = true;
                    closing = true;
                    cancellation.Cancel();
                    state = "cancelling";
                    UpdateStatus();
                }
            };
        }

        private void SetLabel(Label label, string name, Color color)
        {
            label.Name = name;
            label.Dock = DockStyle.Fill;
            label.Margin = Padding.Empty;
            label.ForeColor = color;
            label.AutoEllipsis = true;
            label.TextAlign = ContentAlignment.MiddleLeft;
        }

        private static void ConfigureButton(Button button, string name, int width)
        {
            button.Name = name;
            button.Size = new Size(width, 32);
            button.Margin = new Padding(0, 4, 8, 0);
            button.UseVisualStyleBackColor = true;
        }

        private void AttachDrop(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += delegate(object sender, DragEventArgs args)
            {
                string[] paths = args.Data.GetData(DataFormats.FileDrop) as string[];
                args.Effect = cancellation == null && paths != null && paths.Length == 1 && File.Exists(paths[0]) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            control.DragDrop += async delegate(object sender, DragEventArgs args)
            {
                string[] paths = args.Data.GetData(DataFormats.FileDrop) as string[];
                if (cancellation == null && paths != null && paths.Length == 1) await LoadFileAsync(paths[0]);
            };
            foreach (Control child in control.Controls) AttachDrop(child);
        }

        public void SetLanguage(bool useThai)
        {
            thai = useThai;
            if (language.SelectedIndex != (thai ? 1 : 0)) language.SelectedIndex = thai ? 1 : 0;
            summary.Text = T("Calculate a file's SHA-256, or compare it with a checksum you already have.", "คำนวณ SHA-256 ของไฟล์ หรือเทียบกับค่าแฮชที่คุณมีอยู่");
            fileLabel.Text = T("File — browse or drop one file here", "ไฟล์ — เลือกไฟล์ หรือลากไฟล์หนึ่งไฟล์มาวาง");
            browse.Text = T("Browse…", "เลือกไฟล์…");
            hashLabel.Text = "SHA-256";
            copy.Text = T("Copy hash", "คัดลอกค่าแฮช");
            save.Text = T("Save .sha256…", "บันทึก .sha256…");
            cancel.Text = T("Cancel", "ยกเลิก");
            expectedLabel.Text = T("Expected SHA-256 (optional)", "SHA-256 ที่ต้องการเทียบ (ไม่บังคับ)");
            footer.Text = T("v1.0.0  ·  Offline  ·  Input files are read only", "v1.0.0  ·  ออฟไลน์  ·  อ่านไฟล์ต้นฉบับเท่านั้น");
            UpdateDetails();
            UpdateStatus();
            UpdateComparison();
        }

        private string T(string english, string th) { return thai ? th : english; }

        private async Task BrowseAsync()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = T("Choose a file", "เลือกไฟล์");
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                dialog.Filter = T("All files (*.*)|*.*", "ไฟล์ทั้งหมด (*.*)|*.*");
                if (dialog.ShowDialog(this) == DialogResult.OK) await LoadFileAsync(dialog.FileName);
            }
        }

        public async Task LoadFileAsync(string path)
        {
            if (cancellation != null) return;
            long current = ++operation;
            result = null;
            hash.Clear();
            processed = 0;
            length = 0;
            error = "";
            progressBar.Value = 0;
            state = "working";
            cancellation = new CancellationTokenSource();
            CancellationTokenSource active = cancellation;
            SetBusy(true);
            UpdateComparison();
            try
            {
                filePath.Text = Path.GetFullPath(path);
                length = new FileInfo(path).Length;
                UpdateDetails();
                Progress<long> progress = new Progress<long>(delegate(long bytes)
                {
                    if (IsDisposed || operation != current || cancellation != active) return;
                    processed = bytes;
                    progressBar.Value = length > 0 ? (int)Math.Min(1000, Math.Round((double)bytes / length * 1000)) : 1000;
                    UpdateStatus();
                });
                HashResult completed = await Task.Run(delegate { return HashService.Compute(path, active.Token, progress); });
                active.Token.ThrowIfCancellationRequested();
                result = completed;
                hash.Text = completed.Sha256;
                length = completed.Length;
                progressBar.Value = 1000;
                state = "ready";
            }
            catch (OperationCanceledException) { state = "cancelled"; }
            catch (Exception exception)
            {
                if (!(exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is NotSupportedException || exception is SecurityException)) throw;
                state = "error";
                error = exception.Message;
            }
            finally
            {
                cancellation = null;
                active.Dispose();
                SetBusy(false);
                UpdateDetails();
                UpdateStatus();
                UpdateComparison();
                if (closing) Close();
            }
        }

        private void SetBusy(bool busy)
        {
            browse.Enabled = !busy;
            cancel.Enabled = busy;
            cancel.Visible = busy;
            copy.Enabled = !busy && result != null;
            save.Enabled = !busy && result != null;
        }

        private void UpdateDetails()
        {
            details.Text = String.IsNullOrEmpty(filePath.Text) ? T("No file selected.", "ยังไม่ได้เลือกไฟล์") : String.Format(CultureInfo.InvariantCulture, "{0:N0} bytes  ·  {1}", length, Path.GetFileName(filePath.Text));
        }

        private void UpdateStatus()
        {
            status.ForeColor = state == "error" ? Color.Firebrick : Color.DimGray;
            switch (state)
            {
                case "working": status.Text = T("Reading file… ", "กำลังอ่านไฟล์… ") + (length > 0 ? ((double)processed / length).ToString("P0", CultureInfo.InvariantCulture) : ""); break;
                case "cancelling": status.Text = T("Cancelling…", "กำลังยกเลิก…"); break;
                case "ready": status.Text = T("SHA-256 calculated.", "คำนวณ SHA-256 แล้ว"); break;
                case "cancelled": status.Text = T("Cancelled. No checksum was produced.", "ยกเลิกแล้ว ยังไม่มีผลค่าแฮช"); break;
                case "error": status.Text = T("Could not read this file: ", "อ่านไฟล์นี้ไม่ได้: ") + error; break;
                case "copied": status.Text = T("Hash copied to clipboard.", "คัดลอกค่าแฮชแล้ว"); break;
                case "saved": status.Text = T("Checksum file saved.", "บันทึกไฟล์ค่าแฮชแล้ว"); break;
                default: status.Text = T("Ready.", "พร้อมใช้งาน"); break;
            }
        }

        private void UpdateComparison()
        {
            string normalized;
            comparison.ForeColor = Color.DimGray;
            if (String.IsNullOrWhiteSpace(expected.Text)) comparison.Text = T("Paste an expected hash above to compare.", "วางค่าแฮชที่ต้องการเทียบในช่องด้านบน");
            else if (!HashService.TryNormalizeHash(expected.Text, out normalized)) comparison.Text = T("Use exactly 64 hexadecimal characters (0–9, a–f).", "ใช้ตัวเลขฐานสิบหก 64 ตัว (0–9, a–f)");
            else if (result == null) comparison.Text = cancellation != null ? T("Calculating before comparison…", "กำลังคำนวณก่อนเปรียบเทียบ…") : T("Choose a file to compare with this hash.", "เลือกไฟล์เพื่อเทียบกับค่าแฮชนี้");
            else if (HashService.Matches(result.Sha256, normalized))
            {
                comparison.Text = T("MATCH — the checksums are identical.", "ตรงกัน — ค่าแฮชเหมือนกัน");
                comparison.ForeColor = Color.DarkGreen;
            }
            else
            {
                comparison.Text = T("DOES NOT MATCH — check the file and expected hash.", "ไม่ตรงกัน — ตรวจสอบไฟล์และค่าแฮชอีกครั้ง");
                comparison.ForeColor = Color.Firebrick;
            }
        }

        private void CopyHash()
        {
            if (result == null) return;
            try { Clipboard.SetText(result.Sha256); state = "copied"; UpdateStatus(); }
            catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(this, T("The clipboard is busy. Try again.", "คลิปบอร์ดไม่ว่าง กรุณาลองอีกครั้ง"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }

        private void SaveChecksum()
        {
            if (result == null) return;
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = T("Save checksum", "บันทึกค่าแฮช");
                dialog.FileName = Path.GetFileName(result.FilePath) + ".sha256";
                dialog.Filter = "SHA-256 checksum (*.sha256)|*.sha256|Text file (*.txt)|*.txt";
                dialog.DefaultExt = "sha256";
                dialog.AddExtension = true;
                dialog.OverwritePrompt = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (String.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(result.FilePath), StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, T("Choose a different path so the input file is not overwritten.", "กรุณาเลือกตำแหน่งอื่นเพื่อไม่เขียนทับไฟล์ต้นฉบับ"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try { ChecksumWriter.Write(result, dialog.FileName); state = "saved"; UpdateStatus(); }
                catch (Exception exception)
                {
                    if (!(exception is IOException || exception is UnauthorizedAccessException || exception is SecurityException || exception is ArgumentException)) throw;
                    MessageBox.Show(this, T("Could not save: ", "บันทึกไม่ได้: ") + exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
