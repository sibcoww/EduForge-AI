"use client";
import { useEffect, useState } from "react";
import { api, request } from "../lib/api";

export type SourceSelection = { documentId: string; sectionIds: string[] };
type SourceSummary = { id: string; name: string; format: string; characterCount: number };
type SourceDetail = { id: string; name: string; sections: { id: string; label: string; text: string }[] };
export default function SourcePicker({ selected, onChange, disabled }: {
  selected: SourceSelection[]; onChange: (value: SourceSelection[]) => void; disabled: boolean;
}) {
  const [documents, setDocuments] = useState<SourceSummary[]>([]);
  const [details, setDetails] = useState<Record<string, SourceDetail>>({});
  const [opened, setOpened] = useState<string | null>(null);
  const [busy, setBusy] = useState(false); const [error, setError] = useState("");
  useEffect(() => { const controller = new AbortController(); void request<SourceSummary[]>("/api/sources", { signal: controller.signal }).then(setDocuments).catch(e => { if (!controller.signal.aborted) setError(e.message); }); return () => controller.abort(); }, []);
  const selectionKey = selected.map(s => s.documentId).sort().join(",");
  useEffect(() => {
    const controller = new AbortController();
    const missing = selected.filter(s => !details[s.documentId]);
    if (missing.length) void Promise.all(missing.map(s => request<SourceDetail>(`/api/sources/${s.documentId}`, { signal: controller.signal })))
      .then(values => setDetails(current => ({ ...current, ...Object.fromEntries(values.map(d => [d.id, d])) })))
      .catch(e => { if (!controller.signal.aborted) setError(e.message); });
    return () => controller.abort();
  }, [selectionKey]);
  async function show(id: string) {
    setOpened(id); setError("");
    if (details[id]) return;
    setBusy(true);
    try { const value = await request<SourceDetail>(`/api/sources/${id}`); setDetails(current => ({ ...current, [id]: value })); }
    catch (e) { setError(e instanceof Error ? e.message : "Не удалось загрузить фрагменты."); }
    finally { setBusy(false); }
  }
  async function upload(file: File) {
    if (file.size > 10 * 1024 * 1024) { setError("Размер источника — до 10 МБ."); return; }
    setBusy(true); setError("");
    try {
      const data = new FormData(); data.append("file", file);
      const response = await fetch(api + "/api/sources", { method: "POST", credentials: "include", body: data });
      const result = await response.json();
      if (!response.ok) throw new Error(result.message ?? result.detail ?? "Не удалось загрузить источник.");
      setDocuments(current => [result, ...current.filter(d => d.id !== result.id)]);
      const full = await request<SourceDetail>(`/api/sources/${result.id}`);
      setDetails(current => ({ ...current, [full.id]: full })); setOpened(full.id);
    } catch (e) { setError(e instanceof Error ? e.message : "Нет соединения с сервером."); }
    finally { setBusy(false); }
  }
  function select(documentId: string, sectionIds: string[]) {
    const next = [...selected.filter(s => s.documentId !== documentId), ...(sectionIds.length ? [{ documentId, sectionIds }] : [])];
    if (next.length > 8 || next.reduce((n, s) => n + s.sectionIds.length, 0) > 60) { setError("Выберите до 8 источников и 60 фрагментов."); return; }
    onChange(next); setError("");
  }
  const characterCount = selected.reduce((sum, s) => sum + (details[s.documentId]?.sections.filter(p => s.sectionIds.includes(p.id)).reduce((n, p) => n + p.text.length, 0) ?? 0), 0);
  return <details className="learning-panel source-picker"><summary>Источники для генерации {selected.length > 0 && `· выбрано ${selected.length}`}</summary>
    <p className="hint">PDF с текстовым слоем, DOCX, TXT или Markdown в UTF-8, до 10 МБ. Выберите нужные страницы или фрагменты. При генерации выбранный текст отправляется AI-провайдеру.</p>
    <label className="upload-button">{busy ? "Читаем источник…" : "+ Загрузить источник"}<input type="file" aria-label="Загрузить учебный источник" accept=".pdf,.docx,.txt,.md" disabled={disabled || busy} onChange={e => { const file = e.target.files?.[0]; if (file) void upload(file); e.target.value = ""; }}/></label>
    {error && <p role="alert" className="alert">{error}</p>}
    {documents.length === 0 && <p>Источники ещё не загружены. Без выбранных фрагментов материал создаётся по теме и требованиям.</p>}
    {documents.map(d => <div className="source-document" key={d.id}>
      <button type="button" disabled={disabled || busy} onClick={() => opened === d.id ? setOpened(null) : void show(d.id)}>{d.name} · {d.characterCount.toLocaleString("ru-RU")} символов{selected.some(s => s.documentId === d.id) ? " · выбран" : ""}</button>
      {opened === d.id && details[d.id] && <div>
        <div className="learning-actions"><button type="button" disabled={disabled || busy} onClick={() => select(d.id, details[d.id].sections.map(p => p.id))}>Выбрать все фрагменты</button><button type="button" disabled={disabled || busy} onClick={() => select(d.id, [])}>Снять выбор</button></div>
        <div className="source-section-list">{details[d.id].sections.map(section => <div key={section.id}><label className="check-label"><input type="checkbox" disabled={disabled || busy} checked={selected.find(s => s.documentId === d.id)?.sectionIds.includes(section.id) ?? false} onChange={e => { const ids = selected.find(s => s.documentId === d.id)?.sectionIds ?? []; select(d.id, e.target.checked ? [...ids, section.id] : ids.filter(id => id !== section.id)); }}/>{section.label}</label><pre className="source-text">{section.text}</pre></div>)}</div>
      </div>}
    </div>)}
    {selected.length > 0 && <p className={characterCount > 50000 ? "learning-warning" : "hint"}>Выбрано {selected.reduce((n, s) => n + s.sectionIds.length, 0)} фрагментов · {characterCount.toLocaleString("ru-RU")} из 50 000 символов. {characterCount > 50000 && "Уменьшите объём перед генерацией."}</p>}
  </details>;
}
