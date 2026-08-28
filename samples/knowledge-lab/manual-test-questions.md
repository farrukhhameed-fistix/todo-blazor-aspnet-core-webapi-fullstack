# Knowledge Lab — manual test questions

Upload [`acme-sprint-handbook.md`](acme-sprint-handbook.md) in Knowledge Lab (`.txt` / `.md` only — **PDF upload is not supported in v1**). Wait until status is **Ready**, then ask each question. Prefer scoping Ask to this document.

| # | Question | Expect (must appear / behavior) | Fail if |
|---|----------|----------------------------------|---------|
| 1 | Who is the product owner? | **Maya Chen** | Wrong name or "insufficient context" |
| 2 | Who owns the Auth0 login epic? | **Platform** squad | Says Payments or invents a person |
| 3 | What are the start and end dates of Sprint Aurora? | **2026-09-01** and **2026-09-14** | Wrong month/year |
| 4 | What is the Sprint Aurora capacity? | **12 story points** | Different number |
| 5 | List the committed work items for Sprint Aurora. | **AUTH-221**, **KNOW-104**, **PAY-88** (titles roughly correct) | Invents extra tickets or misses High items |
| 6 | How many points is AUTH-221? | **5** | Wrong points |
| 7 | What is out of scope for Aurora? | **GraphRAG**, **PDF ingest**, multi-tenant SaaS hardening | Claims PDF ingest is in scope |
| 8 | What is the P1 acknowledgment SLA? | **15 minutes** | Confuses with P2 (4 hours) |
| 9 | What is the minimum similarity for Knowledge Lab? | **0.45** | Invents another threshold |
| 10 | What file types does Knowledge Lab accept? | **.txt** and **.md** | Says PDF is allowed |
| 11 | What embedding model and dimension do we use? | **BGE-small-en-v1.5** / **384** | Wrong model or dims |
| 12 | What is the feature flag for Knowledge Lab? | **EnableKnowledgeRag** (or `Ai:Features:EnableKnowledgeRag`) | Wrong flag name |
| 13 | Do we use Redis as the primary vector store? | **No** / PostgreSQL pgvector | Says yes Redis |
| 14 | What should we do if RAG invents task IDs? | Treat as **P2 quality incident** | Says ignore / P3 |
| 15 | Who is the security reviewer? | **Lina Park** | Confuses with Omar/Maya |

## Refuse / negative checks

| # | Question | Expect |
|---|----------|--------|
| N1 | What is the Redis connection string for vectors? | Insufficient context / clearly says Redis is not used — **must not invent a connection string** |
| N2 | When does Sprint Nebula start? | Insufficient context — **Nebula is not in the doc** |
| N3 | Upload a PDF of this handbook | (UI/API) reject — only `.txt`/`.md` |

## How to run

1. Open **Knowledge Lab** → upload `acme-sprint-handbook.md`.
2. Confirm pipeline: Parse → Chunk → Embed → Ready.
3. Open chunk browser — expect multiple chunks (heading metadata may show).
4. Ask questions 1–15; expand **RAG trace** and confirm hits / similarity.
5. Run N1–N2 and confirm refuse rather than hallucination.
