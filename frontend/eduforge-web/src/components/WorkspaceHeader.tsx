"use client";
import { useEffect } from "react";
import Icon from "./Icon";
export const sectionNames={materials:"Материалы",tests:"Тесты",groups:"Группы",sessions:"Сессии и результаты",library:"Общая библиотека",admin:"Аккаунты",profile:"Профиль",assignments:"Задания",experiments:"История генераций"};
export default function WorkspaceHeader({section,name,busy,onLogout}:{section:keyof typeof sectionNames;name:string;busy:boolean;onLogout:()=>void}){
 useEffect(()=>{document.title=`${sectionNames[section]} — EduForge AI`;},[section]);
 return <header className="workspace-header"><div className="workspace-location"><span>Рабочее пространство</span><Icon name="chevron" size={14}/><strong>{sectionNames[section]}</strong></div><div className="workspace-account"><span>{name}</span><button type="button" disabled={busy} onClick={onLogout} className="logout-button"><Icon name="logout" size={17}/><span>Выйти</span></button></div></header>;
}
