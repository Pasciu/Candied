# Candied

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![BepInEx](https://img.shields.io/badge/BepInEx-5.4.21-orange)](https://github.com/BepInEx/BepInEx)
[![HarmonyX](https://img.shields.io/badge/HarmonyX-Ready-blue)](https://github.com/BepInEx/HarmonyX)
[![Protocol](https://img.shields.io/badge/MCP-2024--11--05-green)](https://modelcontextprotocol.io/)

An automated **Modding Wrapper** for Unity Mono games using the **BepInEx** framework and **HarmonyX**.

**Candied** bridges the gap between massive decompiled Unity game codebases and mod development. It solves the friction of setting up a reverse-engineering environment, locating game logic, configuring member publicizers, compiling plugins, and diagnosing runtime errors.

You should use **Candied** if you are a:

* **Gamer** who wants personal or accessibility improvements in a Unity game, even with little or no modding experience.
* **Modpack maintainer** investigating crashes and unexpected behaviour.
* **Modder** who wants to quickly scaffold a mod project, inspect game assemblies, and implement Harmony patches without the overhead of setting up a reverse-engineering environment.

---

## 🌟 Key Features

* 🔍 **ILSpy-like Decompilation:** Automatically inspects Unity game folders, decompiles game assemblies via `ICSharpCode.Decompiler` and stores the source code for following searches. It indexes types, methods, fields, and inheritance hierarchies into a high-performance SQLite database.
* 📦 **Automatic Mod Creation:** Generates a ready-to-compile C# project targeting BepInEx 5. Integrates direct access to private game members without reflection, Harmony patch templates, and post-build deployment directly into the game's `BepInEx/plugins/` directory.
* 📋 **Runtime Log Monitor:** Reads and tails `<GameDir>/BepInEx/LogOutput.log` even while the game process is running (via `FileShare.ReadWrite`), reconstructs stack traces, and detects Harmony patch collisions.
* 🤖 **Model Context Protocol (MCP) Server:** Native stdio JSON-RPC server ready to be plugged into AI agents like **Claude Desktop**, **Cursor**, and **Antigravity**.

---

## 🚀 Quick Start

This guide covers the prerequisites, installation, and basic usage of the Candied Workbench to help you deploy your first mod.

---

### 1. Prerequisites: Configuring the Environment

There is no need to configure anything manually in the terminal. Clone the repository and let your AI assistant handle the setup.

```bash
git clone https://github.com/Pasciu/Candied.git
```

Open your AI assistant (Antigravity, Cursor, or Claude) in the cloned repository and paste this prompt:

```markdown
I just cloned the Candied repository. Set up the environment for me:
1. Verify that the .NET 9 SDK is installed.
2. Run `./setup.ps1` (on Windows) or `./setup.sh` (on Linux/macOS) to validate the test suite and publish the MCP server to `./publish/`.
3. Verify that the generated executable responds correctly to an MCP ping.
4. Check the environment's MCP configuration (see `mcp_config.template.json`) to ensure the Candied server is ready to use.
5. Let me know when the environment is ready for mod development.
```

The agent will run the script, build and publish the MCP server, and verify that everything works.

If the agent reports any errors, fix them before proceeding. Once the environment is verified, you can start using Candied MCP tools to inspect games, scaffold mods, and build plugins.

---

### 2. Creating a Mod

Once the environment is ready, ask the agent to create a mod and provide the path to the game:

#### Quickest way to deploy a mod:

```markdown
I want to create a mod for my game located at `C:\Games\SampleGame` that [does thing]. Inspect the game to locate the correct classes, scaffold a BepInEx mod named '[ModName]', implement the Harmony patch, build it, and check for compile errors.
```

#### Exploring game logic and structure:
To inspect a game without scaffolding a mod or writing code, use the following prompt to explore relevant game logic:
```markdown
I want you to inspect the game located at `C:\Games\SampleGame` and search for logic related to <game mechanic>. Provide a concise summary of the relevant classes, key fields and methods, and their exact signatures.
```
---

## 📚 Documentation

* 🎮 [**MODDING.md**](./MODDING.md): A step-by-step guide for AI agents and modders on creating Unity mods with Candied's MCP tools.
* 🔄 [**docs/mcp-update-guide.md**](./docs/mcp-update-guide.md): How to rebuild, republish (`./setup.ps1`), and reload the Candied MCP Server after codebase modifications.

---

## Note on Multiplayer & Anti-Cheat

Candied has never been tested on multiplayer games or titles with anti-cheat systems. Modding online games with anti-cheat can carry a risk of account bans, so use it at your own discretion—the author cannot take responsibility for any bans or penalties.

---

## Credits & Open-Source Ecosystem

Candied stands on the shoulders of fantastic open-source projects—please consider checking them out and supporting their maintainers too:

* [**BepInEx**](https://github.com/BepInEx/BepInEx) — Unity / XNA game patcher and plugin framework.
* [**HarmonyX**](https://github.com/BepInEx/HarmonyX) (based on [**Harmony**](https://github.com/pardeike/Harmony)) — runtime method patching for .NET and Mono.
* [**ILSpy / ICSharpCode.Decompiler**](https://github.com/icsharpcode/ILSpy) — .NET assembly decompiler engine.
* [**AssetsTools.NET**](https://github.com/nesrak1/AssetsTools.NET) — offline Unity `.assets` and AssetBundle reader and texture decoder.
* [**Roslyn (`Microsoft.CodeAnalysis.CSharp`)**](https://github.com/dotnet/roslyn) — C# syntax analysis for symbol indexing.

---

## 📄 License & Mod Ownership

* **Candied** is licensed under the **[GNU Affero General Public License v3.0 (AGPL-3.0)](./LICENSE)** with a **[Section 7 Unity / BepInEx Linking & Mod Output Exception](./LICENSE-EXCEPTION.txt)** — Copyright (c) 2026 Pasciu.
* **Unity & BepInEx Linking:** The Section 7 exception explicitly permits compiling, linking, loading at runtime, and distributing Candied (including `Candied.VisionBridge`) alongside Unity Engine runtimes, BepInEx, HarmonyX, and legitimately owned Unity games.
* **Your Mods Belong to You:** Any mod, Harmony patch, template output, or original asset you create using Candied remains **100% the property of its author / legitimate user**, under whatever license you choose. Using Candied as a modding workbench does not place your mods under the AGPL-3.0 license.
