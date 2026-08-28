# Platform & Payments Runbook — v2 gap stress doc

Use with [`v2-gap-manual-test.md`](v2-gap-manual-test.md). Facts are spread on purpose to expose v1 retrieval/chunk limits.

## Doc A — Platform squad (Auth0)

Platform owns **AUTH-221** and **AUTH-305**. The Auth0 tenant id for staging is **`acme-staging.us.auth0.com`**. Silent refresh must complete within **800 ms** p95.

Platform P1 SLA: acknowledge **15 minutes**, restore **2 hours**.

Platform on-call rotation: **week A = Omar**, **week B = Lina**.

The runbook keyword **SILENT-REFRESH-PLAYBOOK** must appear in incident tickets when Auth0 refresh fails.

## Doc B — Payments squad (Stripe)

Payments owns **PAY-88** and **PAY-91**. Stripe webhook signing secret env var is **`STRIPE_WHSEC_PAYMENTS`**.

Payments P1 SLA: acknowledge **30 minutes**, restore **4 hours** (stricter restore than Platform).

Payments on-call: **week A = Maya**, **week B = Omar**.

Invoice PDF deep-link route: **`/billing/invoices/{invoiceId}/pdf`**.

## Cross-squad comparison table

| Squad     | P1 ack | P1 restore | Primary vendor |
|-----------|--------|------------|----------------|
| Platform  | 15 min | 2 hours    | Auth0          |
| Payments  | 30 min | 4 hours    | Stripe         |

## Architecture note (far from headings)

Hidden detail for multi-hop tests: Knowledge Lab v1 uses **vector-only** search with **MinSimilarity 0.45** and **RetrievalLimit 5**. It does **not** run hybrid FTS, query rewrite, rerank, or agentic second retrieval. PDF ingest is **not** in v1.

## Incident AUTH-INC-9001 (long narrative)

On 2026-08-10, users reported Blazor WASM losing JWT after 15 minutes idle. Root cause: Auth0 silent refresh iframe blocked by third-party cookie policy in Safari. Mitigation: switch to refresh-token rotation via backend BFF. Related ticket **AUTH-221**. Payments was unaffected; **PAY-88** still shipped on schedule.

## Glossary tokens (exact-match tests)

- **EXACT-TOKEN-ALPHA** = Platform staging tenant
- **EXACT-TOKEN-BETA** = Stripe webhook secret name
- **EXACT-TOKEN-GAMMA** = GraphRAG (explicitly not implemented)

## Appendix — filler to increase chunk count

Paragraph 01: Lorem platform observability dashboards track Auth0 login success rate.
Paragraph 02: Lorem payments reconciliation batch runs nightly at 02:00 UTC.
Paragraph 03: Lorem sprint Aurora capacity remains twelve points.
Paragraph 04: Lorem embedding model remains bge-small-en-v1.5 at three hundred eighty-four dimensions.
Paragraph 05: Lorem chunk size eight hundred overlap one hundred.
Paragraph 06: Lorem max upload two megabytes text markdown only.
Paragraph 07: Lorem rate limit policy ai-knowledge-rag.
Paragraph 08: Lorem owner scope only no admin cross tenant browse.
Paragraph 09: Lorem signalR ingest job updates on parse chunk embed.
Paragraph 10: Lorem refuse ungrounded chunk guids in answers.
Paragraph 11: Lorem insufficient context when similarity below floor.
Paragraph 12: Lorem no conversation history persisted for knowledge ask.
Paragraph 13: Lorem no reindex button change chunk settings requires reupload.
Paragraph 14: Lorem no blob storage original bytes discarded after parse.
Paragraph 15: Lorem v2 adds pdf hybrid rerank agent loop.
