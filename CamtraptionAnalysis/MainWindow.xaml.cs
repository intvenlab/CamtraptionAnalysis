using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using CamtraptionAnalysis.Models;
using CamtraptionAnalysis.Services;
using Microsoft.Win32;

namespace CamtraptionAnalysis;

public partial class MainWindow : Window
{
    private readonly AnalysisPipeline _pipeline = new();
    private readonly CollectionViewSource _observationsView = new();
    private CancellationTokenSource? _runCancellation;
    private List<CaptureTimelineEntry> _currentTimeline = [];

    public MainWindow()
    {
        InitializeComponent();
        _observationsView.Filter += ObservationsView_Filter;
        OpenReportFolderButton.IsEnabled = Directory.Exists(ReportFileWriter.ReportsDirectory);
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select folder containing JPG files",
            Multiselect = false,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        FolderPathTextBox.Text = dialog.FolderName;
        RunButton.IsEnabled = !string.IsNullOrWhiteSpace(dialog.FolderName);
        StatusTextBlock.Text = "Ready";
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        var rootPath = FolderPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            SummaryTextBlock.Text = "Please choose a valid folder first.";
            return;
        }

        SetRunningState(isRunning: true);
        ClearResultsUi();
        ProgressBar.IsIndeterminate = true;
        ProgressBar.Maximum = 100;
        ProgressBar.Value = 0;
        StatusTextBlock.Text = $"Scanning ({ParallelismSettings.MetadataWorkerCount} metadata workers)...";

        _runCancellation = new CancellationTokenSource();
        var progress = new Progress<AnalysisPipelineProgress>(UpdateProgress);

        try
        {
            var includeRawFiles = IncludeRawFilesCheckBox.IsChecked == true;
            var result = await _pipeline.RunAsync(
                rootPath,
                maxFiles: null,
                progress,
                _runCancellation.Token,
                includeRawFiles).ConfigureAwait(true);

            var reportPath = ReportFileWriter.SaveReport(result.ReportText);
            ApplyResults(result.Summary, result.Timeline, reportPath);

            StatusTextBlock.Text =
                $"Done — analyzed {result.SuccessCount}, errors {result.ErrorCount}, discovered {result.FilesDiscovered}";
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Maximum = 100;
            ProgressBar.Value = 100;
            OpenReportFolderButton.IsEnabled = true;
            ShowMismatchesOnlyCheckBox.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Cancelled";
        }
        catch (Exception ex)
        {
            SummaryTextBlock.Text = $"ERROR: {ex.Message}";
            StatusTextBlock.Text = "Failed";
        }
        finally
        {
            _runCancellation?.Dispose();
            _runCancellation = null;
            SetRunningState(isRunning: false);
        }
    }

    private void ApplyResults(AnalysisSummary summary, IReadOnlyList<CaptureTimelineEntry> timeline, string reportPath)
    {
        void Apply()
        {
            SummaryTextBlock.Text = summary.ToDisplayText(reportPath);
            _currentTimeline = timeline.ToList();
            _observationsView.Source = _currentTimeline;
            ObservationsDataGrid.ItemsSource = _observationsView.View;
            _observationsView.View.Refresh();
        }

        if (Dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.Invoke(Apply);
        }
    }

    private void ClearResultsUi()
    {
        SummaryTextBlock.Text = string.Empty;
        _currentTimeline = [];
        _observationsView.Source = null;
        ObservationsDataGrid.ItemsSource = null;
        ShowMismatchesOnlyCheckBox.IsChecked = false;
        ShowMismatchesOnlyCheckBox.IsEnabled = false;
        OpenReportFolderButton.IsEnabled = Directory.Exists(ReportFileWriter.ReportsDirectory);
    }

    private void ObservationsView_Filter(object sender, FilterEventArgs e)
    {
        if (e.Item is not CaptureTimelineEntry entry)
        {
            e.Accepted = false;
            return;
        }

        if (ShowMismatchesOnlyCheckBox.IsChecked != true)
        {
            e.Accepted = true;
            return;
        }

        e.Accepted = entry.IsNotableForFilter;
    }

    private void ShowMismatchesOnlyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _observationsView.View?.Refresh();
    }

    private void OpenReportFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var directory = ReportFileWriter.ReportsDirectory;
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true,
        });
    }

    private void UpdateProgress(AnalysisPipelineProgress progress)
    {
        var discovered = progress.FilesDiscovered;
        var processed = progress.FilesProcessed;

        if (discovered == 0)
        {
            ProgressBar.IsIndeterminate = true;
            ProgressBar.Value = 0;
            StatusTextBlock.Text = IncludeRawFilesCheckBox.IsChecked == true
                ? "Scanning for JPG/JPEG and raw files..."
                : "Scanning for JPG/JPEG files...";
            return;
        }

        ProgressBar.IsIndeterminate = false;
        ProgressBar.Maximum = 100;
        var percent = processed * 100.0 / discovered;
        ProgressBar.Value = percent;

        var percentLabel = $"{percent:F0}%";
        var countsLabel = progress.IsDiscoveryComplete
            ? $"{processed} of {discovered}"
            : $"{processed} of {discovered} found so far";

        var status = $"Collecting metadata {percentLabel} ({countsLabel})";
        if (!string.IsNullOrEmpty(progress.LastProcessedFileName))
        {
            status += $" — {progress.LastProcessedFileName}";
        }

        StatusTextBlock.Text = status;
    }

    private void SetRunningState(bool isRunning)
    {
        BrowseButton.IsEnabled = !isRunning;
        RunButton.IsEnabled = !isRunning && Directory.Exists(FolderPathTextBox.Text.Trim());
        ShowMismatchesOnlyCheckBox.IsEnabled = !isRunning && _currentTimeline.Count > 0;
        IncludeRawFilesCheckBox.IsEnabled = !isRunning;
    }
}
