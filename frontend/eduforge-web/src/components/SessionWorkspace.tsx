"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import { request } from "../lib/api";
import { type Answer, type Participant, type SessionQuestion, type SessionSummary, type TeacherSession, sessionLabels, activityLabels } from "../lib/sessions";
import type { QuizSummary } from "../lib/teaching";
import { Countdown, useSessionWatch } from "./SessionLive";

export default function SessionWorkspace() {
  const [list, setList] = useState<SessionSummary[]>([]); const [tests, setTests] = useState<QuizSummary[]>([]);
  const [groups, setGroups] = useState<{ id: string; name: string; studentCount: number }[]>([]);
  const [quiz, setQuiz] = useState(""); const [group, setGroup] = useState(""); const [minutes, setMinutes] = useState(15);
  const [selected, setSelected] = useState(""); const [session, setSession] = useState<TeacherSession | null>(null);
  const [error, setError] = useState(""); const [notice, setNotice] = useState(""); const [busy, setBusy] = useState(false); const [detail, setDetail] = useState("");
  const generation = useRef(0);
  const [confirmFinish, setConfirmFinish] = useState(false);
  useEffect(() => {
    let cancelled = false;
    void Promise.all([request<SessionSummary[]>("/api/sessions"), request<QuizSummary[]>("/api/tests"), request<typeof groups>("/api/groups")]).then(([sessions, quizzes, studyGroups]) => {
      if (!cancelled) { setList(sessions); setTests(quizzes); setGroups(studyGroups); setQuiz(quizzes[0]?.id ?? ""); setGroup(studyGroups[0]?.id ?? ""); }
    }).catch(ex => { if (!cancelled) setError(ex.message); });
    return () => { cancelled = true; generation.current++; };
  }, []);
  const refresh = useCallback(async () => {
    if (!selected) return; const version = ++generation.current;
    try { const next = await request<TeacherSession>(`/api/sessions/${selected}`); if (version === generation.current) { setSession(next); setList(old => old.map(s => s.id === next.id ? next : s)); setError(""); } }
    catch (ex) { if (version === generation.current) setError(ex instanceof Error ? ex.message : "Не удалось обновить сессию."); }
  }, [selected]);
  useEffect(() => { setSession(null); setDetail(""); setConfirmFinish(false); setNotice(""); void refresh(); return () => { generation.current++; }; }, [refresh]);
  useSessionWatch(selected, null, refresh);
  async function create() {
    setBusy(true); setError("");
    try { const next = await request<TeacherSession>("/api/sessions", { method: "POST", body: JSON.stringify({ quizId: quiz, groupId: group, durationMinutes: minutes }) }); setList(old => [next, ...old]); setSelected(next.id); }
    catch (ex) { setError(ex instanceof Error ? ex.message : "Не удалось создать сессию."); } finally { setBusy(false); }
  }
  async function action(name: "start" | "finish") {
    if (!session) return;
    setConfirmFinish(false);
    setBusy(true); setError("");
    try { await request(`/api/sessions/${session.id}/${name}`, { method: "POST" }); await refresh(); }
    catch (ex) { setError(ex instanceof Error ? ex.message : "Не удалось изменить сессию."); } finally { setBusy(false); }
  }
  const submitted = session?.participants.filter(p => p.submittedAt !== null) ?? [];
  const complete = submitted.filter(p => p.pendingReview === 0);
  const average = session && complete.length ? Math.round(complete.reduce((total, p) => total + (p.score ?? 0), 0) / complete.length / session.totalPoints * 100) : null;
  const participant = session?.participants.find(p => p.studentId === detail);
  return <section className="teaching-page"><div className="page-heading"><div><p className="eyebrow">ПРОВЕДЕНИЕ ЗАНЯТИЙ</p><h1>Тестовые сессии</h1><p>Общий старт, сохранение ответов и результаты группы.</p></div></div>
    {error && <p className="alert" role="alert">{error}</p>}{notice && <p role="status">{notice}</p>}
    <div className="session-create"><label>Тест<select value={quiz} onChange={e => setQuiz(e.target.value)}>{tests.map(t => <option key={t.id} value={t.id}>{t.title}</option>)}</select></label><label>Группа<select value={group} onChange={e => setGroup(e.target.value)}>{groups.map(g => <option key={g.id} value={g.id}>{g.name} · {g.studentCount} студентов</option>)}</select></label><label>Минуты<input type="number" min={1} max={180} value={minutes} onChange={e => setMinutes(Number(e.target.value))}/></label><button className="primary" disabled={busy || !quiz || !group} onClick={create}>Создать сессию</button></div>
    {(!tests.length || !groups.length) && <p className="muted">Сначала сохраните тест и добавьте студентов в группу.</p>}
    <div className="teaching-columns"><div className="library"><h2>Сессии <span>{list.length}</span></h2>{list.map(s => <button key={s.id} className={`material-card ${selected === s.id ? "selected" : ""}`} onClick={() => setSelected(s.id)}><strong>{s.title}</strong><small>{s.groupName} · {sessionLabels[s.status]}</small></button>)}{!list.length && <p className="muted">Создайте первую сессию.</p>}</div>
      <div className="session-panel">{selected && !session ? <p>Загрузка сессии…</p> : !session ? <p className="empty">Выберите или создайте сессию.</p> : <>
        <div className="session-title"><div><h2>{session.title}</h2><p>{session.groupName} · {sessionLabels[session.status]}</p></div>{session.status === "Active" && session.deadline && <Countdown deadline={session.deadline} serverNow={session.serverNow}/>}</div>
        {session.status !== "Finished" && <div className="join-card"><span>Код подключения</span><strong>{session.code}</strong><button onClick={async () => { try { await navigator.clipboard.writeText(`${window.location.origin}/join/${session.code}`); setNotice("Ссылка скопирована."); } catch { setNotice(`Ссылка: ${window.location.origin}/join/${session.code}`); } }}>Скопировать ссылку</button><p><a href={`/join/${session.code}`} target="_blank" rel="noreferrer">Открыть страницу студента ↗</a></p></div>}
        <div className="session-stats"><span>Присоединились <b>{session.participants.filter(p => p.joinedAt !== null).length}</b></span><span>Выполняют <b>{session.status === "Active" ? session.participants.filter(p => p.joinedAt !== null && p.submittedAt === null).length : 0}</b></span><span>Завершили <b>{submitted.length}</b></span></div>
        <div className="actions">{session.status === "Waiting" && <button className="primary" disabled={busy} onClick={() => action("start")}>Начать для всех</button>}{session.status !== "Finished" && <button disabled={busy} onClick={() => setConfirmFinish(true)}>Завершить тестирование</button>}<button onClick={() => void refresh()}>Обновить</button></div>
        {confirmFinish && session.status !== "Finished" && <div className="confirm-action" role="alertdialog" aria-label="Завершение тестирования"><p>Завершить тестирование? Все сохранённые ответы будут отправлены. Изменение ответов закроется.</p><button className="primary" disabled={busy} onClick={() => action("finish")}>Да, завершить</button><button onClick={() => setConfirmFinish(false)}>Отмена</button></div>}{session.status === "Finished" && <p>Средний результат: <strong>{average === null ? "—" : `${average}%`}</strong> · полностью проверено {complete.length} из {submitted.length}. {submitted.some(p => p.pendingReview > 0) && "Открытые и короткие ответы ожидают проверки."}</p>}
        <div className="table-scroll"><table className="results-table"><thead><tr><th>Студент</th><th>Статус</th><th>Ответов</th><th>Балл</th></tr></thead><tbody>{session.participants.map(p => <tr key={p.studentId}><td><button className="text-button" disabled={!p.joinedAt} onClick={() => setDetail(p.studentId)}>{p.name}</button></td><td>{p.submittedAt ? p.pendingReview ? "На проверке" : "Завершил" : p.joinedAt ? session.status === "Waiting" ? "Ожидает" : "Выполняет" : "Не присоединился"}</td><td>{p.answeredCount}/{session.questions.length}</td><td>{p.score === null ? "—" : `${p.score}/${session.totalPoints}${p.pendingReview ? " · предварительно" : ""}`}</td></tr>)}</tbody></table></div>
        {session.status === "Finished" && submitted.length > 0 && <details className="question-analytics"><summary>Результаты по вопросам</summary>{session.questions.map((q, index) => {
          const values = submitted.map(p => p.answers?.find(a => a.questionId === q.id)).filter((a): a is Answer => a !== undefined && a.score !== null);
          const full = values.filter(a => a.score === q.points).length;
          return <p key={q.id}>{index + 1}. {q.text}<br/><span className="muted">Полный балл: {full} из {values.length} проверенных · {submitted.length - values.length} на проверке</span></p>;
        })}</details>}
        {participant && <div className="answer-review"><h3>{participant.name}</h3>{participant.answers && session.questions.map(q => <Review key={`${session.id}-${participant.studentId}-${q.id}`} question={q} answer={participant.answers!.find(a => a.questionId === q.id)!} participant={participant} session={session} refresh={refresh}/>)}<h4>Журнал активности</h4><p className="muted">События сообщает браузер. Они помогают наблюдать за прохождением теста и сами по себе не доказывают списывание.</p>{participant.activity.length ? <ul className="activity-list">{participant.activity.map((event, index) => <li key={index}><time>{new Date(event.at).toLocaleTimeString("ru-RU")}</time> — {activityLabels[event.kind] ?? event.kind}</li>)}</ul> : <p className="muted">Событий нет.</p>}</div>}
      </>}</div></div></section>;
}
function Review({ question: q, answer: a, participant: p, session: s, refresh }: { question: SessionQuestion; answer: Answer; participant: Participant; session: TeacherSession; refresh: () => Promise<void> }) {
  const [score, setScore] = useState(a.score ?? 0); const [feedback, setFeedback] = useState(a.feedback ?? ""); const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  const manual = q.type === "OpenText" || q.type === "ShortText";
  async function save() { setBusy(true); setError(""); try { await request(`/api/sessions/${s.id}/review/${p.studentId}/${q.id}`, { method: "PUT", body: JSON.stringify({ score, feedback }) }); await refresh(); } catch (ex) { setError(ex instanceof Error ? ex.message : "Не удалось сохранить оценку."); } finally { setBusy(false); } }
  return <article className="question-card"><h4>{q.text} · {q.points} балл.</h4><p>Ответ: {manual ? a.text || "Нет ответа" : q.options.filter(o => a.optionIds.includes(o.id)).map(o => o.text).join(", ") || "Нет ответа"}</p><p className="muted">{manual ? `Образец: ${q.modelAnswer || "Не задан"}` : `Правильный ответ: ${q.options.filter(o => o.isCorrect).map(o => o.text).join(", ")}`}</p><p>Оценка: {a.score === null ? "Ожидает проверки" : `${a.score}/${q.points}`}</p>{manual && s.status === "Finished" && <div className="review-controls"><label>Балл<input type="number" min={0} max={q.points} value={score} onChange={e => setScore(Number(e.target.value))}/></label><label>Комментарий<input maxLength={2000} value={feedback} onChange={e => setFeedback(e.target.value)}/></label><button disabled={busy} onClick={save}>Сохранить оценку</button></div>}{error && <p className="alert" role="alert">{error}</p>}</article>;
}
