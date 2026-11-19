# Connecting a Local AI Model (Ollama)

> Applies to: M7 (AI-001 … AI-008) · Last reviewed: 2026-11-19
> Status: implemented and tested against a stub Ollama server; **not yet verified with a real Ollama instance** —
> section 6 is the manual test that closes this gap.

The AI assistant of Codebase Explorer is **optional and off by default**. When it is off, nothing is sent anywhere.
When you switch it on, it answers questions about an analyzed repository and explains components. It uses a language
model that runs in [Ollama](https://ollama.com), normally on your own machine. Answers are generated only from
analysis facts and source snippets of the repository, every statement cites its source, and the app checks each
citation against the analysis model. The AI never changes files.

## 1. What is sent where

| Situation | What leaves the app |
|---|---|
| AI off (default) | Nothing. No connection is opened, no index is built. |
| *Test connection* in Settings | Requests to the endpoint for its status, version and model list. No repository content. |
| A question or *Explain with AI*, endpoint on this machine (`localhost`, `127.0.0.1`, `[::1]`) | The question plus the selected facts and snippets (shown under every answer as *Context sent to the model*). They stay on this machine. |
| The same with an endpoint on **another** machine | The same content goes over the network to that machine — only after you allowed it for this repository (section 5). |

The app never sends the content of secret files (`.env*`, private keys and certificates, `.npmrc`, `.netrc`,
`credentials*`, `secrets.json`, `appsettings.<Environment>.json`), binary files, or files that changed since the last
analysis. The log files contain sizes and durations, never questions, context or answers.

## 2. Requirements

- Codebase Explorer with a repository that has been imported (analyzed).
- Ollama, current version, for Windows, macOS or Linux.
- Enough memory for the model. Rough guide for quantized models: a 3B model needs about 3–4 GB of free RAM, a 7B model
  about 6–8 GB, a 14B model about 12–16 GB. A GPU makes answers much faster but is not required.

## 3. Install Ollama and download a model

1. Install Ollama:
   - **Windows / macOS:** download and run the installer from https://ollama.com/download (macOS also:
     `brew install ollama`).
   - **Linux:** follow https://ollama.com/download/linux (the documented one-line installer, or your distribution's
     package).
2. Check the installation:

   ```bash
   ollama --version
   ```

3. Download a model. Suggestions (all from the Ollama library):

   | Model | Size | Good for |
   |---|---|---|
   | `qwen2.5-coder:7b` | ~4.7 GB | Recommended start: trained on code, follows citation rules well enough. |
   | `llama3.2:3b` | ~2 GB | Small machines; shorter, less precise answers. |
   | `qwen2.5-coder:14b` | ~9 GB | Better answers if you have the memory. |

   ```bash
   ollama pull qwen2.5-coder:7b
   ```

4. Make sure the Ollama server runs. The desktop installers start it automatically; otherwise start it with:

   ```bash
   ollama serve
   ```

5. Check that it answers and lists your model:

   ```bash
   curl http://localhost:11434/api/tags
   ```

   or simply `ollama list`.

## 4. Connect Codebase Explorer

1. Open **Settings → AI assistant (optional)**.
2. Tick **Enable the AI assistant**.
3. **Endpoint:** keep `http://localhost:11434` for an Ollama on this machine. Only `http`/`https` URLs without user
   name, password, query or fragment are accepted.
4. Click **Test connection**. Expected: *Connected to http://localhost:11434 (version …): N model(s) available.* The
   model field then suggests the installed models.
5. **Model:** choose the model, e.g. `qwen2.5-coder:7b` (exactly as `ollama list` shows it).
6. **Context size (tokens):** how much text (prompt and answer) the model may handle; default 8192. The app sends this
   value to Ollama (`num_ctx`) and plans the context to fit. Larger values give the model more facts and code but need
   more memory and time; do not exceed the model's maximum context length (`ollama show <model>` lists it).
7. **Temperature:** 0.2 (default) keeps answers factual; higher values make them more varied.
8. Click **Save AI settings**. Expected: *Saved.*

The settings are stored in the local database (`UserSettings`, keys `ai.*`). No password or API key is involved.

## 5. Use the assistant

- **Assistant page:** open a repository, then the *Assistant* page. Type a question and press **Ask** (or Ctrl+Enter).
  The answer streams in. The **first** question about a repository (and the first after a re-analysis) also indexes
  its files for search, which takes a moment for large repositories.
- **About the selected element:** select a node in the Architecture Explorer; on the Assistant page tick
  *About the selected element* to focus the question on it.
- **Citations:** buttons under each answer. A verified file citation shows the cited lines on the right; an element
  citation opens it in the graph. Citations marked **⚠** could not be verified (the key was not part of the context,
  the file is not in the analysis, or the lines do not exist) — treat those statements with care.
- **Context sent to the model:** expand it under an answer to see every fact and snippet with its key.
- **Cancel** stops the answer; nothing is saved. Conversations are kept per repository and can be deleted; removing the
  repository from the start page deletes them too.
- **Explain with AI:** in the Component Inspector of the Architecture Explorer. The explanation is cached for the
  current analysis run and reused until you re-analyze the repository or click **Regenerate**. Explanations are in
  English; questions are answered in the language you ask in.

### Ollama on another machine

You can point the endpoint at an Ollama on another computer (e.g. a GPU server). The app then treats the endpoint as
**remote**: the Assistant page shows a warning banner, and before the first question about each repository it asks
you to **allow** sending that repository's context there. Changing the endpoint later withdraws all such consents.

- Ollama's API has **no authentication**. Expose it only in a network you trust (on the server, Ollama listens on all
  interfaces when `OLLAMA_HOST=0.0.0.0` is set).
- Safer alternative: an SSH tunnel keeps the endpoint on `localhost` and encrypts the traffic:

  ```bash
  ssh -N -L 11434:localhost:11434 user@gpu-server
  ```

  Then keep `http://localhost:11434` as endpoint. (The app cannot tell that a tunnel leads elsewhere — you decide
  whether the remote machine may see the code.)

## 6. Manual test with a real Ollama instance (AI-002 acceptance)

Run this once per platform and record the result (date, OS, Ollama version, model) in
[TASK_BREAKDOWN.md](../implementation/TASK_BREAKDOWN.md) under AI-002.

1. Start Ollama with `qwen2.5-coder:7b` (or `llama3.2:3b`) installed; import the fixture
   `tests/CodebaseExplorer.TestRepositories/Repositories/dotnet-layered`.
2. Settings: enable AI, *Test connection* → the installed models are listed; select the model; save.
3. Stop Ollama, click *Test connection* → *Ollama at http://localhost:11434 is not available: the connection failed
   (is Ollama running?).* Start it again.
4. Assistant page: ask *"How are orders placed and stored?"* → the answer streams, cites keys such as `[F…]`/`[S…]`,
   mentions `PlaceOrderHandler` and `IOrderRepository`; citation buttons open the lines; the context list is shown.
5. Ask a follow-up (*"Which project implements the repository?"*) in the same conversation → the answer refers to
   `SqliteOrderRepository` in `Shop.Infrastructure`.
6. Ask something long-running and press **Cancel** → the question disappears, *Cancelled. Nothing was saved.*
7. Select a model that is not installed (type a name) and ask → *The model '…' is not available …*.
8. Architecture Explorer: select `PlaceOrderHandler`, **Explain with AI** → explanation with citations; select it
   again and click **Explain with AI** → *Explained earlier for this analysis run.* without waiting.
9. Switch the AI off in Settings → the Assistant page says the assistant is off; nothing can be asked.

## 7. Troubleshooting

| Message | Cause and fix |
|---|---|
| *The AI assistant is switched off. Enable it in Settings.* | Tick *Enable the AI assistant* and save. |
| *Select a model for the AI assistant in Settings.* | Choose a model and save. |
| *Ollama at … is not available: the connection failed (is Ollama running?).* | Start Ollama (`ollama serve`) or fix the endpoint/port. |
| *… is not available: it did not answer in time.* | The server is overloaded or unreachable; check firewall/VPN. |
| *The model '…' is not available at …* | Download it (`ollama pull <model>`) or select an installed one. |
| *The AI provider at … did not answer: …* | Ollama returned an error (often: not enough memory for the model or context size). Choose a smaller model or context size; check Ollama's log. |
| *The model returned an empty answer.* | Try again, or use a larger/better model. |
| *… is not on this machine. Context of this repository is sent there only after you allow it.* | The endpoint is remote; click *Allow for this repository and ask* if that is acceptable (section 5). |
| Answers ignore parts of the code | Increase the context size (and the model's memory), or ask about the selected element to focus the context. |
| Many **⚠** citations | Small models invent keys or line numbers; use a larger model and lower temperature. |

## 8. Turning it off and removing data

- Untick *Enable the AI assistant* and save: no further connection is made.
- Conversations: delete them on the Assistant page; all AI data of a repository (conversations, explanations, search
  index) is deleted when the repository is removed from the start page. Use *Compact database* in Settings to give the
  space back to the disk.
- To uninstall Ollama or delete models (`ollama rm <model>`), see the Ollama documentation.

Related: [AI_ARCHITECTURE.md](../architecture/AI_ARCHITECTURE.md) · [SECURITY.md](../architecture/SECURITY.md) ·
[ADR-008](../decisions/ADR-008-hybrid-ai.md) · [ADR-013](../decisions/ADR-013-search-index-on-first-ai-use.md)
