import type { Env } from "./env";
import { intSetting } from "./env";
import { HttpError } from "./http";

export type UsageKind = "chat" | "stt" | "tts";

/** Constant-time comparison so the dev token can't be guessed byte by byte from response timing. */
export function tokensMatch(a: string, b: string): boolean {
  const left = new TextEncoder().encode(a);
  const right = new TextEncoder().encode(b);
  let diff = left.length ^ right.length;
  for (let i = 0; i < Math.max(left.length, right.length); i++) {
    diff |= (left[i] ?? 0) ^ (right[i] ?? 0);
  }
  return diff === 0;
}

export function isDev(request: Request, env: Env): boolean {
  const token = request.headers.get("x-fieldmate-dev-token");
  return !!env.DEV_TOKEN && !!token && tokensMatch(token, env.DEV_TOKEN);
}

/** Per-IP rate limit (skipped for the dev token or when the binding is missing, e.g. in tests). */
export async function checkRateLimit(request: Request, env: Env, dev: boolean): Promise<void> {
  if (dev || !env.RATE_LIMITER) {
    return;
  }

  const ip = request.headers.get("cf-connecting-ip") ?? "unknown";
  const { success } = await env.RATE_LIMITER.limit({ key: ip });
  if (!success) {
    throw new HttpError(429, "rate_limited", "Too many requests. Wait a minute and try again.");
  }
}

function capFor(kind: UsageKind, env: Env): number {
  switch (kind) {
    case "chat":
      return intSetting(env.DAILY_CHAT_CALLS, 300);
    case "stt":
      return intSetting(env.DAILY_STT_SECONDS, 1800);
    case "tts":
      return intSetting(env.DAILY_TTS_CHARS, 1500);
  }
}

function usageKey(kind: UsageKind, now: Date): string {
  return `usage:${now.toISOString().slice(0, 10)}:${kind}`;
}

/**
 * Daily cap across all public users (UTC day). KV is eventually consistent, so the cap is approximate; it exists to
 * stop runaway cost, not to meter precisely. The dev token is exempt.
 */
export async function reserveUsage(env: Env, kind: UsageKind, amount: number, dev: boolean, now = new Date()): Promise<void> {
  if (dev || !env.USAGE || amount <= 0) {
    return;
  }

  const key = usageKey(kind, now);
  const used = Number((await env.USAGE.get(key)) ?? "0");
  if (used + amount > capFor(kind, env)) {
    throw new HttpError(429, "daily_cap", `The public demo's daily ${kind} budget is used up. Try again tomorrow.`);
  }

  await env.USAGE.put(key, String(used + amount), { expirationTtl: 60 * 60 * 48 });
}
