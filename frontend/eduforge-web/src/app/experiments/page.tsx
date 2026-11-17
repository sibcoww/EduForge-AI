"use client";
import { confirmAction } from "../../lib/ui-dialog";
import Form from "../../components/Form";
import { useEffect, useState } from "react";
import TeacherShell, { useUnsavedChanges } from "../../components/TeacherShell";
import GenerationPassport, { generationKinds, type Evaluation, type RunDetail, type RunSummary } from "../../components/GenerationPassport";
import { api, request } from "../../lib/api";

function Experiments() {
  const [runs, setRuns] = useState<RunSummary[]>([]); const [selected, setSelected] = useState<RunDetail | null>(null);
  const [evaluation, setEvaluation] = useState<Evaluation | null>(null); const [baseline, setBaseline] = useState("");
  const [error, setError] = useState(""); const [message, setMessage] = useState(""); const [busy, setBusy] = useState(false);
  const dirty = !!evaluation && JSON.stringify(evaluation) !== baseline; useUnsavedChanges(dirty);
  async function load() { setRuns(await request<RunSummary[]>("/api/generations")); }
  useEffect(() => { void load().catch(e => setError(e.message)); }, []);
  async function open(id: string) {
    if (dirty && !await confirmAction("Открыть другой результат? Несохранённая оценка будет потеряна.")) return;
    setBusy(true); setError(""); setMessage("");
    try { const value = await request<RunDetail>(`/api/generations/${id}`); setSelected(value); setEvaluation(value.evaluation); setBaseline(JSON.stringify(value.evaluation)); }
    catch (e) { setError(e instanceof Error ? e.message : "Не удалось открыть результат."); } finally { setBusy(false); }
  }
  return <section className="editor"><div className="section-title"><h1>История генераций</h1><a className="download-link" href={api + "/api/generations/export.csv"}>Выгрузить CSV</a></div>
    <p>Здесь сохраняются условия и результаты новых генераций. Для сравнения обычного и контролируемого режима используйте одинаковые материалы, модель и ограничения.</p>
    <p className="hint">Оценки и время подготовки вносит преподаватель после проверки. Пустое значение означает, что показатель ещё не измерен. В CSV выгружаются до 1000 последних по порядку времени записей; на экране — 200 последних.</p>
    {error && <p role="alert" className="alert">{error}</p>}{message && <p role="status" className="notice">{message}</p>}
    <button type="button" disabled={busy} onClick={() => void load().catch(e => setError(e.message))}>Обновить историю</button>
    {runs.length === 0 && <p>История появится после первой генерации материала или теста.</p>}
    <div className="generation-history">{runs.map(run => <button type="button" disabled={busy} className="material" key={run.id} onClick={() => open(run.id)}><strong>{generationKinds[run.kind] ?? run.kind}</strong><small>{new Date(run.startedAt).toLocaleString("ru-RU")} · {run.model} · {run.status === "Completed" ? "Завершено" : run.status === "Failed" ? "Ошибка" : "Выполняется"}</small></button>)}</div>
    {selected && evaluation && <article className="learning-panel"><h2>Оценка результата</h2><GenerationPassport id={selected.id}/>
      {selected.status !== "Completed" ? <p>Для незавершённой генерации оценка не выставляется. Причина: {selected.failure || "Запрос ещё выполняется"}.</p> : <Form noValidate onSubmit={async e => {
        e.preventDefault(); setBusy(true); setError("");
        try { await request(`/api/generations/${selected.id}/evaluation`, { method: "PUT", body: JSON.stringify(evaluation) }); setBaseline(JSON.stringify(evaluation)); setMessage("Оценка сохранена. Измерения доступны в CSV."); }
        catch (ex) { setError(ex instanceof Error ? ex.message : "Не удалось сохранить оценку."); } finally { setBusy(false); }
      }}><fieldset disabled={busy}><label>Название серии эксперимента<input maxLength={100} placeholder="Например: Python — циклы — серия 1" value={evaluation.experiment} onChange={e => setEvaluation({ ...evaluation, experiment: e.target.value })}/></label>
        <div className="fields"><label>Полное время подготовки, минут<input type="number" min={0} max={1440} step="0.01" value={evaluation.preparationMinutes ?? ""} onChange={e => setEvaluation({ ...evaluation, preparationMinutes: e.target.value === "" ? null : Number(e.target.value) })}/></label>
        <label>Исправлено или заменено вопросов<input type="number" min={0} max={1000} step={1} value={evaluation.correctedQuestions ?? ""} onChange={e => setEvaluation({ ...evaluation, correctedQuestions: e.target.value === "" ? null : Number(e.target.value) })}/></label></div>
        {([['correctness','Предметная корректность'],['clarity','Понятность формулировок'],['alignment','Соответствие материалу и целям']] as const).map(([key, label]) => <label key={key}>{label}<select aria-label={label} value={evaluation[key] ?? ""} onChange={e => setEvaluation({ ...evaluation, [key]: e.target.value === "" ? null : Number(e.target.value) })}><option value="">Не оценено</option>{[1,2,3,4,5].map(value => <option key={value} value={value}>{value}</option>)}</select></label>)}
        <label>Наблюдения и причины исправлений<textarea aria-label="Наблюдения и причины исправлений" className="resize-none" maxLength={4000} rows={4} value={evaluation.note} onChange={e => setEvaluation({ ...evaluation, note: e.target.value })}/></label><button className="primary">Сохранить оценку</button></fieldset></Form>}
      <details><summary>Зафиксированные исходные данные</summary><pre className="source-text">{JSON.stringify(selected.sources, null, 2)}</pre></details><details><summary>Исходный результат AI</summary><pre className="source-text">{JSON.stringify(selected.result, null, 2)}</pre></details>
    </article>}
  </section>;
}
export default function Page() { return <TeacherShell active="experiments"><Experiments/></TeacherShell>; }
