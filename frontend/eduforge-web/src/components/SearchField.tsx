"use client";
import { useRef } from "react";
import Icon from "./Icon";
export default function SearchField({value,onChange,label,placeholder,maxLength=200}:{value:string;onChange:(value:string)=>void;label:string;placeholder?:string;maxLength?:number}){
 const input=useRef<HTMLInputElement>(null);
 return <div className="search-field"><Icon name="search" size={19}/><input ref={input} aria-label={label} placeholder={placeholder||label} value={value} maxLength={maxLength} onChange={e=>onChange(e.target.value)}/>{value&&<button type="button" aria-label={`Очистить: ${label.toLowerCase()}`} onClick={()=>{onChange("");input.current?.focus();}}><Icon name="close" size={17}/></button>}</div>;
}
