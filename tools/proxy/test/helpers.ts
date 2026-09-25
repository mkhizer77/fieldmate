import type { Env } from "../src/env";
import type { Deps } from "../src/index";

export class MemoryKv {
  store = new Map<string, string>();
  async get(key: string) {
    return this.store.get(key) ?? null;
  }
  async put(key: string, value: string) {
    this.store.set(key, value);
  }
}

export function makeEnv(overrides: Partial<Env> = {}): Env {
  return {
    ANTHROPIC_API_KEY: "sk-test",
    DEEPGRAM_API_KEY: "dg-test",
    ELEVENLABS_API_KEY: "el-test",
    DEV_TOKEN: "dev-secret",
    CHAT_MODEL: "claude-haiku-4-5",
    MAX_OUTPUT_TOKENS: "600",
    STT_MODEL: "nova-3",
    TTS_MODEL: "eleven_flash_v2_5",
    VOICE_EN: "voice-en",
    VOICE_DE: "voice-de",
    DAILY_CHAT_CALLS: "300",
    DAILY_STT_SECONDS: "1800",
    DAILY_TTS_CHARS: "1500",
    USAGE: new MemoryKv() as unknown as KVNamespace,
    ...overrides,
  };
}

export interface Recorded {
  chatParams: unknown[];
  fetches: Array<{ url: string; init?: RequestInit }>;
}

export function makeDeps(options: {
  chat?: (params: unknown) => unknown;
  fetch?: (url: string, init?: RequestInit) => Response | Promise<Response>;
} = {}): { deps: Deps; recorded: Recorded } {
  const recorded: Recorded = { chatParams: [], fetches: [] };
  const deps: Deps = {
    fetch: (async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      recorded.fetches.push({ url, init });
      if (!options.fetch) throw new Error("unexpected fetch");
      return options.fetch(url, init);
    }) as typeof fetch,
    chatClient: () =>
      ({
        messages: {
          create: async (params: unknown) => {
            recorded.chatParams.push(params);
            return (options.chat ?? defaultChat)(params);
          },
        },
      }) as never,
  };
  return { deps, recorded };
}

function defaultChat() {
  return {
    id: "msg_1",
    model: "claude-haiku-4-5",
    content: [{ type: "text", text: "Hello from the manual [part.pump]." }],
    stop_reason: "end_turn",
    usage: { input_tokens: 10, output_tokens: 5 },
  };
}

export function post(path: string, body: BodyInit, headers: Record<string, string> = {}): Request {
  return new Request(`https://proxy.test${path}`, { method: "POST", body, headers });
}

export function postJson(path: string, value: unknown, headers: Record<string, string> = {}): Request {
  return post(path, JSON.stringify(value), { "content-type": "application/json", ...headers });
}

/** Minimal 16 kHz 16-bit mono WAV of the given length. */
export function wav(seconds: number): Uint8Array {
  const dataBytes = Math.round(seconds * 32_000);
  const bytes = new Uint8Array(44 + dataBytes);
  const view = new DataView(bytes.buffer);
  view.setUint32(0, 0x52494646, false); // RIFF
  view.setUint32(28, 32_000, true); // byte rate
  return bytes;
}
