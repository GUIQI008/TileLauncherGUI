#nullable enable

using HandyControl.Controls;
using HandyControl.Data;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing2D = System.Drawing.Drawing2D;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingImage = System.Drawing.Image;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using Path = System.IO.Path;

namespace TileLauncherGUI
{
    public partial class MainWindow : HandyControl.Controls.Window
    {
        private readonly string _outputPath;
        private string? _imgMasterPath;
        private string? _imgSmallPath;
        private string? _imgWidePath;
        private string? _imgLargePath;
        private Button? _currentSelectedBtn;
        private string? _puzzleImagePath;
        private List<PuzzleItem> _puzzleItems = new List<PuzzleItem>();
        private TranslateTransform _imageTranslate = new TranslateTransform();
        private ScaleTransform _imageScale = new ScaleTransform();
        private TransformGroup _imageTransformGroup = new TransformGroup();
        private Point _lastMousePosition;
        private bool _isDraggingImage = false;

        public MainWindow()
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            InitializeComponent();
            VersionText.Text = "v" + UpdateManager.CurrentVersion;

            _outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MyGameLaunchers");
            Directory.CreateDirectory(_outputPath);
            CheckEnvironment();
            this.Loaded += (s, e) => DrawInteractiveGrid();
        }

        private async void CheckEnvironment()
        {
            bool dotnetExists = await Task.Run(() =>
            {
                try { using var p = Process.Start(new ProcessStartInfo { FileName = "dotnet", Arguments = "--version", CreateNoWindow = true, UseShellExecute = false }); p.WaitForExit(); return p.ExitCode == 0; } catch { return false; }
            });
            if (!dotnetExists) HandyControl.Controls.MessageBox.Warning("未检测到 .NET SDK。\n生成功能依赖 .NET 8.0 SDK，请务必安装。", "环境缺失");
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewSingleApp == null || ViewPuzzle == null || ViewManage == null || ViewAbout == null) return;
            ViewSingleApp.Visibility = ViewPuzzle.Visibility = ViewManage.Visibility = ViewAbout.Visibility = Visibility.Collapsed;
            if (NavList.SelectedIndex == 0) ViewSingleApp.Visibility = Visibility.Visible;
            else if (NavList.SelectedIndex == 1) ViewPuzzle.Visibility = Visibility.Visible;
            else if (NavList.SelectedIndex == 2) { ViewManage.Visibility = Visibility.Visible; LoadInstalledTiles(); }
            else if (NavList.SelectedIndex == 3) ViewAbout.Visibility = Visibility.Visible;
        }

        // ================= 单应用逻辑 =================

        private void BtnSelectTarget_Click(object sender, FunctionEventArgs<string> e)
        {
            var d = new OpenFileDialog { Filter = "Executable|*.exe|All Files|*.*" };
            if (d.ShowDialog() == true) TxtTargetPath.Text = d.FileName;
        }

        private void BtnTileSelect_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            if (_currentSelectedBtn != null) { _currentSelectedBtn.BorderBrush = new SolidColorBrush(Color.FromArgb(51, 255, 255, 255)); _currentSelectedBtn.BorderThickness = new Thickness(2); }
            _currentSelectedBtn = btn;
            _currentSelectedBtn.BorderBrush = (Brush)FindResource("PrimaryBrush"); _currentSelectedBtn.BorderThickness = new Thickness(4);
            BtnSetImg.IsEnabled = true; BtnRemoveImg.IsEnabled = true;
        }

        private void BtnSetImg_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSelectedBtn == null) return;
            var d = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
            if (d.ShowDialog() == true)
            {
                var bmp = LoadBitmapSafe(d.FileName);
                _currentSelectedBtn.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                if (_currentSelectedBtn.Content is TextBlock tb) tb.Opacity = 0;
                string tag = _currentSelectedBtn.Tag.ToString()!;
                switch (tag)
                {
                    case "Master": _imgMasterPath = d.FileName; break;
                    case "Small": _imgSmallPath = d.FileName; break;
                    case "Wide": _imgWidePath = d.FileName; break;
                    case "Large": _imgLargePath = d.FileName; break;
                }
            }
        }

        private void BtnRemoveImg_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSelectedBtn == null) return;
            _currentSelectedBtn.Background = new SolidColorBrush(Color.FromArgb(17, 255, 255, 255));
            if (_currentSelectedBtn.Content is TextBlock tb) tb.Opacity = 0.5;
            string tag = _currentSelectedBtn.Tag.ToString()!;
            switch (tag)
            {
                case "Master": _imgMasterPath = null; break;
                case "Small": _imgSmallPath = null; break;
                case "Wide": _imgWidePath = null; break;
                case "Large": _imgLargePath = null; break;
            }
        }

        private void BtnAddLive_Click(object sender, RoutedEventArgs e)
        {
            var d = new OpenFileDialog { Filter = "Images|*.png;*.jpg", Multiselect = true };
            if (d.ShowDialog() == true) foreach (var f in d.FileNames) if (ListLiveImages.Items.Count < 5) ListLiveImages.Items.Add(f);
        }
        private void BtnClearLive_Click(object sender, RoutedEventArgs e) => ListLiveImages.Items.Clear();

        private async void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            string id = TxtAppId.Text.Trim();
            if (string.IsNullOrEmpty(id) || !Regex.IsMatch(id, @"^[a-zA-Z][a-zA-Z0-9]*$")) { HandyControl.Controls.MessageBox.Warning("AppID 无效！\n必须以字母开头，仅包含字母和数字。"); return; }
            if (string.IsNullOrEmpty(_imgMasterPath)) { HandyControl.Controls.MessageBox.Warning("请至少设置【Medium/主图标】图片。"); return; }

            var cfg = new TileConfig
            {
                AppId = id,
                DisplayName = string.IsNullOrWhiteSpace(TxtDisplayName.Text) ? null : TxtDisplayName.Text.Trim(),
                TargetPath = TxtTargetPath.Text.Trim(),
                Master = _imgMasterPath,
                StaticSmall = _imgSmallPath,
                StaticWide = _imgWidePath,
                StaticLarge = _imgLargePath,
                EnableLiveTile = ChkEnableLive.IsChecked == true,
                AutoCropToFill = ChkAutoCrop.IsChecked == true
            };
            foreach (var it in ListLiveImages.Items) cfg.LiveImages.Add(it.ToString()!);

            BtnGenerate.IsEnabled = false;
            if (PanelSingleProgress != null) PanelSingleProgress.Visibility = Visibility.Visible;

            try { await Task.Run(() => BuildProcess(cfg)); HandyControl.Controls.MessageBox.Success($"成功！\n请在开始菜单搜索: {cfg.DisplayName ?? id}"); }
            catch (Exception ex) { HandyControl.Controls.MessageBox.Error($"生成失败:\n{ex.Message}"); }
            finally { BtnGenerate.IsEnabled = true; if (PanelSingleProgress != null) PanelSingleProgress.Visibility = Visibility.Collapsed; }
        }

        // ================= 拼图逻辑 =================

        private void BtnSelectPuzzleImage_Click(object sender, RoutedEventArgs e)
        {
            var d = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
            if (d.ShowDialog() == true)
            {
                _puzzleImagePath = d.FileName;
                double availableWidth = PuzzleBorder.Width; if (double.IsNaN(availableWidth)) availableWidth = 500;
                int rows = (int)NumRows.Value; int cols = (int)NumCols.Value;
                double cellSize = availableWidth / cols;
                GridPuzzleContainer.Width = availableWidth; GridPuzzleContainer.Height = cellSize * rows;

                var bmp = LoadBitmapSafe(_puzzleImagePath);
                double imgW = bmp.Width; double imgH = bmp.Height;
                double scaleX = availableWidth / imgW; double scaleY = (cellSize * rows) / imgH;
                double finalScale = Math.Min(scaleX, scaleY);
                double offsetX = (availableWidth - imgW * finalScale) / 2;
                double offsetY = ((cellSize * rows) - imgH * finalScale) / 2;

                _imageScale = new ScaleTransform(finalScale, finalScale);
                _imageTranslate = new TranslateTransform(offsetX, offsetY);
                _imageTransformGroup = new TransformGroup();
                _imageTransformGroup.Children.Add(_imageScale); _imageTransformGroup.Children.Add(_imageTranslate);
                ImgPuzzlePreview.Source = bmp; ImgPuzzlePreview.RenderTransform = _imageTransformGroup;

                GridPuzzleInteractive.MouseRightButtonDown -= OnPuzzleRightMouseDown; GridPuzzleInteractive.MouseRightButtonDown += OnPuzzleRightMouseDown;
                GridPuzzleInteractive.MouseRightButtonUp -= OnPuzzleRightMouseUp; GridPuzzleInteractive.MouseRightButtonUp += OnPuzzleRightMouseUp;
                GridPuzzleInteractive.MouseMove -= OnPuzzleMouseMove; GridPuzzleInteractive.MouseMove += OnPuzzleMouseMove;
                GridPuzzleInteractive.MouseWheel -= OnPuzzleMouseWheel; GridPuzzleInteractive.MouseWheel += OnPuzzleMouseWheel;

                _puzzleItems.Clear(); DrawInteractiveGrid();
            }
        }

        private void PuzzleConfig_Changed(object sender, FunctionEventArgs<double> e) { _puzzleItems.Clear(); DrawInteractiveGrid(); }

        private void DrawInteractiveGrid()
        {
            if (GridPuzzleInteractive == null || PuzzleBorder == null) return;
            double w = PuzzleBorder.Width; if (double.IsNaN(w)) w = 500;
            int r = (int)NumRows.Value; int c = (int)NumCols.Value;
            double cell = w / c;
            GridPuzzleContainer.Width = w; GridPuzzleContainer.Height = cell * r;

            GridPuzzleInteractive.Children.Clear(); GridPuzzleInteractive.RowDefinitions.Clear(); GridPuzzleInteractive.ColumnDefinitions.Clear();
            for (int i = 0; i < r; i++) GridPuzzleInteractive.RowDefinitions.Add(new RowDefinition());
            for (int i = 0; i < c; i++) GridPuzzleInteractive.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < r; i++) for (int j = 0; j < c; j++)
                {
                    var b = new Border { BorderBrush = new SolidColorBrush(Colors.White) { Opacity = 0.3 }, BorderThickness = new Thickness(0.5) };
                    Grid.SetRow(b, i); Grid.SetColumn(b, j); GridPuzzleInteractive.Children.Add(b);
                }
            foreach (var it in _puzzleItems.ToList())
            {
                if (it.Row >= r || it.Col >= c) { _puzzleItems.Remove(it); continue; }
                var t = new Border { BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(Colors.Cyan), Background = new SolidColorBrush(Colors.Cyan) { Opacity = 0.2 }, Margin = new Thickness(1), IsHitTestVisible = false };
                t.Child = new TextBlock { Text = it.Type == TileType.Large ? "大" : (it.Type == TileType.Wide ? "宽" : "中"), Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold };
                Grid.SetRow(t, it.Row); Grid.SetColumn(t, it.Col); Grid.SetRowSpan(t, it.Type == TileType.Large ? 2 : 1); Grid.SetColumnSpan(t, (it.Type == TileType.Large || it.Type == TileType.Wide) ? 2 : 1);
                GridPuzzleInteractive.Children.Add(t);
            }
            TxtPuzzleStats.Text = $"当前网格: {r}行 x {c}列 | 已放置: {_puzzleItems.Count} 个磁贴";
        }

        private void OnPuzzleRightMouseDown(object sender, MouseButtonEventArgs e) { _isDraggingImage = true; _lastMousePosition = e.GetPosition(GridPuzzleInteractive); GridPuzzleInteractive.CaptureMouse(); }
        private void OnPuzzleRightMouseUp(object sender, MouseButtonEventArgs e) { _isDraggingImage = false; GridPuzzleInteractive.ReleaseMouseCapture(); }
        private void OnPuzzleMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingImage) { var c = e.GetPosition(GridPuzzleInteractive); var d = c - _lastMousePosition; _imageTranslate.X += d.X; _imageTranslate.Y += d.Y; _lastMousePosition = c; }
        }
        private void OnPuzzleMouseWheel(object sender, MouseWheelEventArgs e) { double z = e.Delta > 0 ? 1.1 : 0.9; _imageScale.ScaleX *= z; _imageScale.ScaleY *= z; }
        private void GridPuzzleInteractive_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            var p = e.GetPosition(GridPuzzleInteractive);
            int rows = (int)NumRows.Value; int cols = (int)NumCols.Value;
            int c = (int)(p.X / (GridPuzzleContainer.Width / cols)); int r = (int)(p.Y / (GridPuzzleContainer.Height / rows));
            if (c >= cols || r >= rows) return;

            if (RadioDelete.IsChecked == true) _puzzleItems.RemoveAll(x => r >= x.Row && r < x.Row + (x.Type == TileType.Large ? 2 : 1) && c >= x.Col && c < x.Col + (x.Type == TileType.Large || x.Type == TileType.Wide ? 2 : 1));
            else
            {
                TileType t = RadioPlaceWide.IsChecked == true ? TileType.Wide : (RadioPlaceLarge.IsChecked == true ? TileType.Large : TileType.Mid);
                int rs = t == TileType.Large ? 2 : 1; int cs = (t == TileType.Large || t == TileType.Wide) ? 2 : 1;
                if (r + rs <= rows && c + cs <= cols)
                {
                    _puzzleItems.RemoveAll(it => { int r1 = it.Row, c1 = it.Col, rs1 = it.Type == TileType.Large ? 2 : 1, cs1 = (it.Type == TileType.Large || it.Type == TileType.Wide) ? 2 : 1; return r < r1 + rs1 && r + rs > r1 && c < c1 + cs1 && c + cs > c1; });
                    _puzzleItems.Add(new PuzzleItem { Row = r, Col = c, Type = t });
                }
            }
            DrawInteractiveGrid();
        }

        private async void BtnGeneratePuzzle_Click(object sender, RoutedEventArgs e)
        {
            if (_puzzleItems.Count == 0) return;
            if (!Regex.IsMatch(TxtPuzzlePrefix.Text, @"^[a-zA-Z][a-zA-Z0-9]*$")) { HandyControl.Controls.MessageBox.Warning("ID前缀无效"); return; }
            BtnGeneratePuzzle.IsEnabled = false; PanelProgress.Visibility = Visibility.Visible; ProgressPuzzle.Maximum = _puzzleItems.Count; ProgressPuzzle.Value = 0;
            try
            {
                int rw = (int)GridPuzzleContainer.Width; int rh = (int)GridPuzzleContainer.Height;
                var rtb = new RenderTargetBitmap(rw, rh, 96, 96, PixelFormats.Pbgra32);
                var dv = new DrawingVisual(); using (var ctx = dv.RenderOpen()) { var b = new VisualBrush(ImgPuzzlePreview) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }; ctx.DrawRectangle(b, null, new Rect(0, 0, rw, rh)); }
                rtb.Render(dv); var frozen = new CachedBitmap(rtb, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); frozen.Freeze();

                int cVal = (int)NumCols.Value; int rVal = (int)NumRows.Value; string prefix = TxtPuzzlePrefix.Text;
                await Task.Run(() => {
                    int fin = 0; double cw = (double)rw / cVal; double ch = (double)rh / rVal;
                    foreach (var it in _puzzleItems)
                    {
                        int wr = (it.Type == TileType.Large || it.Type == TileType.Wide) ? 2 : 1; int hr = (it.Type == TileType.Large) ? 2 : 1;
                        int rx = (int)(it.Col * cw); int ry = (int)(it.Row * ch); int rW = (int)(cw * wr); int rH = (int)(ch * hr);
                        if (rx + rW > frozen.PixelWidth) rW = frozen.PixelWidth - rx; if (ry + rH > frozen.PixelHeight) rH = frozen.PixelHeight - ry;
                        var crop = new CroppedBitmap(frozen, new Int32Rect(rx, ry, rW, rH));
                        string tImg = Path.Combine(Path.GetTempPath(), $"pz_{prefix}_{it.Row}_{it.Col}.png");
                        using (var fs = File.Create(tImg)) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(crop)); enc.Save(fs); }
                        var cfg = new TileConfig { AppId = $"{prefix}R{it.Row}C{it.Col}", Master = tImg };
                        if (it.Type == TileType.Wide) cfg.StaticWide = tImg; if (it.Type == TileType.Large) cfg.StaticLarge = tImg;
                        BuildProcess(cfg);
                        fin++; Dispatcher.Invoke(() => ProgressPuzzle.Value = fin);
                    }
                });
                HandyControl.Controls.MessageBox.Success("拼图生成完成！");
            }
            catch (Exception ex) { HandyControl.Controls.MessageBox.Error($"错误: {ex.Message}"); }
            finally { BtnGeneratePuzzle.IsEnabled = true; PanelProgress.Visibility = Visibility.Collapsed; }
        }

        // ================= 管理页逻辑 =================

        public class InstalledTile { public string Id { get; set; } = ""; public string Path { get; set; } = ""; public ImageSource? Icon { get; set; } }
        private void BtnRefreshList_Click(object sender, RoutedEventArgs e) => LoadInstalledTiles();
        private void LoadInstalledTiles()
        {
            try
            {
                var list = new List<InstalledTile>();
                if (Directory.Exists(_outputPath))
                {
                    foreach (var dir in Directory.GetDirectories(_outputPath))
                    {
                        string pkg = Path.Combine(dir, "Package");
                        if (Directory.Exists(pkg))
                        {
                            string icon = Path.Combine(pkg, "Assets", "Square150x150Logo.png");
                            if (!File.Exists(icon)) icon = Path.Combine(pkg, "AppIcon.ico");
                            ImageSource? src = null;
                            if (File.Exists(icon)) try { src = LoadBitmapSafe(icon); } catch { }
                            list.Add(new InstalledTile { Id = Path.GetFileName(dir), Path = dir, Icon = src });
                        }
                    }
                }
                ListInstalledTiles.ItemsSource = list;
            }
            catch { }
        }

        private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is InstalledTile tile && HandyControl.Controls.MessageBox.Ask($"确认删除 \"{tile.Id}\"？", "确认") == MessageBoxResult.OK)
            {
                try
                {
                    if (ListInstalledTiles.ItemsSource is List<InstalledTile> list) { list.Remove(tile); ListInstalledTiles.ItemsSource = null; ListInstalledTiles.ItemsSource = list; }
                    GC.Collect(); GC.WaitForPendingFinalizers();
                    try { foreach (var p in Process.GetProcessesByName(tile.Id)) p.Kill(); } catch { }
                    await Task.Run(() => RunCmd("powershell", $"-NoProfile -Command \"Get-AppxPackage -Name {tile.Id}.Launcher | Remove-AppxPackage\""));
                    await Task.Delay(1000);
                    int retry = 5; while (retry-- > 0) { try { if (Directory.Exists(tile.Path)) Directory.Delete(tile.Path, true); break; } catch { await Task.Delay(500); } }
                    if (Directory.Exists(tile.Path)) HandyControl.Controls.MessageBox.Warning("文件夹被占用，请稍后手动删除。"); else HandyControl.Controls.MessageBox.Success("删除成功");
                    LoadInstalledTiles();
                }
                catch (Exception ex) { HandyControl.Controls.MessageBox.Error(ex.Message); LoadInstalledTiles(); }
            }
        }

        // ================= 核心构建 =================

        private void BuildProcess(TileConfig config)
        {
            string safeId = config.AppId;
            string projectDir = Path.Combine(_outputPath, safeId, "Source");
            string publishDir = Path.Combine(_outputPath, safeId, "Package");

            if (Directory.Exists(projectDir)) Directory.Delete(projectDir, true);
            Directory.CreateDirectory(projectDir);

            File.WriteAllText(Path.Combine(projectDir, $"{safeId}.csproj"), TemplateModels.GetCsproj(config.EnableLiveTile));
            File.WriteAllText(Path.Combine(projectDir, "app.manifest"), TemplateModels.GetAppManifest());
            File.WriteAllText(Path.Combine(projectDir, "Program.cs"),TemplateModels.GetProgramCs(config.TargetPath, true, true, true, config.EnableLiveTile));
            GeneratorUtils.GenerateIconSafe(config.Master, Path.Combine(projectDir, "AppIcon.ico"));
            GeneratorUtils.GenAssets(config, publishDir);

            RunCmd("dotnet", $"restore \"{projectDir}\"");
            RunCmd("dotnet", $"publish \"{projectDir}\" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o \"{publishDir}\"");

            string finalExe = $"{safeId}.exe";
            string rawExe = Path.Combine(publishDir, $"{safeId}.exe");
            if (!File.Exists(rawExe))
            {
                var exes = Directory.GetFiles(publishDir, "*.exe");
                if (exes.Length > 0) File.Move(exes[0], Path.Combine(publishDir, finalExe));
            }

            string manifest = TemplateModels.GetManifest(safeId, config.DisplayName ?? safeId, finalExe, config.DisplayName == null);
            File.WriteAllText(Path.Combine(publishDir, "AppxManifest.xml"), manifest);
            RunCmd("powershell", $"-NoProfile -Command \"Add-AppxPackage -Register '{Path.Combine(publishDir, "AppxManifest.xml")}'\"");

            try
            {
                if (Directory.Exists(projectDir)) Directory.Delete(projectDir, true);
            }
            catch { /* 忽略删除失败，比如文件被锁，但不影响使用 */ }
        }

        private void RunCmd(string exe, string args)
        {
            var psi = new ProcessStartInfo { FileName = exe, Arguments = args, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
            using var p = Process.Start(psi); string o = p.StandardOutput.ReadToEnd(); string e = p.StandardError.ReadToEnd(); p.WaitForExit();
            if (p.ExitCode != 0) throw new Exception($"执行失败: {exe}\n{e}\n{o}");
        }

        private BitmapImage LoadBitmapSafe(string path) { var b = new BitmapImage(); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.UriSource = new Uri(path); b.EndInit(); b.Freeze(); return b; }

        public enum TileType { Mid, Wide, Large }
        public class PuzzleItem { public int Row; public int Col; public TileType Type; }
        public class TileConfig
        {
            public string AppId = "";
            public string? DisplayName;
            public string TargetPath = "";
            public string Master = "";
            public string? StaticSmall;
            public string? StaticWide;
            public string? StaticLarge;
            public bool EnableLiveTile;
            public bool AutoCropToFill = true;
            public List<string> LiveImages = new List<string>();
        }
    }

    public static class TemplateModels
    {

        public static string GetCsproj(bool enableLive)
        {
            string framework = enableLive ? "net8.0-windows10.0.19041.0" : "net8.0-windows";
            string compression = enableLive ? "<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>" : "";

            return $@"<Project Sdk=""Microsoft.NET.Sdk"">
<PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>{framework}</TargetFramework>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <Platforms>x64</Platforms>
    <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>false</SelfContained>
    {compression}
    <DebugType>none</DebugType>
    <DebugSymbols>false</DebugSymbols>
    <UseWindowsForms>false</UseWindowsForms>
    <UseWPF>false</UseWPF>
</PropertyGroup>
</Project>";
        }

        public static string GetAppManifest() => @"<?xml version=""1.0"" encoding=""utf-8""?><assembly manifestVersion=""1.0"" xmlns=""urn:schemas-microsoft-com:asm.v1""><assemblyIdentity version=""1.0.0.0"" name=""MyLauncher.app""/><trustInfo xmlns=""urn:schemas-microsoft-com:asm.v2""><security><requestedPrivileges xmlns=""urn:schemas-microsoft-com:asm.v3""><requestedExecutionLevel level=""asInvoker"" uiAccess=""false""/></requestedPrivileges></security></trustInfo><application xmlns=""urn:schemas-microsoft-com:asm.v3""><windowsSettings><dpiAware xmlns=""http://schemas.microsoft.com/SMI/2005/WindowsSettings"">true/PM</dpiAware><dpiAwareness xmlns=""http://schemas.microsoft.com/SMI/2016/WindowsSettings"">PerMonitorV2, PerMonitor</dpiAwareness></windowsSettings></application></assembly>";
        public static string GetProgramCs(string t, bool uM, bool uW, bool uL, bool enableLive)
        {
            string safeTarget = (t ?? "").Replace("\\", "\\\\");
            string usings = enableLive ? "using Windows.UI.Notifications;\nusing Windows.Data.Xml.Dom;" : "";
            string updateCall = enableLive ? "TryUpdate();" : "";

            string updateMethod = "";
            if (enableLive)
            {
                string binds = "";
                string com = "branding='none' hint-overlay='0'";
                if (uM) binds += $"<binding template='TileMedium' {com}><image src='ms-appx:///Assets/Live/{{0}}'/></binding>";
                if (uW) binds += $"<binding template='TileWide' {com}><image src='ms-appx:///Assets/Live/{{0}}'/></binding>";
                if (uL) binds += $"<binding template='TileLarge' {com}><image src='ms-appx:///Assets/Live/{{0}}'/></binding>";

                updateMethod = $@"
        static void TryUpdate(){{
            try{{
                string b=AppDomain.CurrentDomain.BaseDirectory;
                string l=Path.Combine(b,""Assets"",""Live"");
                if(!Directory.Exists(l))return;
                var f=Directory.GetFiles(l,""*.png"");
                if(f.Length==0)return;
                var u=TileUpdateManager.CreateTileUpdaterForApplication();
                u.EnableNotificationQueue(true);
                u.Clear();
                string xmlTemplate = @""<tile><visual version='2'>{binds}</visual></tile>"";
                foreach(var x in f.Take(5)){{
                    string fileName=Path.GetFileName(x);
                    string xml = string.Format(xmlTemplate, fileName);
                    XmlDocument d=new XmlDocument();
                    d.LoadXml(xml);
                    u.Update(new TileNotification(d));
                }}
            }}catch{{}}
        }}";
            }

            return $@"using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
{usings}

namespace MyLauncher
{{
    static class Program
    {{
        [STAThread]
        static void Main()
        {{
            {updateCall}
            string i=@""{safeTarget}"";
            if(string.IsNullOrWhiteSpace(i))return;
            ProcessStartInfo p=new ProcessStartInfo();
            p.UseShellExecute=true;
            if(long.TryParse(i,out _))p.FileName=""steam://run/""+i;
            else if(i.StartsWith(""steam://"",StringComparison.OrdinalIgnoreCase))p.FileName=i;
            else{{
                p.FileName=i;
                try{{
                    string d=Path.GetDirectoryName(i);
                    if(!string.IsNullOrEmpty(d))p.WorkingDirectory=d;
                }}catch{{}}
            }}
            try{{Process.Start(p);}}catch{{}}
        }}
        {updateMethod}
    }}
}}";
        }

        public static string GetManifest(string id, string d, string e, bool h)
        {
            string s = d.Length > 30 ? d.Substring(0, 30) : d;
            return $@"<?xml version=""1.0"" encoding=""utf-8""?><Package xmlns=""http://schemas.microsoft.com/appx/manifest/foundation/windows10"" xmlns:uap=""http://schemas.microsoft.com/appx/manifest/uap/windows10"" xmlns:rescap=""http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"" IgnorableNamespaces=""rescap""><Identity Name=""{id}.Launcher"" Publisher=""CN=MyTile"" Version=""1.0.0.0"" /><Properties><DisplayName>{System.Security.SecurityElement.Escape(d)}</DisplayName><PublisherDisplayName>TileFactory</PublisherDisplayName><Logo>Assets\StoreLogo.png</Logo></Properties><Dependencies><TargetDeviceFamily Name=""Windows.Desktop"" MinVersion=""10.0.17763.0"" MaxVersionTested=""10.0.19041.0"" /></Dependencies><Resources><Resource Language=""zh-cn"" /></Resources><Applications><Application Id=""App"" Executable=""{e}"" EntryPoint=""Windows.FullTrustApplication""><uap:VisualElements DisplayName=""{System.Security.SecurityElement.Escape(d)}"" Description=""Tile"" BackgroundColor=""transparent"" Square150x150Logo=""Assets\Square150x150Logo.png"" Square44x44Logo=""Assets\Square44x44Logo.png""><uap:DefaultTile Wide310x150Logo=""Assets\Wide310x150Logo.png"" Square310x310Logo=""Assets\Square310x310Logo.png"" Square71x71Logo=""Assets\SmallTile.png"" {(h ? "" : "ShortName=\"" + System.Security.SecurityElement.Escape(s) + "\"")} /><uap:SplashScreen Image=""Assets\SplashScreen.png"" /></uap:VisualElements></Application></Applications><Capabilities><rescap:Capability Name=""runFullTrust"" /></Capabilities></Package>";
        }
    }

    public static class GeneratorUtils
    {
        private const double Scale = 3.0;
        public static void GenerateIconSafe(string src, string dest) { try { using var b = new DrawingBitmap(256, 256); if (File.Exists(src)) { using var fs = File.OpenRead(src); using var img = DrawingImage.FromStream(fs); using var g = DrawingGraphics.FromImage(b); g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(img, 0, 0, 256, 256); } else { using var g = DrawingGraphics.FromImage(b); g.Clear(DrawingColor.Teal); } using var fd = File.Create(dest); fd.Write(new byte[] { 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 1, 0, 32, 0 }, 0, 14); using var ms = new MemoryStream(); b.Save(ms, DrawingImageFormat.Png); byte[] d = ms.ToArray(); fd.Write(BitConverter.GetBytes(d.Length), 0, 4); fd.Write(BitConverter.GetBytes(22), 0, 4); fd.Write(d, 0, d.Length); } catch { } }
        public static void GenAssets(MainWindow.TileConfig c, string outDir)
        {
            string a = Path.Combine(outDir, "Assets"), l = Path.Combine(a, "Live");
            Directory.CreateDirectory(a); if (c.EnableLiveTile) Directory.CreateDirectory(l);
            using var mb = File.Exists(c.Master) ? LoadImg(c.Master) : new DrawingBitmap(512, 512);
            void S(string? p, int w, int h, string n)
            {
                int tw = (int)(w * Scale), th = (int)(h * Scale);
                using var t = new DrawingBitmap(tw, th);
                using var g = DrawingGraphics.FromImage(t);
                g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic;
                g.CompositingQuality = Drawing2D.CompositingQuality.HighQuality;
                g.SmoothingMode = Drawing2D.SmoothingMode.HighQuality;
                g.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality;
                using var s = (!string.IsNullOrEmpty(p) && File.Exists(p)) ? LoadImg(p) : (DrawingImage)mb.Clone();

                float r;
                if (c.AutoCropToFill)
                {
                    r = Math.Max((float)tw / s.Width, (float)th / s.Height);
                }
                else
                {
                    r = Math.Min((float)tw / s.Width, (float)th / s.Height);
                }

                int dw = (int)(s.Width * r), dh = (int)(s.Height * r);
                g.DrawImage(s, (tw - dw) / 2, (th - dh) / 2, dw, dh);

                t.Save(Path.Combine(a, n), DrawingImageFormat.Png);
            }
            S(c.StaticSmall, 44, 44, "Square44x44Logo.png"); S(null, 50, 50, "StoreLogo.png"); S(c.StaticSmall, 71, 71, "SmallTile.png"); S(null, 150, 150, "Square150x150Logo.png"); S(c.StaticWide, 310, 150, "Wide310x150Logo.png"); S(c.StaticLarge, 310, 310, "Square310x310Logo.png"); S(c.StaticWide, 620, 300, "SplashScreen.png");
            if (c.EnableLiveTile) for (int i = 0; i<c.LiveImages.Count; i++) { if (!File.Exists(c.LiveImages[i])) continue; try { File.Copy(c.LiveImages[i], Path.Combine(l, $"{i+1}.png"), true); } catch { } }
        }
        private static DrawingImage LoadImg(string p) { using var fs = File.OpenRead(p); return DrawingImage.FromStream(fs); }
    }
}