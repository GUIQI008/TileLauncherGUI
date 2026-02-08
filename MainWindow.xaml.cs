#nullable enable
using HandyControl.Data;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
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
            this.Loaded += MainWindow_Loaded;
        }
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await UpdateManager.CheckOnStartup();
        }

        private async void CheckEnvironment()
        {
            bool dotnetExists = await Task.Run(() =>
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo { FileName = "dotnet", Arguments = "--version", CreateNoWindow = true, UseShellExecute = false });
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
                catch { return false; }
            });

            if (!dotnetExists)
            {
                HandyControl.Controls.MessageBox.Warning("未检测到 .NET SDK。\n生成功能依赖 .NET 8.0 SDK，请务必安装。", "环境缺失");
                return; 
            }

            bool isDevModeEnabled = false;
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("AllowDevelopmentWithoutDevLicense");
                        if (val != null && (int)val == 1)
                        {
                            isDevModeEnabled = true;
                        }
                    }
                }
            }
            catch
            {
                isDevModeEnabled = true;
            }

            if (!isDevModeEnabled)
            {
                if (!isDevModeEnabled)
                {
                    var result = HandyControl.Controls.MessageBox.Show(
                        "检测到系统未开启“开发者模式”。\n\n生成磁贴需要向系统注册未签名的应用，请务必在 Windows 设置中开启“开发者模式”，否则生成将失败。",
                        "权限警告",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.OK)
                    {
                        try { Process.Start(new ProcessStartInfo("ms-settings:developers") { UseShellExecute = true }); } catch { }
                    }
                }

            }
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewSingleApp == null || ViewPuzzle == null || ViewManage == null || ViewAbout == null) return;

            ViewSingleApp.Visibility = Visibility.Collapsed;
            ViewPuzzle.Visibility = Visibility.Collapsed;
            ViewManage.Visibility = Visibility.Collapsed;
            ViewAbout.Visibility = Visibility.Collapsed;

            switch (NavList.SelectedIndex)
            {
                case 0:
                    ViewSingleApp.Visibility = Visibility.Visible;
                    break;
                case 1:
                    ViewPuzzle.Visibility = Visibility.Visible;
                    break;
                case 2:
                    ViewManage.Visibility = Visibility.Visible;
                    break;
                case 3:
                    ViewAbout.Visibility = Visibility.Visible;
                    break;
            }
        }

        // 管理页逻辑

        public class InstalledTile
        {
            public string Id { get; set; } = "";
            public string Path { get; set; } = "";
            public ImageSource? Icon { get; set; }
        }

        private void BtnRefreshList_Click(object sender, RoutedEventArgs e)
        {
            LoadInstalledTiles();
        }

        private void LoadInstalledTiles()
        {
            try
            {
                var list = new List<InstalledTile>();
                if (Directory.Exists(_outputPath))
                {
                    var dirs = Directory.GetDirectories(_outputPath);
                    foreach (var dir in dirs)
                    {
                        string folderName = Path.GetFileName(dir);
                        string packageDir = Path.Combine(dir, "Package");
                        if (Directory.Exists(packageDir))
                        {
                            string iconPath = Path.Combine(packageDir, "Assets", "Square150x150Logo.png");
                            if (!File.Exists(iconPath)) iconPath = Path.Combine(packageDir, "AppIcon.ico");

                            ImageSource? iconSource = null;
                            if (File.Exists(iconPath))
                            {
                                try
                                {
                                    iconSource = LoadBitmapSafe(iconPath);
                                }
                                catch
                                {

                                }
                            }

                            list.Add(new InstalledTile
                            {
                                Id = folderName,
                                Path = dir,
                                Icon = iconSource
                            });
                        }
                    }
                }
                ListInstalledTiles.ItemsSource = list;
            }
            catch (Exception ex)
            {
                HandyControl.Controls.MessageBox.Error($"加载列表失败: {ex.Message}");
            }
        }

        private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.Tag is InstalledTile tile)
            {
                var result = HandyControl.Controls.MessageBox.Ask($"确定要卸载并删除 \"{tile.Id}\" 吗？", "确认删除");
                if (result == MessageBoxResult.OK)
                {
                    try
                    {
                        if (ListInstalledTiles.ItemsSource is List<InstalledTile> list)
                        {
                            list.Remove(tile);
                            ListInstalledTiles.ItemsSource = null;
                            ListInstalledTiles.ItemsSource = list; 
                        }

                        GC.Collect();
                        GC.WaitForPendingFinalizers();

                        try
                        {
                            string exeName = tile.Id;
                            foreach (var proc in Process.GetProcessesByName(exeName))
                            {
                                proc.Kill();
                            }
                        }
                        catch { }

                        string packageName = $"{tile.Id}.Launcher";
                        await Task.Run(() =>
                        {
                            string cmd = $"Get-AppxPackage -Name {packageName} | Remove-AppxPackage";
                            RunCmd("powershell", $"-NoProfile -Command \"{cmd}\"");
                        });
                        await Task.Delay(1000);
                        int retries = 5;
                        while (retries > 0)
                        {
                            try
                            {
                                if (Directory.Exists(tile.Path)) Directory.Delete(tile.Path, true);
                                break;
                            }
                            catch
                            {
                                retries--;
                                await Task.Delay(500);
                            }
                        }

                        if (Directory.Exists(tile.Path))
                        {
                            HandyControl.Controls.MessageBox.Warning($"卸载命令已执行，但文件夹被占用无法删除。\n请重启资源管理器或电脑后手动删除：\n{tile.Path}");
                        }
                        else
                        {
                            HandyControl.Controls.MessageBox.Success("删除成功！");
                        }

                        LoadInstalledTiles();
                    }
                    catch (Exception ex)
                    {
                        HandyControl.Controls.MessageBox.Error($"操作失败: {ex.Message}");
                        LoadInstalledTiles();
                    }
                }
            }
        }

        //单应用逻辑

        private void BtnSelectTarget_Click(object sender, FunctionEventArgs<string> e)
        {
            var d = new OpenFileDialog { Filter = "Executable|*.exe|All Files|*.*" };
            if (d.ShowDialog() == true) TxtTargetPath.Text = d.FileName;
        }

        private void BtnTileSelect_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            if (_currentSelectedBtn != null)
            {
                _currentSelectedBtn.BorderBrush = new SolidColorBrush(Color.FromArgb(51, 255, 255, 255));
                _currentSelectedBtn.BorderThickness = new Thickness(2);
            }
            _currentSelectedBtn = btn;
            _currentSelectedBtn.BorderBrush = (Brush)FindResource("PrimaryBrush");
            _currentSelectedBtn.BorderThickness = new Thickness(4);
            BtnSetImg.IsEnabled = true;
            BtnRemoveImg.IsEnabled = true;
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
            if (string.IsNullOrEmpty(id) || !Regex.IsMatch(id, @"^[a-zA-Z][a-zA-Z0-9]*$"))
            {
                HandyControl.Controls.MessageBox.Warning("AppID 无效！\n必须以字母开头，仅包含字母和数字。"); return;
            }
            if (string.IsNullOrEmpty(_imgMasterPath))
            {
                HandyControl.Controls.MessageBox.Warning("请至少设置【Medium/主图标】图片。"); return;
            }

            var cfg = new TileConfig
            {
                AppId = id,
                DisplayName = string.IsNullOrWhiteSpace(TxtDisplayName.Text) ? null : TxtDisplayName.Text.Trim(),
                TargetPath = TxtTargetPath.Text.Trim(),
                Master = _imgMasterPath,
                StaticSmall = _imgSmallPath,
                StaticWide = _imgWidePath,
                StaticLarge = _imgLargePath,
                EnableLiveTile = ChkEnableLive.IsChecked == true
            };
            foreach (var it in ListLiveImages.Items) cfg.LiveImages.Add(it.ToString()!);

            BtnGenerate.IsEnabled = false;
            if (PanelSingleProgress != null) PanelSingleProgress.Visibility = Visibility.Visible;

            try
            {
                await Task.Run(() => BuildProcess(cfg));
                HandyControl.Controls.MessageBox.Success($"成功！\n请在开始菜单搜索: {cfg.DisplayName ?? id}");
            }
            catch (Exception ex)
            {
                HandyControl.Controls.MessageBox.Error($"生成失败:\n{ex.Message}");
            }
            finally
            {
                BtnGenerate.IsEnabled = true;
                if (PanelSingleProgress != null) PanelSingleProgress.Visibility = Visibility.Collapsed;
            }
        }

        //拼图逻辑

        private void BtnSelectPuzzleImage_Click(object sender, RoutedEventArgs e)
        {
            var d = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
            if (d.ShowDialog() == true)
            {
                _puzzleImagePath = d.FileName;
                double availableWidth = PuzzleBorder.Width;
                if (double.IsNaN(availableWidth)) availableWidth = 500;

                int rows = (int)NumRows.Value;
                int cols = (int)NumCols.Value;
                double cellSize = availableWidth / cols;
                double finalGridH = cellSize * rows;

                GridPuzzleContainer.Width = availableWidth;
                GridPuzzleContainer.Height = finalGridH;

                var bmp = LoadBitmapSafe(_puzzleImagePath);
                double imgW = bmp.Width;
                double imgH = bmp.Height;

                double scaleX = availableWidth / imgW;
                double scaleY = finalGridH / imgH;
                double finalScale = Math.Min(scaleX, scaleY);
                double offsetX = (availableWidth - imgW * finalScale) / 2;
                double offsetY = (finalGridH - imgH * finalScale) / 2;

                _imageScale = new ScaleTransform(finalScale, finalScale);
                _imageTranslate = new TranslateTransform(offsetX, offsetY);
                _imageTransformGroup = new TransformGroup();
                _imageTransformGroup.Children.Add(_imageScale);
                _imageTransformGroup.Children.Add(_imageTranslate);

                ImgPuzzlePreview.Source = bmp;
                ImgPuzzlePreview.RenderTransform = _imageTransformGroup;

                GridPuzzleInteractive.MouseRightButtonDown -= OnPuzzleRightMouseDown; GridPuzzleInteractive.MouseRightButtonDown += OnPuzzleRightMouseDown;
                GridPuzzleInteractive.MouseRightButtonUp -= OnPuzzleRightMouseUp; GridPuzzleInteractive.MouseRightButtonUp += OnPuzzleRightMouseUp;
                GridPuzzleInteractive.MouseMove -= OnPuzzleMouseMove; GridPuzzleInteractive.MouseMove += OnPuzzleMouseMove;
                GridPuzzleInteractive.MouseWheel -= OnPuzzleMouseWheel; GridPuzzleInteractive.MouseWheel += OnPuzzleMouseWheel;

                _puzzleItems.Clear();
                DrawInteractiveGrid();
            }
        }

        private void PuzzleConfig_Changed(object sender, FunctionEventArgs<double> e)
        {
            _puzzleItems.Clear();
            DrawInteractiveGrid();
        }

        private void DrawInteractiveGrid()
        {
            if (GridPuzzleInteractive == null || PuzzleBorder == null) return;
            double containerWidth = PuzzleBorder.Width;
            if (double.IsNaN(containerWidth)) containerWidth = 500;

            int rows = (int)NumRows.Value;
            int cols = (int)NumCols.Value;
            double cellSize = containerWidth / cols;
            double totalHeight = cellSize * rows;

            GridPuzzleContainer.Width = containerWidth;
            GridPuzzleContainer.Height = totalHeight;

            GridPuzzleInteractive.Children.Clear();
            GridPuzzleInteractive.RowDefinitions.Clear();
            GridPuzzleInteractive.ColumnDefinitions.Clear();

            for (int i = 0; i < rows; i++) GridPuzzleInteractive.RowDefinitions.Add(new RowDefinition());
            for (int i = 0; i < cols; i++) GridPuzzleInteractive.ColumnDefinitions.Add(new ColumnDefinition());

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var b = new Border { BorderBrush = new SolidColorBrush(Colors.White) { Opacity = 0.3 }, BorderThickness = new Thickness(0.5) };
                    Grid.SetRow(b, r); Grid.SetColumn(b, c);
                    GridPuzzleInteractive.Children.Add(b);
                }
            }

            foreach (var it in _puzzleItems.ToList())
            {
                if (it.Row >= rows || it.Col >= cols) { _puzzleItems.Remove(it); continue; }
                var t = new Border
                {
                    BorderThickness = new Thickness(2),
                    BorderBrush = new SolidColorBrush(Colors.Cyan),
                    Background = new SolidColorBrush(Colors.Cyan) { Opacity = 0.2 },
                    Margin = new Thickness(1),
                    IsHitTestVisible = false
                };
                var txt = new TextBlock
                {
                    Text = it.Type == TileType.Large ? "大" : (it.Type == TileType.Wide ? "宽" : "中"),
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.Bold
                };
                t.Child = txt;
                Grid.SetRow(t, it.Row); Grid.SetColumn(t, it.Col);
                Grid.SetRowSpan(t, it.Type == TileType.Large ? 2 : 1);
                Grid.SetColumnSpan(t, (it.Type == TileType.Large || it.Type == TileType.Wide) ? 2 : 1);
                GridPuzzleInteractive.Children.Add(t);
            }
            TxtPuzzleStats.Text = $"当前网格: {rows}行 x {cols}列 | 已放置: {_puzzleItems.Count} 个磁贴";
        }

        private void OnPuzzleRightMouseDown(object sender, MouseButtonEventArgs e) { _isDraggingImage = true; _lastMousePosition = e.GetPosition(GridPuzzleInteractive); GridPuzzleInteractive.CaptureMouse(); }
        private void OnPuzzleRightMouseUp(object sender, MouseButtonEventArgs e) { _isDraggingImage = false; GridPuzzleInteractive.ReleaseMouseCapture(); }
        private void OnPuzzleMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingImage)
            {
                var current = e.GetPosition(GridPuzzleInteractive);
                var delta = current - _lastMousePosition;
                _imageTranslate.X += delta.X; _imageTranslate.Y += delta.Y;
                _lastMousePosition = current;
            }
        }
        private void OnPuzzleMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double zoom = e.Delta > 0 ? 1.1 : 0.9;
            _imageScale.ScaleX *= zoom; _imageScale.ScaleY *= zoom;
        }

        private void GridPuzzleInteractive_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            var p = e.GetPosition(GridPuzzleInteractive);
            double cw = GridPuzzleContainer.Width / (int)NumCols.Value;
            double ch = GridPuzzleContainer.Height / (int)NumRows.Value;
            int c = (int)(p.X / cw); int r = (int)(p.Y / ch);
            if (c >= NumCols.Value || r >= NumRows.Value) return;

            if (RadioDelete.IsChecked == true)
            {
                _puzzleItems.RemoveAll(x => r >= x.Row && r < x.Row + (x.Type == TileType.Large ? 2 : 1) && c >= x.Col && c < x.Col + (x.Type == TileType.Large || x.Type == TileType.Wide ? 2 : 1));
            }
            else
            {
                TileType t = RadioPlaceWide.IsChecked == true ? TileType.Wide : (RadioPlaceLarge.IsChecked == true ? TileType.Large : TileType.Mid);
                int rs = t == TileType.Large ? 2 : 1; int cs = (t == TileType.Large || t == TileType.Wide) ? 2 : 1;
                if (r + rs <= NumRows.Value && c + cs <= NumCols.Value)
                {
                    _puzzleItems.RemoveAll(it => {
                        int r1 = it.Row, c1 = it.Col, rs1 = it.Type == TileType.Large ? 2 : 1, cs1 = (it.Type == TileType.Large || it.Type == TileType.Wide) ? 2 : 1;
                        return r < r1 + rs1 && r + rs > r1 && c < c1 + cs1 && c + cs > c1;
                    });
                    _puzzleItems.Add(new PuzzleItem { Row = r, Col = c, Type = t });
                }
            }
            DrawInteractiveGrid();
        }

        private async void BtnGeneratePuzzle_Click(object sender, RoutedEventArgs e)
        {
            if (_puzzleItems.Count == 0) return;
            if (!Regex.IsMatch(TxtPuzzlePrefix.Text, @"^[a-zA-Z][a-zA-Z0-9]*$")) { HandyControl.Controls.MessageBox.Warning("ID前缀无效"); return; }

            BtnGeneratePuzzle.IsEnabled = false;
            PanelProgress.Visibility = Visibility.Visible;
            ProgressPuzzle.Maximum = _puzzleItems.Count;
            ProgressPuzzle.Value = 0;
            TxtProgressStatus.Text = "正在初始化...";

            try
            {
                int rw = (int)GridPuzzleContainer.Width;
                int rh = (int)GridPuzzleContainer.Height;
                var rtb = new RenderTargetBitmap(rw, rh, 96, 96, PixelFormats.Pbgra32);
                var dv = new DrawingVisual();
                using (var ctx = dv.RenderOpen())
                {
                    var brush = new VisualBrush(ImgPuzzlePreview) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
                    ctx.DrawRectangle(brush, null, new Rect(0, 0, rw, rh));
                }
                rtb.Render(dv);
                var frozenBmp = new CachedBitmap(rtb, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frozenBmp.Freeze();

                int cols = (int)NumCols.Value;
                int rows = (int)NumRows.Value;
                string currentPrefix = TxtPuzzlePrefix.Text;

                await Task.Run(() => {
                    int finishedCount = 0;
                    double cellW = (double)rw / cols; double cellH = (double)rh / rows;

                    foreach (var it in _puzzleItems)
                    {
                        Dispatcher.Invoke(() => TxtProgressStatus.Text = $"正在生成 {finishedCount + 1}/{_puzzleItems.Count}...");

                        int wRatio = (it.Type == TileType.Large || it.Type == TileType.Wide) ? 2 : 1;
                        int hRatio = (it.Type == TileType.Large) ? 2 : 1;
                        int rectX = (int)(it.Col * cellW); int rectY = (int)(it.Row * cellH);
                        int rectW = (int)(cellW * wRatio); int rectH = (int)(cellH * hRatio);

                        if (rectX + rectW > frozenBmp.PixelWidth) rectW = frozenBmp.PixelWidth - rectX;
                        if (rectY + rectH > frozenBmp.PixelHeight) rectH = frozenBmp.PixelHeight - rectY;

                        var crop = new CroppedBitmap(frozenBmp, new Int32Rect(rectX, rectY, rectW, rectH));
                        string tmpImg = Path.Combine(Path.GetTempPath(), $"pz_{currentPrefix}_{it.Row}_{it.Col}.png");
                        using (var fs = File.Create(tmpImg))
                        {
                            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(crop)); enc.Save(fs);
                        }

                        var c = new TileConfig { AppId = $"{currentPrefix}R{it.Row}C{it.Col}", DisplayName = null, Master = tmpImg };
                        if (it.Type == TileType.Wide) c.StaticWide = tmpImg;
                        if (it.Type == TileType.Large) c.StaticLarge = tmpImg;
                        BuildProcess(c);

                        finishedCount++;
                        Dispatcher.Invoke(() => ProgressPuzzle.Value = finishedCount);
                    }
                });
                HandyControl.Controls.MessageBox.Success("拼图生成完成！");
            }
            catch (Exception ex) { HandyControl.Controls.MessageBox.Error($"错误: {ex.Message}"); }
            finally { BtnGeneratePuzzle.IsEnabled = true; PanelProgress.Visibility = Visibility.Collapsed; }
        }

        private void BuildProcess(TileConfig config)
        {
            string safeId = config.AppId;
            string projectDir = Path.Combine(_outputPath, safeId, "Source");
            string publishDir = Path.Combine(_outputPath, safeId, "Package");

            if (Directory.Exists(projectDir)) Directory.Delete(projectDir, true);
            Directory.CreateDirectory(projectDir);

            File.WriteAllText(Path.Combine(projectDir, $"{safeId}.csproj"), TemplateModels.GetCsproj());
            File.WriteAllText(Path.Combine(projectDir, "app.manifest"), TemplateModels.GetAppManifest());
            File.WriteAllText(Path.Combine(projectDir, "Program.cs"),TemplateModels.GetProgramCs(config.TargetPath, true, true, true));

            GeneratorUtils.GenerateIconSafe(config.Master, Path.Combine(projectDir, "AppIcon.ico"));
            GeneratorUtils.GenAssets(config, publishDir);

            RunCmd("dotnet", $"restore \"{projectDir}\"");
            RunCmd("dotnet", $"publish \"{projectDir}\" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o \"{publishDir}\"");

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
        }

        private void RunCmd(string exe, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                StandardErrorEncoding = System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
            };

            using var p = Process.Start(psi);
            string outS = p.StandardOutput.ReadToEnd();
            string errS = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (p.ExitCode != 0)
            {
                if (errS.Contains("0x80073CFF") || outS.Contains("0x80073CFF"))
                {
                    throw new Exception("部署失败：请在 Windows 设置中开启“开发者模式” (Developer Mode) 后重试。\n错误代码: 0x80073CFF");
                }
                throw new Exception($"CMD失败: {exe} {args}\n\n错误:\n{errS}\n\n日志:\n{outS}");
            }
        }

        private BitmapImage LoadBitmapSafe(string path)
        {
            var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(path); bmp.EndInit(); bmp.Freeze(); return bmp;
        }

        public enum TileType { Mid, Wide, Large }
        public class PuzzleItem { public int Row; public int Col; public TileType Type; }
        public class TileConfig { public string AppId = ""; public string? DisplayName; public string TargetPath = ""; public string Master = ""; public string? StaticSmall; public string? StaticWide; public string? StaticLarge; public bool EnableLiveTile; public List<string> LiveImages = new List<string>(); }

        private void ListBoxItem_Selected(object sender, RoutedEventArgs e)
        {

        }
    }

    public static class TemplateModels
    {
        public static string GetCsproj() => @"<Project Sdk=""Microsoft.NET.Sdk""><PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net8.0-windows10.0.19041.0</TargetFramework><ApplicationManifest>app.manifest</ApplicationManifest><Platforms>x64</Platforms><RuntimeIdentifiers>win-x64</RuntimeIdentifiers><PublishSingleFile>true</PublishSingleFile><SelfContained>true</SelfContained><UseWindowsForms>false</UseWindowsForms><UseWPF>false</UseWPF></PropertyGroup></Project>";

        public static string GetAppManifest() => @"<?xml version=""1.0"" encoding=""utf-8""?><assembly manifestVersion=""1.0"" xmlns=""urn:schemas-microsoft-com:asm.v1""><assemblyIdentity version=""1.0.0.0"" name=""MyLauncher.app""/><trustInfo xmlns=""urn:schemas-microsoft-com:asm.v2""><security><requestedPrivileges xmlns=""urn:schemas-microsoft-com:asm.v3""><requestedExecutionLevel level=""asInvoker"" uiAccess=""false""/></requestedPrivileges></security></trustInfo><application xmlns=""urn:schemas-microsoft-com:asm.v3""><windowsSettings><dpiAware xmlns=""http://schemas.microsoft.com/SMI/2005/WindowsSettings"">true/PM</dpiAware><dpiAwareness xmlns=""http://schemas.microsoft.com/SMI/2016/WindowsSettings"">PerMonitorV2, PerMonitor</dpiAwareness></windowsSettings></application></assembly>";

        public static string GetProgramCs(string t, bool uM, bool uW, bool uL)
        {
            string binds = "";
            string com = "branding='none' hint-overlay='0'";
            string bg = "placement='background'";

            if (uM) binds += $"<binding template='TileMedium' {com}><image src='ms-appx:///Assets/Live/{{0}}' {bg}/></binding>";
            if (uW) binds += $"<binding template='TileWide' {com}><image src='ms-appx:///Assets/Live/{{0}}' {bg}/></binding>";
            if (uL) binds += $"<binding template='TileLarge' {com}><image src='ms-appx:///Assets/Live/{{0}}' {bg}/></binding>";

            return $@"using System;using System.Diagnostics;using System.IO;using System.Linq;using Windows.UI.Notifications;using Windows.Data.Xml.Dom;namespace MyLauncher{{static class Program{{[STAThread]static void Main(){{TryUpdate();string i=@""{(t??"").Replace("\\", "\\\\")}"";if(string.IsNullOrWhiteSpace(i))return;ProcessStartInfo p=new ProcessStartInfo();p.UseShellExecute=true;if(long.TryParse(i,out _))p.FileName=""steam://run/""+i;else if(i.StartsWith(""steam://"",StringComparison.OrdinalIgnoreCase))p.FileName=i;else{{p.FileName=i;try{{string d=Path.GetDirectoryName(i);if(!string.IsNullOrEmpty(d))p.WorkingDirectory=d;}}catch{{}}}}try{{Process.Start(p);}}catch{{}}}}static void TryUpdate(){{try{{string b=AppDomain.CurrentDomain.BaseDirectory;string l=Path.Combine(b,""Assets"",""Live"");if(!Directory.Exists(l))return;var f=Directory.GetFiles(l,""*.png"");if(f.Length==0)return;var u=TileUpdateManager.CreateTileUpdaterForApplication();u.EnableNotificationQueue(true);u.Clear();
            
            // 【生成的 XML 模板】
            string xmlTemplate = @""<tile><visual version='2'>{binds}</visual></tile>"";

            foreach(var x in f.Take(5)){{
                string fileName=Path.GetFileName(x);
                // 【运行时替换】 使用 string.Format 将 {0} 替换为 1.png
                string xml = string.Format(xmlTemplate, fileName);
                
                XmlDocument d=new XmlDocument();
                d.LoadXml(xml);
                u.Update(new TileNotification(d));
            }}}}catch{{}}}}}}}}";
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
        public static void GenerateIconSafe(string src, string dest) { try { using var b = new DrawingBitmap(256, 256); if (File.Exists(src)) { using var fs = File.OpenRead(src); using var img = DrawingImage.FromStream(fs); using var g = DrawingGraphics.FromImage(b); g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(img, 0, 0, 256, 256); } else { using var g = DrawingGraphics.FromImage(b); g.Clear(DrawingColor.Teal); } using var fsDest = File.Create(dest); fsDest.Write(new byte[] { 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 1, 0, 32, 0 }, 0, 14); using var ms = new MemoryStream(); b.Save(ms, DrawingImageFormat.Png); byte[] data = ms.ToArray(); fsDest.Write(BitConverter.GetBytes(data.Length), 0, 4); fsDest.Write(BitConverter.GetBytes(22), 0, 4); fsDest.Write(data, 0, data.Length); } catch { } }
        public static void GenAssets(MainWindow.TileConfig c, string outDir)
        {
            string assets = Path.Combine(outDir, "Assets"), live = Path.Combine(assets, "Live");
            Directory.CreateDirectory(assets); if (c.EnableLiveTile) Directory.CreateDirectory(live);
            using var masterBmp = File.Exists(c.Master) ? LoadImg(c.Master) : new DrawingBitmap(512, 512);
            void Save(string? p, int w, int h, string n) { int tw = (int)(w*Scale), th = (int)(h*Scale); using var t = new DrawingBitmap(tw, th); using var g = DrawingGraphics.FromImage(t); g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic; g.CompositingQuality = Drawing2D.CompositingQuality.HighQuality; g.SmoothingMode = Drawing2D.SmoothingMode.HighQuality; using var s = (!string.IsNullOrEmpty(p) && File.Exists(p)) ? LoadImg(p) : (DrawingImage)masterBmp.Clone(); g.DrawImage(s, 0, 0, tw, th); t.Save(Path.Combine(assets, n), DrawingImageFormat.Png); }
            Save(c.StaticSmall, 44, 44, "Square44x44Logo.png"); Save(null, 50, 50, "StoreLogo.png"); Save(c.StaticSmall, 71, 71, "SmallTile.png"); Save(null, 150, 150, "Square150x150Logo.png"); Save(c.StaticWide, 310, 150, "Wide310x150Logo.png"); Save(c.StaticLarge, 310, 310, "Square310x310Logo.png"); Save(c.StaticWide, 620, 300, "SplashScreen.png");
            if (c.EnableLiveTile) for (int i = 0; i<c.LiveImages.Count; i++) { if (!File.Exists(c.LiveImages[i])) continue; using var lImg = LoadImg(c.LiveImages[i]); void SaveLive(int w, int h, string s) { using var tb = new DrawingBitmap((int)(w*Scale), (int)(h*Scale)); using var tg = DrawingGraphics.FromImage(tb); tg.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic; tg.DrawImage(lImg, 0, 0, tb.Width, tb.Height); tb.Save(Path.Combine(live, $"Live_{i+1}_{s}.png"), DrawingImageFormat.Png); } SaveLive(150, 150, "Medium"); SaveLive(310, 150, "Wide"); SaveLive(310, 310, "Large"); }
        }
        private static DrawingImage LoadImg(string p) { using var fs = File.OpenRead(p); return DrawingImage.FromStream(fs); }
    }
}