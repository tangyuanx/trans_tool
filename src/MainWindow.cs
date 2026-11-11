using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TransTool
{
    internal sealed class MainWindow : Form
    {
        private static readonly Color Ink = Color.FromArgb(30, 40, 35);
        private static readonly Color Muted = Color.FromArgb(74, 90, 81);
        private static readonly Color Green = Color.FromArgb(27, 93, 64);
        private static readonly Color Paper = Color.FromArgb(246, 248, 246);
        private readonly Button encode = ButtonFor("字符 → ASCII", 122);
        private readonly Button decode = ButtonFor("ASCII → 字符", 122);
        private readonly Button reverse = ButtonFor("用结果反转", 112);
        private readonly Button copy = ButtonFor("复制结果", 88);
        private readonly Button version = ButtonFor("检查更新", 138);
        private readonly Button download = ButtonFor("下载更新", 94);
        private readonly Button install = ButtonFor("安装并重启", 112);
        private readonly Label sourceTitle = Caption("字符输入");
        private readonly Label targetTitle = Caption("ASCII 结果");
        private readonly Label radixTitle = Caption("输出进制");
        private readonly Label conversionStatus = Caption("");
        private readonly Label updateStatus = Caption("");
        private readonly ComboBox radix = new ComboBox();
        private readonly TextBox source = Editor(false);
        private readonly TextBox target = Editor(true);
        private readonly ProgressBar progress = new ProgressBar();
        private readonly CheckBox escapes = new CheckBox { Text = "识别转义", AutoSize = true };
        private readonly UpdateService updates = new UpdateService();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly Font codeFont = new Font("Consolas", 12);
        private readonly Font bodyFont = new Font("Microsoft YaHei UI", 10);
        private readonly Font titleFont = new Font("Microsoft YaHei UI", 19, FontStyle.Bold);
        private readonly string[] drafts = new string[2];
        private bool toAscii = true;
        private bool switching;
        private bool installing;
        private UpdatePackage package;
        private string setup;

        internal MainWindow()
        {
            Text = "Trans Tool · ASCII 转换";
            Font = bodyFont;
            ForeColor = Ink;
            BackColor = Paper;
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(880, 535);
            MinimumSize = new Size(715, 450);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(22, 14, 22, 12) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
            Controls.Add(layout);

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var title = Caption("ASCII 转换");
            title.Font = titleFont;
            title.Anchor = AnchorStyles.Left;
            version.Text = "检查更新 · " + Program.AppVersion.ToString(3);
            version.Anchor = AnchorStyles.Right;
            header.Controls.Add(title, 0, 0);
            header.Controls.Add(version, 1, 0);
            layout.Controls.Add(header, 0, 0);

            radix.DropDownStyle = ComboBoxStyle.DropDownList;
            radix.Items.AddRange(new object[] { "十进制", "十六进制", "二进制" });
            radix.Width = 110;
            radix.SelectedIndex = 0;
            radix.AccessibleName = "ASCII 进制";
            var controls = Strip();
            radixTitle.Margin = new Padding(16, 11, 2, 0);
            radix.Margin = new Padding(5, 7, 12, 0);
            escapes.Margin = new Padding(3, 12, 0, 0);
            escapes.AccessibleDescription = @"识别 \0、\t、\n、\r、\\ 和 \xHH。";
            controls.Controls.AddRange(new Control[] { encode, decode, radixTitle, radix, escapes });
            layout.Controls.Add(controls, 0, 1);

            var editors = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            editors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            editors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            editors.Controls.Add(EditorPanel(sourceTitle, source, "输入示例", () =>
            {
                source.Text = toAscii ? "Hello" : FormatRadix == 16 ? "48 65 6C 6C 6F" :
                    FormatRadix == 2 ? "01001000 01100101 01101100 01101100 01101111" : "72 101 108 108 111";
                source.Focus();
                source.SelectAll();
            }, new Padding(0, 0, 7, 0)), 0, 0);
            editors.Controls.Add(EditorPanel(targetTitle, target, "复制结果", CopyOutput, new Padding(7, 0, 0, 0), copy), 1, 0);
            layout.Controls.Add(editors, 0, 2);

            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            conversionStatus.AutoSize = false;
            conversionStatus.AutoEllipsis = true;
            conversionStatus.Dock = DockStyle.Fill;
            conversionStatus.TextAlign = ContentAlignment.MiddleLeft;
            reverse.Anchor = AnchorStyles.Right;
            bottom.Controls.Add(conversionStatus, 0, 0);
            bottom.Controls.Add(reverse, 1, 0);
            layout.Controls.Add(bottom, 0, 3);
            layout.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(219, 228, 222), Margin = Padding.Empty }, 0, 4);

            var updateBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty };
            updateBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            updateBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            updateBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            updateBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            updateStatus.AutoSize = false;
            updateStatus.AutoEllipsis = true;
            updateStatus.Dock = DockStyle.Fill;
            updateStatus.TextAlign = ContentAlignment.MiddleLeft;
            progress.Width = 105;
            progress.Height = 8;
            progress.Anchor = AnchorStyles.Left;
            progress.Visible = false;
            download.Visible = false;
            install.Visible = false;
            download.Anchor = AnchorStyles.Right;
            install.Anchor = AnchorStyles.Right;
            updateBar.Controls.Add(updateStatus, 0, 0);
            updateBar.Controls.Add(progress, 1, 0);
            updateBar.Controls.Add(download, 2, 0);
            updateBar.Controls.Add(install, 3, 0);
            layout.Controls.Add(updateBar, 0, 5);
            updateStatus.Text = "转换在本机完成。";

            encode.Click += (s, e) => SetDirection(true, false);
            decode.Click += (s, e) => SetDirection(false, false);
            reverse.Click += (s, e) => SetDirection(!toAscii, true);
            source.TextChanged += (s, e) => ConvertInput();
            radix.SelectedIndexChanged += (s, e) => ConvertInput();
            escapes.CheckedChanged += (s, e) => ConvertInput();
            version.Click += CheckUpdate;
            download.Click += DownloadUpdate;
            install.Click += InstallUpdate;
            source.MaxLength = 65535;
            UpdateDirection();
            ActiveControl = source;
            Shown += (s, e) => source.Focus();
        }

        private int FormatRadix { get { return radix.SelectedIndex == 1 ? 16 : radix.SelectedIndex == 2 ? 2 : 10; } }

        private void SetDirection(bool next, bool useResult)
        {
            if (next == toAscii) return;
            string result = target.Text;
            drafts[toAscii ? 0 : 1] = source.Text;
            switching = true;
            toAscii = next;
            if (useResult && next) escapes.Checked = true;
            source.Text = useResult ? result : drafts[toAscii ? 0 : 1] ?? "";
            switching = false;
            UpdateDirection();
            source.Focus();
        }

        private void UpdateDirection()
        {
            Highlight(encode, toAscii);
            Highlight(decode, !toAscii);
            sourceTitle.Text = toAscii ? "字符输入" : "ASCII 输入";
            targetTitle.Text = toAscii ? "ASCII 结果" : "字符结果（转义文本）";
            source.AccessibleName = sourceTitle.Text;
            target.AccessibleName = targetTitle.Text;
            radixTitle.Text = toAscii ? "输出进制" : "输入进制";
            escapes.Visible = toAscii;
            source.Font = toAscii ? bodyFont : codeFont;
            target.Font = toAscii ? codeFont : bodyFont;
            ConvertInput();
        }

        private void ConvertInput()
        {
            if (switching) return;
            conversionStatus.ForeColor = Muted;
            try
            {
                if (toAscii)
                {
                    target.Text = AsciiCodec.Encode(source.Text, FormatRadix, escapes.Checked);
                    conversionStatus.Text = target.TextLength == 0 ? @"输入 ASCII 字符。回车按 CRLF 转换，也可开启转义。" :
                        escapes.Checked ? @"已转换。转义示例：\r\n → 13 10（十进制）。" : "已转换。空格和换行也会计入结果。";
                }
                else
                {
                    int count;
                    target.Text = AsciiCodec.Decode(source.Text, FormatRadix, out count);
                    conversionStatus.Text = count == 0 ? "用空格、逗号或换行分隔。也可用 0x、0b 标记单项进制。" :
                        "已转换 " + count + @" 项。控制字符以 \r、\n、\t 或 \xHH 等形式显示。";
                }
            }
            catch (FormatException error)
            {
                target.Clear();
                conversionStatus.ForeColor = Color.FromArgb(164, 43, 43);
                conversionStatus.Text = error.Message;
            }
            conversionStatus.AccessibleName = conversionStatus.Text;
            copy.Enabled = target.TextLength != 0;
            reverse.Enabled = target.TextLength != 0;
        }

        private void CopyOutput()
        {
            if (target.TextLength == 0) return;
            try
            {
                Clipboard.SetText(target.Text);
                conversionStatus.ForeColor = Muted;
                conversionStatus.Text = "已复制转换结果。";
            }
            catch (ExternalException)
            {
                conversionStatus.Text = "剪贴板正被占用，请重试。";
            }
        }

        private async void CheckUpdate(object sender, EventArgs args)
        {
            version.Enabled = false;
            download.Visible = false;
            install.Visible = false;
            ClearDownload();
            updateStatus.Text = "正在检查 GitHub 稳定版本…";
            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                cancel.CancelAfter(TimeSpan.FromSeconds(25));
                try
                {
                    package = await updates.CheckAsync(Program.AppVersion, cancel.Token);
                    if (IsDisposed) return;
                    if (package == null)
                    {
                        updateStatus.Text = "当前已是最新稳定版本。";
                        return;
                    }
                    download.Text = Program.IsInstalled() ? "下载更新" : "发布页面";
                    download.Visible = true;
                    updateStatus.Text = "发现 v" + package.Version + "。";
                }
                catch (Exception error)
                {
                    if (!IsDisposed) updateStatus.Text = ErrorText("检查失败", error);
                }
                finally
                {
                    if (!IsDisposed) version.Enabled = true;
                }
            }
        }

        private async void DownloadUpdate(object sender, EventArgs args)
        {
            if (!Program.IsInstalled())
            {
                try { Program.OpenReleasePage(); }
                catch (Exception error) { updateStatus.Text = ErrorText("无法打开发布页面", error); }
                return;
            }

            version.Enabled = false;
            download.Enabled = false;
            progress.Value = 0;
            progress.Visible = true;
            updateStatus.Text = "正在下载安装包…";
            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                cancel.CancelAfter(TimeSpan.FromMinutes(4));
                try
                {
                    setup = await updates.DownloadAsync(package, new Progress<int>(percent =>
                    {
                        if (!IsDisposed) progress.Value = percent;
                    }), cancel.Token);
                    if (IsDisposed) return;
                    download.Visible = false;
                    install.Visible = true;
                    updateStatus.Text = "下载完成，SHA-256 校验通过。";
                }
                catch (Exception error)
                {
                    if (!IsDisposed) updateStatus.Text = ErrorText("下载失败，可重试", error);
                }
                finally
                {
                    if (!IsDisposed)
                    {
                        version.Enabled = true;
                        download.Enabled = true;
                        progress.Visible = false;
                    }
                }
            }
        }

        private void InstallUpdate(object sender, EventArgs args)
        {
            if (source.TextLength > 0 || !string.IsNullOrEmpty(drafts[toAscii ? 1 : 0]))
            {
                if (MessageBox.Show(this, "安装后会重启 Trans Tool，本次输入将清空。继续安装？",
                    "安装更新", MessageBoxButtons.OKCancel, MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            }

            try
            {
                UpdateService.StartInstaller(Program.Executable, setup, package.Hash);
                installing = true;
                Close();
            }
            catch (Exception error)
            {
                updateStatus.Text = ErrorText("无法启动安装", error);
            }
        }

        private static string ErrorText(string prefix, Exception error)
        {
            return prefix + "：" + (error is OperationCanceledException ? "请求超时。" : error.Message);
        }

        private void ClearDownload()
        {
            if (setup == null) return;
            UpdateService.DeleteCache(Path.GetDirectoryName(setup));
            setup = null;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            lifetime.Cancel();
            if (!installing) ClearDownload();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                updates.Dispose();
                lifetime.Dispose();
                bodyFont.Dispose();
                titleFont.Dispose();
                codeFont.Dispose();
            }
        }

        private static Control EditorPanel(Label label, TextBox editor, string action, Action onClick, Padding padding, Button button = null)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = padding, Margin = Padding.Empty };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            label.Anchor = AnchorStyles.Left;
            if (button == null) button = ButtonFor(action, 88);
            button.Click += (s, e) => onClick();
            button.Anchor = AnchorStyles.Right;
            header.Controls.Add(label, 0, 0);
            header.Controls.Add(button, 1, 0);
            panel.Controls.Add(header, 0, 0);
            panel.Controls.Add(editor, 0, 1);
            return panel;
        }

        private static TextBox Editor(bool readOnly)
        {
            return new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = readOnly,
                ScrollBars = ScrollBars.Vertical, AcceptsReturn = true,
                BackColor = readOnly ? Color.FromArgb(237, 244, 240) : Color.White,
                ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle, Margin = Padding.Empty
            };
        }

        private static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Muted };
        }

        private static Button ButtonFor(string text, int width)
        {
            var button = new Button
            {
                Text = text, Width = width, Height = 32, FlatStyle = FlatStyle.Flat,
                ForeColor = Ink, BackColor = Color.White, Margin = new Padding(3, 3, 3, 3)
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(204, 217, 209);
            return button;
        }

        private static void Highlight(Button button, bool active)
        {
            button.BackColor = active ? Green : Color.White;
            button.ForeColor = active ? Color.White : Ink;
            button.FlatAppearance.BorderColor = active ? Green : Color.FromArgb(204, 217, 209);
        }

        private static FlowLayoutPanel Strip()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        }
    }
}
