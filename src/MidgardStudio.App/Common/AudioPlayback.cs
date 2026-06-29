using System;
using System.IO;
using NAudio.Wave;

namespace MidgardStudio.App.Common;

/// <summary>
/// In-memory wav/mp3 playback via NAudio. Unlike WPF <c>MediaElement</c>, nothing touches the UI thread's media
/// pipeline and there is no temp file: the decompressed bytes are played from a <see cref="MemoryStream"/> on
/// NAudio's own audio thread. <see cref="Load"/> does the codec init, so call it from a background task to keep
/// the UI smooth (mirrors how GRFEditor plays sound — in memory, off the UI thread).
/// </summary>
public sealed class AudioPlayback : IDisposable
{
    private WaveOutEvent? _output;
    private WaveStream? _reader;
    private MemoryStream? _stream;
    private float _volume = 0.8f;

    /// <summary>Raised when playback reaches the end of the clip (NOT on an intentional stop/reload).</summary>
    public event Action? Ended;

    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    public TimeSpan Position
    {
        get => _reader?.CurrentTime ?? TimeSpan.Zero;
        set { if (_reader is not null) _reader.CurrentTime = value; }
    }

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public float Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0f, 1f); if (_output is not null) _output.Volume = _volume; }
    }

    /// <summary>Loads + starts playing wav/mp3 bytes. Call OFF the UI thread — codec init is the expensive part.</summary>
    public void Load(byte[] data)
    {
        Stop();
        _stream = new MemoryStream(data);
        _reader = new StreamMediaFoundationReader(_stream); // wav + mp3 (+ wma/aac)
        _output = new WaveOutEvent { Volume = _volume };
        _output.PlaybackStopped += OnOutputStopped;
        _output.Init(_reader);
        _output.Play();
    }

    public void Play() => _output?.Play();
    public void Pause() => _output?.Pause();

    public void Stop()
    {
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnOutputStopped; // an intentional stop must not raise Ended
            _output.Stop();
            _output.Dispose();
            _output = null;
        }
        _reader?.Dispose(); _reader = null;
        _stream?.Dispose(); _stream = null;
    }

    private void OnOutputStopped(object? sender, StoppedEventArgs e) => Ended?.Invoke();

    public void Dispose() => Stop();
}
