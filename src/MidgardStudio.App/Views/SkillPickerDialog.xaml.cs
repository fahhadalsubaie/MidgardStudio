using MidgardStudio.App.Services;
using Wpf.Ui.Controls;

namespace MidgardStudio.App.Views;

/// <summary>Search-and-pick dialog over skill_db. Player/job skills show by default; monster (NPC_) skills
/// appear only when the toggle is on. Exposes the chosen <see cref="Selected"/> skill on confirm.</summary>
public partial class SkillPickerDialog : FluentWindow
{
    private readonly SkillLookupService _lookup;

    public SkillPickerDialog(SkillLookupService lookup)
    {
        _lookup = lookup;
        InitializeComponent();

        SearchBox.TextChanged += (_, _) => Refresh();
        MonsterToggle.Checked += (_, _) => Refresh();
        MonsterToggle.Unchecked += (_, _) => Refresh();
        ResultsList.MouseDoubleClick += (_, _) => Confirm();
        OkButton.Click += (_, _) => Confirm();
        CancelButton.Click += (_, _) => { DialogResult = false; Close(); };
        Loaded += (_, _) => { Refresh(); SearchBox.Focus(); };
    }

    /// <summary>The chosen skill (null until confirmed).</summary>
    public SkillEntry? Selected { get; private set; }

    private void Refresh() =>
        ResultsList.ItemsSource = _lookup.Search(SearchBox.Text, MonsterToggle.IsChecked == true);

    private void Confirm()
    {
        if (ResultsList.SelectedItem is SkillEntry s)
        {
            Selected = s;
            DialogResult = true;
            Close();
        }
    }
}
