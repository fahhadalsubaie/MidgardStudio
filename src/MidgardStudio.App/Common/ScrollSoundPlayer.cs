using System;
using System.Runtime.InteropServices;

namespace MidgardStudio.App.Common;

/// <summary>
/// Plays a short, loud "ratchet click" for a bike-chain scroll feel. Uses winmm's PlaySound with a
/// persistent in-memory WAV (SND_ASYNC): low latency and a clean interrupt, so rapid scrolling sounds
/// like a fast ratchet. The buffer lives for the app's lifetime (SND_ASYNC reads it after the call).
/// </summary>
public static class ScrollSoundPlayer
{
    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(IntPtr pszSound, IntPtr hModule, uint flags);

    private const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_MEMORY = 0x0004;

    private static readonly IntPtr Buffer = AllocClick();

    /// <summary>Master on/off (driven by the app setting).</summary>
    public static bool Enabled { get; set; } = true;

    public static void Play()
    {
        if (!Enabled || Buffer == IntPtr.Zero) return;
        try { PlaySound(Buffer, IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT); }
        catch { /* no audio device — ignore */ }
    }

    private static IntPtr AllocClick()
    {
        try
        {
            byte[] wav = BuildClickWav();
            IntPtr ptr = Marshal.AllocHGlobal(wav.Length);
            Marshal.Copy(wav, 0, ptr, wav.Length);
            Serilog.Log.Information("DIAG winmm click buffer allocated ({Bytes} bytes)", wav.Length);
            return ptr; // never freed — lives for the app's lifetime
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "DIAG winmm click alloc failed"); return IntPtr.Zero; }
    }

    private static byte[] BuildClickWav()
    {
        const int sampleRate = 44100;
        int n = (int)(sampleRate * 0.035); // ~35 ms — clearly audible
        var pcm = new short[n];
        var rng = new Random(20240624);

        for (int i = 0; i < n; i++)
        {
            double t = i / (double)sampleRate;
            double env = Math.Exp(-t / 0.008);                  // ~8 ms decay
            double noise = rng.NextDouble() * 2 - 1;
            double tone = Math.Sin(2 * Math.PI * 1800 * t);     // clear mid tick
            double s = (0.45 * noise + 0.55 * tone) * env * 0.95;
            pcm[i] = (short)Math.Clamp(s * 32767, short.MinValue, short.MaxValue);
        }

        return WrapWav(pcm, sampleRate);
    }

    private static byte[] WrapWav(short[] pcm, int sampleRate)
    {
        int dataBytes = pcm.Length * 2;
        var buf = new byte[44 + dataBytes];

        void W32(int off, int v) { buf[off] = (byte)v; buf[off + 1] = (byte)(v >> 8); buf[off + 2] = (byte)(v >> 16); buf[off + 3] = (byte)(v >> 24); }
        void W16(int off, int v) { buf[off] = (byte)v; buf[off + 1] = (byte)(v >> 8); }

        buf[0] = (byte)'R'; buf[1] = (byte)'I'; buf[2] = (byte)'F'; buf[3] = (byte)'F';
        W32(4, 36 + dataBytes);
        buf[8] = (byte)'W'; buf[9] = (byte)'A'; buf[10] = (byte)'V'; buf[11] = (byte)'E';
        buf[12] = (byte)'f'; buf[13] = (byte)'m'; buf[14] = (byte)'t'; buf[15] = (byte)' ';
        W32(16, 16);
        W16(20, 1);               // PCM
        W16(22, 1);               // mono
        W32(24, sampleRate);
        W32(28, sampleRate * 2);  // byte rate
        W16(32, 2);               // block align
        W16(34, 16);              // bits per sample
        buf[36] = (byte)'d'; buf[37] = (byte)'a'; buf[38] = (byte)'t'; buf[39] = (byte)'a';
        W32(40, dataBytes);
        for (int i = 0; i < pcm.Length; i++) W16(44 + i * 2, pcm[i]);

        return buf;
    }
}
