"use client";
import { useEffect, useState } from "react";
import TeacherShell from "../../components/TeacherShell";
import CreationDialog from "../../components/CreationDialog";
import { ApiError, request } from "../../lib/api";
type Account = {id:string;name:string;email:string;materials:number;tests:number;groups:number;sessions:number};
function Accounts() {
 const [users,setUsers]=useState<Account[]>([]), [error,setError]=useState(""), [loading,setLoading]=useState(true), [target,setTarget]=useState<Account|null>(null), [confirmation,setConfirmation]=useState(""), [deleteData,setDeleteData]=useState(false), [busy,setBusy]=useState(false);
 async function load(){try{setUsers(await request<Account[]>("/api/admin/users"));}catch(e){setError(e instanceof ApiError && e.status === 403 ? "Нет доступа. Войдите в аккаунт администратора." : e instanceof Error?e.message:"Не удалось загрузить аккаунты");}finally{setLoading(false);}}
 useEffect(()=>{void load();},[]);
 async function remove(){if(!target)return;setBusy(true);setError("");try{await request("/api/admin/users/"+target.id+"?deleteData="+deleteData,{method:"DELETE"});setUsers(current=>current.filter(x=>x.id!==target.id));setTarget(null);}catch(e){setError(e instanceof Error?e.message:"Не удалось удалить аккаунт");}finally{setBusy(false);}}
 const hasData=target && target.materials+target.tests+target.groups+target.sessions>0;
 return <><section className="intro"><h1>Управление аккаунтами</h1><p>Удаление аккаунта завершает его доступ к сайту.</p></section>{error&&<p className="alert" role="alert">{error}</p>}{loading?<p>Загрузка…</p>:users.map(u=><section className="editor" key={u.id}><div className="section-title"><h2>{u.name}</h2><button onClick={()=>{setTarget(u);setConfirmation("");setDeleteData(false);setError("");}}>Удалить аккаунт</button></div><p>{u.email}</p><p>Материалы: {u.materials} · Тесты: {u.tests} · Группы: {u.groups} · Сессии: {u.sessions}</p></section>)}{target&&<CreationDialog title="Удаление аккаунта" busy={busy} onClose={()=>setTarget(null)}><section className="editor"><p>Удалить {target.email}? Это действие нельзя отменить.</p>{hasData&&<label className="check-label"><input type="checkbox" checked={deleteData} onChange={e=>setDeleteData(e.target.checked)}/>Удалить также все материалы, тесты, группы и результаты сессий этого аккаунта</label>}<label>Введите email аккаунта для подтверждения<input value={confirmation} onChange={e=>setConfirmation(e.target.value)}/></label>{error&&<p className="alert">{error}</p>}<button disabled={busy||confirmation!==target.email||!!hasData&&!deleteData} onClick={remove}>Удалить навсегда</button></section></CreationDialog>}</>;
}
export default function AdminPage(){return <TeacherShell active="admin"><Accounts/></TeacherShell>;}
