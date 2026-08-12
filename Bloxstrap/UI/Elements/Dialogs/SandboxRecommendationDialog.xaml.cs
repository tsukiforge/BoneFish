using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Bloxstrap.Sandbox.Models;

namespace Bloxstrap.UI.Elements.Dialogs
{
    /// <summary>One selectable recommendation row.</summary>
    public class SandboxRecommendationItem : INotifyPropertyChanged
    {
        public string FlagName { get; init; } = "";
        public string? CurrentValue { get; init; }
        public string RecommendedValue { get; init; } = "";
        public string Reason { get; init; } = "";

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Compact one-line summary for the row.</summary>
        public string Description =>
            $"{(CurrentValue is null ? "not set" : CurrentValue)} → {RecommendedValue}" +
            (string.IsNullOrEmpty(Reason) ? "" : $"   ·   {Reason}");
    }

    /// <summary>
    /// Review dialog for the "read my device → suggest FastFlags" automation. Shows the detected
    /// device summary plus one selectable row per suggested flag. Only the ticked rows are returned
    /// to the experiment — nothing is written by this dialog itself.
    /// </summary>
    public partial class SandboxRecommendationDialog
    {
        private readonly ObservableCollection<SandboxRecommendationItem> _items = new();

        public MessageBoxResult Result { get; private set; } = MessageBoxResult.Cancel;

        /// <summary>The ticked changes the user wants to add to the experiment.</summary>
        public IReadOnlyList<SandboxChange> SelectedChanges { get; private set; } = Array.Empty<SandboxChange>();

        public SandboxRecommendationDialog(
            string systemInfo,
            IReadOnlyDictionary<string, string> recommendations,
            IReadOnlyDictionary<string, string> currentValues,
            IReadOnlyDictionary<string, string> reasons)
        {
            InitializeComponent();

            SystemInfoText.Text = $"💻 {systemInfo}";

            foreach (var pair in recommendations)
            {
                _items.Add(new SandboxRecommendationItem
                {
                    FlagName = pair.Key,
                    CurrentValue = currentValues.TryGetValue(pair.Key, out string? c) ? c : null,
                    RecommendedValue = pair.Value,
                    Reason = reasons.TryGetValue(pair.Key, out string? r) ? r : ""
                });
            }

            RecommendationsList.ItemsSource = _items;
            RefreshCount();
        }

        private void RefreshCount()
        {
            int selected = _items.Count(x => x.IsSelected);
            SelectedCountText.Text = $"{selected} of {_items.Count} changes selected — they will be added to the experiment.";
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items)
                item.IsSelected = true;
            RefreshCount();
        }

        private void SelectNoneButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items)
                item.IsSelected = false;
            RefreshCount();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = _items.Where(x => x.IsSelected).Select(x => new SandboxChange
            {
                FlagName = x.FlagName,
                NewValue = x.RecommendedValue
            }).ToList();

            if (selected.Count == 0)
            {
                ErrorText.Text = "No changes selected. Tick at least one flag, or close the dialog to keep the experiment as it is.";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            SelectedChanges = selected;
            Result = MessageBoxResult.OK;
            Close();
        }
    }
}
