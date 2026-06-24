using System.Windows;
using Wpf.Ui.Controls;

namespace MidgardStudio.App.Views;

/// <summary>The user's choice from a Save / Don't-save / Cancel prompt.</summary>
public enum SavePrompt { Save, Discard, Cancel }

/// <summary>A themed (Fluent) replacement for native message boxes: confirm, alert, and save-prompt.</summary>
public partial class ConfirmDialog : FluentWindow
{
    private SavePrompt _choice = SavePrompt.Cancel;

    public ConfirmDialog()
    {
        InitializeComponent();
        YesButton.Click += (_, _) => { _choice = SavePrompt.Save; DialogResult = true; Close(); };
        AltButton.Click += (_, _) => { _choice = SavePrompt.Discard; DialogResult = true; Close(); };
        NoButton.Click += (_, _) => { _choice = SavePrompt.Cancel; DialogResult = false; Close(); };
    }

    /// <summary>Shows a themed Yes/Cancel confirmation. Returns true when the primary button is clicked.</summary>
    public static bool Show(string title, string message, string yes = "Yes", string no = "Cancel")
    {
        var dialog = Create(title, message);
        dialog.YesButton.Content = yes;
        dialog.NoButton.Content = no;
        return dialog.ShowDialog() == true;
    }

    /// <summary>Shows a themed single-button informational alert.</summary>
    public static void Alert(string title, string message, string ok = "OK")
    {
        var dialog = Create(title, message);
        dialog.YesButton.Content = ok;
        dialog.NoButton.Visibility = Visibility.Collapsed;
        dialog.ShowDialog();
    }

    /// <summary>Shows a themed Save / Don't-save / Cancel prompt (e.g. closing with unsaved changes).</summary>
    public static SavePrompt AskSave(string title, string message)
    {
        var dialog = Create(title, message);
        dialog.YesButton.Content = "Save";
        dialog.AltButton.Content = "Don't save";
        dialog.AltButton.Visibility = Visibility.Visible;
        dialog.NoButton.Content = "Cancel";
        dialog.ShowDialog();
        return dialog._choice;
    }

    private static ConfirmDialog Create(string title, string message)
    {
        var dialog = new ConfirmDialog { Owner = System.Windows.Application.Current.MainWindow };
        dialog.Title = title;
        dialog.Bar.Title = title;
        dialog.MessageText.Text = message;
        return dialog;
    }
}
