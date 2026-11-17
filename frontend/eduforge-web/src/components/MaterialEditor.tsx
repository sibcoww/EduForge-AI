"use client";
import { plainAiText } from "../lib/teaching";
import { requestUrl } from "../lib/ui-dialog";
import { useEffect, useRef, useState } from "react";
import { EditorContent, useEditor, NodeViewWrapper, NodeViewContent, ReactNodeViewRenderer, Extension, type NodeViewProps } from "@tiptap/react";
import StarterKit from "@tiptap/starter-kit";
import { Node, mergeAttributes } from "@tiptap/react";
import ScreenshotEditor, { uploadScreenshot, type ScreenshotValue } from "./ScreenshotEditor";
import { Plugin } from "@tiptap/pm/state";
import Image from "@tiptap/extension-image";
import { TableKit } from "@tiptap/extension-table";

import { parseMaterial as parse, materialFingerprint } from "../lib/material-content";
import { api, request } from "../lib/api";
import CodeBlockLowlight from "@tiptap/extension-code-block-lowlight";
import { common, createLowlight } from "lowlight";
function CodeView({node,updateAttributes,editor}:NodeViewProps){const [copied,setCopied]=useState(false);return <NodeViewWrapper className="code-panel"><div contentEditable={false} className="code-heading"><select aria-label="Язык кода" disabled={!editor.isEditable} value={node.attrs.language||"plaintext"} onChange={e=>updateAttributes({language:e.target.value})}>{["plaintext","python","javascript","typescript","html","css","csharp","cpp","sql","json","bash"].map(x=><option key={x}>{x}</option>)}</select><button type="button" onClick={async()=>{try{await navigator.clipboard.writeText(node.textContent);setCopied(true);}catch{setCopied(false);}}}>{copied?"Скопировано":"Копировать"}</button></div><pre><NodeViewContent/></pre></NodeViewWrapper>;}
const Code=CodeBlockLowlight.extend({addNodeView(){return ReactNodeViewRenderer(CodeView);}}).configure({lowlight:createLowlight(common)});
function ImageLightbox({src,alt,onClose}:{src:string;alt:string;onClose:()=>void}){const dialog=useRef<HTMLDialogElement>(null);useEffect(()=>{const previous=document.activeElement as HTMLElement|null;dialog.current?.showModal();return()=>{dialog.current?.close();previous?.focus();};},[]);return <dialog ref={dialog} className="image-lightbox" aria-label="Просмотр изображения" onCancel={e=>{e.preventDefault();onClose();}}><button type="button" autoFocus onClick={onClose}>Закрыть изображение</button><img src={src} alt={alt}/></dialog>;}
function ImageView({node,updateAttributes,editor}:NodeViewProps){
 const [editing,setEditing]=useState(false),[zoom,setZoom]=useState(false);const src=node.attrs.src||"";
 return <NodeViewWrapper className="material-figure" style={{textAlign:node.attrs.align}} contentEditable={false}>
 <button type="button" className="image-preview-button" aria-label="Увеличить изображение" onClick={()=>setZoom(true)}> <img src={src.startsWith("/api/")?api+src:src} alt={node.attrs.alt||""} style={{width:node.attrs.width||"100%",maxWidth:"100%",cursor:"zoom-in"}}/></button>
 {node.attrs.caption&&<p className="image-caption">{plainAiText(node.attrs.caption)}</p>}
 {editor.isEditable&&src.startsWith("/api/media/")&&<button type="button" onClick={()=>setEditing(true)}>Редактировать скриншот</button>}
 {editing&&<ScreenshotEditor value={{...node.attrs} as ScreenshotValue} onClose={()=>setEditing(false)} onSave={value=>{updateAttributes(value);setEditing(false);}}/>}
 {zoom&&<ImageLightbox src={src.startsWith("/api/")?api+src:src} alt={node.attrs.alt||""} onClose={()=>setZoom(false)}/>}
 </NodeViewWrapper>;
}
const Picture=Image.extend({addAttributes(){return {...this.parent?.(),caption:{default:""},width:{default:"100%"},align:{default:"center"},originalSrc:{default:""},annotations:{default:[]}};},addNodeView(){return ReactNodeViewRenderer(ImageView);}});
function ScreenshotPlaceholderView({node,updateAttributes,editor,getPos}:NodeViewProps){
 const [error,setError]=useState(""),[busy,setBusy]=useState(false);
 async function insert(file:File){setBusy(true);setError("");try{const src=await uploadScreenshot(file,file.name);const pos=getPos();if(pos===undefined)return;editor.chain().focus().setNodeSelection(pos).insertContent({type:"image",attrs:{src,originalSrc:src,caption:"",alt:node.attrs.description,blockId:node.attrs.blockId}}).run();}catch(e){setError(e instanceof Error?e.message:"Ошибка загрузки");}finally{setBusy(false);}}
 return <NodeViewWrapper className="screenshot-placeholder" contentEditable={false} onPaste={(e:React.ClipboardEvent<HTMLDivElement>)=>{if(!editor.isEditable)return;const file=Array.from(e.clipboardData.files).find(f=>f.type.startsWith("image/"));if(file){e.preventDefault();e.stopPropagation();void insert(file);}}} onDragOver={(e:React.DragEvent<HTMLDivElement>)=>{if(editor.isEditable)e.preventDefault();}} onDrop={(e:React.DragEvent<HTMLDivElement>)=>{if(!editor.isEditable)return;const file=e.dataTransfer.files[0];if(file){e.preventDefault();e.stopPropagation();void insert(file);}}}>
 <strong>Нужен скриншот</strong>{editor.isEditable?<><textarea className="resize-none" aria-label="Описание нужного скриншота" maxLength={2000} value={node.attrs.description} onChange={e=>updateAttributes({description:e.target.value})}/><label className="upload-button">{busy?"Загрузка…":"Вставить скриншот"}<input type="file" hidden accept="image/png,image/jpeg,image/webp" disabled={busy} onChange={e=>{const file=e.target.files?.[0];if(file)void insert(file);e.target.value="";}}/></label><small>Можно вставить из буфера или перетащить файл сюда.</small></>:<p>{node.attrs.description}</p>}{error&&<p role="alert">{error}</p>}
 </NodeViewWrapper>;
}
const ScreenshotPlaceholder=Node.create({name:"screenshotPlaceholder",group:"block",atom:true,draggable:true,addAttributes(){return {description:{default:"Добавьте скриншот нужного шага"},blockId:{default:null,rendered:false}};},parseHTML(){return [{tag:'div[data-screenshot-placeholder]'}];},renderHTML({HTMLAttributes}){return ["div",mergeAttributes(HTMLAttributes,{"data-screenshot-placeholder":"true",class:"screenshot-placeholder"}),HTMLAttributes.description];},addNodeView(){return ReactNodeViewRenderer(ScreenshotPlaceholderView);}});
const BlockIdentity = Extension.create({
  name: "blockIdentity",
  addGlobalAttributes() { return [{ types: ["paragraph", "heading", "codeBlock", "image", "bulletList", "orderedList", "blockquote", "table", "horizontalRule", "screenshotPlaceholder"], attributes: { blockId: { default: null, rendered: false } } }]; },
  addProseMirrorPlugins() { return [new Plugin({ appendTransaction: (transactions, _old, state) => {
    if (!transactions.some(t => t.docChanged) || !this.editor.isEditable) return null;
    const tr = state.tr; const seen = new Set<string>();
    state.doc.forEach((node, position) => { if (!("blockId" in node.attrs)) return;
      const id = node.attrs.blockId as string | null;
      if (!id || seen.has(id)) { const next = crypto.randomUUID(); tr.setNodeMarkup(position, undefined, { ...node.attrs, blockId: next }); seen.add(next); } else seen.add(id);
    });
    return tr.docChanged ? tr : null;
  } })]; }
});
export default function MaterialEditor({ content, disabled, onChange, readOnly = false }: { content: string; disabled: boolean; onChange?: (value: string) => void; readOnly?: boolean }) {
  const [pendingScreenshot,setPendingScreenshot]=useState<ScreenshotValue|null>(null);
  const [illustration,setIllustration]=useState(""); const [preview,setPreview]=useState("");
  const [uploadError,setUploadError]=useState(""); const [uploading,setUploading]=useState(false);
  const editor = useEditor({
    extensions: [StarterKit.configure({codeBlock:false}), Code, Picture, ScreenshotPlaceholder, TableKit, BlockIdentity], immediatelyRender: false, shouldRerenderOnTransaction: true,
    content: parse(content), editable: !disabled && !readOnly,
    editorProps: { handlePaste: (_view,event) => { if(disabled||readOnly)return false;const file=Array.from(event.clipboardData?.files||[]).find(f=>f.type.startsWith("image/"));if(!file)return false;event.preventDefault();void prepareScreenshot(file);return true; }, handleDrop: (_view,event,_slice,moved)=>{if(disabled||readOnly||moved)return false;const file=Array.from(event.dataTransfer?.files||[]).find(f=>f.type.startsWith("image/"));if(!file)return false;event.preventDefault();void prepareScreenshot(file);return true;}, attributes: { role: "textbox", "aria-label": "Содержание материала", "aria-multiline": "true" } },
    onUpdate: ({ editor }) => onChange?.(editor.isEmpty ? "" : JSON.stringify(editor.getJSON()))
  });
  useEffect(() => { if (editor && materialFingerprint(JSON.stringify(editor.getJSON())) !== materialFingerprint(content)) editor.commands.setContent(parse(content), { emitUpdate: false }); }, [editor, content]);
  useEffect(() => { editor?.setEditable(!disabled && !readOnly); }, [editor, disabled, readOnly]);
  async function prepareScreenshot(file:File){setUploading(true);setUploadError("");try{const src=await uploadScreenshot(file,file.name);setPendingScreenshot({src,originalSrc:src,annotations:[],caption:"",alt:""});}catch(e){setUploadError(e instanceof Error?e.message:"Ошибка загрузки");}finally{setUploading(false);}}
  if (!editor) return <p>Загружаем редактор…</p>;
  if (readOnly) return <div className="rich-editor material-reader"><EditorContent editor={editor}/></div>;
  const action = (label: string, run: () => void) => <button type="button" disabled={disabled} onClick={run}>{label}</button>;
  return <div className="rich-editor">{pendingScreenshot&&<ScreenshotEditor value={pendingScreenshot} onClose={()=>setPendingScreenshot(null)} onSave={value=>{editor.chain().focus().insertContent({type:"image",attrs:value}).run();setPendingScreenshot(null);}}/>}<div className="toolbar" aria-label="Форматирование текста">
    {action("Жирный", () => editor.chain().focus().toggleBold().run())}
    {action("Курсив", () => editor.chain().focus().toggleItalic().run())}
    {action("Заголовок", () => editor.chain().focus().toggleHeading({ level: 2 }).run())}
    {action("Список", () => editor.chain().focus().toggleBulletList().run())}
    {action("Цитата", () => editor.chain().focus().toggleBlockquote().run())}
    {action("Код", () => editor.chain().focus().toggleCodeBlock().run())}
    {action("Разделитель", () => editor.chain().focus().setHorizontalRule().run())}
    {action("Таблица", () => editor.chain().focus().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run())}
    {action("Удалить таблицу", () => editor.chain().focus().deleteTable().run())}
    {action("Ссылка", async () => { const url = await requestUrl("Адрес ссылки (https://)"); if (url?.startsWith("https://")) editor.chain().focus().setLink({ href: url }).run(); })}
    {action("Изображение", async () => { const url = await requestUrl("Адрес изображения (https://)"); if (url?.startsWith("https://")) editor.chain().focus().setImage({ src: url }).run(); })}
    {action("Место для скриншота",()=>editor.chain().focus().insertContent({type:"screenshotPlaceholder",attrs:{description:"Укажите, какой инструмент или шаг нужно показать"}}).run())}
    <label className="upload-button">{uploading?"Загрузка…":"Загрузить скриншот"}<input hidden type="file" accept="image/png,image/jpeg,image/webp" disabled={disabled||uploading} onChange={e=>{const file=e.target.files?.[0];if(file)void prepareScreenshot(file);e.target.value="";}}/></label>
    {editor.isActive("image")&&<><input aria-label="Подпись изображения" placeholder="Подпись изображения" disabled={disabled} value={editor.getAttributes("image").caption||""} onChange={e=>editor.commands.updateAttributes("image",{caption:e.target.value,alt:e.target.value})}/><select aria-label="Размер изображения" disabled={disabled} value={editor.getAttributes("image").width||"100%"} onChange={e=>editor.commands.updateAttributes("image",{width:e.target.value})}><option>100%</option><option>75%</option><option>50%</option><option>25%</option></select><select aria-label="Расположение изображения" disabled={disabled} value={editor.getAttributes("image").align||"center"} onChange={e=>editor.commands.updateAttributes("image",{align:e.target.value})}><option value="left">Слева</option><option value="center">По центру</option><option value="right">Справа</option></select></>}
    {action("Отменить", () => editor.chain().focus().undo().run())}
    {action("Повторить", () => editor.chain().focus().redo().run())}
  </div><details><summary>AI-иллюстрация</summary><label>Описание изображения<textarea aria-label="Описание изображения" className="resize-none" maxLength={2000} value={illustration} onChange={e=>setIllustration(e.target.value)} placeholder="Например, схема перспективы в 3D-моделировании"/></label><p>Генерация оплачивается через API. Сначала появится предпросмотр, затем можно вставить картинку.</p><button type="button" disabled={disabled||uploading||!illustration.trim()} onClick={async()=>{setUploading(true);setUploadError("");try{setPreview((await request<{url:string}>("/api/ai/illustration",{method:"POST",body:JSON.stringify({prompt:illustration})})).url);}catch(e){setUploadError(e instanceof Error?e.message:"Ошибка");}finally{setUploading(false);}}}>Сгенерировать иллюстрацию</button>{preview&&<><img src={api+preview} alt="Предпросмотр AI-иллюстрации" style={{maxWidth:300}}/><button type="button" disabled={disabled} onClick={()=>{editor.chain().focus().setImage({src:preview,alt:illustration}).run();setPreview("");}}>Вставить в материал</button><button type="button" onClick={()=>setPreview("")}>Закрыть предпросмотр</button></>}</details>{uploadError&&<p role="alert" className="alert">{uploadError}</p>}<EditorContent editor={editor}/></div>;
}
