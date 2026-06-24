using System;
using System.Windows;
using System.Windows.Controls;

namespace MidgardStudio.App.Common;

/// <summary>
/// Attached behavior: <c>common:ScrollSound.Enabled="True"</c> on a list plays a ratchet click roughly
/// once per row scrolled (distance-accumulated, one click per scroll event max) for a bike-chain feel.
/// </summary>
public static class ScrollSound
{
    private const double Step = 10.0; // pixels of travel per click

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ScrollSound), new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);

    private static readonly DependencyProperty AccumulatedProperty = DependencyProperty.RegisterAttached(
        "Accumulated", typeof(double), typeof(ScrollSound), new PropertyMetadata(0.0));

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control) return;
        // handledEventsToo: catch ScrollChanged even if a parent marked it handled.
        if ((bool)e.NewValue)
            control.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScroll), handledEventsToo: true);
        else
            control.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScroll));
    }

    private static int _diagCount;

    private static void OnScroll(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 || sender is not DependencyObject d) return;

        if (_diagCount < 4)
        {
            _diagCount++;
            Serilog.Log.Information("DIAG OnScroll vChange={V:0.#} enabled={E}", e.VerticalChange, ScrollSoundPlayer.Enabled);
        }

        double acc = (double)d.GetValue(AccumulatedProperty) + Math.Abs(e.VerticalChange);
        if (acc >= Step)
        {
            ScrollSoundPlayer.Play();
            acc = 0;
        }
        d.SetValue(AccumulatedProperty, acc);
    }
}
