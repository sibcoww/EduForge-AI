"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError, request } from "../lib/api";
import { type Answer, type Attempt, type AttemptCredentials, type SessionStatus } from "../lib/sessions";
import { Countdown, useSessionWatch } from "./SessionLive";

type Lobby = { title: string; groupName: string; status: SessionStatus; students: { studentId: string; name: string; joined: boolean }[] };
export default function StudentWorkspace({ initialCode = "" }: { initialCode?: string }) {
  const [code, setCode] = useState(initialCode.toUpperCase()); const [loadedCode, setLoadedCode] = useState(""); const [lobby, setLobby] = useState<Lobby | null>(null);
  const [credentials, setCredentials] = useState<AttemptCredentials | null>(null); const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  const load = useCallback(async (value: string) => {
    const normal = value.trim().toUpperCase(); if (!/^[A-Z2-9]{8}$/.test(normal)) { setError("Введите код из 8 символов."); return; }
    setBusy(true); setError(""); setLobby(null);
    try {
      const stored = sessionStorage.getItem(`eduforge-attempt-${normal}`);
      if (stored) { const parsed: AttemptCredentials = JSON.parse(stored); if (parsed.sessionId && parsed.token) { setCredentials(parsed); setLoadedCode(normal); return; } }
      setLobby(await request<Lobby>(`/api/join/${normal}`)); setLoadedCode(normal);
    } catch (ex) { setError(ex instanceof ApiError && ex.status === 404 ? "Сессия с таким кодом не найдена." : ex instanceof Error ? ex.message : "Не удалось открыть сессию."); }
    finally { setBusy(false); }
  }, []);
  useEffect(() => { if (initialCode) void load(initialCode); }, [initialCode, load]);
  async function join(studentId: string) {
    setBusy(true); setError("");
    try { const next = await request<AttemptCredentials>(`/api/join/${loadedCode}`, { method: "POST", body: JSON.stringify({ studentId }) }); sessionStorage.setItem(`eduforge-attempt-${loadedCode}`, JSON.stringify(next)); setCredentials(next); }
    catch (ex) { setError(ex instanceof Error ? ex.message : "Не удалось присоединиться."); } finally { setBusy(false); }
  }
  return <main className="student-page"><header><span className="brand">✦ EduForge <span>AI</span></span><span>Тестирование</span></header>{credentials ? <StudentAttempt credentials={credentials}/> : <section className="student-lobby"><p className="eyebrow">БЕЗ РЕГИСТРАЦИИ</p><h1>Присоединиться к тесту</h1><form onSubmit={e => { e.preventDefault(); void load(code); }}><label>Код подключения<input autoComplete="off" maxLength={8} placeholder="A7K2P9Q4" value={code} onChange={e => setCode(e.target.value.toUpperCase())}/></label><button className="primary" disabled={busy}>Найти сессию</button></form>{error && <p className="alert" role="alert">{error}</p>}{lobby && <><h2>{lobby.title}</h2><p>{lobby.groupName}</p><h3>Выберите себя</h3><p className="muted">Выбирайте только своё имя. После подключения продолжайте тест в этой вкладке.</p><div className="student-list">{lobby.students.map(s => <button key={s.studentId} disabled={busy || s.joined} onClick={() => join(s.studentId)}>{s.name}{s.joined && <small>Уже присоединился</small>}</button>)}</div></>}</section>}</main>;
}

function StudentAttempt({ credentials }: { credentials: AttemptCredentials }) {
  const [attempt, setAttempt] = useState<Attempt | null>(null); const [answers, setAnswers] = useState<Record<string, Answer>>({});
  const [error, setError] = useState(""); const [saveError, setSaveError] = useState(""); const [pendingCount, setPendingCount] = useState(0); const [busy, setBusy] = useState(false);
  const pending = useRef(new Map<string, Answer>()); const running = useRef<Promise<void> | null>(null); const debounce = useRef<ReturnType<typeof setTimeout> | null>(null);
  const initialized = useRef(false); const mounted = useRef(true); const generation = useRef(0); const current = useRef<Attempt | null>(null);
  const headers = { "X-Attempt-Token": credentials.token };
  const [confirmSubmit, setConfirmSubmit] = useState(false);
  const refresh = useCallback(async () => {
    const version = ++generation.current;
    try {
      const next = await request<Attempt>(`/api/attempts/${credentials.sessionId}`, { headers: { "X-Attempt-Token": credentials.token } });
      if (!mounted.current || version !== generation.current) return;
      current.current = next; setAttempt(next); setError("");
      if (!initialized.current || next.submittedAt !== null) {
        setAnswers(Object.fromEntries(next.answers.map(a => [a.questionId, a]))); initialized.current = true;
      }
      if (next.submittedAt !== null || next.status === "Finished") {
        if (pending.current.size) setSaveError("Приём ответов завершён. Учтены ответы, которые успели сохраниться на сервере.");
        pending.current.clear(); setPendingCount(0);
      }
    } catch (ex) { if (mounted.current && version === generation.current) setError(ex instanceof Error ? ex.message : "Не удалось обновить тест."); }
  }, [credentials.sessionId, credentials.token]);
  useEffect(() => { mounted.current = true; void refresh(); return () => { mounted.current = false; generation.current++; if (debounce.current) clearTimeout(debounce.current); }; }, [refresh]);
  useSessionWatch(credentials.sessionId, credentials.token, refresh);
  useEffect(() => {
    if (attempt?.status !== "Active" || attempt.submittedAt !== null) return;
    const log = (kind: string) => { void request(`/api/attempts/${credentials.sessionId}/activity`, { method: "POST", headers: { "X-Attempt-Token": credentials.token }, body: JSON.stringify({ kind }), keepalive: true }).catch(() => {}); };
    const visibility = () => log(document.hidden ? "Hidden" : "Visible"); const blur = () => log("Blur"); const focus = () => log("Focus");
    const fullscreen = () => log(document.fullscreenElement ? "FullscreenEntered" : "FullscreenExited");
    document.addEventListener("visibilitychange", visibility); window.addEventListener("blur", blur); window.addEventListener("focus", focus); document.addEventListener("fullscreenchange", fullscreen);
    return () => { document.removeEventListener("visibilitychange", visibility); window.removeEventListener("blur", blur); window.removeEventListener("focus", focus); document.removeEventListener("fullscreenchange", fullscreen); };
  }, [attempt?.status, attempt?.submittedAt, credentials.sessionId, credentials.token]);
  useEffect(() => {
    if (!pendingCount) return;
    const warn = (event: BeforeUnloadEvent) => event.preventDefault(); window.addEventListener("beforeunload", warn); return () => window.removeEventListener("beforeunload", warn);
  }, [pendingCount]);

  // Serialize answer writes; replace queued values with their latest revision. On failure
  // keep the queue intact, show an explicit unsaved state and allow retry before submission.
  function flush(): Promise<void> {
    if (debounce.current) clearTimeout(debounce.current);
    if (running.current) return running.current;
    const save = async () => {
      while (pending.current.size && mounted.current) {
        const [id, answer] = pending.current.entries().next().value!;
        try {
          let saved = false;
          for (let retry = 0; retry < 3 && !saved; retry++) {
            try { await request(`/api/attempts/${credentials.sessionId}/answers/${id}`, { method: "PUT", headers, body: JSON.stringify({ optionIds: answer.optionIds, text: answer.text }) }); saved = true; }
            catch (ex) {
              if (!(ex instanceof ApiError && ex.status === 409)) throw ex;
              await refresh(); if (current.current?.status !== "Active" || current.current.submittedAt !== null) throw ex;
              if (retry === 2) throw ex;
            }
          }
          if (pending.current.get(id) === answer) pending.current.delete(id);
          if (mounted.current) { setPendingCount(pending.current.size); setSaveError(""); }
        } catch (ex) { if (mounted.current) setSaveError(ex instanceof Error ? ex.message : "Ответ не сохранён. Повторите сохранение."); throw ex; }
      }
    };
    running.current = save().finally(() => { running.current = null; }); return running.current;
  }
  function change(answer: Answer) {
    setAnswers(old => ({ ...old, [answer.questionId]: answer })); pending.current.set(answer.questionId, answer); setPendingCount(pending.current.size);
    if (debounce.current) clearTimeout(debounce.current); debounce.current = setTimeout(() => { void flush().catch(() => {}); }, 500);
  }
  async function submit() {
    setConfirmSubmit(false);
    setBusy(true); setSaveError("");
    try { await flush(); const next = await request<Attempt>(`/api/attempts/${credentials.sessionId}/submit`, { method: "POST", headers }); current.current = next; setAttempt(next); await refresh(); }
    catch (ex) { setSaveError(ex instanceof Error ? ex.message : "Не удалось отправить ответы."); } finally { setBusy(false); }
  }
  if (!attempt) return <section className="student-lobby"><p>{error || "Открываем тест…"}</p><button onClick={() => void refresh()}>Повторить</button></section>;
  const locked = attempt.status !== "Active" || attempt.submittedAt !== null || busy;
  return <section className="student-test"><div className="session-title"><div><h1>{attempt.title}</h1><p>{attempt.groupName} · {attempt.name}</p></div>{attempt.status === "Active" && attempt.deadline && <Countdown deadline={attempt.deadline} serverNow={attempt.serverNow}/>}</div>
    {error && <p className="alert" role="alert">{error}</p>}{saveError && <p className="alert" role="alert">{saveError}{pendingCount > 0 && <button onClick={() => void flush().catch(() => {})}>Повторить сохранение</button>}</p>}
    {attempt.status === "Waiting" ? <div className="waiting-card"><h2>Вы присоединились</h2><p>Дождитесь общего запуска преподавателем. Тест откроется автоматически.</p></div> : attempt.submittedAt !== null || attempt.status === "Finished" ? <div className="waiting-card"><h2>Ответы отправлены</h2><p>Ваш результат: <strong>{attempt.score ?? 0}/{attempt.totalPoints}</strong>{attempt.pendingReview > 0 && ` · предварительно, ${attempt.pendingReview} ответов ожидают проверки.`}</p><p>Спасибо! Изменение ответов закрыто.</p></div> : <>
      <div className="autosave-status" role="status">{pendingCount ? `Сохраняем ответы: ${pendingCount}` : "Все ответы сохранены"}</div>
      {attempt.questions?.map((q, index) => { const answer = answers[q.id] ?? { questionId: q.id, optionIds: [], text: "" }; return <fieldset disabled={locked} className="question-card student-question" key={q.id}><legend>{index + 1}. {q.text} <small>({q.points} балл.)</small></legend>{q.type === "OpenText" || q.type === "ShortText" ? <textarea aria-label={`Ответ на вопрос ${index + 1}`} maxLength={5000} rows={q.type === "OpenText" ? 5 : 2} value={answer.text} onChange={e => change({ ...answer, text: e.target.value })}/> : <div className="student-options">{q.options.map(o => <label key={o.id}><input type={q.type === "MultipleChoice" ? "checkbox" : "radio"} name={q.id} checked={answer.optionIds.includes(o.id)} onChange={e => change({ ...answer, optionIds: q.type === "MultipleChoice" ? e.target.checked ? [...answer.optionIds, o.id] : answer.optionIds.filter(id => id !== o.id) : [o.id] })}/><span>{o.text}</span></label>)}</div>}</fieldset>; })}
      <button className="primary" disabled={locked} onClick={() => setConfirmSubmit(true)}>{busy ? "Отправляем…" : "Завершить и отправить"}</button>{confirmSubmit && <div className="confirm-action" role="alertdialog" aria-label="Отправка ответов"><p>Отправить ответы? После отправки изменить их нельзя.</p><button className="primary" disabled={busy} onClick={submit}>Да, отправить</button><button onClick={() => setConfirmSubmit(false)}>Отмена</button></div>}<p className="muted">При окончании времени будут отправлены ответы, сохранённые на сервере.</p>
    </>}
  </section>;
}
