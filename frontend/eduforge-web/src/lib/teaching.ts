export const questionTypes = { SingleChoice: "Один правильный ответ", MultipleChoice: "Несколько правильных ответов", TrueFalse: "Да / Нет", OpenText: "Открытый ответ", ShortText: "Короткий ответ" };
export type QuestionType = keyof typeof questionTypes;
export type Question = { type: QuestionType; text: string; points: number; modelAnswer: string; options: { text: string; isCorrect: boolean }[] };
export type QuizDraft = { title: string; materialId: string | null; questions: Question[] };
export type Quiz = QuizDraft & { id: string; updatedAt: string };
export type QuizSummary = { id: string; title: string; materialId: string | null; questionCount: number; totalPoints: number };
export type SourceMaterial = { id: string; title: string };
export function newQuestion(type: QuestionType = "SingleChoice"): Question {
  return { type, text: "", points: 1, modelAnswer: "", options: type === "OpenText" || type === "ShortText" ? [] : type === "TrueFalse" ? [{ text: "Да", isCorrect: true }, { text: "Нет", isCorrect: false }] : [{ text: "", isCorrect: false }, { text: "", isCorrect: false }] };
}
export function cleanQuiz(quiz: QuizDraft): QuizDraft {
  return { title: quiz.title, materialId: quiz.materialId, questions: quiz.questions.map(q => ({ type: q.type, text: q.text, points: q.points, modelAnswer: q.modelAnswer, options: q.options.map(o => ({ text: o.text, isCorrect: o.isCorrect })) })) };
}
