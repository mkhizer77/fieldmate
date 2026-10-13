import { describe, expect, it } from "vitest";
import { route } from "../src/index";
import { makeDeps, makeEnv, MemoryKv, postJson } from "./helpers";

const frame = { image: "/9j/4AAQSkZJRgABAQ==", media_type: "image/jpeg", prompt: "What part is at the centre? Answer with JSON only." };
const answer = '{"label":"relief valve","part_id":"relief_valve","confidence":0.82,"bbox":[0.4,0.3,0.6,0.5],"one_line_help":"Vents above 6 bar."}';

describe("POST /v1/vision", () => {
  it("sends one image and the prompt to the pinned model with a small output cap, and returns the text", async () => {
    const { deps, recorded } = makeDeps({ chat: () => ({ id: "m1", model: "claude-haiku-4-5", content: [{ type: "text", text: answer }], stop_reason: "end_turn", usage: {} }) });

    const response = await route(postJson("/v1/vision", frame), makeEnv(), deps);

    expect(response.status).toBe(200);
    expect(((await response.json()) as { text: string }).text).toBe(answer);
    const params = recorded.chatParams[0] as { model: string; max_tokens: number; messages: Array<{ content: Array<Record<string, unknown>> }> };
    expect(params.model).toBe("claude-haiku-4-5");
    expect(params.max_tokens).toBe(300);
    expect(params.messages).toHaveLength(1);
    expect(params.messages[0].content[0]).toEqual({ type: "image", source: { type: "base64", media_type: "image/jpeg", data: frame.image } });
    expect(params.messages[0].content[1]).toEqual({ type: "text", text: frame.prompt });
  });

  it("uses VISION_MODEL when set", async () => {
    const { deps, recorded } = makeDeps({ chat: () => ({ id: "m", model: "x", content: [], stop_reason: "end_turn", usage: {} }) });

    await route(postJson("/v1/vision", frame), makeEnv({ VISION_MODEL: "claude-sonnet-5-5" }), deps);

    expect((recorded.chatParams[0] as { model: string }).model).toBe("claude-sonnet-5-5");
  });

  it.each([
    [{ ...frame, image: "" }, "'image' must be a base64 string"],
    [{ ...frame, image: "not base64!" }, "not base64"],
    [{ ...frame, image: "A".repeat(240_004) }, "send at most 640 px"],
    [{ ...frame, media_type: "image/gif" }, "image/jpeg or image/png"],
    [{ ...frame, prompt: " " }, "'prompt' must be a non-empty string"],
    [{ ...frame, prompt: "x".repeat(4001) }, "over 4000 characters"],
  ])("rejects invalid body %#", async (body, message) => {
    const { deps, recorded } = makeDeps();
    const response = await route(postJson("/v1/vision", body), makeEnv(), deps);

    expect(response.status).toBe(400);
    expect(((await response.json()) as { error: { message: string } }).error.message).toContain(message);
    expect(recorded.chatParams).toHaveLength(0);
  });

  it("counts against its own daily cap, which the dev token skips", async () => {
    const usage = new MemoryKv();
    const env = makeEnv({ DAILY_VISION_CALLS: "1", USAGE: usage as unknown as KVNamespace });
    const ok = () => ({ id: "m", model: "x", content: [{ type: "text", text: answer }], stop_reason: "end_turn", usage: {} });

    expect((await route(postJson("/v1/vision", frame), env, makeDeps({ chat: ok }).deps)).status).toBe(200);
    const capped = await route(postJson("/v1/vision", frame), env, makeDeps({ chat: ok }).deps);
    expect(capped.status).toBe(429);
    expect(((await capped.json()) as { error: { type: string } }).error.type).toBe("daily_cap");

    const dev = postJson("/v1/vision", frame, { "x-fieldmate-dev-token": "dev-secret" });
    expect((await route(dev, env, makeDeps({ chat: ok }).deps)).status).toBe(200);
  });
});
