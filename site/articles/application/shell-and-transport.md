---
title: "Shell and Transport"
---

# Shell and Transport

The main application presents a Codex-inspired desktop layout:

- left navigation rail
- thread sidebar
- center conversation stream
- right detail pane
- bottom terminal strip

## Transport responsibilities

`CodexGui.AppServer` owns the connection layer and supports two modes:

- launching a local process and speaking JSON-RPC over redirected standard I/O
- connecting to a remote app-server over WebSocket

The client performs the initialize lifecycle explicitly and then loads the read-only and interactive surfaces the UI depends on, including account, model, thread, skills, app, and config requirements data.

## Why the split matters

Keeping the Avalonia shell separate from the transport layer makes it easier to:

- evolve the UI without rewriting protocol code
- reuse typed app-server models across the shell
- reason about connection behavior independently from views
