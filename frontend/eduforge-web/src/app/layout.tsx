import type { Metadata } from "next";
import "./globals.css";
export const metadata: Metadata = { title: "EduForge AI — Обучение и тестирование", description: "Рабочее пространство преподавателя" };
export default function Layout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="ru"><body>{children}</body></html>;
}
