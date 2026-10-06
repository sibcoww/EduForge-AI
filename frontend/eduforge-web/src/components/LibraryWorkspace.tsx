"use client";
import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { request } from "../lib/api";
type Publication = { id: string; title: string; subject: string; level: string; language: string; authorName: string; publishedAt: number; isOwn: boolean };
type Detail = Publication & { content: string };
type Page = { items: Publication[]; total: number; page: number; pageSize: number };
export default function LibraryWorkspace() {
  const router = useRouter(); const [kind, setKind] = useState<"materials" | "tests">("materials"); const endpoint = kind === "tests" ? "/api/library/tests" : "/api/library"; const [q, setQ] = useState(""); const [subject, setSubject] = useState(""); const [language, setLanguage] = useState(""); const [mine, setMine] = useState(false); const [page, setPage] = useState(1);
  const [list, setList] = useState<Page | null>(null); const [filters, setFilters] = useState<{ subjects: string[]; languages: string[] }>({ subjects: [], languages: [] });
  const [selected, setSelected] = useState(""); const [detail, setDetail] = useState<Detail | null>(null); const [loading, setLoading] = useState(true); const [busy, setBusy] = useState(false); const [error, setError] = useState(""); const [detailError, setDetailError] = useState(""); const [reload, setReload] = useState(0);
  useEffect(() => { let cancelled = false; void request<typeof filters>(`${endpoint}/filters`).then(next => { if (!cancelled) setFilters(next); }).catch(() => {}); return () => { cancelled = true; }; }, [reload, endpoint]);
  useEffect(() => {
    let cancelled = false; setLoading(true); setError("");
    const timer = setTimeout(() => {
      const params = new URLSearchParams({ q, subject, language, mine: String(mine), page: String(page) });
      void request<Page>(`${endpoint}?${params}`).then(next => { if (!cancelled) setList(next); }).catch(ex => { if (!cancelled) setError(ex.message); }).finally(() => { if (!cancelled) setLoading(false); });
    }, 250);
    return () => { cancelled = true; clearTimeout(timer); };
  }, [q, subject, language, mine, page, reload, endpoint]);
  useEffect(() => {
    let cancelled = false; setDetail(null); setDetailError(""); if (!selected) return;
    void request<Detail>(`${endpoint}/${selected}`).then(next => { if (!cancelled) setDetail(next); }).catch(ex => { if (!cancelled) setDetailError(ex.message); });
    return () => { cancelled = true; };
  }, [selected, reload, endpoint]);
  async function copy() {
    if (!detail) return; setBusy(true); setDetailError("");
    try { const next = await request<{ id: string }>(`${endpoint}/${detail.id}/copy`, { method: "POST" }); router.push(kind === "tests" ? `/tests?testId=${next.id}` : `/?materialId=${next.id}`); }
    catch (ex) { setDetailError(ex instanceof Error ? ex.message : "Не удалось создать копию."); } finally { setBusy(false); }
  }
  return <section className="teaching-page"><div className="page-heading"><div><p className="eyebrow">БИБЛИОТЕКА СООБЩЕСТВА</p><h1>Общая библиотека</h1><p>Находите материалы и тесты коллег, добавляйте их себе и адаптируйте к занятиям.</p></div></div>
    <div className="actions" style={{marginBottom:16}}><button className={kind === "materials" ? "primary" : ""} onClick={() => {setKind("materials");setSelected("");setPage(1);setSubject("");setLanguage("");}}>Материалы</button><button className={kind === "tests" ? "primary" : ""} onClick={() => {setKind("tests");setSelected("");setPage(1);setSubject("");setLanguage("");}}>Тесты</button></div><div className="library-filters"><label>Поиск<input maxLength={200} placeholder="Название, предмет или автор" value={q} onChange={e => { setQ(e.target.value); setPage(1); }}/></label><label>Предмет<select value={subject} onChange={e => { setSubject(e.target.value); setPage(1); }}><option value="">Все предметы</option>{filters.subjects.map(value => <option key={value}>{value}</option>)}</select></label><label>Язык<select value={language} onChange={e => { setLanguage(e.target.value); setPage(1); }}><option value="">Все языки</option>{filters.languages.map(value => <option key={value}>{value}</option>)}</select></label><label className="check-label"><input type="checkbox" checked={mine} onChange={e => { setMine(e.target.checked); setPage(1); }}/>Мои публикации</label><button onClick={() => setReload(value => value + 1)}>Обновить</button></div>
    {error && <p className="alert" role="alert">{error}</p>}<div className="teaching-columns"><section className="library"><h2>{kind === "tests" ? "Тесты" : "Материалы"} <span>{list?.total ?? 0}</span></h2>{loading ? <p className="muted">Загрузка…</p> : list?.items.length ? list.items.map(item => <button className={`material-card ${selected === item.id ? "selected" : ""}`} key={item.id} onClick={() => setSelected(item.id)}><span className="tag">{item.subject}</span><strong>{item.title}</strong><small>{item.level} · {item.language}</small><small>{item.authorName}{item.isOwn && " · Ваша публикация"}</small></button>) : <p className="empty">{q || subject || language || mine ? "По выбранным условиям ничего не найдено." : kind === "tests" ? "Пока нет опубликованных тестов. Откройте сохранённый тест и нажмите «Опубликовать тест»." : "Пока нет опубликованных материалов. Откройте сохранённый материал и нажмите «Опубликовать материал»."}</p>}{list && list.total > list.pageSize && <div className="pagination"><button disabled={loading || page === 1} onClick={() => setPage(old => old - 1)}>Назад</button><span>{page} / {Math.ceil(list.total / list.pageSize)}</span><button disabled={loading || page * list.pageSize >= list.total} onClick={() => setPage(old => old + 1)}>Далее</button></div>}</section>
      <section className="publication-preview">{detailError && <p role="alert" className="alert">{detailError}</p>}{detail ? <><span className="tag">{detail.subject}</span><h2>{detail.title}</h2><p className="muted">{detail.level} · {detail.language}<br/>Автор: {detail.authorName} · {new Date(detail.publishedAt).toLocaleDateString("ru-RU")}</p><div className="actions"><button className="primary" disabled={busy} onClick={copy}>{busy ? "Создаём копию…" : kind === "tests" ? "Добавить тест себе" : "Добавить материал и опубликованные тесты"}</button>{detail.isOwn && <button onClick={() => router.push(kind === "tests" ? `/tests?testId=${detail.id}` : `/?materialId=${detail.id}`)}>Открыть оригинал</button>}</div><div className="publication-text">{detail.content}</div></> : <p className="empty">{detailError ? "Публикация недоступна. Обновите библиотеку или выберите другой материал." : selected ? "Открываем материал…" : kind === "tests" ? "Выберите тест для просмотра." : "Выберите материал для просмотра."}</p>}</section></div>
  </section>;
}
