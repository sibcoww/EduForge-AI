"use client";
import Form from "./Form";
import { useEffect, useState } from "react";
import { api, request } from "../lib/api";
import Icon, { Brand } from "./Icon";
export type Teacher = { id: string; name: string; email: string; isAdmin?: boolean };
export default function AuthForm({ onLogin }: { onLogin: (teacher: Teacher) => void }) {
  const [register, setRegister] = useState(false);
  const [email, setEmail] = useState(""); const [password, setPassword] = useState(""); const [name, setName] = useState("");
  const [busy, setBusy] = useState(false); const [error, setError] = useState(""); const [google, setGoogle] = useState(false);
  const [showPassword,setShowPassword]=useState(false);
  useEffect(()=>{document.title=register?"Регистрация — EduForge AI":"Вход — EduForge AI";},[register]);
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
  return <main className="auth-page"><section className="auth-story"><a className="brand" href="/"><Brand/></a><div className="auth-story-content"><p className="eyebrow">МАСТЕРСКАЯ ПРЕПОДАВАТЕЛЯ</p><h2>Больше идей.<br/>Больше открытий.</h2><p>Соберите знания в урок,<br/>который захочется изучать.</p><div className="lesson-stack" aria-hidden="true"><div className="lesson-sheet lesson-sheet-back"><Icon name="test" size={28}/><span>Тест по материалу</span><i/><i/><i/></div><div className="lesson-sheet"><span className="sheet-subject">НОВЫЙ УРОК</span><Icon name="book" size={34}/><h3>От объяснения<br/>к пониманию.</h3><i/><i/><div className="sheet-note"><Icon name="spark" size={16}/> С вашей экспертизой и AI</div></div></div></div><small>Материалы · Тесты · Занятия</small></section><section className="auth-card"><div className="auth-mobile-brand"><Brand/></div><h1>{register ? "Создайте аккаунт" : "С возвращением"}</h1><p>Войдите, чтобы продолжить работу над уроками.</p>{error && <p className="alert" role="alert">{error}</p>}
    <Form noValidate onSubmit={submit}><fieldset disabled={busy}>{register && <label>Имя<input required maxLength={100} autoComplete="name" value={name} onChange={e => setName(e.target.value)}/></label>}<label>Email<input type="email" required maxLength={254} autoComplete="email" value={email} onChange={e => setEmail(e.target.value)}/></label><label>Пароль<span className="password-field"><input aria-label="Пароль" type={showPassword?"text":"password"} required minLength={register ? 12 : 1} maxLength={128} autoComplete={register ? "new-password" : "current-password"} value={password} onChange={e => setPassword(e.target.value)}/><button type="button" aria-label={showPassword?"Скрыть пароль":"Показать пароль"} aria-pressed={showPassword} onClick={()=>setShowPassword(!showPassword)}>{showPassword?"Скрыть":"Показать"}</button></span>{register && <small>Не менее 12 символов</small>}</label></fieldset><button className="primary" disabled={busy}>{busy ? "Подождите…" : register ? "Зарегистрироваться" : "Войти"}</button></Form>
    <div className="auth-options"><a href="/reset">Забыли пароль?</a>{google && <a href={`${api}/api/auth/google`} className="google-login"><svg aria-hidden="true" viewBox="0 0 24 24" width="19" height="19"><path fill="currentColor" d="M21.6 12.2c0-.7-.1-1.4-.2-2.1H12v4h5.4a4.6 4.6 0 0 1-2 3c-.9.6-2 1-3.4 1a6.2 6.2 0 1 1 4.4-10.6l3-3A10.4 10.4 0 1 0 12 22.4c3 0 5.6-1 7.3-2.9 1.6-1.7 2.3-4.1 2.3-7.3z"/></svg>Войти через Google</a>}<button type="button" disabled={busy} onClick={() => { setRegister(!register); setError(""); }}>{register ? "Уже есть аккаунт? Войти" : "Создать аккаунт"}</button></div>
  </section></main>;
}
