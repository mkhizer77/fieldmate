import type { Env } from "./env";
import { parseLanguage } from "./env";
import { HttpError, readJson } from "./http";
import { reserveUsage } from "./guard";

export const MAX_TTS_CHARS = 600;
export const TTS_SAMPLE_RATE = 22_050;

/** Streams 16-bit mono PCM at 22.05 kHz back to the app as ElevenLabs produces it. */
export async function handleTts(request: Request, env: Env, dev: boolean, fetchFn: typeof fetch): Promise<Response> {
  const body = (await readJson(request, 8 * 1024)) as { text?: unknown; language?: unknown };
  const text = typeof body?.text === "string" ? body.text.trim() : "";
  if (text.length === 0 || text.length > MAX_TTS_CHARS) {
    throw new HttpError(400, "invalid_request", `'text' must be 1-${MAX_TTS_CHARS} characters.`);
  }

  const language = parseLanguage(typeof body.language === "string" ? body.language : null) ?? "en";
  const voice = language === "de" ? env.VOICE_DE : env.VOICE_EN;
  if (!voice) {
    throw new HttpError(500, "proxy_misconfigured", `No ElevenLabs voice configured for '${language}'.`);
  }

  await reserveUsage(env, "tts", text.length, dev);

  const url = `https://api.elevenlabs.io/v1/text-to-speech/${encodeURIComponent(voice)}/stream?output_format=pcm_${TTS_SAMPLE_RATE}`;
  let upstream: Response;
  try {
    upstream = await fetchFn(url, {
      method: "POST",
      headers: { "xi-api-key": env.ELEVENLABS_API_KEY, "Content-Type": "application/json" },
      body: JSON.stringify({ text, model_id: env.TTS_MODEL, language_code: language }),
    });
  } catch {
    throw new HttpError(502, "upstream_error", "Text-to-speech unreachable.");
  }

  if (upstream.status === 401 || upstream.status === 402 || upstream.status === 403) {
    // 401: key rejected or monthly quota used up. 402: the plan doesn't cover this voice or model
    // (e.g. Voice Library voices on the free plan). Either way the app continues with text answers.
    throw new HttpError(429, "tts_quota", "Text-to-speech unavailable on the current plan or quota; answers continue as text.");
  }
  if (upstream.status === 429) {
    throw new HttpError(429, "upstream_rate_limited", "Text-to-speech is busy. Try again shortly.");
  }
  if (!upstream.ok || !upstream.body) {
    throw new HttpError(502, "upstream_error", `Text-to-speech error (${upstream.status}).`);
  }

  return new Response(upstream.body, {
    headers: {
      "Content-Type": "audio/pcm",
      "X-Sample-Rate": String(TTS_SAMPLE_RATE),
      "X-Channels": "1",
      "X-Bits-Per-Sample": "16",
    },
  });
}
