# AI Features — Implementation Details

Per-feature notes for AI **functionality and implementation** in this codebase (APIs, flows, gates, primary code).  
Project overview, stack, providers, OTel, and eval posture: root [`README.md`](../README.md).

Shared posture: **feature flags, JWT + access guards, sanitize inputs, validate outputs, rate-limit hot paths, prefer refuse/filter over trusting the model.**

---

## 1. RAG (Ask)

**What:** Natural-language Q&A over the caller’s todos.  
**API:** `POST /api/ai/query` (`[Authorize]` + AI rate limit).  
**Flag:** `Ai:Features:EnableRag`.

### Design goal

Treat RAG like a **product feature with hard gates**, not a chatbot bolted onto an LLM:

1. **Code owns facts** (who can see what, date windows, priority/status filters).
2. **Retrieval is scoped and thresholded** (tenant + similarity / hybrid).
3. **The LLM only narrates inside a pre-built context.**
4. **Answers are validated** before they reach the client (refuse empty / ungrounded citations).

### Request flow

```
Client → AiController → MediatR handler
  → intent + temporal routing (deterministic)
  → retrieve (pgvector ± FTS, owner-scoped)
  → RAGPipeline (sanitize → prompt → LLM → validate)
  → answer + sources (+ conversation log)
```

| Layer | Responsibility |
|-------|----------------|
| WebApi | Auth, rate limit, ProblemDetails |
| ServiceLayer | Access control, routing, filters, persistence |
| AiLayer | Embed/search pipeline, generation, I/O validation |
| DataLayer | pgvector + lexical search |

Generation (`RAGPipeline`) does **not** retrieve — callers pass already-filtered sources so the model cannot reopen calendar or tenancy.

### Retrieval strategy (why)

| Decision | Why it’s production-minded |
|----------|----------------------------|
| Embeddings in PostgreSQL/pgvector (local BGE by default) | Same DB/tenant story as app data |
| Owner filter on every search + re-check on hydrate | Stops cross-user leakage even if a hit list is wrong |
| `MinSimilarity` on vector hits | Nearest-neighbor always returns *something*; weak neighbors are dropped |
| Optional hybrid (vector + FTS + RRF) | Exact tokens and paraphrases both work |
| Temporal windows in C# (not the LLM) | “This week / overdue” is calendar math — deterministic and testable |
| Plain list questions → no LLM | Cheap, consistent listings; LLM only when judgment is needed |
| Cap context size + retrieval limit | Bounds cost, latency, and prompt injection surface |

### Safety & controls

**Ingress:** JWT, feature flag → 503, FluentValidation (question ≤ 2000), dedicated RAG rate limit.  
**Authz:** `TodoAccessGuard`; non-admins only see own todos at search and load time.  
**Input:** `PromptInputSanitizer` + hard context budgets.  
**Output:** Empty retrieval → fixed “insufficient context”. Any GUID in the answer must be in retrieved sources; else fixed “ungrounded” message.  
**Ops:** OTel RAG outcomes; prompt version `rag.v1`; offline fixtures `samples/ai-eval-rag.json` (judge off hot path).

### What this is *not* claiming

- Full claim-level faithfulness (GUID grounding only).
- Perfect NLU routing (phrase heuristics for intent/dates).
- Multi-tenant SaaS hardening beyond owner/admin scoping in this app.
- Document / handbook RAG — that is **Knowledge Lab** (§2).

### Primary code

- Orchestration: `AiQueryCommandHandler`
- Generate + validate: `RAGPipeline`, `LlmOutputValidator`
- Retrieve: `SemanticSearchPipeline`, `PgVectorEmbeddingStore`
- Index: `EmbeddingProcessor` → `TodoEmbeddings`

---

## 2. Knowledge Lab (advanced / document RAG)

**What:** Upload owner-scoped documents → chunk + embed → natural-language Ask over the document corpus (optional unified Ask with todos). Rich RAG **trace** for demos and debugging.  
**API** (`KnowledgeController`, `[Authorize]`):  
- `POST /api/ai/knowledge/documents` — multipart upload (`.txt` / `.md`; `.pdf` when enabled)  
- `GET /api/ai/knowledge/documents`, `GET .../documents/{id}`, `GET .../documents/{id}/chunks`, `DELETE .../documents/{id}`  
- `GET /api/ai/knowledge/ingest/{jobId}` — poll ingest job  
- `POST /api/ai/knowledge/query` — Ask (+ optional `DocumentExternalId`, `IncludeTodos`)  
**SignalR:** `KnowledgeIngestHub` (`/hubs/knowledge-ingest`) — `IngestJobUpdated` for UI progress (owner-scoped group).  
**Flag:** `Ai:Features:EnableKnowledgeRag` (also requires `EnableEmbeddings`)  
**Rate limit:** `ai-knowledge-rag` on upload + query  
**Knobs:** `Ai:Features:KnowledgeRag` (chunk size/overlap, retrieval limit, MinSimilarity, hybrid/RRF, query rewrite, agentic second retrieve, PDF ingest, re-ingest)

### Design goal

Document RAG with the **same security posture as todo Ask**: owner scope in code, retrieve before generate, refuse empty / ungrounded answers — plus an ingest lifecycle demos usually skip.

### Request flow

**Ingest**

```
POST documents → UploadKnowledgeDocumentCommandHandler
  → validate type/size (± PDF text extract)
  → optional re-ingest (same filename replaces prior doc)
  → KnowledgeDocument (Pending) + KnowledgeIngestJob
  → background: parse → TextChunker → embed → Ready / Failed
  → SignalR IngestJobUpdated (+ GET poll)
```

**Ask**

```
POST query → KnowledgeQueryCommandHandler
  → sanitize question; optional LLM query rewrite
  → KnowledgeSemanticSearchPipeline (vector ± FTS + RRF, owner + doc filter)
  → optional todo semantic hits when IncludeTodos
  → RAGPipeline (CorpusKind Knowledge | Unified)
  → optional agentic round 2 if insufficient context
  → answer + sources + KnowledgeRagTraceDto
```

| Decision | Why |
|----------|-----|
| Shared `RAGPipeline` with `RagCorpusKind.Knowledge` / `Unified` | One generate+validate path; corpus kind picks prompts / refuse messages |
| Owner filter + document chip resolved to `ExternalId` in code | Scope is authz, not a prompt hint |
| Optional hybrid (vector + Postgres FTS → RRF) | Exact tokens (env vars, ticket ids) + paraphrases |
| Optional query rewrite | Better retrieval for paraphrased / underspecified questions |
| Optional agentic second retrieve | Recover when round 1 returns insufficient context (excludes already-used chunks) |
| Optional PDF ingest (`PdfTextExtractor`) | Handbook demos without forcing markdown-only |
| Optional re-ingest by filename | Replace stale chunks instead of silently accumulating duplicates |
| Trace: rewrite, rounds, hybrid flags, hits, outcome | Debuggable RAG for portfolio / ops — judge still off hot path |
| Ingest job + stuck-after + SignalR | Long embed runs are operable; UI can subscribe or poll Ready |

### Safety & controls

**Ingress:** JWT; flag → 503; FluentValidation on query; upload size cap (`MaxUploadBytes`); rate limit on upload/query.  
**Authz:** `KnowledgeAccessGuard` / owner on documents and jobs; todo branch uses owner-scoped semantic search; ingest hub joins owner-scoped groups.  
**Input:** `PromptInputSanitizer` on question and rewrite output.  
**Output:** Empty / weak context → fixed insufficient message; faithfulness validation → ungrounded message (`LlmOutputValidator`).  
**Ops:** OTel feature `knowledge_rag`; prompt version `rag.knowledge.v1`; samples under `samples/knowledge-lab/`.

### What this is *not* claiming

- Cross-encoder rerank or GraphRAG.
- Perfect PDF layout/table extraction.
- Unified Ask as a replacement for todo-only Ask filters (dates/priority still live on `/api/ai/query`).

### Primary code

- API: `KnowledgeController`
- Ingest: `UploadKnowledgeDocumentCommandHandler`, `KnowledgeIngestProcessor`, `TextChunker`, `PdfTextExtractor`
- Notify: `KnowledgeIngestHub`, `SignalRKnowledgeIngestNotifier`
- Ask: `KnowledgeQueryCommandHandler`, `KnowledgeQueryRewriter`, `KnowledgeSemanticSearchPipeline`
- Generate: `RAGPipeline` (`RagCorpusKind.Knowledge` / `Unified`)
- Data: `IKnowledgeDocumentRepository`, `IKnowledgeChunkEmbeddingRepository`, `IKnowledgeLexicalSearchRepository`

---

## 3. Summarization

**What:** Short AI summary of a todo’s title/description; cached in AI metadata. Client sends task id (+ optional `Force`), not free text.  
**API:** `POST /api/ai/summarize`  
**Flag:** `Ai:Features:EnableSummarization`  
**Rate limit:** `ai-summarize`

### Design goal

Summarize **stored** task content with sanitize → LLM → validate → cache — not arbitrary prompt-in.

### Request flow

```
Client → SummarizeTodoTaskCommandHandler
  → feature flag + TodoAccessGuard
  → cache hit (unless Force) OR SummarizationPipeline
  → UpsertSummaryAsync → TaskSummaryDto
```

| Decision | Why |
|----------|-----|
| Load title/description from DB | Client can’t inject alternate text into the prompt |
| Cache in `TodoAiMetadata` | Avoid repeat LLM cost; `Force` for refresh |
| Delimited prompt + ignore-instructions-inside | Cuts prompt-injection from task body |
| `LlmOutputValidator.ValidateSummary` | Empty/control-char reject; hard length cap |

### Safety & controls

**Ingress:** JWT, flag → 503, FluentValidation, rate limit.  
**Authz:** `TodoAccessGuard`.  
**Input/output:** sanitize + summary length cap; ProblemDetails on unexpected errors.  
**Ops:** OTel summarize; prompt version `summarize.v1`.

### What this is *not* claiming

- Faithfulness beyond prompt rules (“don’t speculate”).
- Identical quality across all LLM providers.

### Primary code

- `SummarizeTodoTaskCommandHandler`, `SummarizationPipeline`
- `PromptInputSanitizer`, `LlmOutputValidator`

---

## 4. Classification (+ apply-priority)

**What:** Suggest priority (`HIGH` / `MEDIUM` / `LOW`) + confidence + reason; user must apply. Auto-queued on create when enabled; SignalR for progress.  
**API:**  
- `GET /api/ai/classify/{todoExternalId}` — status only (no LLM)  
- `POST /api/ai/classify` — run/retry (`Force`)  
- `POST /api/ai/apply-priority` — write suggestion onto the todo  
**Flag:** `Ai:Features:EnableClassification`  
**Rate limit:** `ai-classify` on **POST classify** only

### Design goal

Treat priority as a **suggestion with deterministic guardrails**, not an auto-write from the model.

### Request flow

```
Create todo → ClassificationQueue → ClassificationPipeline
POST classify → same processor (sync retry)
GET classify → metadata only
POST apply-priority → completed suggestion → todo.Priority (user confirm)
```

| Decision | Why |
|----------|-----|
| Suggest ≠ apply | Human confirm before mutating priority |
| `ClassificationGuardrails` after LLM | Keyword/due-date overrides beat model quirks |
| Strict JSON validate (no silent defaults) | Bad schema fails loudly |
| Background queue + parallelism cap | Create path stays fast; LLM load bounded |
| SignalR classification hub | UI progress without spinning the LLM |

### Safety & controls

**Ingress:** JWT; flag → 503; validators; rate limit on POST classify.  
**Authz:** `TodoAccessGuard` (incl. hub).  
**Input/output:** sanitize title/description; normalize priority; clamp confidence; apply only when status is completed.  
**Ops:** OTel classify; offline harness `samples/ai-eval-todos.csv`.

### What this is *not* claiming

- Keyword guardrails are complete (phrase list, not ML).
- Apply-priority has its own rate limit (it doesn’t — relies on classify gating + authz).

### Primary code

- `ClassifyTodoTaskCommandHandler`, `ApplyAiPriorityCommandHandler`, `ClassificationProcessor`
- `ClassificationPipeline`, `ClassificationGuardrails`, `ClassificationBackgroundService`

---

## 5. Embeddings indexing

**What:** Index todo title+description → vector (default local ONNX `bge-small-en-v1.5`, 384-d) in **pgvector**. Foundation for semantic search and RAG.  
**API:** none (side effect of create/update, batch, startup backfill)  
**Flag:** `Ai:Features:EnableEmbeddings`

### Design goal

Keep vectors in the **same Postgres/tenant story** as todos; async so writes stay cheap.

### Request flow

```
Create/Update todo → EmbeddingQueue → EmbeddingProcessor
  → IEmbeddingService → PgVectorEmbeddingStore
Startup: BackfillMissingAsync for current model
```

| Decision | Why |
|----------|-----|
| Default Onnx local BGE | No embedding SaaS required for demo/prod-lite |
| Async queue + backfill | Create path not blocked; heals gaps after enable |
| Passage vs query input kinds | Asymmetric instruction for search quality |
| Store model name with vector | Avoid mixing incompatible dims/models |

### Safety & controls

No public embed API. Text from DB only. Vectors not returned to clients. OTel on embed ops.

### What this is *not* claiming

- Fancy re-embed policies beyond update enqueue + model-named rows.
- Cross-provider compatibility without reindex.

### Primary code

- `EmbeddingProcessor`, `EmbeddingBackgroundService`, `EmbeddingQueue`
- `OnnxBgeEmbeddingService` / `SemanticKernelEmbeddingService`, `PgVectorEmbeddingStore`

---

## 6. Semantic search

**What:** Natural-language similarity search over indexed todos; optional hybrid vector + FTS → RRF.  
**API:** `POST /api/ai/todos/search/semantic`  
**Flags:** `EnableSemanticSearch` **and** `EnableEmbeddings`  
**Rate limit:** `ai-semantic-search`

### Design goal

Return **owner-scoped hits with a similarity floor** — nearest-neighbor alone always returns *something*.

### Request flow

```
Client → SemanticSearchTodosCommandHandler
  → flags + owner scope
  → SemanticSearchPipeline (sanitize → embed → pgvector ± FTS/RRF)
  → hydrate + TodoAccessGuard re-check → hits
```

| Decision | Why |
|----------|-----|
| Dual feature flags | Search useless without embeddings |
| `MinSimilarity` | Drop weak neighbors |
| Optional hybrid + RRF | Exact tokens + paraphrases |
| Owner filter + hydrate re-check | Defense in depth |
| Clamp limit | Bound cost/latency |

### Safety & controls

**Ingress:** JWT, flags → 503, FluentValidation (query ≤ 500), rate limit.  
**Authz:** owner-scoped search; admin broader; hydrate `EnsureCanAccess`.  
**Input:** `PromptInputSanitizer`.  
**Ops:** OTel latency/outcome.

### What this is *not* claiming

- Dedicated search-product ranking quality.
- Identical score semantics between vector-only and hybrid fusion.

### Primary code

- `SemanticSearchTodosCommandHandler`, `SemanticSearchPipeline`
- `PgVectorEmbeddingStore`, lexical search repo

---

## 7. Function calling / tools

**What:** Two-step: LLM **proposes** tool calls; user **confirms**; server executes allowlisted tools.  
**API:** `POST /api/ai/propose-tools`, `POST /api/ai/execute-tools`  
**Flag:** `Ai:Features:EnableFunctionCalling`  
**Rate limit:** `ai-function-calling` (both)

### Design goal

**Propose ≠ execute.** The model never mutates data until the client confirms a validated call list.

### Request flow

```
propose → ToolProposalPipeline
  → sanitize → LLM JSON → allowlist + ToolArgumentValidator (drop bad)
execute → ToolExecutor
  → re-validate → MediatR/repos + TodoAccessGuard → execution log
```

| Decision | Why |
|----------|-----|
| Explicit confirm step | Stops autonomous destructive actions |
| Allowlist (`TodoToolDefinitions`) | No arbitrary tool names |
| Validate on propose **and** execute | Client can’t smuggle bad args past UI |
| Cap calls per execute | Blast-radius bound |
| Soft-fail parse → empty proposals | Prefer no-op over crashing UX |

### Safety & controls

**Ingress:** JWT, flag → 503, validators, rate limit.  
**Authz:** `TodoAccessGuard` on every mutating tool.  
**Output:** per-call success/fail; audit via tool execution log.  
**Ops:** OTel + `ToolArgRejected`; fixtures `samples/ai-eval-tool-proposals.json`.

### What this is *not* claiming

- Propose is safe if a client auto-executes without UI confirm.
- Exhaustive JSON key allowlisting beyond per-tool required fields.

### Primary code

- `ProposeAiToolsCommandHandler`, `ExecuteAiToolsCommandHandler`
- `ToolProposalPipeline`, `ToolExecutor`, `ToolArgumentValidator`, `TodoToolDefinitions`

---

## 8. Agents / Microsoft Agent Framework (sprint optimizer)

**What:** Async MAF job: Analyst → Planner (or single-agent mode) plans a sprint from owner-scoped todos; SignalR progress; **stops at a proposal** until the user approves or rejects. Heuristic fallback if the agent fails or hits budget.  
**API:**  
- `POST /api/ai/agent/sprint-optimizer` — start (rate-limited)  
- `GET .../active`, `GET .../{jobExternalId}` — poll  
- `POST .../{jobExternalId}/cancel`  
- `POST .../{jobExternalId}/approve` — persist sprint from proposal (optional edited task id list)  
- `POST .../{jobExternalId}/reject` — discard proposal  
**SignalR:** `SprintOptimizerHub` (`/hubs/sprint-optimizer`) — `SprintOptimizerUpdated`  
**Flag:** `Ai:Features:EnableAgents` (start)  
**Rate limit:** `ai-agents` on **start** only  
**Budgets:** `Ai:Agents` — `MaxToolInvocationsPerJob`, `MaxPlannerRecoveryPasses`, `JobTimeoutSeconds`, `StuckAfterSeconds` (optional `ChatModel` override for agents only)

### Design goal

Multi-step planning with **tool budgets, real GUID tools, durable job state, and human approval before persist** — not a free-form chat agent that writes sprints autonomously.

### Request flow

```
POST optimize → queue job (one active / user)
  → SprintOptimizerAgent + SprintPlanningTools (owner-scoped)
  → propose_sprint_plan (reject unknown ids)  [Planner does NOT create_sprint]
  → status AwaitingApproval + ProposalJson
  → SignalR + GET poll; cancel supported
  → user POST approve (optional SelectedTaskExternalIds) → create sprint
     or POST reject → clear proposal
```

| Decision | Why |
|----------|-----|
| Durable job + heartbeat / stuck / timeout | Long LLM runs must be operable |
| One active job per owner | Prevents pile-up / cost storms |
| Tools must use real candidate GUIDs | Stops invented ids |
| Max tool invocations + job timeout | Hard cost/latency caps |
| Heuristic fallback | User still gets a reviewable plan when MAF fails |
| Stop at `AwaitingApproval` | Propose ≠ persist — same product posture as tools |
| Approve may re-resolve / edit selected ids | User can trim the proposal; unknown ids dropped; telemetry `proposal_edited` |

### Safety & controls

**Ingress:** JWT; flag → 503 on start; clamps on maxTasks/duration; rate limit on start; 409 if active job exists.  
**Authz:** owner-scoped candidates; get/cancel/approve/reject ownership checks (admin override).  
**Output:** agent text validated; unknown ids rejected at propose and again on approve.  
**Ops:** OTel agent/budget/quality; SignalR hub auth via current user.

### What this is *not* claiming

- Optimal sprint-planning quality.
- Feature flag on GET/cancel/approve/reject (start is gated; those remain authz-scoped).
- That the Planner tool `create_sprint` runs during the agent loop — persist is approval-only.

### Primary code

- `OptimizeSprintCommandHandler`, `SprintOptimizerAgent`, `SprintOptimizerWorkflowHost`, `SprintPlanningTools`
- `SprintOptimizerBackgroundService`, `SprintOptimizerPersistService`
- `ApproveSprintOptimizerProposalCommandHandler`, `RejectSprintOptimizerProposalCommandHandler`
- `SprintOptimizerHub` / notifier, `SprintOptimizerJobMapper`
---

## 9. MCP

**What:** Standalone stdio MCP server for Claude Desktop; HTTP client to WebApi as the Auth0 user (Device Code + refresh). Tools/resources over the public API — **no direct DB**.  
**API:** none on WebApi; MCP tools call normal WebApi routes.  
**Flag:** `Ai:Features:EnableMcp` exists in config but is **not enforced** in McpServer/WebApi today.  
**Rate limit:** inherits WebApi policies on called endpoints.

### Design goal

Give Claude the same **user-scoped HTTP surface** as the app, with real OAuth — not a privileged sidecar.

### Request flow

```
Claude ↔ McpServer (stdio)
  → Auth0 Device Code / token cache
  → TaskManagerApiClient (Bearer) → WebApi → same authz as UI
```

| Decision | Why |
|----------|-----|
| HTTP to WebApi only | Reuses JWT, policies, AI gates |
| Device Code + refresh | Safer desktop auth UX than pasted JWTs |
| Token file mode 600 | Reduce local token theft risk |
| Stdio: protocol on stdout, logs on stderr | MCP transport correctness |

### Safety & controls

Auth0 Native app + audience. Authz is whatever WebApi returns for that user. Tool args validated at the API. Docs: `docs/mcp/README.md`.

### What this is *not* claiming

- `EnableMcp` is a live kill switch (config-only today).
- Extra MCP-specific rate policy beyond WebApi.

### Primary code

- `src/McpServer/` (`Program.cs`, `TodoMcpTools`, `TodoMcpResources`, `TaskManagerApiClient`, Auth0 device-code token services)

---

## 10. AI batch jobs

**What:** Durable multi-step job over many todos: **embedding → classify → summarize** (configurable), with pause/continue/cancel, heartbeat/stuck detection, SignalR progress.  
**API:**  
- `POST /api/ai/batch`  
- `GET /api/ai/batch/active`, `GET /api/ai/batch/{id}`  
- `POST .../pause|continue|cancel`  
**Flag:** no master `EnableBatch`; each step respects `EnableEmbeddings` / `EnableClassification` / `EnableSummarization`  
**Rate limit:** none on batch endpoints — throttled via batch size, delay between items, item timeout

### Design goal

Ops-friendly **backfill/import pipeline** that is pausable and ownership-scoped — not a fire-and-forget loop in the request.

### Request flow

```
POST batch → StartAiBatchJobCommandHandler
  → ImportTag or TodoExternalIds (+ access checks), one active / user
  → AiBatchBackgroundService → AiBatchStepExecutor per item
  → SignalR; pause / continue / cancel
```

| Decision | Why |
|----------|-----|
| Durable cursor + heartbeat / stuck detection | Survive restarts / hung LLM |
| Pause / continue / cancel | Operator control under cost/load |
| `OnlyMissing` | Cheap re-runs after partial success |
| Per-step feature flags | Partial enablement without new endpoints |
| Clamp batch size / delay / item timeout | Bound blast radius without ASP.NET rate policy |

### Safety & controls

**Ingress:** JWT; FluentValidation; 409 if active job.  
**Authz:** owner for job + each todo.  
**Ops:** per-item errors recorded; job continues where possible; SignalR progress.

### What this is *not* claiming

- Global AI rate limiting on batch (can still burn LLM quota if delay is low).
- A single master “batch enabled” kill switch.

### Primary code

- `AiBatchJobCommandHandlers`, `AiBatchBackgroundService`, `AiBatchStepExecutor`
- `AiBatchHub` / notifier

---

## 11. Voice transcription (hold-to-talk commands)

**What:** One page-level hold-to-talk mic (Todo page overlay) → live Web Speech **finals** (or local Whisper captions) → skip Speaches when the sentence is already complete → else Whisper → `ProposeAiTools` → auto-apply reads/UI tools, confirm writes. With the **edit dialog open**, spoken title/description/due date/priority **fill the form** (no confirm, no persist until Save).  
**API:** `GET /api/ai/voice-options`, `POST /api/ai/transcribe` (`[Authorize]` + multipart audio) and SignalR `VoiceTranscribeHub` (`/hubs/voice-transcribe`) for chunked audio while holding.  
**Flag:** `Ai:Features:EnableVoiceTranscription`  
**Local live captions:** `Ai:SpeechToText:EnableLocalLiveCaptions` (default **false**). When true, **skip browser Web Speech**. The mic sends PCM; the hub runs one-in-flight Whisper on a **5s tail window** for “Heard (partial)”. Release reuses that result when the last partial covered the full clip; otherwise Speaches transcribes the full PCM once.  
**Rate limit:** `ai-transcribe` (HTTP). Hub finish uses the same transcribe handler.  
**Sidecar:** Aspire container `whisper` (`ghcr.io/speaches-ai/speaches:latest-cpu`); WebApi gets `Ai__SpeechToText__Endpoint`.

### Design goal

Speech is only an **ingress modality**. The LLM still maps the transcript to tools (`ProposeAiTools`); writes still need confirm. Tools run only at **end of speech** (release / VAD), never on interim captions.

### Request flow

```
Hold Dictate (VoiceTodoCommandComponent)
  → default: MediaRecorder WebM + Web Speech (joined finals)
  → EnableLocalLiveCaptions: PCM batches only (no Web Speech)
  → VoiceTranscribeHub (chunks); HTTP POST /api/ai/transcribe fallback
  → if live finals complete (WebM path) → skip Whisper, AbortSession
  → else FinishSession reuses last full-clip partial or Speaches
  → VoiceTranscriptNormalizer (Whisper path)
  → ProposeAiTools(transcript + UI context) → edit-form field patch OR execute / confirm tools
```

| Decision | Why |
|----------|-----|
| Hold to record, release to stop | Matches push-to-talk; VAD/max duration (12s) are safety stops |
| One overlay button (z-index above dialogs) | Works over details/edit/create without a mic on each form |
| Edit-open `update_todo` / `set_priority` patch the form | Dropdowns and fields should change immediately; persist on Save |
| Empty `search_todos` shows all tasks | “Show all my tasks” clears filters; no 20-item execute cap |
| Skip Whisper when Web Speech finals are complete | Avoid a second full STT wait on Chrome/Safari (WebM path only) |
| Local live captions skip Web Speech | Flag means Speaches-only; overlapping 5s PCM windows for partials |
| Reuse last full-clip PCM partial on release | Do not wait for in-flight Whisper or transcribe the same short clip twice |
| Local STT model defaults to `faster-whisper-small` | Better short-command accuracy than tiny while staying local |
| WebM by default; PCM only if `EnableLocalLiveCaptions` | WebM is the finished-file format; PCM is for local live captions |
| Audio not persisted | Privacy; process in memory only |
| Propose ≠ execute writes | Same confirm posture as tools |

### Safety & controls

**Ingress:** JWT, flag → 503, FluentValidation (size/content-type), rate limit on HTTP, 5MB cap, hub abort on disconnect.  
**Input:** sanitize/truncate transcript; normalize STT slips on the Whisper path.  
**Authz:** same as other AI endpoints (`[Authorize]`).

### Primary code

- `TranscribeAudioCommandHandler`, `OpenAiCompatibleSpeechToTextService`, `VoiceTranscriptNormalizer`
- `VoiceLiveTranscriptPolicy`, `VoiceTranscriptReusePolicy`, `PcmWavWriter`
- `VoiceTranscribeHub` + `VoiceTranscribeHubService`
- `VoiceTodoCommandComponent` + `wwwroot/js/voice.js`

---

## Flag / policy cheat sheet

| Feature | Flag | Rate-limit policy |
|---------|------|-------------------|
| Summarize | `EnableSummarization` | `ai-summarize` |
| Classify | `EnableClassification` | `ai-classify` (POST only) |
| Apply priority | `EnableClassification` | — |
| Embeddings | `EnableEmbeddings` | — |
| Semantic search | `EnableSemanticSearch` + embeddings | `ai-semantic-search` |
| RAG (todos) | `EnableRag` | `ai-rag` |
| Knowledge Lab | `EnableKnowledgeRag` + embeddings | `ai-knowledge-rag` (upload + query) |
| Tools | `EnableFunctionCalling` | `ai-function-calling` |
| Agents (MAF) | `EnableAgents` | `ai-agents` (start) |
| Voice STT | `EnableVoiceTranscription` | `ai-transcribe` |
| Voice local captions | `Ai:SpeechToText:EnableLocalLiveCaptions` | (hub; Speaches) |
| MCP | `EnableMcp` (config only) | via called APIs |
| Batch | per-step flags | — |

**Knowledge Lab knobs** (`Ai:Features:KnowledgeRag`): `HybridEnabled`, `EnableQueryRewrite`, `EnableAgenticRetrieve`, `EnablePdfIngest`, `EnableReIngest`, plus chunk/retrieval/MinSimilarity limits.
