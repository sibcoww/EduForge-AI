"use client";
import { useEffect, useId, useRef } from "react";

export default function CreationDialog({ title, busy, onClose, children }: { title: string; busy: boolean; onClose: () => void; children: React.ReactNode }) {
  const dialog = useRef<HTMLDialogElement>(null); const heading = useId();
  useEffect(() => { const element = dialog.current; element?.showModal(); return () => { element?.close(); }; }, []);
  return <dialog ref={dialog} className="creation-dialog" aria-labelledby={heading} onCancel={event => { event.preventDefault(); if (!busy) onClose(); }}>
    <div className="creation-dialog-heading"><h2 id={heading}>{title}</h2><button type="button" disabled={busy} aria-label="Закрыть окно" onClick={onClose}>×</button></div>
    {children}
  </dialog>;
}
