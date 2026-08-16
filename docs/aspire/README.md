# Task Manager Aspire AppHost

Orchestrates local development for Task Manager:

- PostgreSQL with **pgvector** (`pgvector/pgvector:pg16`)
- **WebApi** on ports 5000 (HTTP) / 5001 (HTTPS)
- **WebApp** (Blazor WASM) on port 5002 (HTTPS)
- Optional **pgAdmin** on host port 5050
- Local **Whisper / Speaches** STT sidecar (`ghcr.io/speaches-ai/speaches:latest-cpu`) for hold-to-talk voice commands

## Run

```bash
dotnet run --project src/AppHost/AppHost.csproj
```

The Aspire dashboard opens automatically. WebApi receives `ConnectionStrings__MainDb` from the `MainDb` database resource and `Ai__SpeechToText__Endpoint` from the Whisper container.

## Notes

- Set AI keys via WebApi user-secrets or process environment (same as non-Aspire runs).
- MCP Server is not started by AppHost (stdio / Claude Desktop process).
- Prefer this over `docker compose` for day-to-day API + UI work; avoid running both Postgres stacks unless you know you need two databases.
- First Whisper start downloads the tiny model into a named Docker volume (`taskmanager-aspire-whisper-cache`); later Aspire restarts reuse that cache and should log `already installed — skipping download`.
- Without AppHost, set `Ai:SpeechToText:Endpoint` to your local Speaches URL (e.g. `http://localhost:8000`).
- Voice commands need `Ai:Features:EnableVoiceTranscription` (and `EnableFunctionCalling` to map speech to tools).
- STT HttpClient clears Aspire’s default 30s resilience pipeline (`RemoveAllResilienceHandlers`) and uses `Ai:SpeechToText:WarmupTimeoutSeconds` (default 600s) so first-run downloads are not canceled.
