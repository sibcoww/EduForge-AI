"use client";
import { useId, useState, type ComponentProps } from "react";
type Field = HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;
function message(field:Field){const v=field.validity;if(v.valueMissing)return "Заполните это поле.";if(v.typeMismatch)return field.type==="email"?"Укажите email в формате name@example.com.":"Проверьте формат значения.";if(v.tooShort)return `Введите не менее ${(field as HTMLInputElement).minLength} символов.`;if(v.rangeUnderflow)return `Минимальное значение: ${(field as HTMLInputElement).min}.`;if(v.rangeOverflow)return `Максимальное значение: ${(field as HTMLInputElement).max}.`;return "Проверьте введённое значение.";}
export default function Form({onSubmit,children,...props}:ComponentProps<"form">){
 const id=useId();const [errors,setErrors]=useState<{id:string;name:string;text:string}[]>([]);
 return <form {...props} noValidate onInputCapture={e=>{const field=e.target as Field;if(field.checkValidity?.()){field.removeAttribute("aria-invalid");const ref=field.getAttribute("aria-describedby");if(ref?.startsWith(id))field.removeAttribute("aria-describedby");if(ref)setErrors(current=>current.filter(x=>x.id!==ref));}}} onSubmit={e=>{
  const fields=Array.from(e.currentTarget.elements).filter((field):field is Field=>field instanceof HTMLInputElement||field instanceof HTMLTextAreaElement||field instanceof HTMLSelectElement);
  const invalid=fields.filter(field=>field.willValidate&&!field.checkValidity());
  const next=invalid.map((field,i)=>{const errorId=`${id}-${i}`;field.setAttribute("aria-invalid","true");field.setAttribute("aria-describedby",errorId);return {id:errorId,name:(field.getAttribute("aria-label")||field.labels?.[0]?.textContent||"Поле").slice(0,65),text:message(field)};});setErrors(next);
  if(invalid.length){e.preventDefault();invalid[0].focus();return;}onSubmit?.(e);
 }}>{errors.length>0&&<div className="alert form-errors" role="alert"><strong>Проверьте заполнение</strong>{errors.map(error=><p id={error.id} key={error.id}>{error.name}: {error.text}</p>)}</div>}{children}</form>;
}
