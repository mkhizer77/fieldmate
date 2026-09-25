import type { Env } from "./env";
import { parseLanguage } from "./env";
import { HttpError, readBytes } from "./http";
import { reserveUsage } from "./guard";

/** 30 s of 16 kHz 16-bit mono is ~960 KB; allow some headroom. */
export const MAX_AUDIO_BYTES = 1_200_000;

/** Seconds of audio in a WAV (from its byte rate); falls back to 16 kHz 16-bit mono for other formats. */
export function estimateSeconds(audio: Uint8Array): number {
  const view = new DataView(audio.buffer, audio.byteOffset, audio.byteLength);
  const isWav = audio.byteLength >= 44 && view.getUint32(0, false) === 0x52494646; // "RIFF"
  const byteRate = isWav ? view.getUint32(28, true) : 32_000;
  const dataBytes = isWav ? audio.byteLength - 44 : audio.byteLength;
  return Math.max(1, Math.ceil(dataBytes / (byteRate > 0 ? byteRate : 32_000)));
}

export async function handleStt(request: Request, env: Env, dev: boolean, fetchFn: typeof fetch): Promise<Response> {
  const language = parseLanguage(new URL(request.url).searchParams.get("language")) ?? "en";
  const audio = await readBytes(request, MAX_AUDIO_BYTES);
  if (audio.byteLength === 0) {
    throw new HttpError(400, "invalid_request", "No audio in the body.");
  }

  await reserveUsage(env, "stt", estimateSeconds(audio), dev);

  const url = new URL("https://api.deepgram.com/v1/listen");
  url.searchParams.set("model", env.STT_MODEL);
  url.searchParams.set("language", language);
  url.searchParams.set("smart_format", "true");

  let upstream: Response;
  try {
    upstream = await fetchFn(url.toString(), {
      method: "POST",
      headers: {
        Authorization: `Token ${env.DEEPGRAM_API_KEY}`,
        "Content-Type": request.headers.get("content-type") ?? "audio/wav",
      },
      body: audio,
    });
  } catch {
    throw new HttpError(502, "upstream_error", "Speech-to-text unreachable.");
  }

  if (upstream.status === 401 || upstream.status === 403) {
    throw new HttpError(500, "proxy_misconfigured", "The proxy's speech-to-text key was rejected.");
  }
  if (!upstream.ok) {
    throw new HttpError(502, "upstream_error", `Speech-to-text error (${upstream.status}).`);
  }

  const result = (await upstream.json()) as {
    results?: { channels?: Array<{ alternatives?: Array<{ transcript?: string; confidence?: number }> }> };
  };
  const best = result.results?.channels?.[0]?.alternatives?.[0];
  return Response.json({ text: best?.transcript ?? "", confidence: best?.confidence ?? 0, language });
}
