using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HVTTool;

public sealed class MainForm : Form
{
    private readonly TreeView _tree = new();
    private readonly PictureBox _picture = new();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripStatusLabel _formatLabel = new();
    private readonly ToolStrip _toolStrip = new();
    private readonly SplitContainer _split = new();
    private readonly ToolStripButton _btnOpen = new("Open Folder...");
    private readonly ToolStripButton _btnExtract = new("Extract PNG...");
    private readonly ToolStripButton _btnExtractAllDic = new("Extract All (DIC)...");
    private readonly ToolStripButton _btnReinsert = new("Reinsert PNG...");
    private readonly ToolStripButton _btnBatchReinsert = new("Batch Reinsert...");
    private readonly ToolStripButton _btnReload = new("Reload");
    private readonly ToolStripDropDownButton _btnView = new("View");
    private readonly ToolStripButton _btnAbout = new("About");
    private readonly ToolStripMenuItem _miCheckered = new("Checkered background");
    private readonly ToolStripMenuItem _miFitToWindow = new("Fit to window");
    private readonly ToolStripMenuItem _miZoom100 = new("100%");
    private readonly ToolStripMenuItem _miZoom200 = new("200%");
    private readonly ToolStripMenuItem _miZoom400 = new("400%");

    private string? _rootFolder;
    private HvtFile? _currentFile;
    private FinalExamHvt? _currentFx;
    private HviFile? _currentHvi;
    private DicTexture? _currentDicTexture;
    private XbrTexture? _currentXbrTexture;
    private readonly System.Collections.Generic.Dictionary<string, DicFile> _dicCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Generic.Dictionary<string, XbrFile> _xbrCache =
        new(StringComparer.OrdinalIgnoreCase);
    private Bitmap? _currentBitmap;
    private float _zoom = 1.0f;
    private bool _fitToWindow = true;
    private bool _checkered = true;

    public MainForm(string? initialPath)
    {
        Text = "ObsCure Texture Editor - PC/PS2/PSP/PS3/Wii/Xbox";
        Width = 1200;
        Height = 800;
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        WireEvents();

        if (!string.IsNullOrEmpty(initialPath))
        {
            if (Directory.Exists(initialPath)) LoadFolder(initialPath);
            else if (File.Exists(initialPath)) LoadFolder(Path.GetDirectoryName(initialPath)!);
        }
    }

    // ----------------------------------------------------------------------
    // UI layout
    // ----------------------------------------------------------------------
    private void BuildUi()
    {
        // Toolbar
        _toolStrip.Items.AddRange(new ToolStripItem[]
        {
            _btnOpen,
            new ToolStripSeparator(),
            _btnExtract,
            _btnExtractAllDic,
            _btnReinsert,
            _btnBatchReinsert,
            new ToolStripSeparator(),
            _btnReload,
            new ToolStripSeparator(),
            _btnView,
            new ToolStripSeparator(),
            _btnAbout
        });
        _btnExtract.Enabled = false;
        _btnExtractAllDic.Enabled = false;
        _btnReinsert.Enabled = false;
        _btnBatchReinsert.Enabled = false;
        _btnReload.Enabled = false;

        _miCheckered.Checked = true;
        _miFitToWindow.Checked = true;
        _btnView.DropDownItems.Add(_miCheckered);
        _btnView.DropDownItems.Add(new ToolStripSeparator());
        _btnView.DropDownItems.Add(_miFitToWindow);
        _btnView.DropDownItems.Add(_miZoom100);
        _btnView.DropDownItems.Add(_miZoom200);
        _btnView.DropDownItems.Add(_miZoom400);

        // Split container (TreeView | Image viewer)
        _split.Dock = DockStyle.Fill;
        _split.SplitterDistance = 280;
        _split.FixedPanel = FixedPanel.Panel1;
        _split.Panel1.Controls.Add(_tree);
        _split.Panel2.Controls.Add(_picture);

        _tree.Dock = DockStyle.Fill;
        _tree.HideSelection = false;
        _tree.PathSeparator = Path.DirectorySeparatorChar.ToString();
        _tree.ShowLines = true;
        _tree.ShowPlusMinus = true;
        _tree.AfterExpand += (_, _) => AutoFitTreeWidth();
        _tree.AfterCollapse += (_, _) => AutoFitTreeWidth();

        _picture.Dock = DockStyle.Fill;
        _picture.SizeMode = PictureBoxSizeMode.Zoom;
        _picture.BackColor = Color.FromArgb(45, 45, 48);
        _picture.Paint += Picture_Paint;

        // Status bar
        _statusStrip.Items.Add(_statusLabel);
        _statusStrip.Items.Add(new ToolStripStatusLabel { Spring = true });
        _statusStrip.Items.Add(_formatLabel);
        _statusLabel.Text = "Open a folder to begin.";

        Controls.Add(_split);
        Controls.Add(_toolStrip);
        Controls.Add(_statusStrip);
    }

    private void WireEvents()
    {
        _btnOpen.Click += (_, _) => OpenFolderDialog();
        _btnReload.Click += (_, _) => { if (_rootFolder != null) LoadFolder(_rootFolder); };
        _btnExtract.Click += (_, _) => ExtractCurrent();
        _btnExtractAllDic.Click += (_, _) => ExtractAllFromDic();
        _btnAbout.Click += (_, _) => ShowAbout();
        _btnReinsert.Click += (_, _) => ReinsertCurrent();
        _btnBatchReinsert.Click += (_, _) => BatchReinsert();
        _tree.AfterSelect += (_, e) => OnTreeSelect(e.Node);
        _miCheckered.Click += (_, _) =>
        {
            _checkered = !_checkered;
            _miCheckered.Checked = _checkered;
            _picture.Invalidate();
        };
        _miFitToWindow.Click += (_, _) => SetFit(true);
        _miZoom100.Click += (_, _) => SetZoom(1.0f);
        _miZoom200.Click += (_, _) => SetZoom(2.0f);
        _miZoom400.Click += (_, _) => SetZoom(4.0f);

        AllowDrop = true;
        DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        };
        DragDrop += (_, e) =>
        {
            var paths = (string[]?)e.Data?.GetData(DataFormats.FileDrop);
            if (paths == null || paths.Length == 0) return;
            string p = paths[0];
            if (Directory.Exists(p)) LoadFolder(p);
            else if (File.Exists(p)) LoadFolder(Path.GetDirectoryName(p)!);
        };
    }

    private void SetFit(bool fit)
    {
        _fitToWindow = fit;
        _miFitToWindow.Checked = fit;
        if (fit)
        {
            _picture.SizeMode = PictureBoxSizeMode.Zoom;
            _picture.Dock = DockStyle.Fill;
        }
        _picture.Invalidate();
    }

    private void SetZoom(float zoom)
    {
        _fitToWindow = false;
        _miFitToWindow.Checked = false;
        _zoom = zoom;
        if (_currentBitmap != null)
        {
            _picture.SizeMode = PictureBoxSizeMode.AutoSize;
            _picture.Dock = DockStyle.None;
            _picture.Size = new Size((int)(_currentBitmap.Width * zoom), (int)(_currentBitmap.Height * zoom));
        }
        _picture.Invalidate();
    }

    // ----------------------------------------------------------------------
    // Folder loading
    // ----------------------------------------------------------------------
    private void OpenFolderDialog()
    {
        using var fbd = new FolderBrowserDialog
        {
            Description = "Select folder containing texture files (.hvt / .hvi / .dic / .dip / .xbr)",
            UseDescriptionForTitle = true
        };
        if (fbd.ShowDialog(this) == DialogResult.OK) LoadFolder(fbd.SelectedPath);
    }

    private void LoadFolder(string folder)
    {
        _rootFolder = folder;
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        var root = new TreeNode(Path.GetFileName(folder.TrimEnd('\\', '/'))) { Tag = folder };
        _tree.Nodes.Add(root);
        try
        {
            PopulateTreeNode(root, folder);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to enumerate folder:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        root.Expand();
        _tree.EndUpdate();
        _btnReload.Enabled = true;
        _btnBatchReinsert.Enabled = true;
        _statusLabel.Text = $"Loaded: {folder}";
        AutoFitTreeWidth();
    }

    // Resize the left panel (tree) so the longest visible node fits without
    // truncation. Bounded so it never eats more than half the window.
    private void AutoFitTreeWidth()
    {
        if (_tree.Nodes.Count == 0) return;
        int max = 0;
        using (var g = _tree.CreateGraphics())
        {
            MeasureVisibleNodes(_tree.Nodes, g, _tree.Font, 0, ref max);
        }
        // indent + scrollbar + checkbox/icon padding
        int target = max + SystemInformation.VerticalScrollBarWidth + 32;
        int minWidth = 160;
        int maxWidth = Math.Max(minWidth, ClientSize.Width / 2);
        target = Math.Clamp(target, minWidth, maxWidth);
        if (Math.Abs(_split.SplitterDistance - target) > 4)
            _split.SplitterDistance = target;
    }

    private static void MeasureVisibleNodes(TreeNodeCollection nodes, Graphics g, Font font, int depth, ref int maxPx)
    {
        foreach (TreeNode n in nodes)
        {
            int w = (int)g.MeasureString(n.Text, font).Width + depth * 19;
            if (w > maxPx) maxPx = w;
            if (n.IsExpanded) MeasureVisibleNodes(n.Nodes, g, font, depth + 1, ref maxPx);
        }
    }

    private void PopulateTreeNode(TreeNode parent, string folder)
    {
        var visibleTextureStems = Directory.EnumerateFiles(folder)
            .Where(IsSupported)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in Directory.EnumerateDirectories(folder)
            .Where(d => !visibleTextureStems.Contains(Path.GetFileName(d)))
            .OrderBy(d => d))
        {
            var dirNode = new TreeNode(Path.GetFileName(dir)) { Tag = dir, ImageKey = "folder" };
            parent.Nodes.Add(dirNode);
            try { PopulateTreeNode(dirNode, dir); }
            catch { /* permission, etc. */ }
        }
        foreach (var file in Directory.EnumerateFiles(folder)
            .Where(f => IsSupported(f))
            .OrderBy(f => f))
        {
            var fileNode = new TreeNode(Path.GetFileName(file)) { Tag = file };
            parent.Nodes.Add(fileNode);

            if (IsDic(file))
            {
                // Lazy-load DIC and add a child entry per texture (name only — the
                // format/dimension info appears in the status bar on selection).
                try
                {
                    var dic = new DicFile(file);
                    _dicCache[file] = dic;
                    foreach (var t in dic.Textures)
                    {
                        var label = $"[{t.Index:D3}] {t.Name}";
                        fileNode.Nodes.Add(new TreeNode(label) { Tag = t });
                    }
                }
                catch (Exception ex)
                {
                    fileNode.Nodes.Add(new TreeNode($"<load error: {ex.Message}>"));
                }
            }
            else if (IsXbr(file))
            {
                try
                {
                    var xbr = new XbrFile(file);
                    _xbrCache[file] = xbr;
                    foreach (var t in xbr.Textures)
                    {
                        var label = $"[{t.Index:D3}] {t.Name}";
                        fileNode.Nodes.Add(new TreeNode(label) { Tag = t });
                    }
                }
                catch (Exception ex)
                {
                    fileNode.Nodes.Add(new TreeNode($"<load error: {ex.Message}>"));
                }
            }
        }
    }

    private static bool IsHvt(string path) => path.EndsWith(".hvt", StringComparison.OrdinalIgnoreCase);
    private static bool IsHvi(string path) => path.EndsWith(".hvi", StringComparison.OrdinalIgnoreCase);
    private static bool IsDic(string path) =>
        path.EndsWith(".dic", StringComparison.OrdinalIgnoreCase)
     || path.EndsWith(".dip", StringComparison.OrdinalIgnoreCase);
    private static bool IsXbr(string path) => path.EndsWith(".xbr", StringComparison.OrdinalIgnoreCase);
    private static bool IsContainer(string path) => IsDic(path) || IsXbr(path);
    private static bool IsSupported(string path) => IsHvt(path) || IsHvi(path) || IsContainer(path);

    // ----------------------------------------------------------------------
    // Selection handling
    // ----------------------------------------------------------------------
    private void OnTreeSelect(TreeNode? node)
    {
        if (node == null) return;
        if (node.Tag is DicTexture dtex) { DisplayDicTexture(dtex); return; }
        if (node.Tag is XbrTexture xtex) { DisplayXbrTexture(xtex); return; }
        if (node.Tag is not string path) { ClearImage(); return; }
        if (Directory.Exists(path)) { ClearImage(); return; }
        if (!File.Exists(path) || !IsSupported(path)) { ClearImage(); return; }
        if (IsContainer(path))
        {
            // Container file selection: clear the viewer (user expands to pick a
            // child texture) but enable the matching "Extract All" button.
            ClearImage();
            int count = 0;
            if (_dicCache.TryGetValue(path, out var dic))
            {
                _currentDicForExtractAll = dic;
                _btnExtractAllDic.Enabled = true;
                count = dic.Textures.Count;
            }
            else if (_xbrCache.TryGetValue(path, out var xbr))
            {
                _currentXbrForExtractAll = xbr;
                _btnExtractAllDic.Enabled = true;
                count = xbr.Textures.Count;
            }
            _statusLabel.Text = count > 0
                ? $"{path}  —  {count} textures (expand to pick one)"
                : path + "  —  expand to pick a texture";
            return;
        }
        TryDisplay(path);
    }

    private DicFile? _currentDicForExtractAll;
    private XbrFile? _currentXbrForExtractAll;

    private void ClearImage()
    {
        _currentFile = null;
        _currentFx = null;
        _currentHvi = null;
        _currentDicTexture = null;
        _currentXbrTexture = null;
        _currentDicForExtractAll = null;
        _currentXbrForExtractAll = null;
        _picture.Image?.Dispose();
        _picture.Image = null;
        _currentBitmap?.Dispose();
        _currentBitmap = null;
        _btnExtract.Enabled = false;
        _btnExtractAllDic.Enabled = false;
        _btnReinsert.Enabled = false;
        _formatLabel.Text = "";
    }

    private void DisplayDicTexture(DicTexture tex)
    {
        try
        {
            byte[] bgra = DicCodec.DecodeToBgra(tex);
            var bmp = BgraToBitmap(bgra, tex.Width, tex.Height);
            _picture.Image?.Dispose();
            _currentBitmap?.Dispose();
            _currentBitmap = bmp;
            _picture.Image = bmp;
            _currentFile = null;
            _currentFx = null;
            _currentHvi = null;
            _currentDicTexture = tex;
            _currentDicForExtractAll = tex.Owner;
            _btnExtract.Enabled = true;
            _btnExtractAllDic.Enabled = true;
            _btnReinsert.Enabled = true;
            _formatLabel.Text = $"{tex.FormatLabel}  {tex.Width}×{tex.Height}  {tex.Bpp}bpp";
            _statusLabel.Text = $"{tex.Owner.Path}  ▸  [{tex.Index}] {tex.Name}";
            if (!_fitToWindow) SetZoom(_zoom);
            _picture.Invalidate();
        }
        catch (Exception ex)
        {
            ClearImage();
            _formatLabel.Text = "decode error";
            _statusLabel.Text = $"[{tex.Index}] {tex.Name}: {ex.Message}";
        }
    }

    private void DisplayXbrTexture(XbrTexture tex)
    {
        try
        {
            byte[] bgra = XbrCodec.DecodeToBgra(tex);
            var bmp = BgraToBitmap(bgra, tex.Width, tex.Height);
            _picture.Image?.Dispose();
            _currentBitmap?.Dispose();
            _currentBitmap = bmp;
            _picture.Image = bmp;
            _currentFile = null;
            _currentFx = null;
            _currentHvi = null;
            _currentDicTexture = null;
            _currentXbrTexture = tex;
            _currentXbrForExtractAll = tex.Owner;
            _btnExtract.Enabled = true;
            _btnExtractAllDic.Enabled = true;
            _btnReinsert.Enabled = true;
            _formatLabel.Text = $"{tex.FormatLabel}  {tex.Width}×{tex.Height}  {tex.Bpp}bpp  mips={tex.MipmapLevels}";
            _statusLabel.Text = $"{tex.Owner.Path}  ▸  [{tex.Index}] {tex.Name}";
            if (!_fitToWindow) SetZoom(_zoom);
            _picture.Invalidate();
        }
        catch (Exception ex)
        {
            ClearImage();
            _formatLabel.Text = "decode error";
            _statusLabel.Text = $"[{tex.Index}] {tex.Name}: {ex.Message}";
        }
    }

    private void TryDisplay(string path)
    {
        try
        {
            byte[] bgra;
            int w, h;
            string formatLabel;

            if (IsHvi(path))
            {
                var hvi = new HviFile(path);
                bgra = Ps2Codec.DecodeToBgra(hvi);
                w = hvi.Width; h = hvi.Height;
                formatLabel = hvi.FormatLabel;
                _currentHvi = hvi;
                _currentFile = null;
                _currentFx = null;
            }
            else if (FinalExamHvt.LooksLikeFinalExam(path))
            {
                var fx = new FinalExamHvt(path);
                bgra = FinalExamCodec.DecodeToBgra(fx);
                w = fx.Width; h = fx.Height;
                formatLabel = fx.FormatLabel;
                _currentFx = fx;
                _currentFile = null;
                _currentHvi = null;
            }
            else
            {
                var hvt = new HvtFile(path);
                bgra = WiiGcCodec.DecodeToBgra(hvt);
                w = hvt.Width; h = hvt.Height;
                formatLabel = $"{hvt.FormatTag} ({hvt.Format})  {hvt.Width}×{hvt.Height}  {hvt.Bpp}bpp";
                _currentFile = hvt;
                _currentHvi = null;
                _currentFx = null;
            }

            var bmp = BgraToBitmap(bgra, w, h);
            _picture.Image?.Dispose();
            _currentBitmap?.Dispose();
            _currentBitmap = bmp;
            _picture.Image = bmp;

            _btnExtract.Enabled = true;
            _btnReinsert.Enabled = true;
            _formatLabel.Text = formatLabel;
            _statusLabel.Text = path;

            if (!_fitToWindow) SetZoom(_zoom);
            _picture.Invalidate();
        }
        catch (Exception ex)
        {
            ClearImage();
            _formatLabel.Text = "load error";
            _statusLabel.Text = $"{Path.GetFileName(path)}: {ex.Message}";
        }
    }

    // ----------------------------------------------------------------------
    // Extract / Reinsert
    // ----------------------------------------------------------------------
    private void ExtractCurrent()
    {
        if (_currentBitmap == null) return;
        string defaultName;
        if (_currentDicTexture != null)
            defaultName = $"{_currentDicTexture.Index:D3}_{SafeFilename(_currentDicTexture.Name)}.png";
        else if (_currentXbrTexture != null)
            defaultName = $"{_currentXbrTexture.Index:D3}_{SafeFilename(_currentXbrTexture.Name)}.png";
        else
        {
            string? srcPath = _currentFile?.Path ?? _currentFx?.Path ?? _currentHvi?.Path;
            if (srcPath == null) return;
            defaultName = Path.GetFileNameWithoutExtension(srcPath) + ".png";
        }
        using var sfd = new SaveFileDialog
        {
            Filter = "PNG image|*.png",
            FileName = defaultName
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _currentBitmap.Save(sfd.FileName, ImageFormat.Png);
            _statusLabel.Text = $"Extracted to {sfd.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string SafeFilename(string name)
    {
        char[] bad = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (var c in name) sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
        return sb.Length > 0 ? sb.ToString() : "texture";
    }

    private void ReinsertCurrent()
    {
        if (_currentDicTexture != null) { ReinsertDicTexture(); return; }
        if (_currentXbrTexture != null) { ReinsertXbrTexture(); return; }
        if (_currentFx != null) { ReinsertFinalExam(); return; }

        bool isHvi = _currentHvi != null;
        bool isHvt = _currentFile != null;
        if (!isHvi && !isHvt) return;

        if (isHvt && _currentFile!.Format == HvtFormat.Unknown)
        {
            MessageBox.Show($"Reinsertion not supported for format \"{_currentFile.FormatTag}\".",
                "Unsupported", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var ofd = new OpenFileDialog
        {
            Filter = "PNG image|*.png|All files|*.*",
            Title = "Select PNG to reinsert"
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            int targetW = isHvi ? _currentHvi!.Width : _currentFile!.Width;
            int targetH = isHvi ? _currentHvi!.Height : _currentFile!.Height;
            string srcPath = isHvi ? _currentHvi!.Path : _currentFile!.Path;
            string ext = isHvi ? ".hvi" : ".hvt";

            using var src = new Bitmap(ofd.FileName);
            if (src.Width != targetW || src.Height != targetH)
            {
                var result = MessageBox.Show(
                    $"PNG is {src.Width}×{src.Height}, file expects {targetW}×{targetH}.\nResize automatically?",
                    "Size mismatch", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result != DialogResult.Yes) return;
            }

            byte[] rgba = BitmapToRgba(src, targetW, targetH);

            string outPath = Path.Combine(
                Path.GetDirectoryName(srcPath)!,
                Path.GetFileNameWithoutExtension(srcPath) + "_new" + ext);

            using var sfd = new SaveFileDialog
            {
                Filter = isHvi ? "HVI file|*.hvi" : "HVT file|*.hvt",
                FileName = Path.GetFileName(outPath),
                InitialDirectory = Path.GetDirectoryName(outPath)
            };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;

            if (isHvi)
            {
                var (pal, pix) = Ps2Codec.EncodeFromRgba(_currentHvi!, rgba);
                _currentHvi!.SaveAs(sfd.FileName, pal, pix);
            }
            else
            {
                byte[] encoded = WiiGcCodec.EncodeFromRgba(_currentFile!, rgba);
                _currentFile!.SaveAs(sfd.FileName, encoded);
            }

            _statusLabel.Text = $"Reinserted: {sfd.FileName}";
            var ask = MessageBox.Show("Reload the new file now?", "Done",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ask == DialogResult.Yes) TryDisplay(sfd.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Reinsert failed:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ----------------------------------------------------------------------
    // DIC: extract every texture in the current DIC to a folder
    // ----------------------------------------------------------------------
    private void ExtractAllFromDic()
    {
        // Resolve which container is currently active.
        string? containerPath = _currentDicForExtractAll?.Path ?? _currentXbrForExtractAll?.Path;
        if (containerPath == null) return;
        int total = _currentDicForExtractAll?.Textures.Count ?? _currentXbrForExtractAll?.Textures.Count ?? 0;

        string defaultFolder = Path.Combine(
            Path.GetDirectoryName(containerPath)!,
            Path.GetFileNameWithoutExtension(containerPath));

        using var fbd = new FolderBrowserDialog
        {
            Description = $"Select output folder for {total} textures",
            UseDescriptionForTitle = true,
            SelectedPath = defaultFolder
        };
        if (fbd.ShowDialog(this) != DialogResult.OK) return;
        Directory.CreateDirectory(fbd.SelectedPath);

        int ok = 0, fail = 0;
        var log = new System.Text.StringBuilder();
        if (_currentDicForExtractAll != null)
        {
            foreach (var t in _currentDicForExtractAll.Textures)
            {
                try
                {
                    byte[] bgra = DicCodec.DecodeToBgra(t);
                    using var bmp = BgraToBitmap(bgra, t.Width, t.Height);
                    string name = $"{t.Index:D3}_{SafeFilename(t.Name)}_{t.Width}x{t.Height}_{t.Format}.png";
                    bmp.Save(Path.Combine(fbd.SelectedPath, name), ImageFormat.Png);
                    ok++;
                }
                catch (Exception ex) { fail++; log.AppendLine($"[{t.Index}] {t.Name}: {ex.Message}"); }
            }
        }
        else if (_currentXbrForExtractAll != null)
        {
            foreach (var t in _currentXbrForExtractAll.Textures)
            {
                try
                {
                    byte[] bgra = XbrCodec.DecodeToBgra(t);
                    using var bmp = BgraToBitmap(bgra, t.Width, t.Height);
                    string name = $"{t.Index:D3}_{SafeFilename(t.Name)}_{t.Width}x{t.Height}_{t.Format}.png";
                    bmp.Save(Path.Combine(fbd.SelectedPath, name), ImageFormat.Png);
                    ok++;
                }
                catch (Exception ex) { fail++; log.AppendLine($"[{t.Index}] {t.Name}: {ex.Message}"); }
            }
        }
        string msg = $"Extracted {ok} / {total} textures to:\n{fbd.SelectedPath}";
        if (fail > 0) msg += $"\n\nFailures ({fail}):\n{log}";
        MessageBox.Show(msg, "Extract All", MessageBoxButtons.OK,
            fail > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        _statusLabel.Text = $"Extract: {ok} ok, {fail} failed.";
    }

    private void ReinsertXbrTexture()
    {
        var tex = _currentXbrTexture;
        if (tex == null) return;

        using var ofd = new OpenFileDialog
        {
            Filter = "PNG image|*.png|All files|*.*",
            Title = $"Select PNG to reinsert into [{tex.Index}] {tex.Name}"
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var src = new Bitmap(ofd.FileName);
            if (src.Width != tex.Width || src.Height != tex.Height)
            {
                var ans = MessageBox.Show(
                    $"PNG is {src.Width}×{src.Height}, texture expects {tex.Width}×{tex.Height}.\nResize automatically?",
                    "Size mismatch", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ans != DialogResult.Yes) return;
            }
            byte[] rgba = BitmapToRgba(src, tex.Width, tex.Height);
            byte[] encoded = XbrCodec.EncodeFromRgba(tex, rgba);

            int expected = tex.Width * tex.Height * tex.Bpp / 8;
            if (encoded.Length != expected)
            {
                MessageBox.Show(
                    $"Encoded mip0 is {encoded.Length} bytes; expected {expected}.",
                    "Size mismatch", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (encoded.Length > tex.ImageSize)
            {
                MessageBox.Show(
                    $"Encoded mip0 ({encoded.Length}) exceeds the slot ({tex.ImageSize}). " +
                    "Cannot reinsert without resizing the container.",
                    "Slot overflow", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string xbrPath = tex.Owner.Path;
            string outPath = Path.Combine(
                Path.GetDirectoryName(xbrPath)!,
                Path.GetFileNameWithoutExtension(xbrPath) + "_new.xbr");

            using var sfd = new SaveFileDialog
            {
                Filter = "XBR file|*.xbr",
                FileName = Path.GetFileName(outPath),
                InitialDirectory = Path.GetDirectoryName(outPath)
            };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;

            tex.Owner.ReplaceImageBytes(tex, encoded, encoded.Length);
            tex.Owner.Save(sfd.FileName);

            _statusLabel.Text = $"Reinserted [{tex.Index}] {tex.Name} → {sfd.FileName}";
            DisplayXbrTexture(tex);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Reinsert failed:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ----------------------------------------------------------------------
    // Final Exam .hvt — reinsert single mip0 from PNG
    // ----------------------------------------------------------------------
    private void ReinsertFinalExam()
    {
        var fx = _currentFx;
        if (fx == null) return;
        if (fx.Format == FxPixelFormat.Unknown)
        {
            MessageBox.Show($"Reinsertion not supported for Final Exam format \"{fx.FormatTag}\".",
                "Unsupported", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var ofd = new OpenFileDialog
        {
            Filter = "PNG image|*.png|All files|*.*",
            Title = "Select PNG to reinsert"
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var src = new Bitmap(ofd.FileName);
            if (src.Width != fx.Width || src.Height != fx.Height)
            {
                var ans = MessageBox.Show(
                    $"PNG is {src.Width}×{src.Height}, file expects {fx.Width}×{fx.Height}.\nResize automatically?",
                    "Size mismatch", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ans != DialogResult.Yes) return;
            }
            byte[] rgba = BitmapToRgba(src, fx.Width, fx.Height);
            byte[] encoded = FinalExamCodec.EncodeFromRgba(fx, rgba);

            if (encoded.Length != fx.Mip0Size)
            {
                MessageBox.Show(
                    $"Encoded mip0 is {encoded.Length} bytes; slot expects {fx.Mip0Size}.",
                    "Size mismatch", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string outPath = Path.Combine(
                Path.GetDirectoryName(fx.Path)!,
                Path.GetFileNameWithoutExtension(fx.Path) + "_new.hvt");

            using var sfd = new SaveFileDialog
            {
                Filter = "HVT file|*.hvt",
                FileName = Path.GetFileName(outPath),
                InitialDirectory = Path.GetDirectoryName(outPath)
            };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;

            fx.SaveAs(sfd.FileName, encoded);
            _statusLabel.Text = $"Reinserted: {sfd.FileName}";
            var ask = MessageBox.Show("Reload the new file now?", "Done",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ask == DialogResult.Yes) TryDisplay(sfd.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Reinsert failed:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ----------------------------------------------------------------------
    // DIC: per-texture reinsert (replaces image bytes in-place inside the DIC)
    // ----------------------------------------------------------------------
    private void ReinsertDicTexture()
    {
        var tex = _currentDicTexture;
        if (tex == null) return;

        using var ofd = new OpenFileDialog
        {
            Filter = "PNG image|*.png|All files|*.*",
            Title = $"Select PNG to reinsert into [{tex.Index}] {tex.Name}"
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var src = new Bitmap(ofd.FileName);
            if (src.Width != tex.Width || src.Height != tex.Height)
            {
                var ans = MessageBox.Show(
                    $"PNG is {src.Width}×{src.Height}, texture expects {tex.Width}×{tex.Height}.\nResize automatically?",
                    "Size mismatch", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ans != DialogResult.Yes) return;
            }
            byte[] rgba = BitmapToRgba(src, tex.Width, tex.Height);
            byte[] encoded = DicCodec.EncodeFromRgba(tex, rgba);

            if (encoded.Length != tex.ImageSize)
            {
                MessageBox.Show(
                    $"Encoded payload is {encoded.Length} bytes but slot needs {tex.ImageSize}. " +
                    $"Cannot reinsert without resizing the container.",
                    "Size mismatch", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string dicPath = tex.Owner.Path;
            string ext = Path.GetExtension(dicPath); // preserve .dic or .dip
            string outPath = Path.Combine(
                Path.GetDirectoryName(dicPath)!,
                Path.GetFileNameWithoutExtension(dicPath) + "_new" + ext);

            using var sfd = new SaveFileDialog
            {
                Filter = ext.Equals(".dip", StringComparison.OrdinalIgnoreCase)
                    ? "DIP file|*.dip" : "DIC file|*.dic",
                FileName = Path.GetFileName(outPath),
                InitialDirectory = Path.GetDirectoryName(outPath)
            };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;

            tex.Owner.ReplaceImageBytes(tex, encoded);
            tex.Owner.Save(sfd.FileName);

            _statusLabel.Text = $"Reinserted [{tex.Index}] {tex.Name} → {sfd.FileName}";
            DisplayDicTexture(tex); // refresh preview
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Reinsert failed:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ----------------------------------------------------------------------
    // Batch reinsert — match PNGs by filename to HVTs in the loaded folder
    // ----------------------------------------------------------------------
    private void BatchReinsert()
    {
        if (_rootFolder == null) return;

        // 1. Index every texture file under the loaded folder by its name (no extension).
        var hvtIndex = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(_rootFolder, "*.*", SearchOption.AllDirectories)
                 .Where(IsSupported))
        {
            string key = Path.GetFileNameWithoutExtension(path);
            if (!hvtIndex.ContainsKey(key)) hvtIndex[key] = path;
        }

        if (hvtIndex.Count == 0)
        {
            MessageBox.Show("No texture files (.hvt / .hvi) found in the loaded folder.", "Batch Reinsert",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 2. Ask for the PNG source folder.
        string pngFolder;
        using (var fbd = new FolderBrowserDialog
        {
            Description = "Select the folder containing the PNGs to reinsert",
            UseDescriptionForTitle = true
        })
        {
            if (fbd.ShowDialog(this) != DialogResult.OK) return;
            pngFolder = fbd.SelectedPath;
        }

        var pngs = Directory.GetFiles(pngFolder, "*.png", SearchOption.TopDirectoryOnly);
        if (pngs.Length == 0)
        {
            MessageBox.Show("No PNG files found in that folder.", "Batch Reinsert",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 3. Ask for the output folder for the new HVTs.
        string outFolder;
        using (var fbd = new FolderBrowserDialog
        {
            Description = "Select the output folder for the rebuilt texture files",
            UseDescriptionForTitle = true,
            SelectedPath = Path.Combine(_rootFolder, "_reinserted")
        })
        {
            if (fbd.ShowDialog(this) != DialogResult.OK) return;
            outFolder = fbd.SelectedPath;
        }
        Directory.CreateDirectory(outFolder);

        bool overwriteOriginals = string.Equals(
            Path.GetFullPath(outFolder).TrimEnd('\\'),
            Path.GetFullPath(_rootFolder).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

        if (overwriteOriginals)
        {
            var ow = MessageBox.Show(
                "Output folder is the same as the source folder.\n" +
                "Original texture files will be OVERWRITTEN. Continue?",
                "Overwrite originals?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (ow != DialogResult.Yes) return;
        }

        // 4. Run the batch with a progress dialog.
        RunBatchReinsert(pngs, hvtIndex, outFolder);
    }

    private void RunBatchReinsert(
        string[] pngs,
        System.Collections.Generic.Dictionary<string, string> hvtIndex,
        string outFolder)
    {
        using var dlg = new BatchProgressDialog(pngs.Length);
        dlg.Show(this);
        dlg.Refresh();

        int ok = 0, skipped = 0, failed = 0;
        var log = new System.Text.StringBuilder();

        for (int i = 0; i < pngs.Length; i++)
        {
            string png = pngs[i];
            string key = Path.GetFileNameWithoutExtension(png);
            dlg.SetProgress(i, $"({i + 1}/{pngs.Length}) {Path.GetFileName(png)}");
            Application.DoEvents();
            if (dlg.Cancelled) { log.AppendLine("Cancelled by user."); break; }

            if (!hvtIndex.TryGetValue(key, out var hvtPath))
            {
                skipped++;
                log.AppendLine($"SKIP  {Path.GetFileName(png)}: no matching HVT for \"{key}\"");
                continue;
            }

            try
            {
                using var src = new Bitmap(png);
                string outPath = Path.Combine(outFolder, Path.GetFileName(hvtPath));

                if (IsHvi(hvtPath))
                {
                    var hvi = new HviFile(hvtPath);
                    byte[] rgba = BitmapToRgba(src, hvi.Width, hvi.Height);
                    var (pal, pix) = Ps2Codec.EncodeFromRgba(hvi, rgba);
                    hvi.SaveAs(outPath, pal, pix);
                }
                else if (FinalExamHvt.LooksLikeFinalExam(hvtPath))
                {
                    var fx = new FinalExamHvt(hvtPath);
                    byte[] rgba = BitmapToRgba(src, fx.Width, fx.Height);
                    byte[] encoded = FinalExamCodec.EncodeFromRgba(fx, rgba);
                    fx.SaveAs(outPath, encoded);
                }
                else
                {
                    var hvt = new HvtFile(hvtPath);
                    byte[] rgba = BitmapToRgba(src, hvt.Width, hvt.Height);
                    byte[] encoded = WiiGcCodec.EncodeFromRgba(hvt, rgba);
                    hvt.SaveAs(outPath, encoded);
                }
                ok++;
            }
            catch (Exception ex)
            {
                failed++;
                log.AppendLine($"FAIL  {Path.GetFileName(png)}: {ex.Message}");
            }
        }
        dlg.SetProgress(pngs.Length, "Done.");
        dlg.Close();

        string summary =
            $"Reinserted: {ok}\n" +
            $"Skipped (no match): {skipped}\n" +
            $"Failed: {failed}\n" +
            $"\nOutput: {outFolder}";

        if (skipped + failed > 0)
        {
            using var resultForm = new Form
            {
                Text = "Batch Reinsert — Summary",
                Width = 700,
                Height = 500,
                StartPosition = FormStartPosition.CenterParent
            };
            var box = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericMonospace, 9),
                Text = summary + "\n\n--- Issues ---\n" + log.ToString()
            };
            resultForm.Controls.Add(box);
            resultForm.ShowDialog(this);
        }
        else
        {
            MessageBox.Show(summary, "Batch Reinsert", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        _statusLabel.Text = $"Batch reinsert: {ok} ok, {skipped} skipped, {failed} failed.";
    }

    // ----------------------------------------------------------------------
    // About dialog — credits and capability summary
    // ----------------------------------------------------------------------
    private void ShowAbout()
    {
        using var dlg = new Form
        {
            Text = "About — ObsCure Texture Editor",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(680, 520)
        };

        var titleLabel = new Label
        {
            Text = "ObsCure Texture Editor",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 16),
            Size = new Size(dlg.ClientSize.Width, 30)
        };
        var subtitleLabel = new Label
        {
            Text = "PC / PS2 / PSP / PS3 / Wii / Xbox texture extractor / reinserter",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Italic),
            ForeColor = Color.DimGray,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 50),
            Size = new Size(dlg.ClientSize.Width, 22)
        };

        var info = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Font = new Font("Consolas", 9f),
            Location = new Point(12, 84),
            Size = new Size(dlg.ClientSize.Width - 24, 380),
            TabStop = false
        };
        info.Text = string.Join(Environment.NewLine, new[]
        {
            "─────────────  CREDITS  ─────────────",
            "",
            "    HeitorSpectre",
            "    Evil Trainer",
            "    marcos_haf  —  HNNEWGAMES",
            "",
            "─────  SUPPORTED FORMATS / PLATFORMS  ─────",
            "",
            "  Containers",
            "    .dic  —  Obscure / Obscure 2 texture dictionary (PC / PS2 / PSP / Wii)",
            "    .dip  —  Obscure 1 (PC)",
            "    .hvi  —  Obscure 1 / Obscure 2 (PS2 / PSP)",
            "    .hvt  —  Obscure 2 (Wii)",
            "    .hvt  —  Final Exam (PC / PS3 / Xbox 360)",
            "    .xbr  —  Obscure 1 (Xbox)",
            "",
            "  PC .dic",
            "    R8G8B8A8 (32 bpp)",
            "    R5G6B5 (16 bpp)",
            "    R5G5B5A1 (16 bpp)",
            "",
            "  PC .dip",
            "    B8G8R8A8 (32 bpp)",
            "    B5G6R5 (16 bpp)",
            "    B5G5R5A1 (16 bpp)",
            "",
            "  PS2 .dic",
            "    PAL4 (4 bpp) (swizzled) (RGBA8888 palette)",
            "    PAL8 (8 bpp) (swizzled) (RGB5551 or RGBA8888 palette)",
            "    RGB5551 (16 bpp) (little-endian)",
            "    RGBA8888 (32 bpp) (PS2 alpha range)",
            "",
            "  PSP .dic / .hvi",
            "    GU_PSM_T4 / PAL4 (4 bpp indexed)",
            "    GU_PSM_T8 / PAL8 (8 bpp indexed)",
            "    RGBA8888 CLUT / palette",
            "    PSP swizzled indexed payload (4-byte palette padding)",
            "    PAL8 .hvi standalone textures",
            "",
            "  Wii .dic",
            "    I8 (8 bpp grayscale)",
            "    IA8 (16 bpp grayscale + alpha)",
            "    RGB5A3 (16 bpp)",
            "    RGBA8 (32 bpp AR/GB interleaved)",
            "    C4 / C8 (4/8 bpp paletted with TLUT)",
            "    CMPR (4 bpp DXT1-style compression)",
            "",
            "  Standalone Texture Files",
            "",
            "  PS2 / PSP .hvi",
            "    PAL8 (8 bpp indexed texture with RGBA palette)",
            "",
            "  Wii .hvt",
            "    I8",
            "    IA8",
            "    RGB5A3",
            "    RGBA8",
            "    C4",
            "    C8",
            "    CMPR",
            "",
            "  Xbox .xbr",
            "    SZ_R5G6B5 (16 bpp) (Morton-order swizzled)",
            "    SZ_A1R5G5B5 (16 bpp) (Morton-order swizzled)",
            "    SZ_A8R8G8B8 (32 bpp) (Morton-order swizzled)",
            "",
            "  Final Exam .hvt",
            "",
            "  PC",
            "    BGRA (32 bpp linear)",
            "    BGRX (32 bpp linear) (alpha forced opaque)",
            "    DXT1 / TXD1 (BC1 compression)",
            "    DXT3 / TXD3 (BC2 compression)",
            "    DXT5 / TXD5 (BC3 compression)",
            "",
            "  PS3",
            "    ARGB (32 bpp swizzled)",
            "    DXT1 / TXD1 (BC1 compression)",
            "    DXT3 / TXD3 (BC2 compression)",
            "    DXT5 / TXD5 (BC3 compression)",
            "",
            "  Xbox 360",
            "    ARGB (32 bpp tiled)",
            "    DXT1 / TXD1 (tiled BC1 compression)",
            "    DXT3 / TXD3 (tiled BC2 compression)",
            "    DXT5 / TXD5 (tiled BC3 compression)",
        });

        var okBtn = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Size = new Size(96, 28),
            Location = new Point(dlg.ClientSize.Width - 116, 478)
        };
        dlg.AcceptButton = okBtn;

        dlg.Controls.Add(titleLabel);
        dlg.Controls.Add(subtitleLabel);
        dlg.Controls.Add(info);
        dlg.Controls.Add(okBtn);
        dlg.Shown += (_, _) =>
        {
            info.SelectionStart = 0;
            info.SelectionLength = 0;
            okBtn.Focus();
        };
        dlg.ShowDialog(this);
    }

    // ----------------------------------------------------------------------
    // Pixel buffer ↔ Bitmap conversions
    // ----------------------------------------------------------------------
    private static Bitmap BgraToBitmap(byte[] bgra, int width, int height)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, width, height);
        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(bgra, y * width * 4, data.Scan0 + y * stride, width * 4);
            }
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }

    private static byte[] BitmapToRgba(Bitmap src, int targetW, int targetH)
    {
        Bitmap working = src;
        bool dispose = false;
        if (src.Width != targetW || src.Height != targetH)
        {
            working = new Bitmap(targetW, targetH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(working))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, targetW, targetH);
            }
            dispose = true;
        }

        byte[] rgba = new byte[targetW * targetH * 4];
        var rect = new Rectangle(0, 0, targetW, targetH);
        var data = working.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] bgra = new byte[targetW * targetH * 4];
            for (int y = 0; y < targetH; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, bgra, y * targetW * 4, targetW * 4);

            for (int i = 0; i < bgra.Length; i += 4)
            {
                rgba[i + 0] = bgra[i + 2];
                rgba[i + 1] = bgra[i + 1];
                rgba[i + 2] = bgra[i + 0];
                rgba[i + 3] = bgra[i + 3];
            }
        }
        finally
        {
            working.UnlockBits(data);
            if (dispose) working.Dispose();
        }
        return rgba;
    }

    // ----------------------------------------------------------------------
    // Painting (checkered background under transparent images)
    // ----------------------------------------------------------------------
    private void Picture_Paint(object? sender, PaintEventArgs e)
    {
        if (!_checkered || _picture.Image == null) return;
        // PictureBox draws image on top of background — nothing else to do.
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _picture.BackgroundImage = BuildCheckerTile();
        _picture.BackgroundImageLayout = ImageLayout.Tile;
    }

    private static Bitmap BuildCheckerTile()
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(80, 80, 80));
        using var brush = new SolidBrush(Color.FromArgb(110, 110, 110));
        g.FillRectangle(brush, 0, 0, 8, 8);
        g.FillRectangle(brush, 8, 8, 8, 8);
        return bmp;
    }
}
