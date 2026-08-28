# Knowledge Lab v1 limits — manual test matrix (v2 gaps)

**Goal:** Prove what v1 does well, where it breaks, and which **v2 feature** fixes it.

**Setup**

1. Upload [`acme-sprint-handbook.md`](acme-sprint-handbook.md) — baseline happy path.
2. Upload [`v2-gap-stress-handbook.md`](v2-gap-stress-handbook.md) — limit stress.
3. Optional: try [`acme-sprint-handbook.pdf`](acme-sprint-handbook.pdf) — ingest rejection.
4. Knowledge Lab → wait **Ready** → use document chip to scope Ask when noted.
5. Expand **RAG trace** every time (hits, similarity, outcome).

**v1 defaults** (from config): 2 MB upload · `.txt`/`.md` only · chunk 800/100 · retrieve 5 · MinSimilarity 0.45 · vector-only · single retrieve · no agent.

---

## A. Ingest & format limits

| ID | Action / question | v1 expected behavior | Ideal answer / UX | v2 need |
|----|-------------------|----------------------|-------------------|---------|
| A1 | Upload `acme-sprint-handbook.pdf` | **400** — "Only .txt and .md" | Accept PDF, parse text | **PDF/Office ingest** |
| A2 | Upload empty `.md` | **400** — empty file | Same | — (v1 OK) |
| A3 | Upload file **> 2 MB** `.txt` | **400** — exceeds byte limit | Configurable limit or blob + stream parse | **Blob storage + large doc pipeline** |
| A4 | Edit handbook locally, re-upload same name | **New document row**; old chunks remain until deleted | Versioning, re-index in place | **Re-ingest / document versions** |
| A5 | Watch ingest on 15-paragraph stress doc | Completes; **many chunks**; embed step slow, no batch pause | Pause/cancel like AI batch | **Ingest job controls** (optional) |

---

## B. Retrieval quality (same doc, scoped Ask)

| ID | Question (scope: stress handbook) | v1 likely result | Ground truth | v2 need |
|----|-----------------------------------|------------------|--------------|---------|
| B1 | What is EXACT-TOKEN-ALPHA? | **Hit or miss** — rare token may fall below MinSimilarity | Staging tenant `acme-staging.us.auth0.com` | **Hybrid FTS + RRF** (exact token) |
| B2 | What env var holds the Stripe webhook secret? | May answer **`STRIPE_WHSEC_PAYMENTS`** or insufficient | `STRIPE_WHSEC_PAYMENTS` | **Hybrid + optional rerank** |
| B3 | Compare Platform vs Payments **P1 restore** times | Often **partial** — only one squad in top-5 chunks | Platform **2h**, Payments **4h** | **Higher retrieval limit / parent-child / agentic multi-retrieve** |
| B4 | Who is on-call for Platform in week B? | May miss if comparison chunk not retrieved | **Lina** | **Multi-chunk fusion or agent second search** |
| B5 | What mitigated AUTH-INC-9001 in Safari? | Needs long narrative chunk in top-5 | **Refresh-token rotation via BFF**; related **AUTH-221** | **Rerank + rewrite** ("Safari cookie Auth0") |
| B6 | Paraphrase: "How fast must silent refresh be?" | May fail if phrasing ≠ "800 ms p95" | **800 ms p95** | **Query rewrite / HyDE** |
| B7 | What keyword goes in Auth0 refresh tickets? | Exact phrase **SILENT-REFRESH-PLAYBOOK** helps vector | That keyword | **Hybrid FTS** |
| B8 | Does v1 use hybrid search? | Should say **vector-only** if architecture chunk retrieved | Correct per doc | v1 OK if retrieved; else **better chunking of tables** |

---

## C. Cross-document & scope

Upload **both** handbooks. Ask **without** document chip unless noted.

| ID | Question | v1 expected | Ground truth | v2 need |
|----|----------|-------------|--------------|---------|
| C1 | Who is product owner? (both docs uploaded) | Answer from **Acme handbook** only if that chunk wins | **Maya Chen** (Acme) | **Source doc label in UI** (v1 OK if correct) |
| C2 | Platform P1 ack vs Payments P1 ack? (no doc filter) | **Unstable** — may merge wrong SLA | 15 min vs 30 min | **Multi-doc rerank + structured compare prompt** |
| C3 | Filter chip: stress doc only → "Sprint Aurora capacity?" | **Insufficient context** (fact only in Acme doc) | Not in stress doc | v1 OK — proves **doc filter works** |
| C4 | "What does my todo AUTH-221 say?" | **No todo retrieval** — docs only | N/A | **Unified Ask (docs + todos)** |

---

## D. Safety & trace (v1 should pass)

| ID | Question | v1 expected | v2 need |
|----|----------|-------------|---------|
| D1 | What is EXACT-TOKEN-GAMMA used for? | **Insufficient** or "not implemented" — must **not** invent GraphRAG features | v1 OK |
| D2 | What is the Redis vector connection string? | **Insufficient** — must not invent | v1 OK |
| D3 | Ask with `EnableKnowledgeRag: false` | **503** Knowledge Lab unavailable | v1 OK |
| D4 | Check trace after any Ask | Shows sanitize, embed model, hits, outcome | v2: **rewrite step, rerank scores, candidate vs final** |

---

## E. Chunking & structure stress

| ID | Observation | v1 expected | v2 need |
|----|-------------|-------------|---------|
| E1 | Open chunk browser on stress doc — **comparison table** | Table may be **split mid-row** across chunks | **Structure-aware chunking** (MD tables, headings) |
| E2 | Count chunks on stress doc | **> 5 chunks** for appendix alone | Proves **RetrievalLimit 5** caps cross-section Q&A |
| E3 | Heading metadata on chunks | `#` headings propagate; plain "Paragraph NN" may share stale heading | **Per-section metadata filters** |

---

## F. Record sheet (fill when testing)

| ID | Pass v1? | Actual answer (short) | Trace hitCount | Similarity range | Notes |
|----|----------|----------------------|----------------|------------------|-------|
| A1 | | | | | |
| B3 | | | | | |
| B6 | | | | | |
| C2 | | | | | |
| C4 | | | | | |

---

## v2 priority from this matrix

| Priority | Feature | Triggered by tests |
|----------|---------|-------------------|
| **P0** | Hybrid FTS + RRF | B1, B2, B7 |
| **P0** | Query rewrite / rerank | B5, B6, C2 |
| **P1** | PDF ingest | A1 |
| **P1** | Agentic multi-retrieve OR parent-child chunks | B3, B4, E2 |
| **P1** | Unified docs + todos Ask | C4 |
| **P2** | Re-ingest / versions | A4 |
| **P2** | Richer trace (rewrite, rerank, candidates) | D4 |
| **P3** | Blob + large files | A3 |
| **Defer** | GraphRAG | D1 |

---

## Quick smoke (5 min)

1. Upload `acme-sprint-handbook.md` → Ready → Q: "Who owns AUTH-221?" → **Platform** ✓  
2. Upload stress handbook → Q: "Compare P1 restore Platform vs Payments" → likely **partial** → **v2**  
3. Upload PDF → **reject** → **v2**  
4. Q: "Redis vector URL?" → **insufficient** ✓  
