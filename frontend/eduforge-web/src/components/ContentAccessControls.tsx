"use client";
import { useEffect,useState } from "react";
import { request } from "../lib/api";
import { confirmAction } from "../lib/ui-dialog";
type Visibility="Private"|"Unlisted"|"Public";
type Access={visibility:Visibility;version:string|null;published:boolean;publishedAt:number|null;status?:string;reason?:string;sharePath:string|null};
const choices:{value:Visibility;label:string;description:string}[]=[
 {value:"Private",label:"Частный",description:"Доступен только вам."},
 {value:"Unlisted",label:"По ссылке",description:"Для вошедших преподавателей со ссылкой. Не виден в общей библиотеке."},
 {value:"Public",label:"Публичный",description:"В общей библиотеке после модерации."}
];
export default function ContentAccessControls({id,kind,disabled=false}:{id:string;kind:"material"|"test";disabled?:boolean}){
 const [state,setState]=useState<Access|null>(null),[visibility,setVisibility]=useState<Visibility>("Private"),[busy,setBusy]=useState(false),[error,setError]=useState(""),[message,setMessage]=useState(""),[reload,setReload]=useState(0);
 useEffect(()=>{const controller=new AbortController();setState(null);setError("");setMessage("");void request<Access>(`/api/access/${kind}/${id}`,{signal:controller.signal}).then(value=>{if(!controller.signal.aborted){setState(value);setVisibility(value.visibility);}}).catch(e=>{if(!controller.signal.aborted)setError(e.message);});return()=>controller.abort();},[id,kind,reload]);
 async function save(){if(!state)return;const detail=visibility==="Public"?"Сохранённая версия и ваше имя будут доступны преподавателям в общей библиотеке после модерации.":visibility==="Unlisted"?`Сохранённая версия и ваше имя станут доступны вошедшим преподавателям, у которых есть ссылка.${kind==="test"?" Правильные ответы и основания по этой ссылке не показываются.":""}`:"Публикация и прежняя ссылка перестанут открываться. Уже созданные личные копии других преподавателей сохранятся.";
  if(!await confirmAction(detail,{title:"Изменить доступ?",label:"Сохранить доступ"}))return;setBusy(true);setError("");setMessage("");
  try{const next=await request<Access>(`/api/access/${kind}/${id}`,{method:"PUT",body:JSON.stringify({visibility,version:state.version})});setState(next);setVisibility(next.visibility);setMessage(visibility==="Private"?"Доступ закрыт. Прежняя ссылка больше не действует.":visibility==="Unlisted"?"Версия доступна по ссылке вошедшим преподавателям.":next.status==="Pending"?"Версия отправлена на модерацию.":"Версия опубликована в общей библиотеке.");}
  catch(e){setError(e instanceof Error?e.message:"Не удалось сохранить доступ.");}finally{setBusy(false);}
 }
 const label=kind==="material"?"материалу":"тесту";
 return <section className="publication-controls" aria-label={`Доступ к ${label}`}><h3>Кто может открыть {kind==="material"?"материал":"тест"}</h3>
 <fieldset disabled={busy||!state} className="access-options"><legend className="visually-hidden">Режим доступа</legend>{choices.map(choice=><label key={choice.value} className={`access-option ${visibility===choice.value?"chosen":""}`}><input type="radio" name={`visibility-${kind}-${id}`} aria-label={`Доступ: ${choice.label}`} checked={visibility===choice.value} onChange={()=>setVisibility(choice.value)}/><span><strong>{choice.label}</strong><small>{choice.description}</small></span></label>)}</fieldset>
 {state?.status==="Pending"&&state.visibility==="Public"&&<p className="learning-warning" role="status">Публичная версия на проверке у администратора.{state.published?" Пока доступна прежняя одобренная версия.":" До одобрения она не появится в библиотеке."}</p>}
 {state?.status==="Rejected"&&state.visibility==="Public"&&<p className="alert">Публикация отклонена: {state.reason}</p>}
 {disabled&&<p className="hint">Сохраните изменения перед открытием или обновлением доступа. Закрыть доступ можно сразу.</p>}
 <div className="actions"><button type="button" disabled={!state||busy||(disabled&&visibility!=="Private")} onClick={save}>{busy?"Сохраняем доступ…":visibility===state?.visibility&&visibility==="Unlisted"?"Обновить версию по ссылке":visibility===state?.visibility&&state?.published?"Обновить публикацию":"Сохранить доступ"}</button><button type="button" disabled={busy} onClick={()=>setReload(x=>x+1)}>Обновить настройки</button></div>
 {!state&&!error&&<p className="hint" role="status">Проверяем доступ…</p>}
 {state?.sharePath&&<div className="share-link"><label>Ссылка для преподавателей<input aria-label="Ссылка для преподавателей" readOnly value={typeof window==="undefined"?state.sharePath:window.location.origin+state.sharePath} onFocus={e=>e.target.select()}/></label><div className="actions"><button type="button" onClick={async()=>{try{await navigator.clipboard.writeText(window.location.origin+state.sharePath);setMessage("Ссылка скопирована.");}catch{setMessage("Выделите ссылку и скопируйте её вручную.");}}}>Копировать ссылку</button><a href={state.sharePath} target="_blank" rel="noreferrer">Открыть просмотр ↗</a></div></div>}
 {state&&state.visibility!=="Private"&&<p className="hint">Доступна сохранённая версия. После правок обновите её здесь.{kind==="test"?" По ссылке показываются вопросы без ответов; для прохождения используйте сессии.":""}</p>}
 {error&&<p className="alert" role="alert">{error}</p>}{message&&<p className="hint" role="status">{message}</p>}
 </section>;
}
