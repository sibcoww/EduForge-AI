export const questionTypes = { SingleChoice: "Один правильный ответ", MultipleChoice: "Несколько правильных ответов", TrueFalse: "Да / Нет", OpenText: "Открытый ответ", ShortText: "Короткий ответ" };
export type QuestionType = keyof typeof questionTypes;
export type Grounding = { revisionId: string; objectiveId: string; objectiveText: string; evidence: { blockId: string; quote: string }[]; explanation: string; reviewed: boolean };
export type Question = { type: QuestionType; text: string; points: number; modelAnswer: string; options: { text: string; isCorrect: boolean }[]; grounding?: Grounding | null };
export type QuizDraft = { title: string; materialId: string | null; questions: Question[]; controlled?: boolean; generationRunId?: string | null };
export type Quiz = QuizDraft & { id: string; updatedAt: string };
export type QuizSummary = { id: string; title: string; materialId: string | null; questionCount: number; totalPoints: number };
export type SourceMaterial = { id: string; title: string };
export function plainAiText(value:string):string {
  return value.split(/(```[\s\S]*?```|`[^`\r\n]*`)/g).map((part,i)=>i%2?part:part.replace(/(?<![\w*])\*\*(\S(?:[^\r\n]*?\S)?)\*\*(?![\w*])/g,"$1")).join("");
}
export function newQuestion(type: QuestionType = "SingleChoice"): Question {
  return { type, text: "", points: 1, modelAnswer: "", options: type === "OpenText" || type === "ShortText" ? [] : type === "TrueFalse" ? [{ text: "Да", isCorrect: true }, { text: "Нет", isCorrect: false }] : [{ text: "", isCorrect: false }, { text: "", isCorrect: false }] };
}
export function cleanQuiz(quiz: QuizDraft): QuizDraft {
  return { title: plainAiText(quiz.title), materialId: quiz.materialId, controlled: !!quiz.controlled, generationRunId: quiz.generationRunId ?? null, questions: quiz.questions.map(q => ({ type: q.type, text: plainAiText(q.text), points: q.points, modelAnswer: plainAiText(q.modelAnswer), options: q.options.map(o => ({ text: plainAiText(o.text), isCorrect: o.isCorrect })), ...(q.grounding ? { grounding: {...q.grounding,explanation:plainAiText(q.grounding.explanation)} } : {}) })) };
}
