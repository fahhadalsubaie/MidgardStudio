using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using MidgardStudio.App.Common;

namespace MidgardStudio.App.Controls;

/// <summary>
/// Audio preview (play/pause/seek/volume) for the GRF Browser, backed by <see cref="AudioPlayback"/> (NAudio,
/// in-memory). Takes the decompressed bytes via <see cref="Data"/> and plays them on NAudio's own thread — the
/// codec init runs on a background task, so clicking an audio file never freezes the UI.
/// </summary>
public partial class AudioPlayer : UserControl
{
    private readonly AudioPlayback _engine = new();
    private readonly DispatcherTimer _timer;
    private bool _dragging;
    private bool _playing;

    public AudioPlayer()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => Tick();
        _engine.Ended += () => Dispatcher.BeginInvoke(new Action(OnEnded));
        Unloaded += (_, _) => _engine.Dispose();
    }

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(byte[]), typeof(AudioPlayer),
            new PropertyMetadata(null, OnDataChanged));

    /// <summary>The decompressed audio bytes to play (null clears the player).</summary>
    public byte[]? Data
    {
        get => (byte[]?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((AudioPlayer)d).LoadData(e.NewValue as byte[]);

    private async void LoadData(byte[]? data)
    {
        _timer.Stop();
        _engine.Stop();
        SetPlaying(false);
        Seek.Value = 0;
        TimeText.Text = "0:00 / 0:00";

        if (data is null || data.Length == 0) { StateText.Text = string.Empty; return; }

        StateText.Text = "loading…";
        bool ok = await Task.Run(() =>
        {
            try { _engine.Load(data); return true; } catch { return false; }
        });
        if (!ReferenceEquals(Data, data)) return; // user moved to another file while we were loading
        if (!ok) { StateText.Text = "(can't play this file)"; return; }

        StateText.Text = string.Empty;
        Seek.Maximum = Math.Max(0.1, _engine.Duration.TotalSeconds);
        UpdateTime();
        SetPlaying(true); // auto-play
        _timer.Start();
    }

    private void OnEnded()
    {
        SetPlaying(false);
        Seek.Value = 0;
        UpdateTime();
    }

    private void OnPlayPause(object sender, RoutedEventArgs e)
    {
        if (Data is null) return;
        if (_playing) { _engine.Pause(); SetPlaying(false); _timer.Stop(); }
        else { _engine.Play(); SetPlaying(true); _timer.Start(); }
    }

    private void SetPlaying(bool playing)
    {
        _playing = playing;
        PlayButton.Content = playing ? "❚❚" : "▶";
    }

    private void Tick()
    {
        if (_dragging) return;
        Seek.Value = _engine.Position.TotalSeconds;
        UpdateTime();
    }

    private void UpdateTime() => TimeText.Text = $"{Fmt(_engine.Position)} / {Fmt(_engine.Duration)}";

    private static string Fmt(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    private void OnSeekDragStarted(object sender, DragStartedEventArgs e) => _dragging = true;

    private void OnSeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _dragging = false;
        _engine.Position = TimeSpan.FromSeconds(Seek.Value);
        UpdateTime();
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => _engine.Volume = (float)e.NewValue;
}
