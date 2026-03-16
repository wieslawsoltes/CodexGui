---
title: "Markdown Stack"
---

# Markdown Stack

CodexGui includes a reusable markdown subsystem that is valuable independently of the desktop shell.

## Base library

`src/CodexGui.Markdown` contains the core controls, rendering services, editing services, and Markdig-based parsing pipeline.

## Separate plugin projects

Feature areas are split into dedicated plugin packages instead of growing the core library indefinitely. This keeps the architecture clearer and makes feature registration explicit in consumers such as the sample app.

## Sample app

`src/CodexGui.Markdown.Sample` demonstrates the markdown stack with editor integration, preview rendering, and plugin registration in one isolated executable.

Continue with [Plugin Ecosystem](plugin-ecosystem/) for the current project breakdown.
