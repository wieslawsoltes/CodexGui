---
title: "Interaction Model"
---

# Interaction Model

CodexGui is no longer a read-only browser. The current application supports active session workflows on top of the live transport.

## Implemented interactions

- starting new threads
- resuming existing threads
- authoring turns
- interrupting active turns
- reviewing commands, diffs, plans, reasoning, and tool-call items in item-specific cards
- responding to approval and user-input requests from the server

## UI surfaces involved

- the center stream shows item cards and live updates
- the right pane renders detail content for the selected item
- the terminal strip surfaces transport notifications and activity
- the connection workspace keeps command path, arguments, and working directory editable

## Current parity gaps

The repository roadmap still calls out review mode, rollback and forking polish, broader auth flows, and fuller app or skill administration as future work.
