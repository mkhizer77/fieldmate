using Fieldmate.AI;
using UnityEngine;

namespace Fieldmate.Assistant
{
    /// <summary>
    /// Plays streamed TTS through a streaming AudioClip that pulls from an <see cref="AudioRingBuffer"/>, so speech starts
    /// with the first chunk (design.md §5.4). The clip is created per sample rate and reused. Playback stops only after
    /// the playhead has passed the last real sample (<see cref="PlaybackCursor"/>), never when the ring merely runs dry.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class StreamingAudioPlayer : MonoBehaviour, IAudioSink
    {
        private const int BufferSeconds = 60;

        // After End(): stop if neither the ring nor the playhead moves for this long (dead audio device, or a playhead
        // that doesn't report). Unity reads at most one clip length (1 s) ahead, so no real audio is lost.
        private const float StallTimeoutSeconds = 1.5f;

        private AudioSource source;
        private AudioRingBuffer ring;
        private PlaybackCursor cursor;
        private int sampleRate;
        private bool ended;
        private float lastProgressAt;
        private int lastRingCount;

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
                cursor = new PlaybackCursor(rate);
                source.clip = AudioClip.Create("Fieldmate TTS", rate, 1, rate, true, OnAudioRead);
            }

            source.Stop();
            ring.Clear();
            cursor.Reset();
            ended = false;
            source.Play();
        }

        public void Write(float[] samples, int count) => ring?.Write(samples, count);

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

            var playedBefore = cursor.Played;
            cursor.Advance(source.timeSamples);
            var ringCount = ring.Count;
            if (!ended || ringCount != lastRingCount || cursor.Played != playedBefore)
            {
                lastProgressAt = Time.unscaledTime;
            }

            lastRingCount = ringCount;
            if (ended && ((ringCount == 0 && cursor.Drained) || Time.unscaledTime - lastProgressAt >= StallTimeoutSeconds))
            {
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
