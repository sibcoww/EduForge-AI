"use client";
import SearchField from "./SearchField";
import Icon from "./Icon";
import { confirmAction } from "../lib/ui-dialog";
import Form from "./Form";
import { useCallback, useEffect, useRef, useState } from "react";
import TestPublicationControls from "./TestPublicationControls";
import CopyUpdates from "./CopyUpdates";
import GenerationPassport from "./GenerationPassport";
import { QualityWarnings } from "./ControlledTest";
import { LearningPlanEditor, CoveragePanel, QuestionGroundingEditor } from "./ControlledTest";
import type { LearningContext, QuizAnalysis } from "../lib/learning";
import CreationDialog from "./CreationDialog";
import { request } from "../lib/api";
import { cleanQuiz, newQuestion, questionTypes, type Question, type QuestionType, type Quiz, type QuizDraft, type QuizSummary, type SourceMaterial } from "../lib/teaching";
import { useUnsavedChanges } from "./TeacherShell";
const blank: QuizDraft = { title: "", materialId: null, questions: [], controlled: false };
export default function TestWorkspace() {
  const [tests, setTests] = useState<QuizSummary[]>([]); const [materials, setMaterials] = useState<SourceMaterial[]>([]);
  const [draft, setDraft] = useState<QuizDraft>(blank); const [baseline, setBaseline] = useState(JSON.stringify(cleanQuiz(blank)));
  const [selected, setSelected] = useState<string | null>(null); const [query, setQuery] = useState("");
  const [sourceFilter, setSourceFilter] = useState<string | null>(null);
  const [busy, setBusy] = useState(false); const [loading, setLoading] = useState(true); const [error, setError] = useState(""); const [message, setMessage] = useState("");
  const [totalPoints, setTotalPoints] = useState(100);
  const [count, setCount] = useState(10); const [difficulty, setDifficulty] = useState("Средняя"); const [types, setTypes] = useState<QuestionType[]>(["SingleChoice"]);
  const [testRequirements,setTestRequirements]=useState("");
  const [teachingContext,setTeachingContext]=useState<{level:string;language:string;requirements:string}|null>(null);
  const [contextError,setContextError]=useState("");
  const [creating, setCreating] = useState(false);
  const [learning, setLearning] = useState<LearningContext | null>(null);
  const [analysis, setAnalysis] = useState<QuizAnalysis | null>(null);
  const [analysisBusy, setAnalysisBusy] = useState(false); const [analysisError, setAnalysisError] = useState("");
  const [goalsDirty, setGoalsDirty] = useState(false);
  const handleLearningLoaded=useCallback((source:LearningContext|null)=>{setLearning(source);if(source&&!source.planNeedsReview)setError("");},[]);
  useEffect(()=>{setTeachingContext(null);setContextError("");if(!draft.materialId)return;const controller=new AbortController();void request<{level:string;language:string;requirements:string}>(`/api/materials/${draft.materialId}/teaching-context`,{signal:controller.signal}).then(value=>{if(!controller.signal.aborted)setTeachingContext(value);}).catch(ex=>{if(!controller.signal.aborted)setContextError(ex.message);});return()=>controller.abort();},[draft.materialId]);
  const previousDraft = useRef({ selected: null as string | null, draft: blank, baseline: JSON.stringify(cleanQuiz(blank)) });
  function newItem() { setTestRequirements("");previousDraft.current = { selected, draft, baseline }; const initial = { ...blank, materialId: sourceFilter, controlled: !!sourceFilter }; setDraft(initial); setTotalPoints(100); setBaseline(JSON.stringify(cleanQuiz(initial))); setSelected(null); setCreating(true); setError(""); setMessage(""); }
  function cancelCreation() { const p = previousDraft.current; setDraft(p.draft); setBaseline(p.baseline); setSelected(p.selected); setCreating(false); setError(""); }
  async function closeEditor() { if (creating) { if(dirty&&!await confirmAction("Закрыть черновик? Несохранённые изменения будут потеряны."))return;cancelCreation();return; } if (dirty && !await confirmAction("Закрыть окно? Несохранённые изменения будут потеряны.")) return; setSelected(null); setDraft(blank); setBaseline(JSON.stringify(cleanQuiz(blank))); setError(""); }
  const dirty = JSON.stringify(cleanQuiz(draft)) !== baseline || goalsDirty; useUnsavedChanges(dirty);
  useEffect(() => {
    setAnalysis(null); setAnalysisError("");
    if ((!draft.materialId && draft.questions.length === 0) || (!selected && !creating)) { setAnalysisBusy(false); return; }
    const controller = new AbortController(); setAnalysisBusy(true);
    const timer = setTimeout(() => {
      void request<QuizAnalysis>("/api/tests/analyze", { method: "POST", body: JSON.stringify(cleanQuiz(draft)), signal: controller.signal })
        .then(value => { if (!controller.signal.aborted) setAnalysis(value); })
        .catch(ex => { if (!controller.signal.aborted) setAnalysisError(ex instanceof Error ? ex.message : "Не удалось проверить тест."); })
        .finally(() => { if (!controller.signal.aborted) setAnalysisBusy(false); });
    }, 350);
    return () => { clearTimeout(timer); controller.abort(); };
  }, [draft, learning, selected, creating]);
  const fail = (ex: unknown) => setError(ex instanceof Error ? ex.message : "Не удалось выполнить запрос.");
  useEffect(() => { let cancelled = false;
    void Promise.all([request<QuizSummary[]>("/api/tests"), request<SourceMaterial[]>("/api/materials")]).then(([list, sources]) => {
      if (cancelled) return; setTests(list); setMaterials(sources);
      const source = sources.find(m => m.id === new URLSearchParams(location.search).get("materialId"));
      if (source) { const initial = { ...blank, materialId: source.id, controlled: true, title: `${source.title} — тест`.slice(0, 200) }; setDraft(initial); setBaseline(JSON.stringify(cleanQuiz(initial))); setSourceFilter(source.id); setCreating(true); }
    }).catch(ex => { if (!cancelled) fail(ex); }).finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, []);
  async function canLeave() { return !dirty || await confirmAction("Открыть другой тест? Несохранённые изменения будут потеряны."); }
  async function open(id: string) {
    if (!await canLeave()) return; setBusy(true); setError("");
    try { const value = await request<Quiz>(`/api/tests/${id}`); const clean = cleanQuiz(value); setDraft(clean); setTestRequirements(""); setBaseline(JSON.stringify(clean)); setSelected(id); setCreating(false); setTotalPoints(clean.questions.reduce((sum, q) => sum + q.points, 0)); setMessage(""); }
    catch (ex) { fail(ex); } finally { setBusy(false); }
  }
  async function save(event: React.FormEvent) {
    event.preventDefault(); if (goalsDirty) { setError("Сначала сохраните изменения учебных целей."); return; } if (draft.questions.reduce((sum, q) => sum + q.points, 0) !== totalPoints) { setError("Сумма баллов вопросов должна совпадать с максимальным баллом теста."); return; } setBusy(true); setError(""); setMessage("");
    try { const value = await request<Quiz>(`/api/tests${selected ? `/${selected}` : ""}`, { method: selected ? "PUT" : "POST", body: JSON.stringify(cleanQuiz(draft)) });
      const clean = cleanQuiz(value); setDraft(clean); setBaseline(JSON.stringify(clean)); setSelected(value.id); setCreating(false); setQuery("");
      setTests(current => [{ ...value, questionCount: value.questions.length, totalPoints: value.questions.reduce((sum, q) => sum + q.points, 0) }, ...current.filter(t => t.id !== value.id)]); setMessage("Тест сохранён");
    } catch (ex) { fail(ex); } finally { setBusy(false); }
  }
  async function generate() {
    if(goalsDirty){setError("Сначала дождитесь предложения целей и подтвердите их или сохраните изменения целей.");return;}
    if (!Number.isInteger(totalPoints) || totalPoints < count || totalPoints > count * 100) { setError("При генерации общий балл должен быть от количества вопросов до количества вопросов × 100."); return; }
    if (!draft.materialId) { setError("Выберите сохранённый материал для генерации теста."); return; }
    if (draft.controlled && (!learning || learning.planNeedsReview)) { setError("Добавьте и подтвердите учебные цели по текущему материалу."); return; }
    if (draft.questions.length && !await confirmAction("Заменить вопросы черновика AI-вопросами?")) return;
    setBusy(true); setError(""); setMessage("AI составляет вопросы по материалу…");
    try { const quiz = { materialId: draft.materialId, questionCount: count, totalPoints, difficulty, types, requirements:testRequirements };
      const value = await request<QuizDraft>(draft.controlled ? "/api/ai/test/controlled" : "/api/ai/test", { method: "POST", body: JSON.stringify(draft.controlled ? { quiz, revisionId: learning!.revisionId, planVersion: learning!.planVersion } : quiz) }); setDraft(cleanQuiz(value)); setMessage("Проверьте вопросы, основания ответов и покрытие целей перед публикацией."); }
    catch (ex) { setMessage(""); fail(ex); } finally { setBusy(false); }
  }
  async function remove() {
    if (!selected || !await confirmAction(`Удалить тест «${draft.title}» и его вопросы? Публикация и ссылка закроются. Сессии и результаты сохранятся.`)) return;
    setBusy(true); setError("");
    try { await request(`/api/tests/${selected}`, { method: "DELETE" }); setTests(current => current.filter(t => t.id !== selected)); setSelected(null); setDraft(blank); setBaseline(JSON.stringify(cleanQuiz(blank))); setMessage("Тест удалён"); }
    catch (ex) { fail(ex); } finally { setBusy(false); }
  }
  function update(index: number, value: Question, keepReview = false) { setDraft(current => ({ ...current, questions: current.questions.map((q, i) => i === index ? { ...value, ...(value.grounding ? { grounding: { ...value.grounding, reviewed: keepReview && value.grounding.reviewed } } : {}) } : q) })); }
  async function regenerate(index: number) {
    if(goalsDirty){setError("Сначала подтвердите изменения учебных целей.");return;}
    if (!learning || !draft.materialId || learning.planNeedsReview) return;
    if (!await confirmAction("Заменить только этот вопрос? Его текущие правки будут заменены AI-черновиком. Остальные вопросы сохранятся.")) return;
    setBusy(true); setError("");
    const previous = draft.questions[index];
    try {
      const objectiveId = previous.grounding?.objectiveId;
      const value = await request<QuizDraft>("/api/ai/test/controlled", { method: "POST", body: JSON.stringify({
        quiz: { materialId: draft.materialId, questionCount: 1, totalPoints: previous.points, difficulty, requirements:testRequirements, types: [previous.type] },
        revisionId: learning.revisionId, planVersion: learning.planVersion,
        ...(objectiveId && learning.objectives.some(o => o.id === objectiveId) ? { objectiveIds: [objectiveId] } : {})
      }) });
      update(index, value.questions[0]); setMessage("Заменён один вопрос. Проверьте его ответ и основание перед сохранением.");
    } catch (ex) { fail(ex); } finally { setBusy(false); }
  }
  function move(index: number, step: number) { const questions = [...draft.questions]; [questions[index], questions[index + step]] = [questions[index + step], questions[index]]; setDraft({ ...draft, questions }); }
  useEffect(() => { const id = new URLSearchParams(location.search).get("testId"); if (id) void open(id); }, []);
  const editor = (<section className="editor"><div className="section-title"><h2>{selected ? "Редактирование теста" : "Новый тест"}</h2>{selected && <button type="button" disabled={busy} className="danger-button" onClick={remove}>Удалить тест</button>}</div>
      <Form noValidate onSubmit={save}><fieldset disabled={busy}><label>Название<input required maxLength={200} value={draft.title} onChange={e => setDraft({ ...draft, title: e.target.value })}/></label><label>Исходный материал<select aria-label="Исходный материал" value={draft.materialId ?? ""} onChange={async e => { const materialId=e.target.value; if (draft.questions.length && !await confirmAction("Сменить исходный материал? Связи вопросов и отметки проверки будут сброшены.")) return; setLearning(null); setDraft({ ...draft, materialId: materialId || null, controlled: !!materialId, questions: draft.questions.map(q => ({ ...q, grounding: null })) }); }}><option value="">Без материала — ручной тест</option>{materials.map(m => <option key={m.id} value={m.id}>{m.title}</option>)}</select></label>
        {draft.materialId && <><label className="check-label"><input type="checkbox" checked={!!draft.controlled} onChange={e => setDraft({ ...draft, controlled: e.target.checked })}/>Контролировать основания и покрытие учебных целей</label>
          {!draft.controlled && <p className="hint">Обычный режим: основания и покрытие не обязательны. Включите контроль для проверки связей с материалом.</p>}
          {draft.controlled && <><LearningPlanEditor key={draft.materialId} materialId={draft.materialId} disabled={busy} onLoaded={handleLearningLoaded} onDirty={setGoalsDirty}/><CoveragePanel analysis={analysis} pending={analysisBusy} error={analysisError}/></>}
        </>}
        {!draft.controlled && <QualityWarnings analysis={analysis}/>}
        <label>Максимальный балл за тест<input type="number" required min={1} max={10000} step={1} value={totalPoints} onChange={e => setTotalPoints(Number(e.target.value))}/></label><p className="hint">Сумма баллов вопросов: {draft.questions.reduce((sum, q) => sum + q.points, 0)} из {totalPoints}. AI распределит общий балл по вопросам. Для ручного теста укажите баллы каждого вопроса.</p><details className="ai-settings"><summary>✦ Создать вопросы с AI</summary><div className="fields"><label>Количество<input type="number" min={1} max={30} value={count} onChange={e => setCount(Number(e.target.value))}/></label><label>Сложность<select aria-label="Сложность" value={difficulty} onChange={e => setDifficulty(e.target.value)}><option>Лёгкая</option><option>Средняя</option><option>Сложная</option></select></label></div><div className="type-options">{Object.entries(questionTypes).map(([type, label]) => <label className="check-label" key={type}><input type="checkbox" checked={types.includes(type as QuestionType)} onChange={e => setTypes(current => e.target.checked ? [...current, type as QuestionType] : current.filter(t => t !== type))}/>{label}</label>)}</div><label>Пожелания к тесту<textarea aria-label="Пожелания к тесту" className="resize-none" rows={2} maxLength={2000} value={testRequirements} onChange={e=>setTestRequirements(e.target.value)} placeholder="Например: для детей 6–10 лет, короткие вопросы и простые слова"/></label>
        {draft.materialId&&<div className="test-teaching-context">{teachingContext?<><p className="hint"><strong>Из материала:</strong> {teachingContext.level} · {teachingContext.language}</p>{teachingContext.requirements?<p className="hint">{teachingContext.requirements}</p>:<p className="hint">Сохранённых пожеланий к материалу нет. Возраст и особенности аудитории можно указать выше.</p>}</>:<p className="hint">{contextError||"Загружаем условия исходного материала…"}</p>}</div>}
        <p className="hint">AI использует знания из материала и учитывает его сохранённые пожелания, возраст, уровень и язык. Пожелания к тесту уточняют эти настройки. Проверьте возрастную уместность и правильность вопросов.</p><button type="button" disabled={!draft.materialId || types.length === 0} onClick={generate}>Сгенерировать вопросы</button></details>
        {draft.questions.map((q, index) => <div className="question-card" key={index}><div className="section-title"><h2>Вопрос {index + 1}</h2><div className="row-actions"><button type="button" disabled={index === 0} aria-label={`Вопрос ${index + 1} вверх`} onClick={() => move(index, -1)}>↑</button><button type="button" disabled={index === draft.questions.length - 1} aria-label={`Вопрос ${index + 1} вниз`} onClick={() => move(index, 1)}>↓</button><button type="button" aria-label={`Удалить вопрос ${index + 1}`} onClick={() => setDraft({ ...draft, questions: draft.questions.filter((_, i) => i !== index) })}>×</button></div></div>
          <div className="fields"><label>Тип<select aria-label="Тип" value={q.type} onChange={e => update(index, { ...newQuestion(e.target.value as QuestionType), text: q.text, points: q.points })}>{Object.entries(questionTypes).map(([type, label]) => <option key={type} value={type}>{label}</option>)}</select></label><label>Баллы<input type="number" required min={1} max={100} value={q.points} onChange={e => update(index, { ...q, points: Number(e.target.value) })}/></label></div>
          <label>Формулировка<textarea aria-label="Формулировка" className="resize-none" required maxLength={4000} rows={3} value={q.text} onChange={e => update(index, { ...q, text: e.target.value })}/></label>
          {q.options.map((option, position) => <div className="option-row" key={position}><input aria-label={`Вопрос ${index + 1}, правильный вариант ${position + 1}`} type={q.type === "MultipleChoice" ? "checkbox" : "radio"} name={`correct-${index}`} checked={option.isCorrect} onChange={e => update(index, { ...q, options: q.options.map((o, i) => ({ ...o, isCorrect: q.type === "MultipleChoice" ? (i === position ? e.target.checked : o.isCorrect) : i === position })) })}/><input required maxLength={1000} aria-label={`Вопрос ${index + 1}, вариант ${position + 1}`} readOnly={q.type === "TrueFalse"} placeholder={`Вариант ${position + 1}`} value={option.text} onChange={e => update(index, { ...q, options: q.options.map((o, i) => i === position ? { ...o, text: e.target.value } : o) })}/>{q.type !== "TrueFalse" && <button type="button" disabled={q.options.length <= 2} aria-label={`Удалить вариант ${position + 1}`} onClick={() => update(index, { ...q, options: q.options.filter((_, i) => i !== position) })}>×</button>}</div>)}
          {(q.type === "SingleChoice" || q.type === "MultipleChoice") && <button type="button" disabled={q.options.length >= 10} onClick={() => update(index, { ...q, options: [...q.options, { text: "", isCorrect: false }] })}>+ Вариант</button>}
          {(q.type === "OpenText" || q.type === "ShortText") && <label>Эталон ответа {q.type === "OpenText" && "(необязательно)"}<textarea className="resize-none" required={q.type === "ShortText"} maxLength={5000} rows={2} value={q.modelAnswer} onChange={e => update(index, { ...q, modelAnswer: e.target.value })}/></label>}
          {draft.controlled && learning?.materialId === draft.materialId && <QuestionGroundingEditor question={q} source={learning} review={analysis?.questions[index]} disabled={busy || analysisBusy} onChange={value => update(index, value, true)} onRegenerate={() => regenerate(index)}/>}
        </div>)}<button type="button" disabled={draft.questions.length >= 100} onClick={() => setDraft({ ...draft, questions: [...draft.questions, newQuestion()] })}>+ Добавить вопрос</button></fieldset>
        <p className="hint">{draft.questions.length} вопросов · {draft.questions.reduce((sum, q) => sum + q.points, 0)} баллов</p><footer><span role="status">{message}</span><button className="primary" disabled={busy || draft.questions.length === 0}>{busy ? "Подождите…" : "Сохранить тест"}</button></footer>
      </Form>{draft.generationRunId && <GenerationPassport key={draft.generationRunId} id={draft.generationRunId}/>}
      {selected && <CopyUpdates key={selected+"copy"} id={selected} kind="test" disabled={busy||dirty} onUpdated={()=>{location.href="/tests?testId="+selected;}}/>}{selected && <TestPublicationControls key={selected} testId={selected} disabled={busy || dirty}/>}</section>);
  const visibleTests = tests.filter(t => (!sourceFilter || t.materialId === sourceFilter) && t.title.toLowerCase().includes(query.toLowerCase()));
  return <><section className="intro"><p className="eyebrow">ПРОВЕРКА ЗНАНИЙ</p><h1>Вопросы по вашему уроку</h1><p>Создавайте тесты вручную или используйте AI для вопросов по материалу.</p></section>
    {error && <p className="alert" role="alert">{error}</p>}
    {!creating&&!selected&&message&&<p className="notice" role="status">{message}</p>}<div className="columns catalogue"><section className="library"><div className="section-title"><h2>{sourceFilter ? "Тесты по материалу" : "Мои тесты"} <span>{visibleTests.length}</span></h2><button disabled={busy} type="button" className="primary create-button" onClick={newItem}><Icon name="plus" size={18}/> Новый тест</button></div>{sourceFilter && <p className="hint">{materials.find(m => m.id === sourceFilter)?.title} <button type="button" onClick={() => setSourceFilter(null)}>Все тесты</button></p>}<SearchField label="Поиск тестов" value={query} onChange={setQuery}/>
    {loading ? <p className="empty">Загружаем тесты…</p> : visibleTests.length === 0 ? <p className="empty">{query ? "По запросу ничего не найдено." : "Нажмите «+ Новый», чтобы создать первый тест."}</p> : <div className="catalog-grid">{visibleTests.map(t => <button className={`material ${selected === t.id ? "selected" : ""}`} disabled={busy} key={t.id} onClick={() => open(t.id)}><div className="catalog-card-top"><span className="tag">Тест</span><Icon name="test" size={26}/></div><h3>{t.title}</h3><p>{t.questionCount} вопросов · {t.totalPoints} баллов</p><small>{materials.find(m => m.id === t.materialId)?.title ?? "Самостоятельный тест"}</small><span className="card-open">Открыть тест <Icon name="arrow" size={17}/></span></button>)}</div>}</section>
    {creating || selected ? <CreationDialog title={selected ? "Редактирование теста" : "Новый тест"} busy={busy} onClose={closeEditor}>{error && <p className="alert" role="alert">{error}</p>}{message && <p className="notice" role="status">{message}</p>}{editor}</CreationDialog> : null}</div></>;
}
