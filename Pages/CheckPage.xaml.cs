using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using UselessChecker.Backend;

namespace UselessChecker.Pages
{
    public partial class CheckPage : Page
    {
        private CancellationTokenSource? _cancellationTokenSource;
        private List<string> _foundCheats = new();
        private int _filesScanned;

        public CheckPage()
        {
            InitializeComponent();
        }

        private async void StartScan_Click(object sender, RoutedEventArgs e)
        {
            IdlePanel.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            
            _cancellationTokenSource = new CancellationTokenSource();
            _foundCheats = new();
            _filesScanned = 0;

            var progress = new Progress<Backend.CheckerLogic.ScanProgress>(p =>
            {
                ScanProgress.Value = p.Percent;
                StatusText.Text = p.Status;
                FilesCount.Text = p.FilesScanned.ToString("N0");
                ThreatsCount.Text = p.CheatsFound.ToString();
                CurrentPath.Text = p.Path;
            });

            try
            {
                var result = await Backend.CheckerLogic.RunCheatScanAsync(progress, _cancellationTokenSource.Token);
                
                _foundCheats = result.FoundCheats;
                _filesScanned = result.FilesScanned;

                ProgressPanel.Visibility = Visibility.Collapsed;
                ResultsPanel.Visibility = Visibility.Visible;

                if (_foundCheats.Count > 0)
                {
                    ResultHeader.Text = $"⚠️ Обнаружено угроз: {_foundCheats.Count}";
                    ResultHeader.Foreground = System.Windows.Media.Brushes.FromArgb(255, 224, 38, 64);
                    ResultsList.ItemsSource = _foundCheats;
                }
                else
                {
                    ResultHeader.Text = "✅ Угроз не найдено";
                    ResultHeader.Foreground = System.Windows.Media.Brushes.FromArgb(255, 86, 168, 120);
                    ResultsList.ItemsSource = new List<string> { "Активных следов запрещенного ПО не обнаружено." };
                }

                ResultSummary.Text = $"Проверено файлов: {_filesScanned:N0}";
            }
            catch (OperationCanceledException)
            {
                ProgressPanel.Visibility = Visibility.Collapsed;
                IdlePanel.Visibility = Visibility.Visible;
                MessageBox.Show("Сканирование завершено пользователем.", "Отменено", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ProgressPanel.Visibility = Visibility.Collapsed;
                IdlePanel.Visibility = Visibility.Visible;
                MessageBox.Show($"Ошибка сканирования: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelScan_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
        }

        private void NewScan_Click(object sender, RoutedEventArgs e)
        {
            ResultsPanel.Visibility = Visibility.Collapsed;
            IdlePanel.Visibility = Visibility.Visible;
        }

        private async void SaveReport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var tempPath = Path.Combine(Path.GetTempPath(), $"UselessChecker_Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                
                var report = new System.Text.StringBuilder();
                report.AppendLine("=== UselessChecker Report ===");
                report.AppendLine($"Date: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
                report.AppendLine();
                report.AppendLine($"Files scanned: {_filesScanned:N0}");
                report.AppendLine($"Threats found: {_foundCheats.Count}");
                report.AppendLine();
                
                if (_foundCheats.Count > 0)
                {
                    report.AppendLine("=== Found Threats ===");
                    foreach (var cheat in _foundCheats)
                        report.AppendLine(cheat);
                }
                else
                {
                    report.AppendLine("No threats detected.");
                }

                File.WriteAllText(tempPath, report.ToString());
                
                Process.Start(new ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true
                });

                MessageBox.Show($"Отчет сохранен:\n{tempPath}", "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось сохранить отчет: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
