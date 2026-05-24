#!/usr/bin/env npx ts-node
// OpenClaw → Mate Engine Agent Bridge Adapter
// Connects to OpenClaw's WebSocket RPC and writes agent status to a JSON file bus
// that Mate Engine's AvatarAgentBridge.cs polls.
//
// Usage:
//   npx ts-node openclaw_adapter.ts [ws://localhost:8080]
//   MATE_ENGINE_BUS_PATH=/custom/path npx ts-node openclaw_adapter.ts
//
// Dependencies: ws (npm install ws)

import { writeFileSync, unlinkSync, existsSync, mkdirSync } from "fs";
import { join, dirname } from "path";

// --- Config ---

const WS_URL = process.argv[2] || process.env.OPENCLAW_WS_URL || "ws://localhost:8080";

function getBusPath(): string {
  const envPath = process.env.MATE_ENGINE_BUS_PATH;
  if (envPath) return envPath;

  const home = process.env.HOME || process.env.USERPROFILE || "";
  if (process.platform === "win32") {
    return join(home, "AppData", "LocalLow", "Shinymoon", "MateEngineX", "AgentBridge", "agent_status.json");
  }
  return join(home, ".local", "share", "unity3d", "Shinymoon", "MateEngineX", "AgentBridge", "agent_status.json");
}

const BUS_PATH = getBusPath();

// --- Atomic Write ---

let version = 0;

function atomicWrite(filePath: string, data: object): void {
  mkdirSync(dirname(filePath), { recursive: true });
  const tmp = filePath + ".tmp";
  writeFileSync(tmp, JSON.stringify(data));
  if (existsSync(filePath)) unlinkSync(filePath);
  require("fs").renameSync(tmp, filePath);
}

function writeStatus(state: string, message: string, error?: string): void {
  version++;
  const payload = {
    v: version,
    agent: "openclaw",
    state,
    message: message || "",
    progress: 0,
    error: error || null,
    task_name: "",
    writeUtc: Date.now() / 1000,
  };
  atomicWrite(BUS_PATH, payload);
  console.log(`[openclaw-adapter] v=${version} state=${state} msg="${message}"`);
}

// --- WebSocket Connection ---

let WebSocket: any;
try {
  WebSocket = require("ws");
} catch {
  console.error("ws package not found. Run: npm install ws");
  process.exit(1);
}

console.log(`[openclaw-adapter] Connecting to ${WS_URL}...`);
console.log(`[openclaw-adapter] Bus path: ${BUS_PATH}`);

const ws = new WebSocket(WS_URL);

ws.on("open", () => {
  console.log("[openclaw-adapter] Connected");
  writeStatus("idle", "");
});

ws.on("message", (raw: Buffer) => {
  try {
    const event = JSON.parse(raw.toString());

    // OpenClaw Gateway emits typed events: agent, chat, health, heartbeat, etc.
    switch (event.type) {
      case "agent":
        if (event.status === "processing" || event.status === "running") {
          writeStatus("working", event.task || event.description || "Working...");
        } else if (event.status === "completed" || event.status === "done") {
          writeStatus("success", event.result || "Task completed");
        } else if (event.status === "error" || event.status === "failed") {
          writeStatus("error", "", event.error || event.message || "Unknown error");
        } else if (event.status === "thinking" || event.status === "planning") {
          writeStatus("thinking", event.description || "Thinking...");
        }
        break;

      case "chat":
        if (event.content || event.text) {
          writeStatus("streaming", event.content || event.text || "");
        }
        break;

      case "health":
        if (event.status === "ok" || event.status === "healthy") {
          // Heartbeat OK — no state change needed unless we were disconnected
        }
        break;

      case "heartbeat":
        // Keep-alive, ignore
        break;

      default:
        // Unknown event type, ignore
        break;
    }
  } catch (e: any) {
    console.error("[openclaw-adapter] Parse error:", e.message);
  }
});

ws.on("close", (code: number, reason: Buffer) => {
  console.log(`[openclaw-adapter] Disconnected (code=${code})`);
  writeStatus("disconnected", "");
  process.exit(0);
});

ws.on("error", (err: Error) => {
  console.error("[openclaw-adapter] Error:", err.message);
  writeStatus("error", "", err.message);
});
