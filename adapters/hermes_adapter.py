#!/usr/bin/env python3
"""
Hermes → Mate Engine Agent Bridge Adapter

Polls Hermes Dashboard API and writes agent status to a JSON file bus
that Mate Engine's AvatarAgentBridge.cs polls.

Usage:
    python hermes_adapter.py [--api http://localhost:8000] [--bus /path/to/agent_status.json]
    MATE_ENGINE_BUS_PATH=/custom/path python hermes_adapter.py

Dependencies:
    pip install requests
"""

import argparse
import json
import os
import sys
import time

try:
    import requests
except ImportError:
    print("requests package not found. Run: pip install requests")
    sys.exit(1)


def get_bus_path() -> str:
    env_path = os.environ.get("MATE_ENGINE_BUS_PATH")
    if env_path:
        return env_path

    home = os.path.expanduser("~")
    if sys.platform == "win32":
        return os.path.join(
            home, "AppData", "LocalLow", "Shinymoon",
            "MateEngineX", "AgentBridge", "agent_status.json"
        )
    return os.path.join(
        home, ".local", "share", "unity3d",
        "Shinymoon", "MateEngineX", "AgentBridge", "agent_status.json"
    )


def atomic_write(path: str, data: dict) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False)
    if os.path.exists(path):
        os.remove(path)
    os.rename(tmp, path)


def write_status(bus_path: str, version: int, state: str, message: str = "", error: str = None) -> int:
    version += 1
    payload = {
        "v": version,
        "agent": "hermes",
        "state": state,
        "message": message,
        "progress": 0,
        "error": error,
        "task_name": "",
        "writeUtc": time.time(),
    }
    atomic_write(bus_path, payload)
    print(f"[hermes-adapter] v={version} state={state} msg={message!r}")
    return version


def map_hermes_status(data: dict) -> tuple:
    """Map Hermes API response to agent bridge state."""
    status = data.get("status", data.get("state", "idle"))
    message = data.get("message", data.get("output", ""))
    error = data.get("error", None)

    state_map = {
        "idle": "idle",
        "ready": "idle",
        "running": "working",
        "busy": "working",
        "processing": "working",
        "thinking": "thinking",
        "planning": "thinking",
        "streaming": "streaming",
        "generating": "streaming",
        "completed": "success",
        "done": "success",
        "finished": "success",
        "error": "error",
        "failed": "error",
        "disconnected": "disconnected",
    }

    mapped_state = state_map.get(str(status).lower(), "idle")
    return mapped_state, message, error


def poll_hermes(api_url: str, bus_path: str, poll_interval: float) -> None:
    version = 0
    last_state = None
    session = requests.Session()

    print(f"[hermes-adapter] Polling {api_url}/api/agent/status every {poll_interval}s")
    print(f"[hermes-adapter] Bus path: {bus_path}")

    while True:
        try:
            r = session.get(f"{api_url}/api/agent/status", timeout=3)
            r.raise_for_status()
            data = r.json()

            state, message, error = map_hermes_status(data)

            if state != last_state:
                version = write_status(bus_path, version, state, message, error)
                last_state = state

        except requests.ConnectionError:
            if last_state != "disconnected":
                version = write_status(bus_path, version, "disconnected", "", "Cannot connect to Hermes API")
                last_state = "disconnected"
        except requests.Timeout:
            pass
        except Exception as e:
            if last_state != "error":
                version = write_status(bus_path, version, "error", "", str(e))
                last_state = "error"

        time.sleep(poll_interval)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Hermes → Mate Engine Agent Bridge")
    parser.add_argument("--api", default=os.environ.get("HERMES_API_URL", "http://localhost:8000"),
                        help="Hermes API base URL")
    parser.add_argument("--bus", default=get_bus_path(),
                        help="Path to agent_status.json bus file")
    parser.add_argument("--interval", type=float, default=2.0,
                        help="Polling interval in seconds")
    args = parser.parse_args()

    try:
        poll_hermes(args.api, args.bus, args.interval)
    except KeyboardInterrupt:
        print("\n[hermes-adapter] Stopped")
