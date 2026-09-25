/** Bindings and settings of the Worker. Secrets are set with `wrangler secret put`, never committed. */
export interface Env {
  ANTHROPIC_API_KEY: string;
  DEEPGRAM_API_KEY: string;
  ELEVENLABS_API_KEY: string;
  /** Optional: requests carrying this token skip the rate limit and daily caps (Khizer's headset). */
  DEV_TOKEN?: string;

  CHAT_MODEL: string;
  MAX_OUTPUT_TOKENS: string;
  STT_MODEL: string;
  TTS_MODEL: string;
  VOICE_EN: string;
  VOICE_DE: string;
  DAILY_CHAT_CALLS: string;
  DAILY_STT_SECONDS: string;
  DAILY_TTS_CHARS: string;

  USAGE?: KVNamespace;
  RATE_LIMITER?: { limit(options: { key: string }): Promise<{ success: boolean }> };
}

export type Language = "en" | "de";

export function parseLanguage(value: string | null | undefined): Language | null {
  return value === "en" || value === "de" ? value : null;
}

export function intSetting(value: string | undefined, fallback: number): number {
  const parsed = Number.parseInt(value ?? "", 10);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : fallback;
}
