"use client";
import { confirmAction } from "../lib/ui-dialog";
import { useEffect, useRef, useState } from "react";
import { request } from "../lib/api";
import type { LearningContext, LearningObjective, QuestionReview, QuizAnalysis } from "../lib/learning";
import type { Grounding, Question } from "../lib/teaching";

export function LearningPlanEditor({ materialId, disabled, onLoaded, onDirty }: {
  materialId: string; disabled: boolean; onLoaded: (source: LearningContext | null) => void; onDirty: (dirty: boolean) => void;
}) {
  const [source, setSource] = useState<LearningContext | null>(null);
  const [objectives, setObjectives] = useState<LearningObjective[]>([]);
  const [baseline, setBaseline] = useState("[]");
  const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  const [suggesting,setSuggesting]=useState(false),[notice,setNotice]=useState("");
  const suggestion=useRef<AbortController|null>(null);
  useEffect(()=>()=>suggestion.current?.abort(),[]);
  const dirty = JSON.stringify(objectives) !== baseline;
  useEffect(() => { onDirty(dirty||suggesting); return () => onDirty(false); }, [dirty, suggesting, onDirty]);
  async function load(signal?: AbortSignal) {
    setError("");
    try {
      const value = await request<LearningContext>(`/api/materials/${materialId}/learning`, { signal });
      if (signal?.aborted) return;
      setSource(value); setObjectives(value.objectives); setBaseline(JSON.stringify(value.objectives)); onLoaded(value);
    } catch (ex) { if (!signal?.aborted) setError(ex instanceof Error ? ex.message : "Не удалось загрузить цели."); }
  }
  useEffect(() => { const controller = new AbortController(); onLoaded(null); void load(controller.signal); return () => controller.abort(); }, [materialId]);
  function edit(next: LearningObjective[]) { setObjectives(next); if (source) onLoaded({ ...source, planNeedsReview: true }); }
  async function save() {
    if (!source) return;
    setBusy(true); setError("");
    try {
      const value = await request<LearningContext>(`/api/materials/${materialId}/learning`, { method: "PUT", body: JSON.stringify({ revisionId: source.revisionId, planVersion: source.planVersion, objectives }) });
      setSource(value); setObjectives(value.objectives); setBaseline(JSON.stringify(value.objectives)); onLoaded(value);
      setNotice("Учебные цели подтверждены и сохранены.");
    } catch (ex) { setError(ex instanceof Error ? ex.message : "Не удалось сохранить цели."); }
    finally { setBusy(false); }
  }
  async function suggest() {
    if(!source)return;
    if(objectives.length&&!await confirmAction("Заменить текущий список целей предложениями AI? Сохранённые цели не изменятся, пока вы не подтвердите новый список."))return;
    const controller=new AbortController();suggestion.current=controller;setBusy(true);setSuggesting(true);setError("");setNotice("");
    try {
      const result=await request<{revisionId:string;planVersion:string;objectives:LearningObjective[]}>(`/api/materials/${materialId}/learning/suggest`,{method:"POST",signal:controller.signal,body:JSON.stringify({revisionId:source.revisionId,planVersion:source.planVersion})});
      if(controller.signal.aborted)return;
      edit(result.objectives);setNotice("AI предложил цели по материалу. Проверьте формулировки, при необходимости измените их и подтвердите список.");
    }catch(ex){if(!controller.signal.aborted)setError(ex instanceof Error?ex.message:"Не удалось предложить цели. Текущий список сохранён в редакторе.");}
    finally{if(!controller.signal.aborted){setBusy(false);setSuggesting(false);}}
  }
  return <section className="learning-panel" aria-label="Учебные цели материала">
    <div className="section-title"><h3>Учебные цели</h3><button type="button" disabled={disabled || busy} onClick={async () => { if (!dirty || await confirmAction("Загрузить сохранённые цели? Ваши изменения в списке будут потеряны.")) void load(); }}>Обновить</button></div>
    <p className="hint">Укажите, что студент должен уметь после урока, или попросите AI предложить цели по содержанию. Цели общие для всех тестов по этому материалу.</p>
    <button type="button" disabled={!source||disabled||busy||source.blocks.length===0} onClick={suggest}>{suggesting?"Предлагаем цели…":"Предложить цели с AI"}</button>
    {notice&&<p role="status" className="notice">{notice}</p>}
    {source?.planNeedsReview && <p className="learning-warning">Подтвердите цели по текущей версии материала. После изменения урока их нужно проверить снова.</p>}
    {error && <p role="alert" className="alert">{error}</p>}
    {!source && !error ? <p role="status">Загружаем материал и цели…</p> : <>
      {objectives.map((objective, i) => <div className="objective-row" key={objective.id}>
        <label>Цель {i + 1}<input maxLength={500} value={objective.text} disabled={disabled || busy} placeholder="Например: определить результат выполнения цикла for" onChange={e => edit(objectives.map(o => o.id === objective.id ? { ...o, text: e.target.value } : o))}/></label>
        <button type="button" disabled={disabled || busy} aria-label={`Удалить цель ${i + 1}`} onClick={() => edit(objectives.filter(o => o.id !== objective.id))}>×</button>
      </div>)}
      <div className="learning-actions"><button type="button" disabled={!source || disabled || busy || objectives.length >= 30} onClick={() => edit([...objectives, { id: crypto.randomUUID(), text: "" }])}>+ Учебная цель</button>
        <button type="button" disabled={!source || disabled || busy || objectives.length === 0 || objectives.some(o => !o.text.trim())} onClick={save}>{busy&&!suggesting ? "Сохраняем…" : "Подтвердить и сохранить цели"}</button></div>
      {dirty && <p className="hint" role="status">Есть несохранённые изменения целей. Сохраните их перед генерацией.</p>}
      <details><summary>Исходные фрагменты материала ({source?.blocks.length ?? 0})</summary>{source?.blocks.map((b, i) => <blockquote key={b.id}><strong>Фрагмент {i + 1}</strong><pre className="source-text">{b.text}</pre></blockquote>)}</details>
    </>}
  </section>;
}

export function CoveragePanel({ analysis, pending, error }: { analysis: QuizAnalysis | null; pending: boolean; error: string }) {
  return <section className="learning-panel" aria-label="Покрытие учебных целей">
    <h3>Покрытие и готовность теста</h3>
    {error && <p className="alert" role="alert">{error}</p>}
    {pending ? <p role="status">Проверяем связи вопросов…</p> : analysis && <>
      <p role="status" className={analysis.ready ? "learning-success" : "learning-warning"}>{analysis.ready ? "Все вопросы проверены, цели покрыты. Тест готов к публикации и созданию сессии." : "Требуется проверка вопросов или учебных целей."}</p>
      {analysis.materialChanged && <p>Версия материала или список целей требует подтверждения. Новые разделы также могут потребовать дополнительных вопросов.</p>}
      {analysis.coverage.length > 0 ? <div className="coverage-scroll"><table className="coverage-table"><thead><tr><th>Учебная цель</th><th>Вопросов</th><th>Проверено</th><th>Баллов</th></tr></thead><tbody>{analysis.coverage.map(o => <tr key={o.id}><td>{o.text}{o.reviewedCount === 0 && <span className="learning-badge">Не покрыта проверенными вопросами</span>}</td><td>{o.questionCount}</td><td>{o.reviewedCount}</td><td>{o.points}</td></tr>)}</tbody></table></div> : <p>Сначала добавьте и подтвердите учебные цели.</p>}
      <p className="hint">Покрытие проверенными вопросами: {analysis.coveragePercent === null ? "не определено" : `${analysis.coveragePercent}%`}. Наличие связей не доказывает качество вопросов или усвоение темы.</p>
      <QualityWarnings analysis={analysis}/>
    </>}
  </section>;
}

export function QualityWarnings({ analysis }: { analysis: QuizAnalysis | null }) {
  if (!analysis?.qualityWarnings?.length) return null;
  return <section className="quality-warnings" aria-label="Предупреждения о качестве вопросов"><h3>Проверьте качество вопросов</h3>
    {analysis.qualityWarnings.map((warning, i) => <p className="learning-warning" key={`${warning.index}-${warning.code}-${i}`}>Вопрос {warning.index + 1}: {warning.message}</p>)}
    <p className="hint">Это эвристические предупреждения. Похожие формулировки могут быть уместны; окончательное решение принимает преподаватель.</p>
  </section>;
}

export function QuestionGroundingEditor({ question, source, review, disabled, onChange, onRegenerate }: {
  question: Question; source: LearningContext; review?: QuestionReview; disabled: boolean;
  onChange: (question: Question) => void; onRegenerate: () => void;
}) {
  const g = question.grounding;
  function change(value: Grounding) { onChange({ ...question, grounding: { ...value, reviewed: false } }); }
  function initial(): Grounding { return { revisionId: source.revisionId, objectiveId: source.objectives[0]?.id ?? "", objectiveText: source.objectives[0]?.text ?? "", evidence: [], explanation: "", reviewed: false }; }
  function rebind() {
    const previous = g ?? initial();
    change({ ...previous, revisionId: source.revisionId,
      objectiveId: source.objectives.some(o => o.id === previous.objectiveId) ? previous.objectiveId : source.objectives[0]?.id ?? "",
      objectiveText: (source.objectives.find(o => o.id === previous.objectiveId) ?? source.objectives[0])?.text ?? "",
      evidence: previous.evidence.filter(e => source.blocks.some(b => b.id === e.blockId)).map(e => ({ ...e, quote: source.blocks.find(b => b.id === e.blockId)!.text.slice(0, 8000) })) });
  }
  const status = review?.status === "Reviewed" ? "Проверен преподавателем" : review?.status === "NeedsReview" ? "Требует пересмотра" : "Ожидает проверки";
  return <details className="question-grounding" aria-label="Основание ответа">
    <summary><strong>Основание ответа</strong> <span className="learning-badge">{status}</span></summary>
    {review?.reasons.map(reason => <p className="learning-warning" key={reason}>{reason}</p>)}
    {!g ? <button type="button" disabled={disabled || source.objectives.length === 0} onClick={() => change(initial())}>Связать с учебной целью и материалом</button> : <>
      <label>Проверяемая цель<select aria-label="Проверяемая цель" value={g.objectiveId} disabled={disabled} onChange={e => change({ ...g, objectiveId: e.target.value, objectiveText: source.objectives.find(o => o.id === e.target.value)?.text ?? "" })}><option value="">Выберите цель</option>{source.objectives.map(o => <option value={o.id} key={o.id}>{o.text}</option>)}</select></label>
      {g.objectiveText !== source.objectives.find(o => o.id === g.objectiveId)?.text && <button type="button" disabled={disabled} onClick={() => change({ ...g, objectiveText: source.objectives.find(o => o.id === g.objectiveId)?.text ?? "" })}>Принять текущую формулировку цели и проверить вопрос</button>}
      {g.evidence.map((e, i) => <div key={`${e.blockId}-${i}`} className="evidence-card">
        <label>Исходный фрагмент<select aria-label="Исходный фрагмент" disabled={disabled} value={e.blockId} onChange={event => { const block = source.blocks.find(b => b.id === event.target.value); if (block) change({ ...g, revisionId: source.revisionId, evidence: g.evidence.map((v, n) => n === i ? { blockId: block.id, quote: block.text.slice(0, 8000) } : v) }); }}><option value="">Выберите фрагмент</option>{!source.blocks.some(b => b.id === e.blockId) && <option value={e.blockId}>Удалённый фрагмент</option>}{source.blocks.map((b, n) => <option key={b.id} value={b.id}>Фрагмент {n + 1}: {b.text.slice(0, 100)}</option>)}</select></label>
        <label>Цитата<textarea aria-label="Цитата" className="resize-none" maxLength={8000} rows={3} disabled={disabled} value={e.quote} onChange={event => change({ ...g, evidence: g.evidence.map((v, n) => n === i ? { ...v, quote: event.target.value } : v) })}/></label>
        {review?.evidence[i]?.changed && <div className="evidence-diff"><div><strong>Прежний текст</strong><pre className="source-text">{review.evidence[i].originalText ?? "Основание недоступно"}</pre></div><div><strong>Текущий текст</strong><pre className="source-text">{review.evidence[i].currentText ?? "Блок удалён"}</pre></div></div>}
        <button type="button" disabled={disabled} onClick={() => change({ ...g, evidence: g.evidence.filter((_, n) => n !== i) })}>Убрать фрагмент</button>
      </div>)}
      <button type="button" disabled={disabled || g.evidence.length >= 8 || source.blocks.every(b => g.evidence.some(e => e.blockId === b.id))} onClick={() => { const block = source.blocks.find(b => !g.evidence.some(e => e.blockId === b.id)); if (block) change({ ...g, revisionId: source.revisionId, evidence: [...g.evidence, { blockId: block.id, quote: block.text.slice(0, 8000) }] }); }}>+ Исходный фрагмент</button>
      <label>Почему ответ следует из материала<textarea aria-label="Почему ответ следует из материала" className="resize-none" maxLength={5000} rows={3} disabled={disabled} value={g.explanation} onChange={e => change({ ...g, explanation: e.target.value })}/></label>
      {g.revisionId !== source.revisionId && <button type="button" disabled={disabled} onClick={rebind}>Использовать текущие фрагменты и проверить заново</button>}
      <label className="check-label"><input type="checkbox" checked={g.reviewed} disabled={disabled || source.planNeedsReview || !review || review.status === "NeedsReview"} onChange={e => onChange({ ...question, grounding: { ...g, reviewed: e.target.checked } })}/>Я проверил формулировку, правильный ответ и его основание</label>
    </>}
    <p className="hint">Совпадение цитаты проверяется автоматически. Предметную точность и однозначность ответа проверяет преподаватель.</p>
    <button type="button" disabled={disabled || source.planNeedsReview || source.objectives.length === 0} onClick={onRegenerate}>Перегенерировать только этот вопрос</button>
  </details>;
}
