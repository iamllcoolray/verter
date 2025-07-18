using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Xabe.FFmpeg;
using System.IO;
using Xabe.FFmpeg.Downloader;
using System.Threading;

namespace Verter;

public partial class MainPage : ContentPage
{
    private CancellationTokenSource? cancellationTokenSource;
    private List<string> inputFiles = [];
    private readonly string[] supportedExtensions = { ".flv", ".mov", ".mkv", ".m4v", ".ts", ".webm", ".avi" };

    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnPickClicked(object sender, EventArgs e)
    {
        var result = await FilePicker.Default.PickMultipleAsync(new PickOptions
        {
            FileTypes = FilePickerFileType.Videos,
            PickerTitle = "Select one or more video files"
        });

        if (result != null && result.Any())
        {
            if (!result.All(file => supportedExtensions.Any(ext => file.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))))
            {
                StatusMessage("MP4 files were selected. Please select files with different extensions.", Colors.Red);
                return;
            }
            
            inputFiles = [.. result.Select(file => file.FullPath)];
            
            RefreshFileList();

            StatusMessage("Idle", Colors.Gray);

            SelectedFileMessage($"Selected {inputFiles.Count} file(s)", Colors.Gray);
        }
    }

    private async void OnConvertClicked(object sender, EventArgs e)
    {
        StatusLabel.Text = "Converting...";
        PickButton.IsEnabled = false;
        ConvertButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ClearButton.IsEnabled = false;
        ConversionProgressBar.IsVisible = true;
        ConversionProgressBar.Progress = 0;

        cancellationTokenSource = new CancellationTokenSource();
        var token = cancellationTokenSource.Token;

        try
        {
            await DownloadFFmpegToHomeDirAsync();

            int totalFiles = inputFiles.Count;
            int currentFile = 0;

            foreach (var inputPath in inputFiles)
            {
                if (token.IsCancellationRequested)
                    break;

                currentFile++;

                string outputDir = Path.Combine(Path.GetDirectoryName(inputPath)!, "output");
                string outputPath = Path.Combine(
                    outputDir!,
                    Path.GetFileNameWithoutExtension(inputPath) + "_converted.mp4"
                );

                var mediaInfo = await FFmpeg.GetMediaInfo(inputPath);

                var conversion = FFmpeg.Conversions.New()
                    .AddStream(mediaInfo.VideoStreams)
                    .AddStream(mediaInfo.AudioStreams)
                    .SetOutput(outputPath)
                    .SetOverwriteOutput(true);

                conversion.OnProgress += (s, args) =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        // Per file progress normalized across total files
                        double fileProgress = args.Percent / 100.0;
                        double totalProgress = ((currentFile - 1) + fileProgress) / totalFiles;
                        
                        ConversionProgressBar.Progress = totalProgress;

                        SelectedFileMessage($"Converting {Path.GetFileName(inputPath)}: {args.Percent:0}%", Colors.Gray);

                        StatusMessage($"File {currentFile} of {totalFiles}", Colors.Gray);
                    });
                };

                conversion.OnDataReceived += (s, args) =>
                {
                    Console.WriteLine($"[{Path.GetFileName(inputPath)}] {args.Data}");
                };

                await conversion.Start(token);

                Console.WriteLine($"✅ Finished: {outputPath}");
            }

            if (!token.IsCancellationRequested)
            {
                StatusMessage("Conversion completed successfully!", Colors.Green);
            }
            else
            {
                StatusMessage("Conversion was canceled.", Colors.Red);
            }

            ConversionProgressBar.Progress = 1;
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
            StatusLabel.TextColor = Colors.Red;
        }
        finally
        {
            cancellationTokenSource = null;
            PickButton.IsEnabled = true;
            ConvertButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            ClearButton.IsEnabled = inputFiles.Any();
            ConversionProgressBar.IsVisible = false;

            SelectedFileMessage($"Selected {inputFiles.Count} file(s)", Colors.Gray);
        }
    }

    private void OnCancelClicked(object sender, EventArgs e)
    {
        cancellationTokenSource?.Cancel();
        StatusMessage("Conversion canceled.", Colors.Red);
    }

    private void OnClearClicked(object sender, EventArgs e)
    {
        inputFiles.Clear();
        RefreshFileList();

        StatusMessage("Cleared file list.", Colors.Red);

        SelectedFileMessage("No file selected.", Colors.Gray);

        ConversionProgressBar.Progress = 0;
        ClearButton.IsEnabled = false;
        ConvertButton.IsEnabled = false;
    }

    public async Task DownloadFFmpegToHomeDirAsync()
    {
        // Get user home directory cross-platform
        string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string ffmpegFolder = Path.Combine(homeDir, ".ffmpeg");

        if (Directory.Exists(ffmpegFolder))
        {
            FFmpeg.SetExecutablesPath(ffmpegFolder);
            return;
        }

        Directory.CreateDirectory(ffmpegFolder);

        await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegFolder);

        FFmpeg.SetExecutablesPath(ffmpegFolder);
    }

    private void RefreshFileList()
    {
        FileList.ItemsSource = null;
        FileList.ItemsSource = inputFiles;

        ConvertButton.IsEnabled = inputFiles.Any();
        ClearButton.IsEnabled = inputFiles.Any();
        FileListScrollView.IsVisible = inputFiles.Any();
    }

    private void StatusMessage(string message, Color color)
    {
        StatusLabel.Text = message;
        StatusLabel.TextColor = color;
    }

    private void SelectedFileMessage(string message, Color color)
    {
        SelectedFileLabel.Text = message;
        SelectedFileLabel.TextColor = color;
    }
}
