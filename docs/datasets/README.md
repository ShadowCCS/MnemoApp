# Chat fine-tuning dataset examples

Raw captures live under `%LocalAppData%\mnemo\chat_dataset\conversations.jsonl`.  
**Export** (Developer settings → *Export training datasets*) writes `manager_dataset.jsonl` and `main_model_dataset.jsonl` next to that file.

## Is this the right format for lots of rows?

**Yes.** Each line is one JSON object. The same shape the exporter produces—`messages` (with optional `content` + `tool_calls` on assistant turns, then matching `tool` messages) and optional top-level `tools`—is what you want for a main-model fine-tune, repeated for every conversation turn you keep.

**Qwen3 + tool calling:** Qwen3-family models are built for tools when you use the **official chat template** and a backend that speaks **OpenAI-style** tool messages (what llama.cpp / many trainers expect). Your app already uses that shape. Still verify: same template at **train** and **inference**, and tool names/schemas match what the server registers.

**Match deployment:** Prefer training `system` strings and `tools` lists that match what the app will send after your prompt trim (fine-tuning fills behavior; prompts should stay stable enough not to confuse the adapter).

## Main model: “user feels included” without breaking tool calling

Some traces only show **silent** tool rounds: the assistant message has `tool_calls` and `content` is `null`. That is valid for the API, but it feels like the AI is doing things in the background.

You **cannot** replace real `tool_calls` with plain text like `[tool_calls: list_notes]` — the runtime expects OpenAI-style `tool_calls` (see `ToolCallParser`, `AIOrchestrator`). Training on pseudo-tags would teach the wrong output format.

**What works:** the same assistant turn can include **both**:

1. **`content`** — short, user-visible line(s): what you’re doing and why (“I’ll search your notes for Spanish…”, “Opening that note now…”).
2. **`tool_calls`** — the actual structured calls (`name` + JSON `arguments`) the app executes.

That matches what OpenAI-compatible chat APIs allow: optional assistant text **and** `tool_calls` in one message.

- **Bad (do not use for training):** `role: assistant`, `content: "… [tool_calls: list_notes]"`, and `role: tool` without `tool_call_id` / `name`.
- **Good:** `role: assistant`, `content: "…"`, `tool_calls: [{ id, type, function: { name, arguments } }]`, then `role: tool` with `tool_call_id`, `name`, `content`.

## Example files

| File | Purpose |
|------|--------|
| [`main_model_proper_example.formatted.json`](./main_model_proper_example.formatted.json) | **Human-readable** reference: same data as below, indented for editing and learning. |
| [`main_model_proper_example.jsonl`](./main_model_proper_example.jsonl) | One **minified** line (same JSON object) ready to append to a training `.jsonl` file. |

Use them as a template when **hand-editing** exported rows or writing synthetic rows. Keep `tool_call_id` aligned with the `id` in the preceding `tool_calls` entry.

## Optional improvements to exported rows

- Remove **duplicate** consecutive `user` messages if the logger captured the same turn twice.
- Deduplicate repeated blocks inside `system` if the composer appended the skill twice.
- Normalize `function.arguments` to compact one-line JSON (cosmetic only).
