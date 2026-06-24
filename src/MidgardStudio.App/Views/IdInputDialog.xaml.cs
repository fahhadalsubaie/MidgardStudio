using System.Globalization;
using System.Windows;
using Wpf.Ui.Controls;

namespace MidgardStudio.App.Views;

/// <summary>Small modal that asks for an integer id (used by Change ID / Copy to ID).</summary>
public partial class IdInputDialog : FluentWindow
{
    public IdInputDialog(string title, string prompt, int initial)
    {
        InitializeComponent();
        Title = title;
        TitleBarCtl.Title = title;
        PromptText.Text = prompt;
        IdBox.Text = initial.ToString(CultureInfo.InvariantCulture);
        Loaded += (_, _) => { IdBox.Focus(); IdBox.SelectAll(); };
        OkButton.Click += (_, _) => Confirm();
        CancelButton.Click += (_, _) => { DialogResult = false; Close(); };
    }

    public int Value { get; private set; }

    private void Confirm()
    {
        if (!int.TryParse(IdBox.Text, out int v) || v <= 0)
        {
            ErrorText.Text = "Enter a valid positive id.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Value = v;
        DialogResult = true;
        Close();
    }
}
