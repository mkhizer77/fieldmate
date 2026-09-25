using Fieldmate.AI;
using UnityEngine;

namespace Fieldmate.Assistant
{
    /// <summary>
    /// Plays streamed TTS through a streaming AudioClip that pulls from an <see cref="AudioRingBuffer"/>, so speech starts
    /// with the first chunk (design.md §5.4). The clip is created per sample rate and reused.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class StreamingAudioPlayer : MonoBehaviour, IAudioSink
    {
        private const int BufferSeconds = 60;

        private AudioSource source;
        private AudioRingBuffer ring;
        private int sampleRate;
        private bool ended;

        public bool IsPlaying => source != null && source.isPlaying && (!ended || ring.Count > 0);

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
                source.clip = AudioClip.Create("Fieldmate TTS", rate, 1, rate, true, OnAudioRead);
            }

            ring.Clear();
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
            if (ended && source.isPlaying && ring != null && ring.Count == 0)
            {
                source.Stop();
            }
        }

        // Audio thread.
        private void OnAudioRead(float[] data) => ring?.Read(data);
    }
}
