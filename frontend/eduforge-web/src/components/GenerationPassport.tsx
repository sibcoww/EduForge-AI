"use client";
import { useEffect, useState } from "react";
import Icon from "./Icon";
import { request } from "../lib/api";
export const generationKinds: Record<string, string> = { LearningObjectives: "Предложение учебных целей", Material: "Материал по теме", SourceMaterial: "Материал по источникам", Test: "Обычная генерация теста", ControlledTest: "Контролируемая генерация теста" };
export type Evaluation = { experiment: string; preparationMinutes: number | null; correctness: number | null; clarity: number | null; alignment: number | null; correctedQuestions: number | null; note: string };
export type RunSummary = { id: string; kind: string; templateVersion: string; model: string; status: string; startedAt: number; durationMs: number | null; inputTokens: number | null; outputTokens: number | null };
export type RunDetail = RunSummary & { parameters: unknown; sources: unknown; result: unknown; failure: string; evaluation: Evaluation };
export default function GenerationPassport({ id }: { id: string }) {
  const [value, setValue] = useState<RunDetail | null>(null); const [error, setError] = useState("");
  useEffect(() => { const controller = new AbortController(); void request<RunDetail>(`/api/generations/${id}`, { signal: controller.signal }).then(setValue).catch(e => { if (!controller.signal.aborted) setError(e.message); }); return () => controller.abort(); }, [id]);
  const labels:Record<string,string>={subject:"Предмет",topic:"Тема",title:"Название",requirements:"Пожелания",level:"Уровень",language:"Язык",questioncount:"Количество вопросов",totalpoints:"Общий балл",difficulty:"Сложность",types:"Типы вопросов",materialid:"Исходный материал"};
  const params=value?.parameters&&typeof value.parameters==="object"?Object.entries(value.parameters).filter(([key,item])=>labels[key.toLowerCase()]&&key.toLowerCase()!=="materialid"&&item!==null&&item!==""):[];
  return <details className="generation-passport"><summary><Icon name="spark" size={17}/><span>Сведения о генерации</span></summary>{error&&<p className="alert" role="alert">{error}</p>}{value?<>
    <p className="hint">{generationKinds[value.kind]??value.kind} · {new Date(value.startedAt).toLocaleString("ru-RU")}</p>
    <dl className="passport-data">{params.map(([key,item])=><div key={key}><dt>{labels[key.toLowerCase()]}</dt><dd>{Array.isArray(item)?item.join(", "):typeof item==="object"?"Задано при генерации":String(item)}</dd></div>)}</dl>
    <p className="hint">Это условия создания исходного черновика. Ваши последующие правки сохраняются в самом материале или тесте.</p>
    <details className="generation-technical"><summary>Технические сведения</summary><dl className="passport-data"><div><dt>Модель</dt><dd>{value.model||"Не указана"}</dd></div><div><dt>Версия инструкции</dt><dd>{value.templateVersion}</dd></div><div><dt>Время генерации</dt><dd>{value.durationMs===null?"Нет данных":`${(value.durationMs/1000).toLocaleString("ru-RU",{maximumFractionDigits:1})} с`}</dd></div><div><dt>Токены: запрос / ответ</dt><dd>{value.inputTokens??"Нет данных"} / {value.outputTokens??"Нет данных"}</dd></div></dl><p className="hint">Время включает запрос к AI и проверку ответа, без работы преподавателя. Оценки и экспорт доступны в разделе «История генераций».</p></details>
  </>:!error&&<p className="hint" role="status">Загружаем сведения…</p>}</details>;

}

type SourceEvidence = { blocks: { id: string; originalText: string | null; currentText: string | null; changed: boolean; evidence: { documentName: string; label: string; quote: string; sourceText: string }[] }[] };
export function MaterialSourceEvidence({ materialId, refreshKey }: { materialId: string; refreshKey: string }) {
  const [value, setValue] = useState<SourceEvidence | null>(null); const [error, setError] = useState("");
  useEffect(() => { const controller = new AbortController(); void request<SourceEvidence>(`/api/materials/${materialId}/source-evidence`, { signal: controller.signal }).then(setValue).catch(e => { if (!controller.signal.aborted) setError(e.message); }); return () => controller.abort(); }, [materialId, refreshKey]);
  if (!value?.blocks.length && !error) return null;
  return <details className="learning-panel"><summary>Основания учебного материала</summary><p className="hint">Цитаты относятся к исходному AI-черновику. После редактирования отмеченные блоки нужно проверить заново. Совпадение цитаты не доказывает правильность вывода.</p>{error && <p className="alert">{error}</p>}{value?.blocks.map((block, i) => <article className="evidence-card" key={block.id}><h3>Блок {i + 1}</h3>{block.changed && <p className="learning-warning">Блок изменён или удалён после генерации. Проверьте связь с источником.</p>}<pre className="source-text">{block.currentText ?? block.originalText}</pre>{block.evidence.map((e, n) => <blockquote key={n}><strong>{e.documentName} · {e.label}</strong><pre className="source-text">{e.quote}</pre><details><summary>Показать исходный фрагмент</summary><pre className="source-text">{e.sourceText}</pre></details></blockquote>)}</article>)}</details>;
}
