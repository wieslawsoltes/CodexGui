---
title: "CodexGui"
description: "Native Avalonia desktop client for the Codex app-server protocol with a multi-pane workspace shell, typed transport, and reusable markdown rendering."
layout: simple
og_type: website
---

<div class="cg-hero">
  <div class="cg-eyebrow"><i class="bi bi-window-sidebar" aria-hidden="true"></i> Avalonia desktop client</div>
  <h1>CodexGui</h1>

  <p class="lead"><strong>CodexGui</strong> is a native Avalonia desktop client for the Codex app-server protocol. It combines a typed transport layer, a multi-pane workspace shell, and a reusable markdown stack for rich conversation, diff, and document experiences.</p>

  <div class="cg-hero-actions">
    <a class="btn btn-primary btn-lg" href="articles/getting-started/overview"><i class="bi bi-rocket-takeoff" aria-hidden="true"></i> Start with the overview</a>
    <a class="btn btn-outline-secondary btn-lg" href="articles/getting-started/running-the-app"><i class="bi bi-play-circle" aria-hidden="true"></i> Run the desktop app</a>
    <a class="btn btn-outline-secondary btn-lg" href="https://github.com/wieslawsoltes/CodexGui"><i class="bi bi-github" aria-hidden="true"></i> GitHub repository</a>
  </div>

  <div class="cg-pill-list">
    <span class="cg-pill">Typed transport</span>
    <span class="cg-pill">Workspace shell</span>
    <span class="cg-pill">Approval flows</span>
    <span class="cg-pill">Reusable markdown plugins</span>
  </div>
</div>

## Start Here

<div class="cg-link-grid">
  <a class="cg-link-card" href="articles/getting-started/overview">
    <span class="cg-link-card-title"><i class="bi bi-signpost-split" aria-hidden="true"></i> Getting Started Overview</span>
    <p>See the project layout, core capabilities, and how the desktop client fits the Codex workflow.</p>
  </a>
  <a class="cg-link-card" href="articles/getting-started/running-the-app">
    <span class="cg-link-card-title"><i class="bi bi-play-circle" aria-hidden="true"></i> Running the App</span>
    <p>Build the solution, launch the Avalonia desktop shell, and connect to an app-server endpoint.</p>
  </a>
  <a class="cg-link-card" href="articles/application">
    <span class="cg-link-card-title"><i class="bi bi-app-indicator" aria-hidden="true"></i> Application Shell</span>
    <p>Understand the shell layout, transport lifecycle, turn flows, and approval surfaces.</p>
  </a>
  <a class="cg-link-card" href="articles/markdown">
    <span class="cg-link-card-title"><i class="bi bi-markdown" aria-hidden="true"></i> Markdown Stack</span>
    <p>Explore the reusable markdown subsystem, plugin projects, and rich rendering building blocks.</p>
  </a>
</div>

## Documentation Areas

<div class="cg-link-grid cg-link-grid--wide">
  <a class="cg-link-card" href="articles/getting-started">
    <span class="cg-link-card-title"><i class="bi bi-rocket" aria-hidden="true"></i> Getting Started</span>
    <p>Use the overview and run instructions to get the desktop client working quickly.</p>
  </a>
  <a class="cg-link-card" href="articles/application">
    <span class="cg-link-card-title"><i class="bi bi-diagram-3" aria-hidden="true"></i> Application Shell</span>
    <p>Dive into the transport orchestration, shell layout, and interaction model.</p>
  </a>
  <a class="cg-link-card" href="articles/markdown">
    <span class="cg-link-card-title"><i class="bi bi-file-earmark-richtext" aria-hidden="true"></i> Markdown Stack</span>
    <p>Review the renderer, editor integration, and extension/plugin ecosystem.</p>
  </a>
  <a class="cg-link-card" href="articles/development">
    <span class="cg-link-card-title"><i class="bi bi-tools" aria-hidden="true"></i> Development</span>
    <p>Build, test, and validate the repository, including the Lunet docs pipeline.</p>
  </a>
  <a class="cg-link-card" href="articles/reference">
    <span class="cg-link-card-title"><i class="bi bi-collection" aria-hidden="true"></i> Reference</span>
    <p>Browse repository structure, roadmap direction, and supporting project references.</p>
  </a>
</div>

## What you get today

<div class="cg-highlight-grid">
  <div class="cg-highlight-card">
    <strong>Native workspace shell</strong>
    <p>Navigation rail, thread list, center conversation stream, detail pane, and terminal strip designed for desktop workflows.</p>
  </div>
  <div class="cg-highlight-card">
    <strong>Protocol-aware transport</strong>
    <p>Local <code>codex app-server</code> process transport plus remote <code>ws://</code> and <code>wss://</code> connections with lifecycle and interrupt handling.</p>
  </div>
  <div class="cg-highlight-card">
    <strong>Turn and approval flows</strong>
    <p>Initialize handshake, live snapshots, turn authoring, interrupts, and approval prompts integrated into the UI model.</p>
  </div>
  <div class="cg-highlight-card">
    <strong>Reusable markdown system</strong>
    <p>Shared rendering and editing infrastructure with plugin projects for alerts, math, Mermaid, figures, footers, syntax highlighting, and more.</p>
  </div>
</div>

## Repository

- Source code and issues: [github.com/wieslawsoltes/CodexGui](https://github.com/wieslawsoltes/CodexGui)
- Desktop application entry point: `src/CodexGui.App`
- Transport and protocol client: `src/CodexGui.AppServer`
- Markdown libraries and sample: `src/CodexGui.Markdown*`
- Internal design notes in the repository: [`docs/`](https://github.com/wieslawsoltes/CodexGui/tree/main/docs)
