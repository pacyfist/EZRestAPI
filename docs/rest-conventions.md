# REST conventions

The rules EZRestAPI's generated API answers to. When a design question comes up,
this file is the answer, so the same question is not re-argued per feature.

Sources: RFC 9457 (Problem Details), RFC 9110 (HTTP semantics), the Zalando and
Microsoft REST guidelines, Google AIP, OpenAPI 3.1, and Stripe/GitHub practice.

## The rules

| # | Question | Answer |
| --- | --- | --- |
| 1 | Body fails validation | `422`. `400` is for a body that cannot be parsed. RFC 9457 body with an `errors` field map. |
| 2 | Foreign key names a missing parent | `422` when it is in the body; `404` when it is in the path. |
| 3 | Delete a parent that still has children | `409`. |
| 4 | Nested item is not under that parent | `404`. `403` would leak whether it exists. |
| 5 | Error bodies | `application/problem+json`, always `type` + `title` + `status`, plus a one-sentence `detail`, an `instance`, and a machine-readable `code`. |
| 6 | Route depth | The flat item URL is canonical. Nest the child *collection*; keep depth at 1, never past 3. |
| 7 | Paging | Enveloped. Clamp an over-large page size instead of rejecting it. Echo the size actually used. Cursor paging is preferred over offset. |
| 8 | `PUT` | Full replace, idempotent, no upsert. `200` with the new body is preferred; `204` is allowed. `PATCH` is for partial updates. |
| 9 | `POST` create | `201`, a `Location` header holding the flat canonical URL, and the created representation. |
| 10 | OpenAPI 3.1 | A stable `operationId`, a typed schema for every status including errors, one shared ProblemDetails schema, `tags` per resource. |
| 11 | Naming | Paths plural, lowercase, kebab-case. JSON and query names camelCase. Id in the path. |

## Where EZRestAPI knowingly differs

Everything above is followed except these four. Each is a considered choice, not
an oversight.

| # | What we do instead | Why |
| --- | --- | --- |
| 6 | Deep nested item paths (`/authors/{id}/books/{bookId}`) also exist | Shipped deliberately; the flat URL is still the canonical one used in `Location`. Dropping them is a breaking routing change. |
| 7 | Offset paging, `{ items, totalCount, page, pageSize }`, default size 20 | Cursor paging and a `{ data, pagination }` envelope would be a breaking redesign. The clamp rule *is* followed. |
| 8 | `PUT` returns `204` | Explicitly permitted by the rule. Kept to limit churn. |
| 11 | Paths are the plural name lowercased, so `AuditNotes` → `/auditnotes` | Not kebab-cased. A multi-word plural reads worse than it should. Worth fixing with the `Route = "..."` option in ROADMAP Phase 2. |

## The reasoning, in short

- **1.** RFC 9110 §15.5.1 scopes `400` to syntax and framing. §15.5.21 defines
  `422` as well-formed content the server will not accept. Rails, Laravel and
  FastAPI all default to `422`. `errors` is a sanctioned RFC 9457 extension
  member (§3.2) and matches ASP.NET's `HttpValidationProblemDetails`.
- **2.** A body field pointing at a missing row is invalid content. A path
  `/authors/999/…` addresses a resource that is not there — literally `404`
  (§15.5.5). `409` over-signals; it is for state conflicts.
- **3.** §15.5.10 — refused because of current state, and fixable by deleting
  the children and retrying.
- **4.** `/parents/{p}/children/{c}` names one composite resource, "c of p". It
  does not exist.
- **5.** RFC 9457 §3. `type` defaults to `about:blank` and should not be
  auto-dereferenced. `detail` is occurrence-specific and must not carry secrets.
  The camelCase `code` member follows Microsoft's guidelines.
- **6.** Zalando rule 147 caps nesting at three levels. Google AIP-122, Stripe
  and GitHub all use short id-addressable item URLs. Having both a deep and a
  flat item path makes the canonical URL ambiguous.
- **7.** Zalando rule 160 and Stripe prefer cursor paging. No major API returns
  `400` for an over-large page size.
- **8.** §9.3.4 — `PUT` replaces the whole state and is idempotent. A partial
  `PUT` is ambiguous. Upsert is allowed but a poor default.
- **9.** §15.3.2 plus the Microsoft guidelines. `Location` is the flat canonical
  URL even when the create was nested.
- **10.** OpenAPI 3.1 uses JSON Schema 2020-12: unique `operationId`, typed
  responses for every status including 4xx, reusable components, and `null` via
  `type: [..., "null"]`.
- **11.** Zalando rules 129 and 134 for paths; Microsoft for camelCase JSON.
  Verbs appear only in command sub-resources.
