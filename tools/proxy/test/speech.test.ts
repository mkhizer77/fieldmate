import { describe, expect, it } from "vitest";
import { route } from "../src/index";
import { estimateSeconds } from "../src/stt";
import { makeDeps, makeEnv, post, postJson, wav } from "./helpers";

const deepgramOk = () =>
  Response.json({ results: { channels: [{ alternatives: [{ transcript: "Why is the pressure high?", confidence: 0.97 }] }] } });

describe("POST /v1/stt", () => {
  it("sends the audio to Deepgram Nova-3 and returns the transcript", async () => {
    const { deps, recorded } = makeDeps({ fetch: deepgramOk });
    const response = await route(post("/v1/stt?language=de", wav(2), { "content-type": "audio/wav" }), makeEnv(), deps);

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ text: "Why is the pressure high?", confidence: 0.97, language: "de" });
    const call = recorded.fetches[0];
    expect(call.url).toBe("https://api.deepgram.com/v1/listen?model=nova-3&language=de&smart_format=true");
    expect((call.init?.headers as Record<string, string>).Authorization).toBe("Token dg-test");
  });

  it("defaults to English and handles an empty result", async () => {
    const { deps, recorded } = makeDeps({ fetch: () => Response.json({ results: { channels: [] } }) });
    const response = await route(post("/v1/stt?language=fr", wav(1)), makeEnv(), deps);

    expect(await response.json()).toEqual({ text: "", confidence: 0, language: "en" });
    expect(recorded.fetches[0].url).toContain("language=en");
  });

  it("rejects empty or oversized audio", async () => {
    const { deps } = makeDeps({ fetch: deepgramOk });
    expect((await route(post("/v1/stt", new Uint8Array(0)), makeEnv(), deps)).status).toBe(400);
    expect((await route(post("/v1/stt", new Uint8Array(1_300_000)), makeEnv(), deps)).status).toBe(413);
  });

  it.each([
    [401, 500, "proxy_misconfigured"],
    [500, 502, "upstream_error"],
  ])("maps Deepgram %i to %i", async (upstream, status, type) => {
    const { deps } = makeDeps({ fetch: () => new Response("x", { status: upstream }) });
    const response = await route(post("/v1/stt", wav(1)), makeEnv(), deps);

    expect(response.status).toBe(status);
    expect(((await response.json()) as { error: { type: string } }).error.type).toBe(type);
  });

  it("estimates WAV duration from the byte rate", () => {
    expect(estimateSeconds(wav(3))).toBe(3);
    expect(estimateSeconds(new Uint8Array(64_000))).toBe(2); // not a WAV: 16 kHz 16-bit mono assumed
  });
});

describe("POST /v1/tts", () => {
  const pcm = () => new Response(new Uint8Array([1, 2, 3, 4]), { status: 200 });

  it("streams ElevenLabs Flash PCM with the voice for the language", async () => {
    const { deps, recorded } = makeDeps({ fetch: pcm });
    const response = await route(postJson("/v1/tts", { text: "Close the inlet valve.", language: "de" }), makeEnv(), deps);

    expect(response.status).toBe(200);
    expect(response.headers.get("content-type")).toBe("audio/pcm");
    expect(response.headers.get("x-sample-rate")).toBe("22050");
    expect(new Uint8Array(await response.arrayBuffer())).toEqual(new Uint8Array([1, 2, 3, 4]));

    const call = recorded.fetches[0];
    expect(call.url).toBe("https://api.elevenlabs.io/v1/text-to-speech/voice-de/stream?output_format=pcm_22050");
    expect((call.init?.headers as Record<string, string>)["xi-api-key"]).toBe("el-test");
    expect(JSON.parse(call.init?.body as string)).toEqual({ text: "Close the inlet valve.", model_id: "eleven_flash_v2_5", language_code: "de" });
  });

  it("rejects empty and overlong text", async () => {
    const { deps } = makeDeps({ fetch: pcm });
    expect((await route(postJson("/v1/tts", { text: "  " }), makeEnv(), deps)).status).toBe(400);
    expect((await route(postJson("/v1/tts", { text: "x".repeat(601) }), makeEnv(), deps)).status).toBe(400);
  });

  it("reports a missing voice as misconfiguration", async () => {
    const { deps } = makeDeps({ fetch: pcm });
    const response = await route(postJson("/v1/tts", { text: "hi" }), makeEnv({ VOICE_EN: "" }), deps);
    expect(response.status).toBe(500);
  });

  it.each([
    [401, 429, "tts_quota"],
    [429, 429, "upstream_rate_limited"],
    [500, 502, "upstream_error"],
  ])("maps ElevenLabs %i to %i", async (upstream, status, type) => {
    const { deps } = makeDeps({ fetch: () => new Response("x", { status: upstream }) });
    const response = await route(postJson("/v1/tts", { text: "hi" }), makeEnv(), deps);

    expect(response.status).toBe(status);
    expect(((await response.json()) as { error: { type: string } }).error.type).toBe(type);
  });

  it("maps a network failure to 502", async () => {
    const { deps } = makeDeps({ fetch: () => { throw new TypeError("offline"); } });
    expect((await route(postJson("/v1/tts", { text: "hi" }), makeEnv(), deps)).status).toBe(502);
  });
});
