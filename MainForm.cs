using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OreNoYomeKoreanPatcher
{
    public sealed class MainForm : Form
    {
        private Panel dropPanel;
        private Label isoStatusLabel;
        private Label dlcStatusLabel;
        private CheckBox introSkipCheckBox;
        private CheckBox developerModeCheckBox;
        private Button patchButton;
        private ProgressBar progressBar;
        private TextBox statusTextBox;

        private string selectedIsoPath;
        private string selectedDlcFolder;
        private BackgroundWorker patchWorker;

        private sealed class ProgressMessage
        {
            public string Text;
        }

        public MainForm()
        {
            Text = "나의 신부 ~당신만의 새 신부~ 한글 패치";
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(246, 247, 250);
            MinimumSize = new Size(900, 560);
            Size = new Size(980, 620);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;

            TableLayoutPanel root = new TableLayoutPanel();
            root.ColumnCount = 5;
            root.RowCount = 3;
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(18);
            root.BackColor = BackColor;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150F));
            Controls.Add(root);

            dropPanel = BuildDropPanel();
            root.Controls.Add(dropPanel, 0, 0);
            root.Controls.Add(BuildSeparator(), 1, 0);
            root.Controls.Add(BuildOptionsPanel(), 2, 0);
            root.Controls.Add(BuildSeparator(), 3, 0);
            root.Controls.Add(BuildButtonPanel(), 4, 0);

            progressBar = new ProgressBar();
            progressBar.Dock = DockStyle.Fill;
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            root.Controls.Add(progressBar, 0, 1);
            root.SetColumnSpan(progressBar, 5);

            statusTextBox = new TextBox();
            statusTextBox.Dock = DockStyle.Fill;
            statusTextBox.BorderStyle = BorderStyle.FixedSingle;
            statusTextBox.BackColor = Color.White;
            statusTextBox.ForeColor = Color.FromArgb(65, 72, 86);
            statusTextBox.ReadOnly = true;
            statusTextBox.Multiline = true;
            statusTextBox.ScrollBars = ScrollBars.Vertical;
            statusTextBox.WordWrap = false;
            statusTextBox.Text = "대기 중";
            root.Controls.Add(statusTextBox, 0, 2);
            root.SetColumnSpan(statusTextBox, 5);

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            UpdateStatusDisplay();
        }

        private Panel BuildDropPanel()
        {
            Panel panel = new Panel();
            panel.BackColor = Color.White;
            panel.BorderStyle = BorderStyle.FixedSingle;
            panel.Dock = DockStyle.Fill;
            panel.Margin = new Padding(0, 0, 14, 14);
            panel.Cursor = Cursors.Hand;

            TableLayoutPanel inner = new TableLayoutPanel();
            inner.ColumnCount = 1;
            inner.RowCount = 6;
            inner.Dock = DockStyle.Fill;
            inner.Padding = new Padding(22);
            inner.BackColor = Color.White;
            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 14F));
            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            panel.Controls.Add(inner);

            Label title = new Label();
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold, GraphicsUnit.Point);
            title.ForeColor = Color.FromArgb(34, 42, 54);
            title.Text = "원본 ISO / DLC 폴더를 여기에 드래그 & 드롭";
            inner.Controls.Add(title, 0, 0);

            Label hint = new Label();
            hint.Dock = DockStyle.Fill;
            hint.TextAlign = ContentAlignment.TopLeft;
            hint.ForeColor = Color.FromArgb(92, 101, 116);
            hint.Text = "또는 클릭해서 ISO 파일 선택\r\nDLC는 494607D8 폴더 또는 그 상위 폴더를 놓아 주세요.";
            inner.Controls.Add(hint, 0, 1);

            isoStatusLabel = CreateStatusLabel();
            inner.Controls.Add(isoStatusLabel, 0, 3);

            dlcStatusLabel = CreateStatusLabel();
            inner.Controls.Add(dlcStatusLabel, 0, 4);

            AttachDropHandlers(panel);
            return panel;
        }

        private Label CreateStatusLabel()
        {
            Label label = new Label();
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoEllipsis = true;
            label.UseMnemonic = false;
            label.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold, GraphicsUnit.Point);
            label.Margin = new Padding(0);
            return label;
        }

        private void AttachDropHandlers(Control control)
        {
            control.AllowDrop = true;
            control.Click += OnDropPanelClick;
            control.DragEnter += OnDragEnter;
            control.DragDrop += OnDragDrop;
            foreach (Control child in control.Controls)
            {
                AttachDropHandlers(child);
            }
        }

        private Panel BuildSeparator()
        {
            Panel separator = new Panel();
            separator.BackColor = Color.FromArgb(205, 211, 222);
            separator.Dock = DockStyle.Fill;
            separator.Margin = new Padding(0, 8, 0, 22);
            return separator;
        }

        private Panel BuildOptionsPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Margin = new Padding(16, 0, 12, 14);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.ColumnCount = 1;
            layout.RowCount = 7;
            layout.Dock = DockStyle.Fill;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            panel.Controls.Add(layout);

            Label header = new Label();
            header.Dock = DockStyle.Fill;
            header.Text = "치트 옵션";
            header.TextAlign = ContentAlignment.MiddleLeft;
            header.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold, GraphicsUnit.Point);
            header.ForeColor = Color.FromArgb(34, 42, 54);
            layout.Controls.Add(header, 0, 1);

            introSkipCheckBox = CreateOption("인트로 스킵");
            layout.Controls.Add(introSkipCheckBox, 0, 2);
            layout.Controls.Add(CreateDescription("- 게임 시작 시 나오는 로고/오프닝 영상을 건너뜁니다."), 0, 3);

            developerModeCheckBox = CreateOption("개발자 모드 활성화");
            layout.Controls.Add(developerModeCheckBox, 0, 4);
            layout.Controls.Add(CreateDescription("- 새 게임 시작 시 개발용 디버그 메뉴가 열립니다.\r\n- 결과 ISO 이름에 _repacked_debug가 붙습니다."), 0, 5);

            return panel;
        }

        private CheckBox CreateOption(string text)
        {
            CheckBox box = new CheckBox();
            box.Anchor = AnchorStyles.Left;
            box.AutoSize = true;
            box.Text = text;
            box.ForeColor = Color.FromArgb(65, 72, 86);
            box.Margin = new Padding(0);
            return box;
        }

        private Label CreateDescription(string text)
        {
            Label label = new Label();
            label.Dock = DockStyle.Fill;
            label.Text = text;
            label.TextAlign = ContentAlignment.TopLeft;
            label.Font = new Font(Font.FontFamily, 8F, FontStyle.Regular, GraphicsUnit.Point);
            label.ForeColor = Color.FromArgb(104, 112, 126);
            label.Padding = new Padding(18, 0, 0, 0);
            label.Margin = new Padding(0);
            label.UseMnemonic = false;
            return label;
        }

        private Panel BuildButtonPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Margin = new Padding(8, 0, 0, 14);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.ColumnCount = 1;
            layout.RowCount = 3;
            layout.Dock = DockStyle.Fill;
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            panel.Controls.Add(layout);

            patchButton = new Button();
            patchButton.Anchor = AnchorStyles.None;
            patchButton.Size = new Size(150, 58);
            patchButton.Text = "한글 패치";
            patchButton.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold, GraphicsUnit.Point);
            patchButton.Click += OnPatchButtonClick;
            layout.Controls.Add(patchButton, 0, 1);
            return panel;
        }

        private bool IsBusy
        {
            get { return patchWorker != null && patchWorker.IsBusy; }
        }

        private void OnDropPanelClick(object sender, EventArgs e)
        {
            if (IsBusy)
            {
                return;
            }

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "원본 ISO 선택";
                dialog.Filter = "ISO files (*.iso)|*.iso|All files (*.*)|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    SelectPaths(dialog.FileNames);
                }
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = !IsBusy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (IsBusy)
            {
                return;
            }

            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null)
            {
                SelectPaths(paths);
            }
        }

        private void SelectPaths(string[] paths)
        {
            foreach (string path in paths)
            {
                if (File.Exists(path) && String.Equals(Path.GetExtension(path), ".iso", StringComparison.OrdinalIgnoreCase))
                {
                    selectedIsoPath = path;
                    SetStatus("ISO 선택: " + path);
                }
                else if (Directory.Exists(path))
                {
                    int count;
                    try
                    {
                        count = DlcPatcher.FindPackages(path).Count;
                    }
                    catch (Exception ex)
                    {
                        SetStatus("폴더를 읽을 수 없습니다: " + ex.Message);
                        continue;
                    }

                    if (count == 0)
                    {
                        SetStatus("이 폴더에서 게임 DLC를 찾지 못했습니다: " + path);
                        continue;
                    }

                    selectedDlcFolder = path;
                    SetStatus(String.Format("DLC 폴더 선택: {0} (DLC {1}개)", path, count));
                }
                else
                {
                    SetStatus("ISO 파일 또는 DLC 폴더를 선택해 주세요: " + path);
                }
            }

            UpdateStatusDisplay();
        }

        private void UpdateStatusDisplay()
        {
            SetFileStatus(isoStatusLabel, "(필수) 원본 ISO", selectedIsoPath, File.Exists(selectedIsoPath ?? ""), true);
            SetFileStatus(dlcStatusLabel, "(옵션) DLC 폴더", selectedDlcFolder, Directory.Exists(selectedDlcFolder ?? ""), false);
            bool hasInput = File.Exists(selectedIsoPath ?? "") || Directory.Exists(selectedDlcFolder ?? "");
            patchButton.Enabled = hasInput && !IsBusy;
            introSkipCheckBox.Enabled = !IsBusy;
            developerModeCheckBox.Enabled = !IsBusy;
            dropPanel.Enabled = !IsBusy;
        }

        private static void SetFileStatus(Label label, string caption, string path, bool ready, bool required)
        {
            if (ready)
            {
                label.Text = "✅ " + caption + "  —  " + Path.GetFileName(path.TrimEnd('\\', '/'));
                label.ForeColor = Color.FromArgb(28, 128, 74);
            }
            else if (required)
            {
                label.Text = "❌ " + caption;
                label.ForeColor = Color.FromArgb(190, 55, 62);
            }
            else
            {
                label.Text = "○ " + caption;
                label.ForeColor = Color.FromArgb(104, 112, 126);
            }
        }

        private void OnPatchButtonClick(object sender, EventArgs e)
        {
            PatchOptions options = new PatchOptions
            {
                IsoPath = File.Exists(selectedIsoPath ?? "") ? selectedIsoPath : null,
                DlcFolder = Directory.Exists(selectedDlcFolder ?? "") ? selectedDlcFolder : null,
                IntroSkip = introSkipCheckBox.Checked,
                DeveloperMode = developerModeCheckBox.Checked,
            };
            if (options.IsoPath == null && options.DlcFolder == null)
            {
                SetStatus("원본 ISO 또는 DLC 폴더를 먼저 선택해 주세요.");
                return;
            }

            progressBar.Value = 0;
            SetStatus("패치 실행 중...");

            patchWorker = new BackgroundWorker();
            patchWorker.WorkerReportsProgress = true;
            patchWorker.DoWork += delegate(object s, DoWorkEventArgs args)
            {
                BackgroundWorker worker = (BackgroundWorker)s;
                PatchLog log = new PatchLog(delegate(int percent, string message, bool append)
                {
                    worker.ReportProgress(percent, new ProgressMessage { Text = message });
                });
                PatchJob.Run(options, log);
            };
            patchWorker.ProgressChanged += delegate(object s, ProgressChangedEventArgs args)
            {
                if (args.ProgressPercentage >= 0)
                {
                    progressBar.Value = Math.Max(0, Math.Min(100, args.ProgressPercentage));
                }

                ProgressMessage message = args.UserState as ProgressMessage;
                if (message != null)
                {
                    AppendStatus(message.Text);
                }
            };
            patchWorker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs args)
            {
                UpdateStatusDisplay();
                if (args.Error != null)
                {
                    AppendStatus("오류: " + args.Error.Message);
                    MessageBox.Show(this, args.Error.Message, "패치 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                MessageBox.Show(this, "한글 패치가 완료되었습니다.", "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            patchWorker.RunWorkerAsync();
            UpdateStatusDisplay();
        }

        private void SetStatus(string message)
        {
            statusTextBox.Text = message;
            statusTextBox.SelectionStart = statusTextBox.TextLength;
            statusTextBox.ScrollToCaret();
        }

        private void AppendStatus(string message)
        {
            if (String.IsNullOrEmpty(message))
            {
                return;
            }

            if (statusTextBox.TextLength > 0)
            {
                statusTextBox.AppendText(Environment.NewLine);
            }

            statusTextBox.AppendText(message);
            statusTextBox.SelectionStart = statusTextBox.TextLength;
            statusTextBox.ScrollToCaret();
        }
    }
}
