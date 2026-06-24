using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace MidgardStudio.App.Common;

/// <summary>
/// ObservableCollection that supports bulk replacement with a single Reset notification — avoids the
/// per-item churn that makes large filtered lists slow.
/// </summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
