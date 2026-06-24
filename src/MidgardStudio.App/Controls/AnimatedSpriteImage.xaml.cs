using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MidgardStudio.App.Controls;

/// <summary>
/// Cycles through a list of pre-rendered sprite frames on a timer. Self-contained: it starts when
/// loaded into the visual tree and stops when removed, so it never leaks a running timer when the
/// host view model is replaced.
/// </summary>
public partial class AnimatedSpriteImage : UserControl
{
    private readonly DispatcherTimer _timer = new();
    private IReadOnlyList<ImageSource>? _frames;
    private int _index;

    public AnimatedSpriteImage()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Advance();
        Loaded += (_, _) => Restart();
        Unloaded += (_, _) => _timer.Stop();
    }

    public static readonly DependencyProperty FramesProperty = DependencyProperty.Register(
        nameof(Frames), typeof(IReadOnlyList<ImageSource>), typeof(AnimatedSpriteImage),
        new PropertyMetadata(null, OnChanged));

    public IReadOnlyList<ImageSource>? Frames
    {
        get => (IReadOnlyList<ImageSource>?)GetValue(FramesProperty);
        set => SetValue(FramesProperty, value);
    }

    public static readonly DependencyProperty IntervalMsProperty = DependencyProperty.Register(
        nameof(IntervalMs), typeof(int), typeof(AnimatedSpriteImage), new PropertyMetadata(0, OnChanged));

    public int IntervalMs
    {
        get => (int)GetValue(IntervalMsProperty);
        set => SetValue(IntervalMsProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((AnimatedSpriteImage)d).Restart();

    private void Restart()
    {
        _timer.Stop();
        _frames = Frames;
        _index = 0;

        if (_frames is null || _frames.Count == 0)
        {
            Frame.Source = null;
            return;
        }

        Frame.Source = _frames[0];
        if (_frames.Count > 1 && IntervalMs > 0)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(IntervalMs);
            _timer.Start();
        }
    }

    private void Advance()
    {
        if (_frames is null || _frames.Count == 0) return;
        _index = (_index + 1) % _frames.Count;
        Frame.Source = _frames[_index];
    }
}
