"use client";
import { useEffect, useRef, useState } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { api } from "../lib/api";

export function useSessionWatch(id: string | undefined, token: string | null, refresh: () => Promise<void>) {
  const callback = useRef(refresh); callback.current = refresh;
  useEffect(() => {
    if (!id) return;
    let disposed = false;
    const connection = new HubConnectionBuilder().withUrl(`${api}/api/hubs/sessions`, { withCredentials: true }).withAutomaticReconnect().configureLogging(LogLevel.None).build();
    const reload = () => { if (!disposed) void callback.current(); };
    const watch = async () => { await connection.invoke("Watch", id, token); reload(); };
    connection.on("Changed", reload); connection.onreconnected(() => { void watch().catch(() => {}); });
    void connection.start().then(watch).catch(() => {});
    // Polling also recovers after failed initial negotiation and missed invalidations.
    const poll = window.setInterval(reload, 5000);
    return () => { disposed = true; window.clearInterval(poll); void connection.stop(); };
  }, [id, token]);
}
export function Countdown({ deadline, serverNow }: { deadline: number; serverNow: number }) {
  const [remaining, setRemaining] = useState(Math.max(0, deadline - serverNow));
  useEffect(() => {
    const reference = performance.now();
    const tick = () => setRemaining(Math.max(0, deadline - serverNow - (performance.now() - reference)));
    tick(); const interval = window.setInterval(tick, 1000); return () => window.clearInterval(interval);
  }, [deadline, serverNow]);
  const seconds = Math.ceil(remaining / 1000);
  return <span className="countdown" role="timer">{Math.floor(seconds / 60)}:{String(seconds % 60).padStart(2, "0")}</span>;
}
