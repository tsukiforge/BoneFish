using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Bloxstrap.Sandbox;
using Bloxstrap.Sandbox.Models;

namespace Bloxstrap.UI.Elements.Dialogs
{
    /// <summary>
    /// Dialog for adding a single FastFlag change to an optimization experiment.
    /// Searches the existing BoneFish FastFlag database (App.FastFlags + presets), shows the
    /// detected current value and validates the new value before accepting the change.
    /// </summary>
    public partial class AddSandboxChangeDialog
    {
        private readonly IReadOnlyCollection<string> _knownFlags;
        private readonly IReadOnlyDictionary<string, string> _currentValues;
        private readonly HashSet<string> _existingChangeNames;
        private readonly ObservableCollection<string> _filteredFlags = new();

        public MessageBoxResult Result { get; private set; } = MessageBoxResult.Cancel;

        public string FlagName { get; private set; } = "";

        /// <summary>Null means "remove the flag" (leave the new value empty).</summary>
        public string? NewValue { get; private set; }

        public AddSandboxChangeDialog(
            IReadOnlyCollection<string> knownFlags,
            IReadOnlyDictionary<string, string> currentValues,
            IReadOnlyCollection<string> existingChangeNames)
        {
            _knownFlags = knownFlags;
            _currentValues = currentValues;
            _existingChangeNames = existingChangeNames.ToHashSet(StringComparer.Ordinal);

            InitializeComponent();

            FlagsListBox.ItemsSource = _filteredFlags;
            RefreshFilter();
        }

        private void RefreshFilter()
        {
            string filter = SearchTextBox.Text.Trim();

            // Drop the selection when it no longer matches, so the search text itself can act
            // as the flag name (for flags that are not in the known database).
            if (FlagsListBox.SelectedItem is string selected && !selected.Contains(filter, StringComparison.OrdinalIgnoreCase))
                FlagsListBox.SelectedItem = null;

            _filteredFlags.Clear();
            foreach (var flag in _knownFlags.OrderBy(f => f, StringComparer.Ordinal))
            {
                if (filter.Length == 0 || flag.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    _filteredFlags.Add(flag);
            }

            UpdatePreview();
        }

        private void UpdatePreview()
        {
            string? selected = FlagsListBox.SelectedItem as string;
            string search = SearchTextBox.Text.Trim();
            string name = string.IsNullOrEmpty(selected) ? search : selected;

            if (string.IsNullOrEmpty(name))
            {
                FlagNameText.Text = "—";
                CurrentValueTextBox.Text = "";
                PreviewText.Text = "Select a flag to preview the change.";
                ExistingHintText.Visibility = Visibility.Collapsed;
                return;
            }

            FlagNameText.Text = name;
            string? current = _currentValues.TryGetValue(name, out string? value) ? value : null;
            CurrentValueTextBox.Text = current ?? "(not set)";

            string newValueText = NewValueTextBox.Text.Trim();
            string newDisplay = string.IsNullOrEmpty(newValueText) ? "(removed)" : newValueText;

            PreviewText.Text = $"{name}: {current ?? "—"} → {newDisplay}";

            if (_existingChangeNames.Contains(name))
            {
                ExistingHintText.Text = "This flag is already in the experiment — adding it updates the existing entry.";
                ExistingHintText.Visibility = Visibility.Visible;
            }
            else if (selected is null)
            {
                ExistingHintText.Text = "Not in the known FastFlag database — it will be added as a custom flag.";
                ExistingHintText.Visibility = Visibility.Visible;
            }
            else
            {
                ExistingHintText.Visibility = Visibility.Collapsed;
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshFilter();

        private void FlagsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

        private void NewValueTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            string name = FlagNameText.Text.Trim();
            string? newValue = string.IsNullOrWhiteSpace(NewValueTextBox.Text) ? null : NewValueTextBox.Text.Trim();

            var change = new SandboxChange { FlagName = name, NewValue = newValue };

            string? error = SandboxChangeValidator.GetFirstInvalidChangeMessage(change);
            if (error is not null)
            {
                ErrorText.Text = error;
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            FlagName = name;
            NewValue = newValue;
            Result = MessageBoxResult.OK;
            Close();
        }
    }
}
