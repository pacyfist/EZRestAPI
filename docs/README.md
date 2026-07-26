# EZRestAPI design docs

These docs say what the generator **guarantees**. [`../README.md`](../README.md)
shows how to *use* it; these say what it produces and why.

Each spec describes shipped behaviour unless a section is marked **Planned**.

Start with **model-vs-aggregate**. The rest follows from that choice.

| Doc | Covers |
| --- | --- |
| [specs/model-vs-aggregate.md](specs/model-vs-aggregate.md) | The two kinds of class, and which to use |
| [specs/models.md](specs/models.md) | `[Model]`: keys, properties, column types, validation |
| [specs/endpoints.md](specs/endpoints.md) | The `Endpoints` flags, the routes, paging |
| [specs/relationships.md](specs/relationships.md) | Foreign keys between models, nested routes |
| [specs/nested-types.md](specs/nested-types.md) | `[Nested]` owned data |
| [specs/aggregates.md](specs/aggregates.md) | `[Aggregate]`, `[Factory]`, `[Command]` |
| [specs/errors.md](specs/errors.md) | Status codes, `problem+json`, OpenAPI |
| [specs/diagnostics.md](specs/diagnostics.md) | `EZR001`–`EZR015` |
| [rest-conventions.md](rest-conventions.md) | The REST rules the design answers to |
| [architecture.md](architecture.md) | How the generator is built, and how to test it |

[`../ROADMAP.md`](../ROADMAP.md) tracks what is done and what is next.

## Rules for these docs

- Say what is true **now**. No dated change logs — git holds the history.
- One topic per file. If a change touches two topics, edit two files.
- Short sentences, plain words, small tables.
- Every rule here should be backed by a test. Name the test file when it helps.
