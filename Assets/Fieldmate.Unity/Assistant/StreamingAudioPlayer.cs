using Fieldmate.AI;
using UnityEngine;

namespace Fieldmate.Assistant
{
    /// <summary>
    /// Plays streamed TTS through a streaming AudioClip that pulls from an <see cref="AudioRingBuffer"/>, so speech starts
    /// with the first chunk (design.md §5.4). The clip is created per sample rate and reused. Playback stops on the audio
    /// clock once the last real sample has been heard (<see cref="PlaybackCursor"/>), never when the ring merely runs dry:
    /// Unity reads streaming clips ahead by an unreported amount, which cut the end of every answer.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class StreamingAudioPlayer : MonoBehaviour, IAudioSink
    {
        private const int BufferSeconds = 60;
        private const float DuckedVolume = 0.3f;

        // After End(), with audio still buffered: stop if the audio thread stops reading for this long (dead device).
        private const float StallTimeoutSeconds = 1.5f;

        // Heard-time margin on top of the DSP buffer latency.
        private const double TailMarginSeconds = 0.15;

        private AudioSource source;
        private AudioRingBuffer ring;
        private PlaybackCursor cursor;
        private int sampleRate;
        private bool ended;
        private float lastProgressAt;
        private int lastRingCount;
        private double playStartDsp;
        private double outputLatency;

        /// <summary>True from <see cref="Begin"/> until the last sample has been heard (or <see cref="Stop"/>).</summary>
        public bool IsPlaying => source != null && source.isPlaying;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
        }

        public void Begin(int rate)
        {
            if (ring == null || rate != sampleRate)
            {
                sampleRate = rate;
                ring = new AudioRingBuffer(rate * BufferSeconds);
                cursor = new PlaybackCursor();
                source.clip = AudioClip.Create("Fieldmate TTS", rate, 1, rate, true, OnAudioRead);
            }

            source.Stop();
            ring.Clear();
            cursor.Reset();
            ended = false;
            AudioSettings.GetDSPBufferSize(out var bufferLength, out var bufferCount);
            outputLatency = (double)bufferLength * bufferCount / AudioSettings.outputSampleRate;
            playStartDsp = AudioSettings.dspTime;
            source.volume = 1f;
            source.Play();
        }

        public void Write(float[] samples, int count) => ring?.Write(samples, count);

        /// <summary>Lowers the reply while the user may be about to speak, so they know they're heard.</summary>
        public void Duck(bool ducked)
        {
            if (source != null)
            {
                source.volume = ducked ? DuckedVolume : 1f;
            }
        }

        public void End() => ended = true;

        /// <summary>Stops immediately (barge-in).</summary>
        public void Stop()
        {
            ring?.Clear();
            ended = true;
            if (source != null)
            {
                source.Stop();
            }
        }

        private void Update()
        {
            if (cursor == null || !source.isPlaying)
            {
                return;
            }

            var ringCount = ring.Count;
            if (!ended || ringCount != lastRingCount)
            {
                lastProgressAt = Time.unscaledTime;
            }

            lastRingCount = ringCount;
            if (!ended)
            {
                return;
            }

            var audioSeconds = cursor.LastRealEndSeconds(sampleRate);
            var elapsed = AudioSettings.dspTime - playStartDsp;
            if (ringCount == 0 && elapsed >= audioSeconds + outputLatency + TailMarginSeconds)
            {
                Debug.Log($"[Assistant] speech done: {audioSeconds:0.00}s of audio, stopped after {elapsed:0.00}s");
                source.Stop();
            }
            else if (ringCount > 0 && Time.unscaledTime - lastProgressAt >= StallTimeoutSeconds)
            {
                Debug.LogWarning($"[Assistant] speech stalled: audio device stopped reading ({ringCount} samples left)");
                source.Stop();
            }
        }

        // Audio thread.
        private void OnAudioRead(float[] data)
        {
            if (ring == null)
            {
                return;
            }

            cursor.OnRead(data.Length, ring.Read(data));
        }
    }
}
