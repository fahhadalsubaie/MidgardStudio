using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MidgardStudio.App.ViewModels;

/// <summary>The "Change ID" dialog: type a new Item ID, validated live against the ids already used on BOTH
/// sides (the active-mode server item_db and the client item info) — the status says where a taken id is in
/// use. <see cref="Confirm"/> is only enabled for an available id, so a duplicate can't be committed. On
/// confirm the chosen id is exposed via <see cref="Result"/>.</summary>
public sealed partial class ChangeIdViewModel : ObservableObject
{
    private readonly Func<int, (bool ok, string status)> _check;
    private readonly Func<int> _nextFree;

    public ChangeIdViewModel(int currentId, Func<int, (bool ok, string status)> check, Func<int> nextFree)
    {
        _check = check;
        _nextFree = nextFree;
        _idInput = currentId.ToString();
        Validate();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _idInput;
    partial void OnIdInputChanged(string value) => Validate();

    [ObservableProperty] private string _status = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isValid;

    /// <summary>The confirmed id, or null if the dialog was cancelled.</summary>
    public int? Result { get; private set; }

    public event Action<bool>? CloseRequested;

    private void Validate()
    {
        if (!int.TryParse((IdInput ?? string.Empty).Trim(), out int id))
        { IsValid = false; Status = "Enter a numeric Item ID."; return; }
        (IsValid, Status) = _check(id);
    }

    [RelayCommand]
    private void NextFree() => IdInput = _nextFree().ToString();

    [RelayCommand(CanExecute = nameof(IsValid))]
    private void Confirm()
    {
        if (int.TryParse(IdInput.Trim(), out int id)) Result = id;
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
