import type { Env } from "./env";
import { createAnthropic, handleChat, type MessagesClient } from "./chat";
import { checkRateLimit, isDev, reserveUsage } from "./guard";
import { errorResponse, HttpError } from "./http";
import { handleStt } from "./stt";
import { handleTts } from "./tts";

/** Injected so tests can replace the upstream clients. */
export interface Deps {
  fetch: typeof fetch;
  chatClient: (env: Env) => MessagesClient;
}

const defaultDeps: Deps = { fetch: (input, init) => fetch(input, init), chatClient: createAnthropic };

export async function route(request: Request, env: Env, deps: Deps = defaultDeps): Promise<Response> {
  const { pathname } = new URL(request.url);
  try {
    if (request.method === "GET" && pathname === "/v1/health") {
      return Response.json({ ok: true, chatModel: env.CHAT_MODEL, sttModel: env.STT_MODEL, ttsModel: env.TTS_MODEL });
    }

    if (request.method !== "POST") {
      return errorResponse(405, "method_not_allowed", "Use POST.");
    }

    const dev = isDev(request, env);
    switch (pathname) {
      case "/v1/chat":
        await checkRateLimit(request, env, dev);
        await reserveUsage(env, "chat", 1, dev);
        return await handleChat(request, env, deps.chatClient(env));
      case "/v1/stt":
        await checkRateLimit(request, env, dev);
        return await handleStt(request, env, dev, deps.fetch);
      case "/v1/tts":
        await checkRateLimit(request, env, dev);
        return await handleTts(request, env, dev, deps.fetch);
      default:
        return errorResponse(404, "not_found", "Unknown route.");
    }
  } catch (error) {
    if (error instanceof HttpError) {
      return error.toResponse();
    }

    console.error("proxy error", error);
    return errorResponse(500, "internal_error", "Unexpected proxy error.");
  }
}

export default {
  fetch: (request: Request, env: Env) => route(request, env),
} satisfies ExportedHandler<Env>;
