using Ps1Forge.Core;

namespace Ps1Forge;

public sealed class MainForm : Form
{
    private readonly TextBox _discPath = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox _artPath = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly PictureBox _preview = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(28, 28, 30)
    };
    private readonly Label _status = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Text = "Select a PS1 game and start-screen image."
    };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly Button _convert = new() { Text = "CONVERT", Dock = DockStyle.Fill, Height = 48 };
    private readonly Button _discBrowse = new() { Text = "Select Game", AutoSize = true };
    private readonly Button _artBrowse = new() { Text = "Select Image", AutoSize = true };
    private CancellationTokenSource? _cts;

    public MainForm()
    {
        Text = "Ps1Forge";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 620);
        Size = new Size(820, 680);
        Font = new Font("Segoe UI", 10f);

        var title = new Label
        {
            Text = "PS1Forge",
            Font = new Font("Segoe UI Semibold", 22f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12)
        };

        var subtitle = new Label
        {
            Text = "Input  →  Process  →  Output",
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        };

        var header = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false
        };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        var gameRow = BuildPickerRow("PS1 Game", _discPath, _discBrowse);
        var artRow = BuildPickerRow("Start Screen Image", _artPath, _artBrowse);

        var previewGroup = new GroupBox
        {
            Text = "Preview",
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        previewGroup.Controls.Add(_preview);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 7
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(gameRow, 0, 1);
        layout.Controls.Add(artRow, 0, 2);
        layout.Controls.Add(previewGroup, 0, 3);
        layout.Controls.Add(_progress, 0, 4);
        layout.Controls.Add(_convert, 0, 5);
        layout.Controls.Add(_status, 0, 6);

        Controls.Add(layout);

        _discBrowse.Click += (_, _) => PickDisc();
        _artBrowse.Click += (_, _) => PickArtwork();
        _convert.Click += async (_, _) => await ConvertAsync();
        FormClosing += (_, _) => _cts?.Cancel();
    }

    private static Control BuildPickerRow(string title, TextBox path, Button button)
    {
        var group = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 78,
            Padding = new Padding(10)
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.Controls.Add(path, 0, 0);
        table.Controls.Add(button, 1, 0);

        group.Controls.Add(table);
        return group;
    }

    private void PickDisc()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select PS1 disc image",
            Filter = "PS1 Disc Images|*.cue;*.bin;*.iso;*.img|CUE files|*.cue|BIN files|*.bin|ISO files|*.iso|IMG files|*.img"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _discPath.Text = dialog.FileName;

        try
        {
            var analysis = DiscAnalyzer.Analyze(dialog.FileName);
            _status.Text = $"Ready — {analysis.Serial ?? Path.GetFileNameWithoutExtension(dialog.FileName)} / {analysis.Region}";
        }
        catch (Exception ex)
        {
            _status.Text = "Input warning: " + ex.Message;
        }
    }

    private void PickArtwork()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select start-screen image",
            Filter = "Images|*.png;*.jpg;*.jpeg|PNG|*.png|JPEG|*.jpg;*.jpeg"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _artPath.Text = dialog.FileName;

        using var temp = Image.FromFile(dialog.FileName);
        _preview.Image?.Dispose();
        _preview.Image = new Bitmap(temp);
    }

    private async Task ConvertAsync()
    {
        if (!File.Exists(_discPath.Text))
        {
            MessageBox.Show(this, "Select a PS1 game first.", "Ps1Forge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!File.Exists(_artPath.Text))
        {
            MessageBox.Show(this, "Select a start-screen image first.", "Ps1Forge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var folder = new FolderBrowserDialog
        {
            Description = "Select output folder",
            UseDescriptionForTitle = true
        };

        if (folder.ShowDialog(this) != DialogResult.OK)
            return;

        ToggleBusy(true);
        _cts = new CancellationTokenSource();

        try
        {
            var service = new ConversionService(new ManifestPackageBackend());
            var progress = new Progress<string>(line => _status.Text = line);

            var result = await service.ConvertAsync(
                new ConversionRequest(_discPath.Text, _artPath.Text, folder.SelectedPath),
                progress,
                _cts.Token);

            _status.Text = "Completed: " + result.OutputPath;

            MessageBox.Show(
                this,
                "Pipeline test completed successfully.\n\n" +
                result.OutputPath +
                "\n\nThe current backend emits a validation manifest; the PS4 PKG backend is the next integration step.",
                "Ps1Forge",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            _status.Text = "Failed: " + ex.Message;
            MessageBox.Show(this, ex.ToString(), "Conversion failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            ToggleBusy(false);
        }
    }

    private void ToggleBusy(bool busy)
    {
        _discBrowse.Enabled = !busy;
        _artBrowse.Enabled = !busy;
        _convert.Enabled = !busy;
        _progress.Visible = busy;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _preview.Image?.Dispose();
            _cts?.Dispose();
        }

        base.Dispose(disposing);
    }
}
