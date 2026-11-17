"use client";
import SearchField from "./SearchField";
import Icon from "./Icon";
import { confirmAction } from "../lib/ui-dialog";
import Form from "./Form";
import { useEffect, useRef, useState } from "react";
import GroupInvitations from "./GroupInvitations";
import CreationDialog from "./CreationDialog";
import { request } from "../lib/api";
import { useUnsavedChanges } from "./TeacherShell";
type Student = { id: string; firstName: string; lastName: string };
type Group = { id: string; name: string; description: string; students: Student[] };
type Summary = { id: string; name: string; description: string; studentCount: number };
const blank = { name: "", description: "" }; const blankStudent = { firstName: "", lastName: "" };
export default function GroupWorkspace() {
  const [groups, setGroups] = useState<Summary[]>([]); const [selected, setSelected] = useState<string | null>(null);
  const [draft, setDraft] = useState(blank); const [baseline, setBaseline] = useState(JSON.stringify(blank));
  const [students, setStudents] = useState<Student[]>([]); const [student, setStudent] = useState(blankStudent);
  const [editingStudent, setEditingStudent] = useState<string | null>(null); const [studentBaseline, setStudentBaseline] = useState(JSON.stringify(blankStudent));
  const [query, setQuery] = useState(""); const [loading, setLoading] = useState(true); const [busy, setBusy] = useState(false); const [error, setError] = useState(""); const [message, setMessage] = useState("");
  const [creating, setCreating] = useState(false);
  const previousDraft = useRef({ selected: null as string | null, draft: blank, baseline: JSON.stringify(blank), students: [] as Student[], student: blankStudent, editingStudent: null as string | null, studentBaseline: JSON.stringify(blankStudent) });
  function newItem() { previousDraft.current = { selected, draft, baseline, students, student, editingStudent, studentBaseline }; setDraft(blank); setBaseline(JSON.stringify(blank)); setSelected(null); setStudents([]); resetStudent(); setCreating(true); setError(""); setMessage(""); }
  function cancelCreation() { const p = previousDraft.current; setDraft(p.draft); setBaseline(p.baseline); setSelected(p.selected); setStudents(p.students); setStudent(p.student); setStudentBaseline(p.studentBaseline); setEditingStudent(p.editingStudent); setCreating(false); setError(""); }
  async function closeEditor() { if (creating) { if(dirty&&!await confirmAction("Закрыть черновик? Несохранённые изменения будут потеряны."))return;cancelCreation();return; } if (dirty && !await confirmAction("Закрыть окно? Несохранённые изменения будут потеряны.")) return; setSelected(null); setDraft(blank); setBaseline(JSON.stringify(blank)); setStudents([]); resetStudent(); setError(""); }
  const dirty = JSON.stringify(draft) !== baseline || JSON.stringify(student) !== studentBaseline; useUnsavedChanges(dirty);
  const fail = (ex: unknown) => setError(ex instanceof Error ? ex.message : "Не удалось выполнить запрос.");
  useEffect(() => { let cancelled = false; void request<Summary[]>("/api/groups").then(value => { if (!cancelled) setGroups(value); }).catch(ex => { if (!cancelled) fail(ex); }).finally(() => { if (!cancelled) setLoading(false); }); return () => { cancelled = true; }; }, []);
  function resetStudent() { setStudent(blankStudent); setStudentBaseline(JSON.stringify(blankStudent)); setEditingStudent(null); }
  function apply(group: Group) { const data = { name: group.name, description: group.description }; setDraft(data); setBaseline(JSON.stringify(data)); setSelected(group.id); setStudents(group.students); }
  async function canLeave() { return !dirty || await confirmAction("Открыть другую группу? Несохранённые изменения будут потеряны."); }
  async function open(id: string) {
    if (!await canLeave()) return; setBusy(true); setError("");
    try { apply(await request<Group>(`/api/groups/${id}`)); resetStudent(); setMessage(""); } catch (ex) { fail(ex); } finally { setBusy(false); }
  }
  async function save(event: React.FormEvent) {
    event.preventDefault(); setBusy(true); setError(""); setMessage("");
    try { const value = await request<Group>(`/api/groups${selected ? `/${selected}` : ""}`, { method: selected ? "PUT" : "POST", body: JSON.stringify(draft) }); apply(value); setCreating(false); setQuery(""); setGroups(current => [{ ...value, studentCount: value.students.length }, ...current.filter(g => g.id !== value.id)]); setMessage("Группа сохранена"); }
    catch (ex) { fail(ex); } finally { setBusy(false); }
  }
  async function saveStudent(event: React.FormEvent) {
    event.preventDefault(); if (!selected) return; setBusy(true); setError(""); setMessage("");
    try { const value = await request<Student>(`/api/groups/${selected}/students${editingStudent ? `/${editingStudent}` : ""}`, { method: editingStudent ? "PUT" : "POST", body: JSON.stringify(student) });
      setStudents(current => [...current.filter(s => s.id !== value.id), value]); if (!editingStudent) setGroups(current => current.map(g => g.id === selected ? { ...g, studentCount: g.studentCount + 1 } : g)); resetStudent(); setMessage("Студент сохранён");
    } catch (ex) { fail(ex); } finally { setBusy(false); }
  }
  async function removeStudent(value: Student) {
    if (!await confirmAction(`Удалить студента ${value.lastName} ${value.firstName} из группы?`)) return; setBusy(true); setError("");
    try { await request(`/api/groups/${selected}/students/${value.id}`, { method: "DELETE" }); setStudents(current => current.filter(s => s.id !== value.id)); setGroups(current => current.map(g => g.id === selected ? { ...g, studentCount: g.studentCount - 1 } : g)); if (editingStudent === value.id) resetStudent(); setMessage("Студент удалён"); }
    catch (ex) { fail(ex); } finally { setBusy(false); }
  }
  async function removeGroup() {
    if (!selected || !await confirmAction("Удалить группу вместе со списком студентов?")) return; setBusy(true); setError("");
    try { await request(`/api/groups/${selected}`, { method: "DELETE" }); setGroups(current => current.filter(g => g.id !== selected)); setSelected(null); setDraft(blank); setBaseline(JSON.stringify(blank)); setStudents([]); resetStudent(); setMessage("Группа удалена"); }
    catch (ex) { fail(ex); } finally { setBusy(false); }
  }
  async function refreshGroup(){ if (!selected) return; const value = await request<Group>(`/api/groups/${selected}`); setStudents(value.students); setGroups(current => current.map(g => g.id === value.id ? {...g,studentCount:value.students.length} : g)); }
  const editor = (<section className="editor"><div className="section-title"><h2>{selected ? "Учебная группа" : "Новая группа"}</h2>{selected && <button disabled={busy} onClick={removeGroup}>Удалить группу</button>}</div>
    <Form noValidate onSubmit={save}><fieldset disabled={busy}><label>Название<input required maxLength={100} placeholder="Например, Python-24" value={draft.name} onChange={e => setDraft({ ...draft, name: e.target.value })}/></label><label>Описание<textarea aria-label="Описание" className="resize-none" maxLength={2000} rows={3} value={draft.description} onChange={e => setDraft({ ...draft, description: e.target.value })}/></label></fieldset><button className="primary" disabled={busy}>Сохранить группу</button></Form>
    {selected && <><div className="section-title students-heading"><h2>Студенты <span>{students.length}</span></h2></div><Form noValidate onSubmit={saveStudent}><fieldset disabled={busy}><div className="fields"><label>Фамилия<input required maxLength={100} value={student.lastName} onChange={e => setStudent({ ...student, lastName: e.target.value })}/></label><label>Имя<input required maxLength={100} value={student.firstName} onChange={e => setStudent({ ...student, firstName: e.target.value })}/></label></div></fieldset><div className="row-actions"><button disabled={busy}>{editingStudent ? "Сохранить студента" : "+ Добавить студента"}</button>{editingStudent && <button type="button" disabled={busy} onClick={resetStudent}>Отмена</button>}</div></Form>
    {students.length === 0 ? <p className="empty">Пока никто не добавлен.</p> : <div className="student-list">{[...students].sort((a, b) => `${a.lastName} ${a.firstName}`.localeCompare(`${b.lastName} ${b.firstName}`, "ru")).map((s, i) => <div className="student-row" key={s.id}><span>{i + 1}. {s.lastName} {s.firstName}</span><div className="row-actions"><button disabled={busy} onClick={async () => { if (JSON.stringify(student) !== studentBaseline && !await confirmAction("Отменить изменения студента?")) return; const value = { firstName: s.firstName, lastName: s.lastName }; setStudent(value); setStudentBaseline(JSON.stringify(value)); setEditingStudent(s.id); }}>Изменить</button><button disabled={busy} onClick={() => removeStudent(s)}>Удалить</button></div></div>)}</div>}</>}
    <p className="hint" role="status">{message}</p>{selected && <GroupInvitations key={selected} id={selected} onChange={()=>{void refreshGroup();}}/>}</section>);
  const visibleGroups=groups.filter(g=>g.name.toLowerCase().includes(query.toLowerCase()));
  return <><section className="intro"><p className="eyebrow">УЧЕБНЫЕ ГРУППЫ</p><h1>Знайте каждого студента</h1><p>Создавайте группы и ведите списки студентов без отдельных аккаунтов.</p></section>{error && <p className="alert" role="alert">{error}</p>}
    {!creating&&!selected&&message&&<p className="notice" role="status">{message}</p>}<div className="columns catalogue"><section className="library"><div className="section-title"><h2>Мои группы <span>{groups.length}</span></h2><button disabled={busy} type="button" className="primary create-button" onClick={newItem}><Icon name="plus" size={18}/> Новая группа</button></div><SearchField label="Поиск групп" value={query} onChange={setQuery}/>
    {loading ? <p className="empty">Загружаем группы…</p> : visibleGroups.length === 0 ? <p className="empty">{query?"По запросу ничего не найдено.":"Создайте группу и добавьте студентов."}</p> : <div className="catalog-grid">{visibleGroups.map(g => <button disabled={busy} className={`material ${selected === g.id ? "selected" : ""}`} key={g.id} onClick={() => open(g.id)}><div className="catalog-card-top"><span className="tag">Учебная группа</span><Icon name="users" size={26}/></div><h3>{g.name}</h3><p>{g.description}</p><small>Студентов: {g.studentCount}</small><span className="card-open">Открыть группу <Icon name="arrow" size={17}/></span></button>)}</div>}</section>
    {creating || selected ? <CreationDialog title={selected ? "Редактирование группы" : "Новая группа"} busy={busy} onClose={closeEditor}>{error && <p className="alert" role="alert">{error}</p>}{message && <p className="notice" role="status">{message}</p>}{editor}</CreationDialog> : null}</div></>;
}
