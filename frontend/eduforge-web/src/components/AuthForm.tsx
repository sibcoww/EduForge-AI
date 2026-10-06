"use client";
import { useEffect, useState } from "react";
import { api, request } from "../lib/api";
export type Teacher = { id: string; name: string; email: string; isAdmin?: boolean };
export default function AuthForm({ onLogin }: { onLogin: (teacher: Teacher) => void }) {
  const [register, setRegister] = useState(false);
  const [email, setEmail] = useState(""); const [password, setPassword] = useState(""); const [name, setName] = useState("");
  const [busy, setBusy] = useState(false); const [error, setError] = useState(""); const [google, setGoogle] = useState(false);
  useEffect(() => {
    void request<{google: boolean}>("/api/auth/providers").then(value => setGoogle(value.google)).catch(() => {});
    const reason = new URLSearchParams(location.search).get("authError");
    if (reason) { setError(reason === "existing" ? "Этот email уже зарегистрирован. Войдите с паролем." : "Не удалось войти через Google. Попробуйте ещё раз."); history.replaceState(null, "", "/"); }
  }, []);
  async function submit(event: React.FormEvent) {
    event.preventDefault(); setBusy(true); setError("");
    try { onLogin(await request<Teacher>(`/api/auth/${register ? "register" : "login"}`, { method: "POST", body: JSON.stringify({ email, password, name }) })); }
    catch (ex) { setError(ex instanceof Error ? ex.message : "Не удалось войти."); }
    finally { setBusy(false); }
  }
  return <main className="auth-page"><section className="auth-card"><a className="brand" href="/">✦ EduForge <span>AI</span></a><h1>{register ? "Создайте аккаунт" : "С возвращением"}</h1><p>Ваши материалы и идеи для новых занятий.</p>{error && <p className="alert" role="alert">{error}</p>}
    <form onSubmit={submit}><fieldset disabled={busy}>{register && <label>Имя<input required maxLength={100} autoComplete="name" value={name} onChange={e => setName(e.target.value)}/></label>}<label>Email<input type="email" required maxLength={254} autoComplete="email" value={email} onChange={e => setEmail(e.target.value)}/></label><label>Пароль<input type="password" required minLength={register ? 12 : 1} maxLength={128} autoComplete={register ? "new-password" : "current-password"} value={password} onChange={e => setPassword(e.target.value)}/>{register && <small>Не менее 12 символов</small>}</label></fieldset><button className="primary" disabled={busy}>{busy ? "Подождите…" : register ? "Зарегистрироваться" : "Войти"}</button></form>
    <div className="auth-options">{google && <a href={`${api}/api/auth/google`}>Войти через Google</a>}<button type="button" disabled={busy} onClick={() => { setRegister(!register); setError(""); }}>{register ? "Уже есть аккаунт? Войти" : "Создать аккаунт"}</button></div>
  </section></main>;
}
