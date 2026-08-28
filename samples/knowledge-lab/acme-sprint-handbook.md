# Acme Task Manager — Sprint Handbook (Q3 2026)

Internal notes for Knowledge Lab RAG manual testing. Distinct facts below are intentional.

## Product overview

Acme Task Manager is a Blazor WASM + ASP.NET Core application. Users authenticate with **Auth0 JWT**. The primary database is **PostgreSQL with pgvector**. Local embeddings use **ONNX BGE-small-en-v1.5** at **384 dimensions**.

## Team and ownership

- Product owner: **Maya Chen**
- Engineering lead: **Omar Farooq**
- Security reviewer: **Lina Park**
- The Auth0 login epic is owned by the **Platform** squad.
- The Stripe billing epic is owned by the **Payments** squad.

## Sprint Aurora (14 days)

Sprint Aurora runs from **2026-09-01** to **2026-09-14**. Capacity is **12 story points**. The sprint goal is: ship Knowledge Lab document RAG and harden Auth0 silent token refresh.

Committed work:

1. **AUTH-221** — Fix Auth0 silent refresh on Blazor WASM (High, 5 points). Owner: Platform.
2. **KNOW-104** — Knowledge Lab parse → chunk → embed pipeline (High, 5 points). Owner: AI guild.
3. **PAY-88** — Show Stripe invoice PDF link on billing page (Medium, 2 points). Owner: Payments.

Out of scope for Aurora: GraphRAG, PDF ingest, and multi-tenant SaaS hardening.

## Support SLAs

- **P1 (production outage):** acknowledge within **15 minutes**, mitigate within **2 hours**.
- **P2 (degraded AI feature):** acknowledge within **4 hours**, fix or feature-flag off within **1 business day**.
- **P3 (cosmetic):** next sprint.

If RAG answers cite invented task IDs, treat that as a **P2 quality incident**.

## Configuration defaults

Knowledge Lab defaults:

- Max upload size: **2 MB**
- Allowed file types: **.txt** and **.md** only
- Chunk size: **800** characters
- Chunk overlap: **100** characters
- Retrieval limit: **5** chunks
- Minimum similarity: **0.45**

Feature flag name: `Ai:Features:EnableKnowledgeRag`.

## Forbidden facts (for negative tests)

Do **not** claim any of the following in this handbook:

- We do **not** use Redis as the primary vector store.
- We do **not** support PDF upload in Knowledge Lab v1.
- Sprint Aurora does **not** include GraphRAG.

## Glossary

- **ExternalId:** public GUID used in APIs (never expose EF internal integer Ids).
- **RAGPipeline:** generates answers only from caller-supplied sources; it does not retrieve.
- **MinSimilarity:** cosine similarity floor; weak neighbors are dropped.
