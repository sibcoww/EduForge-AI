"use client";
import { useEffect } from "react";
import { EditorContent, useEditor } from "@tiptap/react";
import StarterKit from "@tiptap/starter-kit";
import Image from "@tiptap/extension-image";
import { TableKit } from "@tiptap/extension-table";

function parse(content: string) {
  try { const value = JSON.parse(content); if (value.type === "doc") return value; } catch { /* Legacy text and generated text. */ }
  return { type: "doc", content: content.split("\n").map(text => ({ type: "paragraph", ...(text ? { content: [{ type: "text", text }] } : {}) })) };
}
export default function MaterialEditor({ content, disabled, onChange }: { content: string; disabled: boolean; onChange: (value: string) => void }) {
  const editor = useEditor({
    extensions: [StarterKit, Image, TableKit], immediatelyRender: false,
    content: parse(content), editable: !disabled,
    editorProps: { attributes: { role: "textbox", "aria-label": "Содержание материала", "aria-multiline": "true" } },
    onUpdate: ({ editor }) => onChange(editor.isEmpty ? "" : JSON.stringify(editor.getJSON()))
  });
  useEffect(() => { if (editor && JSON.stringify(editor.getJSON()) !== content) editor.commands.setContent(parse(content), { emitUpdate: false }); }, [editor, content]);
  useEffect(() => { editor?.setEditable(!disabled); }, [editor, disabled]);
  if (!editor) return <p>Загружаем редактор…</p>;
  const action = (label: string, run: () => void) => <button type="button" disabled={disabled} onClick={run}>{label}</button>;
  return <div className="rich-editor"><div className="toolbar" aria-label="Форматирование текста">
    {action("Жирный", () => editor.chain().focus().toggleBold().run())}
    {action("Курсив", () => editor.chain().focus().toggleItalic().run())}
    {action("Заголовок", () => editor.chain().focus().toggleHeading({ level: 2 }).run())}
    {action("Список", () => editor.chain().focus().toggleBulletList().run())}
    {action("Цитата", () => editor.chain().focus().toggleBlockquote().run())}
    {action("Код", () => editor.chain().focus().toggleCodeBlock().run())}
    {action("Разделитель", () => editor.chain().focus().setHorizontalRule().run())}
    {action("Таблица", () => editor.chain().focus().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run())}
    {action("Удалить таблицу", () => editor.chain().focus().deleteTable().run())}
    {action("Ссылка", () => { const url = window.prompt("Адрес ссылки (https://)"); if (url?.startsWith("https://")) editor.chain().focus().setLink({ href: url }).run(); })}
    {action("Изображение", () => { const url = window.prompt("Адрес изображения (https://)"); if (url?.startsWith("https://")) editor.chain().focus().setImage({ src: url }).run(); })}
    {action("Отменить", () => editor.chain().focus().undo().run())}
    {action("Повторить", () => editor.chain().focus().redo().run())}
  </div><EditorContent editor={editor}/></div>;
}
