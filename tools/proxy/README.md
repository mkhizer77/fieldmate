# Fieldmate AI proxy

A Cloudflare Worker that holds the provider keys, so the public repo and APK never contain them
(see `docs/adr/003-voice-providers-and-proxy.md`). The Quest app talks only to this Worker.

| Route | Upstream | Notes |
|---|---|---|
| `POST /v1/chat` | Anthropic Messages API (`claude-haiku-4-5`) | App sends `{system, messages, tools, max_tokens}`; model, output cap (600) and prompt caching are set here |
| `POST /v1/stt?language=en\|de` | Deepgram Nova-3 | Body: WAV audio (≤ 1.2 MB, ~30 s). Returns `{text, confidence, language}` |
| `POST /v1/tts` | ElevenLabs Flash v2.5, streamed | Body `{text ≤ 600 chars, language}`. Returns 16-bit mono PCM at 22.05 kHz as it is generated |
| `GET /v1/health` | — | Models in use; no secrets |

Guardrails: request size limits, a per-IP rate limit (20/min), and daily caps across all public users
(chat calls, STT seconds, TTS characters; see `wrangler.toml`). Requests with the dev token
(`x-fieldmate-dev-token` header) skip the rate limit and caps. Errors come back as
`{ "error": { "type", "message" } }`; `tts_quota` means answers continue as text.

## Deploy (one time)

```bash
cd tools/proxy
npm install
npx wrangler login                          # free Cloudflare account
npx wrangler kv namespace create USAGE      # paste the id into wrangler.toml
```

Pick one English and one German voice in the ElevenLabs voice library and put their ids in `VOICE_EN` /
`VOICE_DE` in `wrangler.toml` (voice ids are not secret). Then set the secrets. Wrangler prompts for each
value, so it never lands in shell history or the repo:

```bash
npx wrangler secret put ANTHROPIC_API_KEY
npx wrangler secret put DEEPGRAM_API_KEY
npx wrangler secret put ELEVENLABS_API_KEY
npx wrangler secret put DEV_TOKEN           # any long random string, e.g. from: openssl rand -hex 24
npx wrangler deploy                         # prints https://fieldmate-proxy.<account>.workers.dev
```

Also set a monthly spending limit in each provider's console. The Worker's daily caps limit public use,
not your own dev-token use.

## Develop

```bash
npm test            # vitest, upstreams mocked
npm run typecheck
npx wrangler dev    # local Worker; put secrets in .dev.vars (gitignored)
```

## Notes

- ElevenLabs free plan: 10k credits/month (roughly 10–20k characters with Flash) and attribution is required
  wherever the audio is used. Switch to a paid plan before recording the demo video.
- The daily caps use KV, which is eventually consistent: they stop runaway cost, they don't meter exactly.
