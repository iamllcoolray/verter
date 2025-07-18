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
    private int currentProcessId = 0;

    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnPickClicked(object sender, EventArgs e)
    {
    #if ANDROID || IOS
        var result = await FilePicker.Default.PickMultipleAsync(new PickOptions
        {
            FileTypes = FilePickerFileType.Videos,
            PickerTitle = "Pick one or more video files"
        });
    #else
        var result = await FilePicker.PickMultipleAsync(new PickOptions
        {
            FileTypes = FilePickerFileType.Videos,
            PickerTitle = "Pick one or more video files"
        });
    #endif

        if (result != null && result.Any())
        {
            inputFiles = result.Select(file => file.FullPath).ToList();
            SelectedFileLabel.Text = $"Selected {inputFiles.Count} file(s)";
            ConvertButton.IsEnabled = true;
        }
    }

    private async void OnConvertClicked(object sender, EventArgs e)
    {
        StatusLabel.Text = "Converting...";
        ConvertButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ClearButton.IsEnabled = false;
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
                        StatusLabel.Text = $"Converting {Path.GetFileName(inputPath)}: {args.Percent:0}%";
                    });
                };

                conversion.OnDataReceived += (s, args) =>
                {
                    Console.WriteLine($"[{Path.GetFileName(inputPath)}] {args.Data}");
                };

                await conversion.Start(token);

                Console.WriteLine($"✅ Finished: {outputPath}");
            }

            StatusLabel.Text = token.IsCancellationRequested ? "Conversion canceled." : "All files converted!";

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
            ConvertButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            ClearButton.IsEnabled = true;
        }
    }

    private void OnCancelClicked(object sender, EventArgs e)
    {
        cancellationTokenSource?.Cancel();
        StatusLabel.Text = "Canceling...";
    }

    private void OnClearClicked(object sender, EventArgs e)
    {
        inputFiles.Clear();
        StatusLabel.Text = "Cleared file list.";
        SelectedFileLabel.Text = "No file selected.";
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
}
