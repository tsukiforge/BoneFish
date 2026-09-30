using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

using Bloxstrap.Integrations;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// ── Diagnostic Center (Phase 6) ──────────────────────────────────────────────
    /// Dedicated lightweight window replacing the old diagnostics MessageBox.
    /// Renders the 10 report sections once, on open. No charts, no WebView, no
    /// animation, no timers — after the initial render this window costs zero CPU.
    /// Suitable for HDD + 4/8 GB RAM machines.
    /// </summary>
    public partial class DiagnosticCenterWindow
    {
        private IReadOnlyList<DiagnosticSection> _sections = new List<DiagnosticSection>();

        public DiagnosticCenterWindow()
        {
            InitializeComponent();

            LoadReport();
        }

        private void LoadReport()
        {
            try
            {
                // Semua data dikumpulkan SEKALI di sini (on-demand). Tidak ada apa pun
                // yang berjalan periodik setelah window tampil.
                _sections = DiagnosticReportBuilder.Build();
                RenderSections();
                GeneratedText.Text = $"Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss} — read-only, on-demand";
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("DiagnosticCenterWindow", $"Report failed: {ex.Message}");
                RootPanel.Children.Add(new TextBlock
                {
                    Text = $"Report generation failed: {ex.Message}",
                    Foreground = Brushes.OrangeRed,
                    Margin = new Thickness(4),
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }

        private void RenderSections()
        {
            RootPanel.Children.Clear();

            foreach (DiagnosticSection section in _sections)
            {
                var header = new TextBlock
                {
                    Text = section.Title.ToUpperInvariant(),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(4, 14, 0, 4),
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                };
                RootPanel.Children.Add(header);

                var card = new Border
                {
                    Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 8, 10, 8)
                };

                var stack = new StackPanel();

                foreach (DiagnosticEntry entry in section.Entries)
                {
                    var line = new TextBlock
                    {
                        Margin = new Thickness(0, 2, 0, 2),
                        TextWrapping = TextWrapping.Wrap
                    };

                    var glyphRun = new Run($"{entry.Glyph} ") { FontWeight = FontWeights.Bold };
                    ApplyStatusColor(glyphRun, entry.Status);
                    line.Inlines.Add(glyphRun);
                    line.Inlines.Add(new Run(entry.Text));

                    if (!String.IsNullOrWhiteSpace(entry.Detail))
                    {
                        line.Inlines.Add(new Run($"\n    {entry.Detail}")
                        {
                            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                            FontSize = 11
                        });
                    }

                    stack.Children.Add(line);
                }

                card.Child = stack;
                RootPanel.Children.Add(card);
            }
        }

        private static void ApplyStatusColor(Run run, DiagnosticStatus status)
        {
            run.Foreground = status switch
            {
                DiagnosticStatus.Ok => Brushes.LimeGreen,
                DiagnosticStatus.Attention => Brushes.Orange,
                DiagnosticStatus.Problem => Brushes.OrangeRed,
                _ => Application.Current.TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray
            };
        }

        private void CopyReportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== BoneFish Diagnostic Center (read-only) ===");

                foreach (DiagnosticSection section in _sections)
                {
                    sb.AppendLine();
                    sb.AppendLine($"-- {section.Title} --");
                    foreach (DiagnosticEntry entry in section.Entries)
                    {
                        sb.AppendLine($"{entry.Glyph} {entry.Text}");
                        if (!String.IsNullOrWhiteSpace(entry.Detail))
                            sb.AppendLine($"    {entry.Detail}");
                    }
                }

                Clipboard.SetText(sb.ToString());
                CopyReportButton.Content = "Copied!";
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("DiagnosticCenterWindow", $"Copy failed: {ex.Message}");
                Frontend.ShowMessageBox($"Copy failed: {ex.Message}", System.Windows.MessageBoxImage.Warning);
            }
        }
    }
}
