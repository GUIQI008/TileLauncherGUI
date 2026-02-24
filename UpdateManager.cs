using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

public class UpdateManager
{
    private const string ConfigUrl = "https://www.npoint.io/docs/8283f4909db763ed786f";

    // 当前版本号
    public const string CurrentVersion = "1.6.5";

    public static async Task CheckOnStartup()
    {
        try
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "TileLauncher/1.0");
                client.Timeout = TimeSpan.FromSeconds(10);

                string url = ConfigUrl + (ConfigUrl.Contains("?") ? "&" : "?") + "t=" + DateTime.Now.Ticks;

                string json = await client.GetStringAsync(url);

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var config = JsonSerializer.Deserialize<VersionConfig>(json, options);

                if (config == null) return;

                CheckUpdate(config);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("更新检查失败：" + ex.Message);
        }
    }

    private static void CheckUpdate(VersionConfig config)
    {
        Version local = new Version(CurrentVersion);
        Version remote = new Version(config.Version);

        string minVerStr = string.IsNullOrEmpty(config.MinVersion) ? "0.0.0" : config.MinVersion;
        Version minVer = new Version(minVerStr);

        bool isForced = local < minVer;
        bool hasUpdate = local < remote;

        if (isForced)
        {
            MessageBox.Show($"【版本已停用】\n{config.Message}\n\n当前版本过低，点击确定前往下载页面。",
                            "强制更新", MessageBoxButton.OK, MessageBoxImage.Error);

            OpenUrl(config.DownloadUrl);

            Application.Current.Shutdown();
        }
        else if (hasUpdate)
        {
            var result = MessageBox.Show($"发现新版本 v{config.Version}\n{config.Message}\n\n是否前往下载页面？",
                                         "发现更新", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                OpenUrl(config.DownloadUrl);
            }
        }
    }
    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法打开浏览器，请手动复制链接：\n" + url);
            Clipboard.SetText(url);
        }
    }
}

public class VersionConfig
{
    public string Version { get; set; }
    public string MinVersion { get; set; }
    public string DownloadUrl { get; set; }
    public string Message { get; set; }
}