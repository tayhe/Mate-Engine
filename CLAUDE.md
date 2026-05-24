# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Mate Engine is a free, open-source desktop pet/companion application built with Unity. It loads VRM 3D character models onto the desktop that idle, dance to music, sit on windows/taskbar, track the mouse, and interact via touch. Licensed under GNU AGPL v3. Steam App ID: 3625270.

## Unity Version

Unity 6000.2.6f2 (Unity 6 series). Open the project in this exact version.

## Build and Run

This is a Unity project — there are no CLI build/test/lint commands. Development workflow:

1. Open the project in Unity 6000.2.6f2
2. Open scene `Assets/MATE ENGINE - Scenes/Mate Engine Main.unity`
3. Enter Play mode to test
4. Build via File > Build Settings > Windows Standalone (primary target)

Quality levels: Ultra, Very High, High, Normal, Low. Color space: Linear.

## Key Dependencies

- **Localization** (`com.unity.localization` 1.5.8) — multi-language support (EN, JA, ZH)
- **Addressables** — asset loading (Windows + OSX build profiles)
- **Steamworks.NET** — Steam Workshop, DRM, version checks
- **UniVRM** (UniGLTF + VRM + VRM10) — VRM 0.x and 1.0 import
- **LLM for Unity** (`ai.undream.llm` v2.5.1) — local AI chat (Qwen models in StreamingAssets, gitignored)
- **DiscordRPC** — Discord Rich Presence
- **NAudio** — audio session detection for music-driven dancing
- **Newtonsoft.Json** — settings serialization

## Architecture

### Script Organization (`Assets/MATE ENGINE - Scripts/`)

| Directory | Purpose |
|---|---|
| `AvatarHandlers/` | Core avatar behavior (33 scripts): animation state machine, mouse tracking, window sitting, taskbar hiding, dance player, food, sleep, particles, chat bubbles, accessories |
| `Settings/` | Configuration and UI (21 scripts): JSON settings persistence, mod management, FPS limiting, keybindings, menus |
| `APIs/` | External integrations (7 scripts): Discord, Steam Workshop, Win32/DWM API bindings |
| `Tools/` | Utilities (22 scripts): theming, memory optimization, screenshots, markdown rendering |
| `VRMLoader/` | VRM/ME model loading, avatar library, multi-instance launcher |
| `BlendshapeManager/` | Runtime blendshape editing UI |

### Key Architectural Patterns

**Singletons everywhere**: `SaveLoadHandler.Instance`, `MEModLoader.Instance`, `SteamWorkshopHandler.Instance`, `MEModHandler.GlobalInstances`. Check for existing instances before creating new ones.

**Settings persistence**: All user settings serialize to `settings.json` via Newtonsoft.Json in `Application.persistentDataPath`. Supports `--savefile` and `--datadir` command-line args for multi-instance.

**VRM component injection**: When loading custom VRMs, the system instantiates a template prefab, copies all MonoBehaviours to the loaded model via reflection, then wires up the Animator. This is in `VRMLoader.cs`.

**Reflection-heavy mod system**: Mods are `.me` files (ZIP archives with AssetBundles + metadata JSON). `MEModHandler.cs` and `MEModLoader.cs` use reflection to copy field values, wire scene references via `reference_paths.json`/`scene_links.json`, and manipulate private fields on handlers.

**Win32 interop**: Heavy P/Invoke usage for window management. `AvatarWindowHandler.cs` (~48KB, the largest script) implements window snapping with occluders. `WinApi.cs` and `DwmApi.cs` define native bindings. `AvatarHideHandler.cs` handles taskbar sitting. `SystemTray.cs` provides Windows tray icon.

**Audio-driven dancing**: NAudio enumerates audio sessions; sound from allowed apps (Spotify, etc.) triggers dance animations via `AvatarAnimatorController.cs`.

**Multi-avatar sync**: Up to 9 avatars with dance synchronization via `avatar_dance_play_bus.json` and `Sync/dance_sync.json`.

### Scenes

| Scene | Purpose |
|---|---|
| `Mate Engine Main.unity` | Primary scene (use this for development) |
| `Mate Engine Update.unity` | Update/changelog display |
| `Mate Engine Screenshots.unity` | Promo screenshots |
| `Animating.unity` | Animation development |

### Mod System

Custom `.me` file format (ZIP archives containing):
- AssetBundles (`.unity3d`)
- Metadata JSON
- Optional dance data

Mod SDK public API in `Assets/MATE ENGINE - Mod SDK/`: `MEManipulator`, `MERemover`, `MEReceiver`, `MEClothes`, `AvatarClothesHandler`.

### Shaders

Custom shaders in `Assets/MATE ENGINE - Shaders/`: FadeByScreenOrWorld, GlowShader, UiBlur, ShadowOnly. Also bundles Poiyomi Toon and lilToon shader packages, plus Mochie Shader Pack.

## Conventions

- Code style: follow existing formatting and naming conventions in `MATE ENGINE - Scripts/`
- Scripts are organized by feature domain, not by type
- The project is Windows-focused with Win32 API dependencies — Linux port is unofficial
- `.gitignore` excludes AI models (Qwen GGUF files), test station, and generated files
- Third-party packages are embedded in `Assets/MATE ENGINE - Packages/` (not via UPM where possible)
