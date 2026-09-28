using Fieldmate.AI;
using UnityEngine;

namespace Fieldmate.Assistant;

/// <summary>Push-to-talk capture at 16 kHz mono (what speech-to-text wants). Records at most <see cref="MaxSeconds"/>.</summary>
public sealed class MicrophoneRecorder
{
    public const int SampleRate = 16000;
    public const int MaxSeconds = 20;

    private const int LevelWindow = SampleRate / 10; // 100 ms

    private readonly float[] window = new float[LevelWindow];
    private AudioClip clip;
    private string device;

    public bool IsRecording => clip != null;

    public static bool HasMicrophone => Microphone.devices.Length > 0;

    public bool Start()
    {
        if (IsRecording || !HasMicrophone)
        {
            return false;
        }

        device = Microphone.devices[0];
        clip = Microphone.Start(device, false, MaxSeconds, SampleRate);
        return clip != null;
    }

    /// <summary>RMS of the last 100 ms, for voice-activity detection. No allocations.</summary>
    public float Level()
    {
        if (!IsRecording)
        {
            return 0f;
        }

        var position = Microphone.GetPosition(device);
        if (position < LevelWindow)
        {
            return 0f;
        }

        clip.GetData(window, position - LevelWindow);
        var sum = 0f;
        for (var i = 0; i < window.Length; i++)
        {
            sum += window[i] * window[i];
        }

        return Mathf.Sqrt(sum / window.Length);
    }

    /// <summary>Stops and returns what was said; null if nothing was recorded.</summary>
    public AudioData Stop()
    {
        if (!IsRecording)
        {
            return null;
        }

        var length = Microphone.GetPosition(device);
        Microphone.End(device);
        var recorded = clip;
        clip = null;
        if (length <= 0)
        {
            length = recorded.samples; // recording hit the maximum length
        }

        var samples = new float[length];
        recorded.GetData(samples, 0);
        Object.Destroy(recorded);
        return new AudioData(samples, SampleRate);
    }
}
