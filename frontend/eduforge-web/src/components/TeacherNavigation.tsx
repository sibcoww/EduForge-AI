"use client";
import Link from "next/link";
import { useRouter } from "next/navigation";
export function allowNavigation(dirty: boolean) { return !dirty || window.confirm("Перейти в другой раздел? Несохранённые изменения будут потеряны."); }
export default function TeacherNavigation({ active, dirty, isAdmin = false }: { active: "materials" | "tests" | "groups" | "sessions" | "library" | "admin"; isAdmin?: boolean; dirty: boolean }) {
  const router = useRouter();
  const guard = (event: { preventDefault: () => void }) => { if (!allowNavigation(dirty)) event.preventDefault(); };
  const navigate = (href: string) => { if (allowNavigation(dirty)) router.push(href); };
  return <><Link className="brand" href="/" onNavigate={guard}>✦ EduForge <span>AI</span></Link><p className="caption">РАБОЧЕЕ ПРОСТРАНСТВО</p><nav><button type="button" className={active === "materials" ? "active" : ""} aria-current={active === "materials" ? "page" : undefined} onClick={() => navigate("/")}>▤ Материалы</button><button type="button" className={active === "tests" ? "active" : ""} aria-current={active === "tests" ? "page" : undefined} onClick={() => navigate("/tests")}>✓ Тесты</button><button type="button" className={active === "groups" ? "active" : ""} aria-current={active === "groups" ? "page" : undefined} onClick={() => navigate("/groups")}>♧ Группы</button><button type="button" className={active === "sessions" ? "active" : ""} aria-current={active === "sessions" ? "page" : undefined} onClick={() => navigate("/sessions")}>◷ Сессии и результаты</button><button type="button" className={active === "library" ? "active" : ""} aria-current={active === "library" ? "page" : undefined} onClick={() => navigate("/library")}>▥ Общая библиотека</button>{isAdmin && <button type="button" className={active === "admin" ? "active" : ""} onClick={() => navigate("/admin")}>⚙ Аккаунты</button>}</nav><div className="aside-note">Рабочее пространство преподавателя</div></>;
}
