"use client";
import { useEffect, useId, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import Form from "../components/Form";
let pending=false;
function ask(message:string,input:boolean,options?:{title?:string;label?:string}):Promise<string|null>{
 if(pending)return Promise.resolve(null);pending=true;
 return new Promise(resolve=>{const host=document.createElement("div");document.body.appendChild(host);const root=createRoot(host);
  function finish(value:string|null){pending=false;resolve(value);queueMicrotask(()=>{root.unmount();host.remove();});}
  function Dialog(){const ref=useRef<HTMLDialogElement>(null),title=useId();const [value,setValue]=useState("");const danger=/^Удалить/.test(message);
   useEffect(()=>{const previous=document.activeElement as HTMLElement|null;ref.current?.showModal();return()=>{ref.current?.close();previous?.focus();};},[]);
   return <dialog className="action-dialog" ref={ref} aria-labelledby={title} onCancel={e=>{e.preventDefault();finish(null);}}><h2 id={title}>{options?.title||(input?"Добавить ссылку":danger?"Подтвердите удаление":"Продолжить без сохранения?")}</h2><p>{message}</p><Form noValidate onSubmit={e=>{e.preventDefault();finish(input?value:"yes");}}>{input&&<label>Адрес (https://)<input autoFocus type="url" required pattern="https://.*" value={value} onChange={e=>setValue(e.target.value)} placeholder="https://"/></label>}<div className="action-dialog-buttons"><button type="button" autoFocus={!input} onClick={()=>finish(null)}>Отмена</button><button className={danger?"danger-solid":"primary"}>{options?.label||(input?"Вставить":danger?"Удалить":"Продолжить")}</button></div></Form></dialog>;
  }root.render(<Dialog/>);
 });
}
export async function confirmAction(message:string,options?:{title?:string;label?:string}){return (await ask(message,false,options))!==null;}
export function requestUrl(message:string){return ask(message,true);}
