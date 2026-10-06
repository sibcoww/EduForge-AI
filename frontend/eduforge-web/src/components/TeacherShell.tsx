"use client";
import { createContext, useContext, useEffect, useState } from "react";
import AuthForm, { type Teacher } from "./AuthForm";
import { ApiError, request } from "../lib/api";
import TeacherNavigation from "./TeacherNavigation";
const DirtyContext = createContext<(dirty: boolean) => void>(() => {});
export function useUnsavedChanges(dirty: boolean) {
  const markDirty = useContext(DirtyContext);
  useEffect(() => { markDirty(dirty); return () => markDirty(false); }, [dirty, markDirty]);
  useEffect(() => {
    if (!dirty) return;
    const warn = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener("beforeunload", warn); return () => window.removeEventListener("beforeunload", warn);
  }, [dirty]);
}
export default function TeacherShell({ active, children }: { active: "tests" | "groups" | "sessions" | "library" | "admin"; children: React.ReactNode }) {
  const [teacher, setTeacher] = useState<Teacher | null>(null); const [checking, setChecking] = useState(true);
  const [error, setError] = useState(""); const [busy, setBusy] = useState(false); const [dirty, setDirty] = useState(false);
  useEffect(() => { void request<Teacher>("/api/auth/me").then(setTeacher).catch(ex => { if (!(ex instanceof ApiError && ex.status === 401)) setError("Не удалось подключиться к серверу."); }).finally(() => setChecking(false)); }, []);
  async function logout() {
    if (dirty && !window.confirm("Выйти? Несохранённые изменения будут потеряны.")) return;
    setBusy(true); setError("");
    try { await request("/api/auth/logout", { method: "POST" }); setTeacher(null); }
    catch (ex) { if (ex instanceof ApiError && ex.status === 401) setTeacher(null); else setError(ex instanceof Error ? ex.message : "Не удалось выйти."); }
    finally { setBusy(false); }
  }
  if (checking) return <p className="empty">Открываем ваше пространство…</p>;
  if (!teacher) return <>{error && <p className="alert" role="alert">{error}</p>}<AuthForm onLogin={value => { setTeacher(value); setError(""); }}/></>;
  return <DirtyContext.Provider value={setDirty}><div className="workspace"><aside><TeacherNavigation active={active} dirty={dirty} isAdmin={teacher.isAdmin}/></aside><main><header><span>{teacher.name} / {active === "tests" ? "Тесты" : active === "groups" ? "Группы" : active === "sessions" ? "Сессии и результаты" : active === "admin" ? "Аккаунты" : "Общая библиотека"}</span><button disabled={busy} onClick={logout}>Выйти</button></header>{error && <p className="alert" role="alert">{error}</p>}{children}</main></div></DirtyContext.Provider>;
}
