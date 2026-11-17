"use client";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Icon, { Brand, type IconName } from "./Icon";
import { confirmAction } from "../lib/ui-dialog";
export async function allowNavigation(dirty:boolean){return !dirty||await confirmAction("Перейти в другой раздел? Несохранённые изменения будут потеряны.");}
const links:{href:string;key:string;label:string;icon:IconName}[]=[
 {href:"/",key:"materials",label:"Материалы",icon:"book"},{href:"/tests",key:"tests",label:"Тесты",icon:"test"},{href:"/groups",key:"groups",label:"Группы",icon:"users"},{href:"/sessions",key:"sessions",label:"Сессии и результаты",icon:"clock"},{href:"/assignments",key:"assignments",label:"Задания",icon:"task"},
 {href:"/library",key:"library",label:"Общая библиотека",icon:"library"},{href:"/experiments",key:"experiments",label:"История генераций",icon:"history"}];
export default function TeacherNavigation({active,dirty,isAdmin=false,name="Преподаватель"}:{active:string;dirty:boolean;isAdmin?:boolean;name?:string}){
 const router=useRouter();
 const guard=async(event:React.MouseEvent<HTMLAnchorElement>,href:string)=>{if(!dirty||event.ctrlKey||event.metaKey||event.shiftKey||event.altKey)return;event.preventDefault();if(await allowNavigation(dirty))router.push(href);};
 return <><Link className="brand" href="/" onClick={e=>void guard(e,"/")}><Brand/></Link><p className="caption">ВАШИ ЗАНЯТИЯ</p><nav className="teacher-nav" aria-label="Основная навигация">{links.map((link,i)=><Link key={link.key} href={link.href} className={`${active===link.key?"active":""} ${i===5?"nav-section-start":""}`} aria-current={active===link.key?"page":undefined} onClick={e=>void guard(e,link.href)}><Icon name={link.icon}/><span>{link.label}</span></Link>)}{isAdmin&&<Link href="/admin" className={active==="admin"?"active":""} aria-current={active==="admin"?"page":undefined} onClick={e=>void guard(e,"/admin")}><Icon name="settings"/><span>Аккаунты</span></Link>}</nav><div className="aside-note"><span className="aside-workshop"><Icon name="spark" size={16}/> Мастерская ваших уроков</span><Link href="/profile" className={`sidebar-profile ${active==="profile"?"active":""}`} aria-current={active==="profile"?"page":undefined} onClick={e=>void guard(e,"/profile")}><span className="avatar-initials">{name.split(/\s+/).filter(Boolean).slice(0,2).map(x=>x[0]).join("").toUpperCase()}</span><span><strong>{name}</strong><small>{isAdmin?"Администратор":"Профиль преподавателя"}</small></span><Icon name="chevron" size={16}/></Link></div><Link className="mobile-profile" href="/profile" onClick={e=>void guard(e,"/profile")} aria-label="Профиль преподавателя"><Icon name="profile"/></Link></>;
}
