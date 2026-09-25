import Anthropic from "@anthropic-ai/sdk";
import { describe, expect, it } from "vitest";
import { route } from "../src/index";
import { makeDeps, makeEnv, postJson } from "./helpers";

const conversation = { system: "You are Fieldmate.", messages: [{ role: "user", content: "Why is the pressure high?" }] };

function apiError(status: number): Error {
  return Object.assign(Object.create(Anthropic.APIError.prototype), { status, message: `upstream ${status}` });
}

describe("POST /v1/chat", () => {
  it("forwards the conversation with the pinned model, capped output and caching", async () => {
    const { deps, recorded } = makeDeps();
    const response = await route(postJson("/v1/chat", { ...conversation, max_tokens: 5000, tools: [] }), makeEnv(), deps);

    expect(response.status).toBe(200);
    const body = (await response.json()) as { content: Array<{ text: string }>; stop_reason: string };
    expect(body.content[0].text).toContain("[part.pump]");
    expect(body.stop_reason).toBe("end_turn");

    const params = recorded.chatParams[0] as Record<string, unknown>;
    expect(params.model).toBe("claude-haiku-4-5");
    expect(params.max_tokens).toBe(600);
    expect(params.cache_control).toEqual({ type: "ephemeral" });
    expect(params.system).toBe("You are Fieldmate.");
    expect(params).not.toHaveProperty("tools");
  });

  it("passes tool definitions and tool results through", async () => {
    const { deps, recorded } = makeDeps();
    const tools = [{ name: "highlight_part", description: "d", input_schema: { type: "object", properties: {} } }];
    const messages = [
      { role: "user", content: "Where is the motor?" },
      { role: "assistant", content: [{ type: "tool_use", id: "t1", name: "highlight_part", input: { part_id: "motor" } }] },
      { role: "user", content: [{ type: "tool_result", tool_use_id: "t1", content: "Highlighted." }] },
    ];

    const response = await route(postJson("/v1/chat", { messages, tools }), makeEnv(), deps);

    expect(response.status).toBe(200);
    expect((recorded.chatParams[0] as { tools: unknown }).tools).toEqual(tools);
    expect((recorded.chatParams[0] as { max_tokens: number }).max_tokens).toBe(400);
  });

  it.each([
    [{}, "'messages' must be a non-empty array"],
    [{ messages: [] }, "'messages' must be a non-empty array"],
    [{ messages: [{ role: "system", content: "x" }] }, "role must be user or assistant"],
    [{ messages: [{ role: "user", content: [{ type: "image" }] }] }, "unsupported block type 'image'"],
    [{ messages: [{ role: "user", content: "x" }], tools: [{ type: "web_search_20260209", name: "web_search", input_schema: {} }] }, "only custom tools"],
    [{ messages: [{ role: "user", content: "x" }], tools: [{ name: "x" }] }, "needs a name and an input_schema"],
    [{ messages: [{ role: "user", content: "x" }], max_tokens: 0 }, "positive integer"],
    [{ system: 5, messages: [{ role: "user", content: "x" }] }, "'system' must be a string"],
  ])("rejects invalid body %#", async (body, message) => {
    const { deps, recorded } = makeDeps();
    const response = await route(postJson("/v1/chat", body), makeEnv(), deps);

    expect(response.status).toBe(400);
    expect(((await response.json()) as { error: { message: string } }).error.message).toContain(message);
    expect(recorded.chatParams).toHaveLength(0);
  });

  it("rejects bodies that are not JSON or too large", async () => {
    const { deps } = makeDeps();
    expect((await route(postJson("/v1/chat", undefined), makeEnv(), deps)).status).toBe(400);
    const big = { messages: [{ role: "user", content: "x".repeat(70_000) }] };
    expect((await route(postJson("/v1/chat", big), makeEnv(), deps)).status).toBe(413);
  });

  it.each([
    [400, 400, "invalid_request"],
    [401, 500, "proxy_misconfigured"],
    [429, 429, "upstream_rate_limited"],
    [529, 502, "upstream_error"],
  ])("maps upstream %i to %i", async (upstream, status, type) => {
    const { deps } = makeDeps({ chat: () => { throw apiError(upstream); } });
    const response = await route(postJson("/v1/chat", conversation), makeEnv(), deps);

    expect(response.status).toBe(status);
    expect(((await response.json()) as { error: { type: string } }).error.type).toBe(type);
  });

  it("maps non-API failures to 502", async () => {
    const { deps } = makeDeps({ chat: () => { throw new TypeError("network"); } });
    expect((await route(postJson("/v1/chat", conversation), makeEnv(), deps)).status).toBe(502);
  });
});
