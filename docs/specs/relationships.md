# Relationships between models

A link between two independent models is declared by a foreign-key property.
(Owned data is a different thing — see [nested-types.md](nested-types.md).)

## Detecting a foreign key

A property is a foreign key when **all** of these hold:

1. It is named `{Singular}Id`, where `{Singular}` is the singular name of an
   existing `[Model]` — so `AuthorId` finds the model `Author`.
2. Its type is `int` or `int?`. `int?` means the parent is optional.
3. It does not carry `[EZRestAPI.Scalar]`.

A model may hold several foreign keys. Each one makes it a child of that parent
and gets its own nested route group. `ReviewModel` in the Example project holds
two (`AuthorId`, `BookId`).

### Opting out

`[EZRestAPI.Scalar]` on an id-shaped property forces it to stay a plain column.

A property named `{X}Id` of type `int` where no model `X` exists raises
`EZR011` (Warning) — either add the model or mark it `[Scalar]`. It is a warning
and never an error: a false positive must not break a build.

A name match alone is not enough. `AuthorModel.OrderId` is a `Guid` with no
`Order` model, and it stays a plain column with **no** `EZR011` — a non-`int`
type is strong evidence it was never meant to be a key. That property is kept in
the Example project as a regression guard.

## Database

Each foreign key becomes a real relationship in the generated
`OnModelCreating`:

```csharp
entity.HasOne<AuthorModel>().WithMany()
      .HasForeignKey(e => e.AuthorId)
      .OnDelete(DeleteBehavior.Restrict);
```

`Restrict` means deleting a parent that still has children fails, and the
children survive. This differs on purpose from `[Nested]` owned data, which
cascades. A nullable foreign key makes the relationship optional.

## Routes

For child `Book` under parent `Author`, with `Endpoints.All`:

| Route | Behaviour |
| --- | --- |
| `GET /books` | Page of every book |
| `POST /books` | Create; `AuthorId` comes from the body |
| `GET`/`PUT`/`DELETE` `/books/{id}` | The flat item |
| `GET /authors/{authorId}/books` | Page of that author's books only |
| `POST /authors/{authorId}/books` | Create; `AuthorId` comes from the **route** and is absent from the body |
| `GET`/`PUT`/`DELETE` `/authors/{authorId}/books/{id}` | The item, scoped to that author |

The flat item URL is the canonical one — a `201` `Location` header always points
there, even for a nested create.

Which of these actually appear depends on the child's `Endpoints` flags; see
[endpoints.md](endpoints.md).

## Status codes

| Case | Code |
| --- | --- |
| Body holds an `AuthorId` that does not exist (flat `POST`/`PUT`) | `422` |
| Path names an author that does not exist (`/authors/999/books`) | `404` |
| Book exists but belongs to another author (`/authors/5/books/9`) | `404` |
| Deleting an author that still has books | `409` |

The reasoning is in [rest-conventions.md](../rest-conventions.md): a bad id in a
body is invalid content, a bad id in a path is a missing resource, and a delete
blocked by existing children is a state conflict.

## Generated repository methods

Beyond the flat `CreateAsync`/`ReadAsync`/`ListAsync`/`UpdateAsync`/`DeleteAsync`,
each relationship adds a parent-scoped set:

```
List{Child}By{Parent}Async(parentId, page, pageSize)
Create{Child}Under{Parent}Async(parentId, request)
Read{Child}Under{Parent}Async(parentId, id)
Update{Child}Under{Parent}Async(parentId, id, request)
Delete{Child}Under{Parent}Async(parentId, id)
```

Methods that can fail for more than one reason return `WriteResult` so the
handler can tell `404` from `409`.

## Planned: navigation properties

Today a link is *implicit* — a property that happens to be named `{Singular}Id`.
The goal is to also let a model declare links the way EF Core itself does, with
navigation properties carrying EF's own settings. It is too large for one change,
so it is phased. **None of this is implemented yet.**

- **P1 — one-to-many.** A reference nav (`public AuthorModel Author`) marks the
  declaring type as the dependent; a collection nav (`public List<BookModel>
  Books`) marks it as the principal. Either side alone is enough. If no matching
  foreign-key property is declared, `int AuthorId` is generated on the
  dependent's partial class; nav nullability decides whether it is `int?`.
  Per-link settings: `[OnDelete(DeleteBehavior.X)]`, `[ForeignKey("...")]`,
  `[InverseProperty("...")]`.
- **P2** — one-to-one (reference nav on both ends, unique FK).
- **P3** — many-to-many (collection nav on both ends → join entity, plus
  link/unlink routes).
- **P4** — composite and alternate keys, shadow FKs, self-references, and the
  full delete-behavior matrix.

Decisions already made for P1:

- **The REST surface does not change.** A to-one link still appears in DTOs as
  the foreign-key id, never as an embedded object; a to-many link stays the
  paginated sub-resource route. Because nothing embeds a related model, DTOs
  cannot cycle.
- **`EZR004` reverses.** Model-to-model navigation moves from an error to the
  supported mechanism.
- **The `{Singular}Id` convention keeps working** and can be mixed with navs.
- **Delete behavior splits.** Nav relationships take EF's default, `Cascade`.
  The convention keeps `Restrict`, so no existing model changes behaviour. Both
  honour `[OnDelete]`. This is an intentional difference between the two
  mechanisms and needs to be documented for users when P1 ships.
- Shapes P1 cannot model (one-to-one, many-to-many, ambiguous inverse) each get
  their own diagnostic. They are never silently mis-modelled.
