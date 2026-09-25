import Anthropic from "@anthropic-ai/sdk";
import type { Env } from "./env";
import { intSetting } from "./env";
import { HttpError, readJson } from "./http";

export const MAX_CHAT_BYTES = 64 * 1024;
const MAX_MESSAGES = 40;
const MAX_TOOLS = 16;
const MAX_SYSTEM_CHARS = 12_000;
const ALLOWED_BLOCKS = new Set(["text", "tool_use", "tool_result"]);

/** What the app sends: the conversation it owns. The model, output cap and caching are the proxy's decision. */
export interface ChatBody {
  system?: string;
  messages: Anthropic.MessageParam[];
  tools?: Anthropic.Tool[];
  max_tokens?: number;
}

export type MessagesClient = Pick<Anthropic, "messages">;

export function createAnthropic(env: Env): MessagesClient {
  // One SDK retry for 429/5xx/network errors; the app adds one more only on network failure.
  return new Anthropic({ apiKey: env.ANTHROPIC_API_KEY, maxRetries: 1, timeout: 15_000 });
}

export function validateChatBody(value: unknown): ChatBody {
  const fail = (message: string): never => {
    throw new HttpError(400, "invalid_request", message);
  };

  if (typeof value !== "object" || value === null) fail("Body must be a JSON object.");
  const body = value as Record<string, unknown>;

  if (body.system !== undefined && typeof body.system !== "string") fail("'system' must be a string.");
  if (typeof body.system === "string" && body.system.length > MAX_SYSTEM_CHARS) fail(`'system' is over ${MAX_SYSTEM_CHARS} characters.`);

  const messages = body.messages;
  if (!Array.isArray(messages) || messages.length === 0) fail("'messages' must be a non-empty array.");
  if ((messages as unknown[]).length > MAX_MESSAGES) fail(`At most ${MAX_MESSAGES} messages.`);
  for (const [i, message] of (messages as unknown[]).entries()) {
    const m = message as Record<string, unknown>;
    if (m?.role !== "user" && m?.role !== "assistant") fail(`messages[${i}].role must be user or assistant.`);
    if (typeof m.content === "string") continue;
    if (!Array.isArray(m.content)) fail(`messages[${i}].content must be a string or an array of blocks.`);
    for (const block of m.content as Array<Record<string, unknown>>) {
      if (!ALLOWED_BLOCKS.has(String(block?.type))) fail(`messages[${i}] has an unsupported block type '${String(block?.type)}'.`);
    }
  }

  if (body.tools !== undefined) {
    if (!Array.isArray(body.tools) || body.tools.length > MAX_TOOLS) fail(`'tools' must be an array of at most ${MAX_TOOLS}.`);
    for (const [i, tool] of (body.tools as Array<Record<string, unknown>>).entries()) {
      if (typeof tool?.name !== "string" || typeof tool?.input_schema !== "object" || tool.input_schema === null) {
        fail(`tools[${i}] needs a name and an input_schema.`);
      }
      if ("type" in tool) fail(`tools[${i}]: only custom tools are allowed.`);
    }
  }

  if (body.max_tokens !== undefined && (!Number.isInteger(body.max_tokens) || (body.max_tokens as number) < 1)) {
    fail("'max_tokens' must be a positive integer.");
  }

  return body as unknown as ChatBody;
}

export async function handleChat(request: Request, env: Env, client: MessagesClient): Promise<Response> {
  const body = validateChatBody(await readJson(request, MAX_CHAT_BYTES));
  const maxTokens = Math.min(body.max_tokens ?? 400, intSetting(env.MAX_OUTPUT_TOKENS, 600));

  try {
    const response = await client.messages.create({
      model: env.CHAT_MODEL,
      max_tokens: maxTokens,
      // Automatic prompt caching: the system prompt and tool list are identical across a session.
      cache_control: { type: "ephemeral" },
      ...(body.system ? { system: body.system } : {}),
      ...(body.tools?.length ? { tools: body.tools } : {}),
      messages: body.messages,
    });

    return Response.json({
      id: response.id,
      model: response.model,
      content: response.content,
      stop_reason: response.stop_reason,
      usage: response.usage,
    });
  } catch (error) {
    throw toHttpError(error);
  }
}

/** Maps SDK errors to responses the app can act on without leaking provider details. */
export function toHttpError(error: unknown): HttpError {
  if (error instanceof Anthropic.APIError) {
    const status = error.status ?? 0;
    if (status === 400) return new HttpError(400, "invalid_request", error.message);
    if (status === 401 || status === 403) return new HttpError(500, "proxy_misconfigured", "The proxy's chat key was rejected.");
    if (status === 429) return new HttpError(429, "upstream_rate_limited", "The chat model is busy. Try again shortly.");
    return new HttpError(502, "upstream_error", `Chat model error (${status || "network"}).`);
  }

  return new HttpError(502, "upstream_error", "Chat model unreachable.");
}
