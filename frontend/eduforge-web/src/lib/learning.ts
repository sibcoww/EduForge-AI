export type LearningObjective = { id: string; text: string };
export type SourceBlock = { id: string; kind: string; text: string };
export type LearningContext = {
  materialId: string; revisionId: string; blocks: SourceBlock[]; planVersion: string;
  objectives: LearningObjective[]; planNeedsReview: boolean;
};
export type QuestionReview = {
  index: number; status: "NeedsReview" | "Unreviewed" | "Reviewed"; reasons: string[];
  evidence: { blockId: string; quote: string; originalText: string | null; currentText: string | null; changed: boolean }[];
};
export type QuizAnalysis = {
  source: LearningContext | null; questions: QuestionReview[];
  coverage: { id: string; text: string; questionCount: number; reviewedCount: number; points: number }[];
  qualityWarnings: { index: number; code: string; message: string }[]; coveragePercent: number | null; ready: boolean; materialChanged: boolean;
};
