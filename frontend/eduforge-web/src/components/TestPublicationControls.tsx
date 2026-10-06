"use client";
import { useEffect, useState } from "react";
import { request } from "../lib/api";
type Publication = { published: boolean; publishedAt: number | null };
export default function TestPublicationControls({ testId, disabled = false }: { testId: string; disabled?: boolean }) {
  const [state, setState] = useState<Publication | null>(null); const [busy, setBusy] = useState(false); const [confirm, setConfirm] = useState<"publish" | "remove" | null>(null);
  const [error, setError] = useState(""); const [message, setMessage] = useState(""); const [reload, setReload] = useState(0);
  useEffect(() => {
    let cancelled = false; setState(null); setError(""); setMessage(""); setConfirm(null);
    void request<Publication>(`/api/tests/${testId}/publication`).then(next => { if (!cancelled) setState(next); }).catch(ex => { if (!cancelled) setError(ex.message); });
    return () => { cancelled = true; };
  }, [testId, reload]);
  async function change() {
    if (!confirm) return; setBusy(true); setError(""); setMessage("");
    try {
      if (confirm === "remove") { await request(`/api/tests/${testId}/publication`, { method: "DELETE" }); setState({ published: false, publishedAt: null }); setMessage("Публикация снята. Личные копии других преподавателей сохранены."); }
      else { setState(await request<Publication>(`/api/tests/${testId}/publication`, { method: "POST" })); setMessage("Сохранённая версия опубликована в общей библиотеке."); }
      setConfirm(null);
    } catch (ex) { setError(ex instanceof Error ? ex.message : "Не удалось изменить публикацию."); } finally { setBusy(false); }
  }
  return <section className="publication-controls" aria-label="Публикация теста"><div><strong>Общая библиотека</strong><p className="muted">{state?.published ? `Опубликован ${new Date(state.publishedAt!).toLocaleDateString("ru-RU")}. Изменения нужно публиковать отдельно.` : "Публикуйте тест, чтобы другие преподаватели могли использовать его."}</p></div>{disabled && <p className="hint">Сохраните изменения перед публикацией.</p>}{state && <div className="actions"><button type="button" disabled={disabled || busy} onClick={() => setConfirm("publish")}>{state.published ? "Обновить публикацию" : "Опубликовать тест"}</button>{state.published && <button type="button" disabled={busy} onClick={() => setConfirm("remove")}>Снять публикацию</button>}</div>}{!state && !error && <p className="muted">Проверяем публикацию…</p>}{confirm && <div className="confirm-action" role="alertdialog" aria-label="Изменение публикации"><p>{confirm === "publish" ? "Сохранённый тест и ваше имя станут доступны преподавателям в общей библиотеке. Опубликовать?" : "Снять тест с общей библиотеки? Уже созданные личные копии останутся у преподавателей."}</p><button type="button" className="primary" disabled={busy || (confirm === "publish" && disabled)} onClick={change}>Подтвердить</button><button type="button" disabled={busy} onClick={() => setConfirm(null)}>Отмена</button></div>}{error && <p className="alert" role="alert">{error} {!state && <button type="button" onClick={() => setReload(value => value + 1)}>Повторить</button>}</p>}{message && <p role="status" className="hint">{message}</p>}</section>;
}
