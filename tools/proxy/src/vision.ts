import type { Env } from "./env";
import { intSetting } from "./env";
import { toHttpError, type MessagesClient } from "./chat";
import { HttpError, readJson } from "./http";

/** A 640 px JPEG is 40–90 KB, about 120 KB as base64; the cap leaves room without inviting large uploads. */
export const MAX_VISION_BYTES = 256 * 1024;
const MAX_IMAGE_BASE64 = 240_000;
const MAX_PROMPT_CHARS = 4_000;
const VISION_OUTPUT_TOKENS = 300;
const MEDIA_TYPES = new Set(["image/jpeg", "image/png"]);

/** One camera frame and the app's question about it. The model and output cap are the proxy's decision. */
export interface VisionBody {
  image: string;
  media_type: "image/jpeg" | "image/png";
  prompt: string;
}

export function validateVisionBody(value: unknown): VisionBody {
  const fail = (message: string): never => {
    throw new HttpError(400, "invalid_request", message);
  };

  if (typeof value !== "object" || value === null) fail("Body must be a JSON object.");
  const body = value as Record<string, unknown>;

  if (typeof body.image !== "string" || body.image.length === 0) fail("'image' must be a base64 string.");
  if ((body.image as string).length > MAX_IMAGE_BASE64) fail(`'image' is over ${MAX_IMAGE_BASE64} base64 characters; send at most 640 px.`);
  if (!/^[A-Za-z0-9+/]+={0,2}$/.test(body.image as string)) fail("'image' is not base64.");
  if (!MEDIA_TYPES.has(String(body.media_type))) fail("'media_type' must be image/jpeg or image/png.");
  if (typeof body.prompt !== "string" || body.prompt.trim().length === 0) fail("'prompt' must be a non-empty string.");
  if ((body.prompt as string).length > MAX_PROMPT_CHARS) fail(`'prompt' is over ${MAX_PROMPT_CHARS} characters.`);

  return body as unknown as VisionBody;
}

/** Asks the vision model about one frame (design.md §5.5 step 3) and returns its text; the app parses the JSON. */
export async function handleVision(request: Request, env: Env, client: MessagesClient): Promise<Response> {
  const body = validateVisionBody(await readJson(request, MAX_VISION_BYTES));

  try {
    const response = await client.messages.create({
      model: env.VISION_MODEL || env.CHAT_MODEL,
      max_tokens: Math.min(VISION_OUTPUT_TOKENS, intSetting(env.MAX_OUTPUT_TOKENS, 600)),
      messages: [
        {
          role: "user",
          content: [
            { type: "image", source: { type: "base64", media_type: body.media_type, data: body.image } },
            { type: "text", text: body.prompt },
          ],
        },
      ],
    });

    const text = response.content
      .filter((block): block is Extract<typeof block, { type: "text" }> => block.type === "text")
      .map((block) => block.text)
      .join("");
    return Response.json({ id: response.id, model: response.model, text, stop_reason: response.stop_reason, usage: response.usage });
  } catch (error) {
    throw toHttpError(error);
  }
}
