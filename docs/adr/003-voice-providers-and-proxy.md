# ADR-003: Voice providers and a key-holding proxy

Date: 2026-09-25 · Status: accepted

## Context
design.md §5.1/§5.4 keep speech-to-text, chat and text-to-speech provider-agnostic, and §8 puts API keys in
`Assets/_Project/Secrets/keys.json`. Two things were open: which providers, and how keys stay safe. The repo and
the release APK are public, so any key inside the app can be extracted by anyone who downloads it.

Providers were compared on latency (budget: STT 0.8 s, first token 1.0 s, TTS start 0.7 s), English and German
quality, tool-calling reliability, and cost for about 2,000 development and demo turns (research on 2026-09-25):

| Stage | Chosen | Why | Considered |
|---|---|---|---|
| STT | Deepgram Nova-3 | Fast, accurate EN/DE; $200 credit covers the project | OpenAI transcribe, AssemblyAI, Azure, Google, Meta Voice SDK (German unconfirmed), on-device Whisper (Quest perf unknown) |
| Chat | Claude Haiku 4.5 | Fastest Claude; reliable tool use and grounding rules | Claude Sonnet 5 (slower), OpenAI gpt-4.1-mini/gpt-4o-mini, Gemini Flash |
| TTS | ElevenLabs Flash v2.5, streamed | Most natural voice, strong German, ~75 ms first audio (vendor claim) | Azure/Google neural (free tiers, more synthetic), OpenAI TTS, Deepgram Aura, Piper on-device |

Speech-to-speech models (OpenAI Realtime, Gemini Live) were rejected: they bypass the separate STT → chat → TTS
pipeline, which is what the project demonstrates and what keeps tool calls visible and grounded.

## Decision
- Use Deepgram Nova-3, Claude Haiku 4.5 and ElevenLabs Flash v2.5 behind the existing `Fieldmate.Core.AI` interfaces.
  ElevenLabs runs on the free plan during development; a paid month covers the demo video.
- Keys live only in a Cloudflare Worker (`tools/proxy`). The Worker pins the model and output cap, applies request
  limits, a per-IP rate limit and daily caps, and exposes `/v1/chat`, `/v1/stt`, `/v1/tts`.
- `keys.json` now holds the proxy URL (and optionally Khizer's dev token, which lifts the Worker's caps), not provider
  keys. Without it the assistant runs in offline fallback mode (#16).

## Consequences
- The public APK works for anyone without exposing keys; spend is bounded by the Worker caps and provider limits.
- One extra network hop (Worker at the edge, typically tens of ms). Measure it with the rest of the latency budget.
- A free Cloudflare account and a one-time deploy are needed (`tools/proxy/README.md`).
- Swapping a provider changes the Worker and one Unity provider class; gameplay code is unaffected.
- Revisit TTS if the ElevenLabs free quota blocks development, or if measured latency misses the budget.
