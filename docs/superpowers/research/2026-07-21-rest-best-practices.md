# REST API best-practices reference (research)

Authoritative decision reference for the EZRestAPI generator, produced by a
research agent on 2026-07-21 and used as the binding answer to design questions
in place of asking the maintainer. Sources: RFC 9457, RFC 9110, Zalando RESTful
API Guidelines, Microsoft REST API Guidelines, Google AIP, OpenAPI 3.1,
Stripe/GitHub conventions.

## Binding decisions (quick reference)

| # | Decision | Recommendation |
|---|----------|----------------|
| 1 | Body validation failure | **422** Unprocessable Content (400 = unparseable only); RFC 9457 body with `errors` field-map |
| 2 | FK to missing parent | **422** if the FK is in the body; **404** if the parent is named in the path |
| 3 | Delete parent with children | **409 Conflict** |
| 4 | Nested item not under parent | **404** |
| 5 | Problem Details | `application/problem+json`; always `type`+`title`+`status`; add one-sentence `detail`, `instance`, machine-readable `code`; `type` URI resolvable but not required |
| 6 | Routing | Flat item URL is canonical; nest only the child *collection*; depth ≤ 3 (prefer 1); drop deep nested item paths |
| 7 | Pagination | Cursor/keyset default (offset opt-in); enveloped; default 25 / max 100; **clamp** over-limit, don't 400 |
| 8 | PUT | Full replace, **200**+body (204 permitted), no upsert by default; PATCH for partial |
| 9 | POST create | **201** + `Location` (the flat canonical URL) + created representation |
| 10 | OpenAPI 3.1 | Stable `operationId`; typed schemas for every status incl. errors; shared ProblemDetails schema; `tags` per resource |
| 11 | Naming | plural, lowercase, kebab-case paths; camelCase JSON/query; id in path |
| 12 | DDD → REST | Aggregate root = the resource; inner entities/value objects embedded (not independently addressable); command endpoints (`POST /orders/{id}/cancel` or `:cancel`) for guarded transitions instead of PUT |

## How EZRestAPI currently conforms (2026-07-21)

- **Conforms:** #3 (409 delete-with-children), #4 (404 nested mismatch), #7 clamp, #9 (201 + flat Location).
- **Corrected in the contract/correctness increment:** #1 (was 400 → **422**), #2 (flat bad-FK was 409 → **422**), #5 (add problem+json/code/detail), #10 (add operationId/tags/typed errors).
- **Deferred (flagged, not yet done):** #6 (shipped deep nested item paths; research says drop them), #7 (shipped offset + `{items,totalCount,page,pageSize}`; research prefers cursor + `{data,pagination}`), #8 (returns 204; research prefers 200+body).
- **Informs future work:** #12 drives the queued DDD-entities increment (command endpoints for guarded transitions; aggregate root as the only addressable resource).

## Full report

<details><summary>Complete per-item justification and citations</summary>

See the summary table above for the binding choices. Key justifications:

- **1 (422):** RFC 9110 §15.5.1 scopes 400 to syntax/framing the server "cannot process"; 422 (§15.5.21) is well-formed-but-unacceptable content, the default in Rails/Laravel/FastAPI. ASP.NET defaults to 400 — a defensible dissent — so keep the status a future config knob. Body = RFC 9457 with an `errors` field→messages map (the ASP.NET `HttpValidationProblemDetails` shape; `errors` is a sanctioned extension member, RFC 9457 §3.2).
- **2 (422 body / 404 path):** a body FK to a missing parent is invalid content (422); a path `/authors/{id}/…` addressing a nonexistent author is literally 404 (RFC 9110 §15.5.5). 409 over-signals (it is for state conflicts).
- **3 (409):** RFC 9110 §15.5.10 — refused due to current resource state, resolvable by deleting children and resubmitting.
- **4 (404):** `/parents/{p}/children/{c}` names one composite resource "c of p"; it doesn't exist. 403 would leak existence.
- **5:** RFC 9457 §3 — `application/problem+json`; `type` defaults to `about:blank`; SHOULD NOT auto-dereference `type`; `detail` occurrence-specific and secret-free; extension members allowed; Microsoft adds a camelCase `code`.
- **6:** Zalando rule 147 (≤3 nesting levels); Google AIP-122 / Stripe / GitHub use short id-addressable item URLs; dual deep+flat item paths create canonical-URL ambiguity.
- **7:** Zalando rule 160 & Stripe prefer cursor; clamp over-limit page size (no major API 400s on it); echo effective size.
- **8:** RFC 9110 §9.3.4 — PUT replaces full state, idempotent; partial PUT is ambiguous; prefer PATCH (RFC 7386/6902); upsert allowed but a poor default.
- **9:** RFC 9110 §15.3.2 + Microsoft guidelines — 201 + `Location` + representation; `Location` = flat canonical URL even for nested creates.
- **10:** OpenAPI 3.1 (JSON Schema 2020-12): unique `operationId`, typed responses for all statuses (incl. 4xx referencing a shared ProblemDetails schema), `tags`, reusable components, nullable via `type: [..,"null"]`.
- **11:** Zalando rules 129/134 — plural, kebab-case, lowercase paths; Microsoft — camelCase JSON; verbs only for command sub-resources.
- **12:** Evans/Vernon — aggregate root is the transactional/consistency boundary and the only addressable resource; Google AIP-136 custom methods (`POST …:verb`) for actions that don't fit the 5 standard methods; guarded transitions (cancel/ship/approve) become command endpoints, not PUT-a-status.

</details>
