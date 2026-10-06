export const api = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5080";
export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message); }
}
export async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  let response: Response;
  try { response = await fetch(`${api}${path}`, { ...options, credentials: "include", headers: { "Content-Type": "application/json", ...options.headers } }); }
  catch (ex) { if (ex instanceof DOMException && ex.name === "AbortError") throw ex; throw new ApiError(0, "Нет соединения с сервером. Проверьте подключение и повторите попытку."); }
  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    const fallback = response.status === 401 ? "Сессия завершена. Войдите снова." : response.status === 429 ? "Слишком много запросов. Попробуйте через минуту." : "Не удалось выполнить запрос.";
    throw new ApiError(response.status, body.message ?? body.detail ?? (body.errors ? Object.values(body.errors).flat().join(" ") : fallback));
  }
  return response.status === 204 ? undefined as T : await response.json();
}
