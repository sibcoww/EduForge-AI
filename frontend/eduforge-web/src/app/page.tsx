"use client";
import Form from "../components/Form";
import { useEffect, useRef, useState } from "react";
import { subjectTone } from "../lib/catalog-style";
import WorkspaceHeader from "../components/WorkspaceHeader";
import SearchField from "../components/SearchField";
import Icon from "../components/Icon";
import { confirmAction } from "../lib/ui-dialog";
import AuthForm, { type Teacher } from "../components/AuthForm";
import MaterialEditor from "../components/MaterialEditor";
import SourcePicker, { type SourceSelection } from "../components/SourcePicker";
import GenerationPassport, { MaterialSourceEvidence } from "../components/GenerationPassport";
import CopyUpdates from "../components/CopyUpdates";
import CreationDialog from "../components/CreationDialog";
import PublicationControls from "../components/PublicationControls";
import { ApiError, request } from "../lib/api";
import { useRouter } from "next/navigation";
import { materialFingerprint } from "../lib/material-content";
import TeacherNavigation, { allowNavigation } from "../components/TeacherNavigation";
type Material = { id: string; title: string; subject: string; content: string; level: string; language: string; updatedAt: string; generationRunId: string | null };
const blank = { title: "", subject: "", content: "", level: "Начинающий", language: "Русский", generationRunId: null as string | null };
const comparableContent = materialFingerprint;
export default function Home() {
  const router = useRouter();
  const [teacher, setTeacher] = useState<Teacher | null>(null);
  const [checking, setChecking] = useState(true);
  const currentTeacher = useRef<string | null>(null);
  const loadedOwner = useRef<string | null>(null);
  currentTeacher.current = teacher?.id ?? null;
  const [materials, setMaterials] = useState<Material[]>([]);
  const [draft, setDraft] = useState(blank);
  const [selected, setSelected] = useState<string | null>(null);
  const [query, setQuery] = useState(""); const [requirements, setRequirements] = useState("");
  const [loading, setLoading] = useState(true); const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState(""); const [error, setError] = useState("");
  const [creating, setCreating] = useState(false);
  const [sourceSelection, setSourceSelection] = useState<SourceSelection[]>([]);
  const previousDraft = useRef({ selected: null as string | null, draft: blank, requirements: "", sourceSelection: [] as SourceSelection[] });
  function newItem() { previousDraft.current = { selected, draft, requirements, sourceSelection }; setSourceSelection([]); setSelected(null); setDraft(blank); setRequirements(""); setCreating(true); setError(""); setMessage(""); }
  function cancelCreation() { const p = previousDraft.current; setSelected(p.selected); setDraft(p.draft); setRequirements(p.requirements); setSourceSelection(p.sourceSelection); setCreating(false); setError(""); }
  const original = materials.find(item => item.id === selected) ?? blank;
  async function closeEditor() { if (creating) { if(dirty&&!await confirmAction("Закрыть черновик? Несохранённые изменения будут потеряны."))return;cancelCreation();return; } if (dirty && !await confirmAction("Закрыть окно? Несохранённые изменения будут потеряны.")) return; setSelected(null); setDraft(blank); setRequirements(""); setError(""); }
  const dirty = draft.title !== original.title || draft.subject !== original.subject || comparableContent(draft.content) !== comparableContent(original.content) || draft.level !== original.level || draft.language !== original.language;
  useEffect(() => {
    if (!dirty) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); };
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [dirty]);
  function failure(ex: unknown) {
    if (ex instanceof ApiError && ex.status === 401) { setTeacher(null); setMaterials([]); setDraft(blank); setSelected(null); }
    setError(ex instanceof Error ? ex.message : "Проверьте подключение к серверу.");
  }
  useEffect(() => { void request<Teacher>("/api/auth/me").then(setTeacher).catch(ex => { if (!(ex instanceof ApiError && ex.status === 401)) setError("Не удалось подключиться к серверу."); }).finally(() => setChecking(false)); }, []);
  async function load() {
    const owner = teacher?.id;
    setLoading(true); setError("");
    try { const result = await request<Material[]>("/api/materials"); if (currentTeacher.current === owner) { setMaterials(result); if (loadedOwner.current !== owner) { loadedOwner.current = owner ?? null; const id = new URLSearchParams(window.location.search).get("materialId"); const material = result.find(item => item.id === id); if (material) { setSelected(material.id); setDraft(material); } } } }
    catch (ex) { if (currentTeacher.current === owner) failure(ex); } finally { if (currentTeacher.current === owner) setLoading(false); }
  }
  useEffect(() => { if (teacher) void load(); }, [teacher]);
  async function save(event: React.FormEvent) {
    event.preventDefault(); setBusy(true); setError(""); setMessage("");
    try {
      const material = await request<Material>(`/api/materials${selected ? `/${selected}` : ""}`, { method: selected ? "PUT" : "POST", body: JSON.stringify(draft) });
      setSelected(material.id); setDraft(material); setCreating(false); setQuery("");
      setMaterials(current => [material, ...current.filter(item => item.id !== material.id)]);
      setMessage("Материал сохранён");
    } catch (ex) { failure(ex); } finally { setBusy(false); }
  }
  async function generate() {
    if (!draft.title.trim() || !draft.subject.trim()) { setError("Укажите тему в названии и предмет для генерации."); return; }
    if (draft.content && !await confirmAction("Заменить содержание черновика новым AI-материалом?")) return;
    setBusy(true); setError(""); setMessage("AI готовит материал…");
    try {
      const parameters = { subject: draft.subject, topic: draft.title, level: draft.level, language: draft.language, requirements };
      const result = await request<{content: string; generationRunId: string}>(sourceSelection.length ? "/api/ai/material/from-sources" : "/api/ai/material", { method: "POST", body: JSON.stringify(sourceSelection.length ? { material: parameters, sources: sourceSelection } : parameters) });
      setDraft(current => ({ ...current, content: result.content, generationRunId: result.generationRunId })); setMessage("Материал готов. Проверьте его и сохраните.");
    } catch (ex) { setMessage(""); failure(ex); } finally { setBusy(false); }
  }
  async function logout() {
    if (dirty && !await confirmAction("Выйти? Несохранённые изменения будут потеряны.")) return;
    setBusy(true);
    try { await request("/api/auth/logout", { method: "POST" }); setTeacher(null); setMaterials([]); setDraft(blank); setSelected(null); setRequirements(""); setMessage(""); }
    catch (ex) { failure(ex); } finally { setBusy(false); }
  }
  if (checking) return <p className="empty">Открываем ваше пространство…</p>;
  if (!teacher) return <>{error && <p className="alert" role="alert">{error}</p>}<AuthForm onLogin={value => { setTeacher(value); setError(""); }}/></>;
  async function removeMaterial(){if(!selected)return;if(!await confirmAction(`Удалить материал «${draft.title}»? Публикация и ссылка закроются. Связанные тесты сохранятся без исходного урока и контроля оснований. Сессии и результаты останутся.`))return;setBusy(true);setError("");try{await request(`/api/materials/${selected}`,{method:"DELETE"});setMaterials(current=>current.filter(m=>m.id!==selected));setSelected(null);setDraft(blank);setRequirements("");setSourceSelection([]);setMessage("Материал удалён");}catch(ex){failure(ex);}finally{setBusy(false);}}
  const editor = (<section className="editor"><div className="section-title"><h2>{selected ? "Редактирование" : "Новый материал"}</h2>{selected && <button type="button" className="action-link" onClick={async () => { if (await allowNavigation(dirty)) router.push(`/tests?materialId=${selected}`); }}>Тесты по материалу →</button>}{selected&&<button type="button" className="danger-button" disabled={busy} onClick={removeMaterial}>Удалить материал</button>}</div>
        <Form noValidate onSubmit={save}><fieldset disabled={busy}><label>Название / тема<input required maxLength={200} placeholder="Например, цикл for в Python" value={draft.title} onChange={e => setDraft({ ...draft, title: e.target.value })}/></label><label>Предмет<input required maxLength={100} placeholder="Python" value={draft.subject} onChange={e => setDraft({ ...draft, subject: e.target.value })}/></label><div className="fields"><label>Уровень<select aria-label="Уровень" value={draft.level} onChange={e => setDraft({ ...draft, level: e.target.value })}><option>Начинающий</option><option>Средний</option><option>Продвинутый</option></select></label><label>Язык<select aria-label="Язык" value={draft.language} onChange={e => setDraft({ ...draft, language: e.target.value })}><option>Русский</option><option>Қазақша</option><option>English</option></select></label></div><label>Пожелания для AI<textarea aria-label="Пожелания для AI" className="resize-none" rows={2} maxLength={4000} placeholder="Добавить примеры и вопросы для самопроверки" value={requirements} onChange={e => setRequirements(e.target.value)}/></label><SourcePicker key={selected ?? "new"} selected={sourceSelection} onChange={setSourceSelection} disabled={busy}/><button type="button" onClick={generate}>{sourceSelection.length ? "✦ Сгенерировать по выбранным источникам" : "✦ Сгенерировать с AI по теме"}</button></fieldset>
        <p className="editor-label">Содержание</p><MaterialEditor content={draft.content} disabled={busy} onChange={content => setDraft(current => ({ ...current, content }))}/><footer><span role="status">{message}</span><button className="primary" disabled={busy}>{busy ? "Подождите…" : "Сохранить материал"}</button></footer></Form>
        {draft.generationRunId && <GenerationPassport key={draft.generationRunId} id={draft.generationRunId}/>}
        {selected && draft.generationRunId && <MaterialSourceEvidence materialId={selected} refreshKey={materials.find(m => m.id === selected)?.updatedAt ?? ""}/>}
        {selected && <CopyUpdates key={selected+"copy"} id={selected} kind="material" disabled={busy||dirty} onUpdated={()=>{setSelected(null);setDraft(blank);void load();}}/>}
        {selected && <PublicationControls key={selected} materialId={selected} disabled={busy || dirty}/>}
      </section>);
  const filtered = materials.filter(item => `${item.title} ${item.subject}`.toLowerCase().includes(query.toLowerCase()));
  return <div className="workspace">
    <aside><TeacherNavigation active="materials" dirty={dirty} isAdmin={teacher.isAdmin} name={teacher.name}/></aside>
    <main><WorkspaceHeader section="materials" name={teacher.name} busy={busy} onLogout={logout}/>
      <section className="intro"><p className="eyebrow">БИБЛИОТЕКА ПРЕПОДАВАТЕЛЯ</p><h1>От идеи —<br/><span>к готовому уроку.</span></h1><p>Ваши материалы, наглядные объяснения и новые открытия.</p></section>
      {error && <div role="alert" className="alert">{error}</div>}
      {!selected&&!creating&&message&&<p className="notice" role="status">{message}</p>}<div className="columns catalogue"><section className="library"><div className="section-title"><h2>Мои материалы <span>{materials.length}</span></h2><button disabled={busy} type="button" className="primary create-button" onClick={newItem}><Icon name="plus" size={18}/> Новый материал</button></div>
        <SearchField label="Поиск материалов" placeholder="Найти материал по названию или предмету" value={query} onChange={setQuery}/>
        {loading ? <p className="empty">Загружаем библиотеку…</p> : filtered.length === 0 ? <div className="empty"><strong>{query ? "Ничего не найдено" : "Здесь начнётся ваша библиотека"}</strong><p>{query ? "Попробуйте другой запрос." : "Нажмите «+ Новый», чтобы создать первый материал."}</p><button onClick={load}>Обновить</button></div> : <div className="catalog-grid">{filtered.map(item => <button disabled={busy} className={`material ${selected === item.id ? "selected" : ""}`} key={item.id} data-tone={subjectTone(item.subject)} onClick={async () => { if (dirty && !await confirmAction("Открыть материал? Несохранённые изменения будут потеряны.")) return; setSelected(item.id); setDraft(item); setSourceSelection([]); setMessage(""); }}><div className="catalog-card-top"><span className="tag">{item.subject}</span><Icon name="book" size={26}/></div><h3>{item.title}</h3><small>{item.level} · {item.language} · {new Date(item.updatedAt).toLocaleDateString("ru-RU")}</small><span className="card-open">Открыть материал <Icon name="arrow" size={17}/></span></button>)}</div>}
      </section>{creating || selected ? <CreationDialog title={selected ? "Редактирование материала" : "Новый материал"} busy={busy} onClose={closeEditor}>{error && <p className="alert" role="alert">{error}</p>}{message && <p className="notice" role="status">{message}</p>}{editor}</CreationDialog> : null}</div>
    </main></div>;
}
