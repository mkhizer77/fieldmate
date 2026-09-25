import { describe, expect, it } from "vitest";
import { tokensMatch } from "../src/guard";
import { route } from "../src/index";
import { makeDeps, makeEnv, MemoryKv, postJson } from "./helpers";

const chatBody = { messages: [{ role: "user", content: "hi" }] };
const tts = () => new Response(new Uint8Array([0]), { status: 200 });

describe("guardrails", () => {
  it("enforces the daily chat cap for public requests", async () => {
    const env = makeEnv({ DAILY_CHAT_CALLS: "2" });
    const { deps } = makeDeps();

    expect((await route(postJson("/v1/chat", chatBody), env, deps)).status).toBe(200);
    expect((await route(postJson("/v1/chat", chatBody), env, deps)).status).toBe(200);
    const third = await route(postJson("/v1/chat", chatBody), env, deps);
    expect(third.status).toBe(429);
    expect(((await third.json()) as { error: { type: string } }).error.type).toBe("daily_cap");
  });

  it("counts TTS characters against the daily cap", async () => {
    const env = makeEnv({ DAILY_TTS_CHARS: "10" });
    const { deps } = makeDeps({ fetch: tts });

    expect((await route(postJson("/v1/tts", { text: "12345678" }), env, deps)).status).toBe(200);
    expect((await route(postJson("/v1/tts", { text: "123" }), env, deps)).status).toBe(429);
  });

  it("lets the dev token past the caps and the rate limit", async () => {
    const env = makeEnv({
      DAILY_CHAT_CALLS: "0",
      RATE_LIMITER: { limit: async () => ({ success: false }) },
    });
    const { deps } = makeDeps();

    expect((await route(postJson("/v1/chat", chatBody), env, deps)).status).toBe(429);
    expect((await route(postJson("/v1/chat", chatBody, { "x-fieldmate-dev-token": "dev-secret" }), env, deps)).status).toBe(200);
    expect((await route(postJson("/v1/chat", chatBody, { "x-fieldmate-dev-token": "wrong" }), env, deps)).status).toBe(429);
  });

  it("applies the per-IP rate limit", async () => {
    const keys: string[] = [];
    const env = makeEnv({ RATE_LIMITER: { limit: async ({ key }) => (keys.push(key), { success: false }) } });
    const { deps } = makeDeps();
    const response = await route(postJson("/v1/chat", chatBody, { "cf-connecting-ip": "203.0.113.7" }), env, deps);

    expect(response.status).toBe(429);
    expect(((await response.json()) as { error: { type: string } }).error.type).toBe("rate_limited");
    expect(keys).toEqual(["203.0.113.7"]);
  });

  it("stores usage per UTC day", async () => {
    const kv = new MemoryKv();
    const { deps } = makeDeps();
    await route(postJson("/v1/chat", chatBody), makeEnv({ USAGE: kv as unknown as KVNamespace }), deps);

    expect([...kv.store.keys()]).toEqual([`usage:${new Date().toISOString().slice(0, 10)}:chat`]);
  });

  it("compares tokens exactly", () => {
    expect(tokensMatch("abc", "abc")).toBe(true);
    expect(tokensMatch("abc", "abd")).toBe(false);
    expect(tokensMatch("abc", "abcd")).toBe(false);
    expect(tokensMatch("", "")).toBe(true);
  });
});

describe("routing", () => {
  it("serves health without secrets", async () => {
    const response = await route(new Request("https://proxy.test/v1/health"), makeEnv(), makeDeps().deps);
    const body = await response.json();

    expect(body).toEqual({ ok: true, chatModel: "claude-haiku-4-5", sttModel: "nova-3", ttsModel: "eleven_flash_v2_5" });
    expect(JSON.stringify(body)).not.toContain("test");
  });

  it("rejects unknown routes and methods", async () => {
    const { deps } = makeDeps();
    expect((await route(postJson("/v1/nope", {}), makeEnv(), deps)).status).toBe(404);
    expect((await route(new Request("https://proxy.test/v1/chat"), makeEnv(), deps)).status).toBe(405);
  });
});
