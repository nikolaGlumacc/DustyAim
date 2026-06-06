using Class;
using SecondaryWindows;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace AimmyWPF.UserController
{
    public partial class ADownloadGateway : UserControl
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly DownloadItem _downloadItem;
        private readonly string _localDirectory;

        public ADownloadGateway(DownloadItem downloadItem, string localDirectory)
        {
            InitializeComponent();
            _downloadItem = downloadItem;
            _localDirectory = localDirectory;
            Title.Content = downloadItem.DisplayTitle;
            Subtitle.Text = downloadItem.MetadataLine;

            DownloadButton.Click += async (s, e) =>
            {
                if (DownloadButton.Content.ToString() != "\xE895")
                {
                    DownloadButton.Content = "\xE895";
                    new NoticeBar("The download is starting...").Show();

                    try
                    {
                        string branch = string.IsNullOrWhiteSpace(_downloadItem.Branch) ? "main" : _downloadItem.Branch;
                        string remotePath = string.IsNullOrWhiteSpace(_downloadItem.RemotePath) ? string.Empty : _downloadItem.RemotePath.Trim('/');
                        string remoteSegment = string.IsNullOrEmpty(remotePath) ? string.Empty : remotePath + "/";
                        string escapedFileName = Uri.EscapeDataString(_downloadItem.Name);
                        string url = $"https://github.com/{_downloadItem.Owner}/{_downloadItem.Repo}/raw/{branch}/{remoteSegment}{escapedFileName}";

                        if (!Directory.Exists(_localDirectory))
                            Directory.CreateDirectory(_localDirectory);

                        string localPath = Path.Combine(_localDirectory, _downloadItem.Name);

                        using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                        {
                            response.EnsureSuccessStatusCode();

                            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                            var canReportProgress = totalBytes != -1L;

                            using (var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                            using (var downloadStream = await response.Content.ReadAsStreamAsync())
                            {
                                var buffer = new byte[8192];
                                long totalRead = 0;
                                int bytesRead;

                                while ((bytesRead = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                                {
                                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                                    totalRead += bytesRead;

                                    if (canReportProgress)
                                    {
                                        DownloadProgress.Dispatcher.Invoke(() => 
                                            DownloadProgress.Value = (double)totalRead / totalBytes * 100);
                                    }
                                }
                            }
                        }

                        if (new FileInfo(localPath).Length > 0)
                        {
                            if (this.Parent is StackPanel panel)
                            {
                                panel.Children.Remove(this);
                            }
                            new NoticeBar("The file has been completed.").Show();
                        }
                        else
                        {
                            throw new Exception("Downloaded file is empty.");
                        }
                    }
                    catch (Exception ex)
                    {
                        DownloadButton.Content = "\xE896"; // Error icon or reset
                        new NoticeBar($"Download failed: {ex.Message}").Show();
                    }
                }
            };
        }
    }
}
