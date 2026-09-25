/** JSON error body the Unity client understands: { error: { type, message } }. */
export function errorResponse(status: number, type: string, message: string): Response {
  return Response.json({ error: { type, message } }, { status });
}

export async function readJson(request: Request, maxBytes: number): Promise<unknown> {
  const text = await readText(request, maxBytes);
  try {
    return JSON.parse(text);
  } catch {
    throw new HttpError(400, "invalid_request", "Body is not valid JSON.");
  }
}

export async function readText(request: Request, maxBytes: number): Promise<string> {
  return new TextDecoder().decode(await readBytes(request, maxBytes));
}

export async function readBytes(request: Request, maxBytes: number): Promise<Uint8Array> {
  const declared = Number(request.headers.get("content-length") ?? "0");
  if (declared > maxBytes) {
    throw new HttpError(413, "too_large", `Body over ${maxBytes} bytes.`);
  }

  const bytes = new Uint8Array(await request.arrayBuffer());
  if (bytes.byteLength > maxBytes) {
    throw new HttpError(413, "too_large", `Body over ${maxBytes} bytes.`);
  }

  return bytes;
}

export class HttpError extends Error {
  constructor(readonly status: number, readonly type: string, message: string) {
    super(message);
  }

  toResponse(): Response {
    return errorResponse(this.status, this.type, this.message);
  }
}
