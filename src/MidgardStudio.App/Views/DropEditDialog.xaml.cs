using System.Globalization;
using System.Windows;
using MidgardStudio.App.Services;
using Wpf.Ui.Controls;

namespace MidgardStudio.App.Views;

/// <summary>Edits a single drop: item id (with picker), drop chance, steal-protected, random option group.</summary>
public partial class DropEditDialog : FluentWindow
{
    private readonly DropService _drops;

    public DropEditDialog(DropService drops, bool isMvp, int itemId, int rate, bool stealProtected, string randGroup)
    {
        _drops = drops;
        InitializeComponent();

        IdBox.Text = itemId > 0 ? itemId.ToString(CultureInfo.InvariantCulture) : string.Empty;
        RateBox.Text = rate.ToString(CultureInfo.InvariantCulture);
        StealBox.IsChecked = stealProtected;
        RandBox.Text = randGroup ?? string.Empty;
        StealBox.Visibility = isMvp ? Visibility.Collapsed : Visibility.Visible;

        IdBox.TextChanged += (_, _) => UpdateName();
        RateBox.TextChanged += (_, _) => UpdatePercent();
        PickButton.Click += (_, _) => Pick();
        OkButton.Click += (_, _) => Confirm();
        CancelButton.Click += (_, _) => { DialogResult = false; Close(); };

        UpdateName();
        UpdatePercent();
    }

    public int ItemId { get; private set; }
    public string Aegis { get; private set; } = string.Empty;
    public int Rate { get; private set; }
    public bool StealProtected { get; private set; }
    public string RandGroup { get; private set; } = string.Empty;

    private void Pick()
    {
        var dlg = new RecordPickerDialog("Select item", (q, n) => _drops.SearchItems(q, n)) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Selected is { } item)
            IdBox.Text = item.Id.ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateName()
    {
        if (int.TryParse(IdBox.Text, out int id) && _drops.AegisForItemId(id) is { } aegis)
        {
            var (_, name) = _drops.ResolveItem(aegis);
            ItemNameText.Text = $"{name}  ({aegis})";
        }
        else
        {
            ItemNameText.Text = "Unknown item id";
        }
    }

    private void UpdatePercent()
    {
        RatePercentText.Text = int.TryParse(RateBox.Text, out int r) ? $"= {r / 100.0:0.##} %" : string.Empty;
    }

    private void Confirm()
    {
        if (!int.TryParse(IdBox.Text, out int id) || _drops.AegisForItemId(id) is not { } aegis)
        {
            Error("Enter a valid item id (use the … button to pick one).");
            return;
        }
        if (!int.TryParse(RateBox.Text, out int rate) || rate < 0)
        {
            Error("Drop chance must be a non-negative number (per 10000).");
            return;
        }

        ItemId = id;
        Aegis = aegis;
        Rate = rate;
        StealProtected = StealBox.IsChecked == true;
        RandGroup = RandBox.Text?.Trim() ?? string.Empty;
        DialogResult = true;
        Close();
    }

    private void Error(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
