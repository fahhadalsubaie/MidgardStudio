using System;
using System.Collections.Generic;
using System.Windows;
using MidgardStudio.App.Services;
using Wpf.Ui.Controls;

namespace MidgardStudio.App.Views;

/// <summary>Modal search-and-pick dialog over a database (items or mobs). Returns the chosen row.</summary>
public partial class RecordPickerDialog : FluentWindow
{
    private readonly Func<string, int, IReadOnlyList<PickerItem>> _search;

    public RecordPickerDialog(string title, Func<string, int, IReadOnlyList<PickerItem>> search)
    {
        _search = search;
        InitializeComponent();
        Title = title;
        TitleBarCtl.Title = title;

        SearchBox.TextChanged += (_, _) => Refresh();
        ResultsList.MouseDoubleClick += (_, _) => Confirm();
        OkButton.Click += (_, _) => Confirm();
        CancelButton.Click += (_, _) => { DialogResult = false; Close(); };
        Loaded += (_, _) => { Refresh(); SearchBox.Focus(); };
    }

    public PickerItem? Selected { get; private set; }

    private void Refresh()
    {
        ResultsList.ItemsSource = _search(SearchBox.Text ?? string.Empty, 200);
    }

    private void Confirm()
    {
        if (ResultsList.SelectedItem is PickerItem item)
        {
            Selected = item;
            DialogResult = true;
            Close();
        }
    }
}
