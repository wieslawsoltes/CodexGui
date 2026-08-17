---
title: "CodexGui"
description: "Native Avalonia desktop client for the Codex app-server protocol."
layout: simple
og_type: website
---

<div class="cg-hero">
  <div class="cg-eyebrow"><i class="bi bi-window-sidebar" aria-hidden="true"></i> Avalonia desktop client</div>
  <h1>CodexGui</h1>

  <p class="lead"><strong>CodexGui</strong> combines a typed Codex app-server transport, a multi-pane workspace shell, and rich document rendering powered by ProMarkdown.</p>

  <div class="cg-hero-actions">
    <a class="btn btn-primary btn-lg" href="articles/getting-started/overview"><i class="bi bi-rocket-takeoff" aria-hidden="true"></i> Start with the overview</a>
    <a class="btn btn-outline-secondary btn-lg" href="articles/getting-started/running-the-app"><i class="bi bi-play-circle" aria-hidden="true"></i> Run the desktop app</a>
    <a class="btn btn-outline-secondary btn-lg" href="https://github.com/wieslawsoltes/CodexGui"><i class="bi bi-github" aria-hidden="true"></i> GitHub repository</a>
  </div>

  <div class="cg-pill-list">
    <span class="cg-pill">Typed transport</span>
    <span class="cg-pill">Workspace shell</span>
    <span class="cg-pill">Approval flows</span>
    <span class="cg-pill">ProMarkdown rendering</span>
  </div>
</div>

## Start here

<div class="cg-link-grid">
  <a class="cg-link-card" href="articles/getting-started/overview">
    <span class="cg-link-card-title"><i class="bi bi-signpost-split" aria-hidden="true"></i> Getting Started</span>
    <p>Clone the repository with its submodule, build the solution, and launch the client.</p>
  </a>
  <a class="cg-link-card" href="articles/application">
    <span class="cg-link-card-title"><i class="bi bi-app-indicator" aria-hidden="true"></i> Application Shell</span>
    <p>Understand the shell layout, transport lifecycle, turn flows, and approval surfaces.</p>
  </a>
  <a class="cg-link-card" href="articles/development">
    <span class="cg-link-card-title"><i class="bi bi-tools" aria-hidden="true"></i> Development</span>
    <p>Build, package, and validate the repository and documentation site.</p>
  </a>
  <a class="cg-link-card" href="https://github.com/wieslawsoltes/ProMarkdown">
    <span class="cg-link-card-title"><i class="bi bi-markdown" aria-hidden="true"></i> ProMarkdown</span>
    <p>Explore the separately maintained Markdown library, plugins, sample, tests, and documentation.</p>
  </a>
</div>

## What you get today

<div class="cg-highlight-grid">
  <div class="cg-highlight-card">
    <strong>Native workspace shell</strong>
    <p>Navigation, thread, conversation, detail, and terminal surfaces designed for desktop workflows.</p>
  </div>
  <div class="cg-highlight-card">
    <strong>Protocol-aware transport</strong>
    <p>Local <code>codex app-server</code> process transport plus remote WebSocket connections.</p>
  </div>
  <div class="cg-highlight-card">
    <strong>Turn and approval flows</strong>
    <p>Live snapshots, turn authoring, interrupts, and approval prompts integrated into the UI model.</p>
  </div>
  <div class="cg-highlight-card">
    <strong>Rich Markdown rendering</strong>
    <p>The application consumes the pinned ProMarkdown submodule through a source project reference.</p>
  </div>
</div>

## Repository

- Source code and issues: [github.com/wieslawsoltes/CodexGui](https://github.com/wieslawsoltes/CodexGui)
- Desktop application: `src/CodexGui.App`
- App-server transport: `src/CodexGui.AppServer`
- Markdown dependency: `external/ProMarkdown`
