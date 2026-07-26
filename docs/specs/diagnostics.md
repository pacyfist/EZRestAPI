# Diagnostics

The generator checks models at compile time and reports at the user's own code —
never inside a generated file. Errors stop the build; warnings and info do not.

Every id is registered in `EZRestAPI/AnalyzerReleases.Unshipped.md`. Adding a
diagnostic without registering it fails the analyzer release tracking build.

| Code | Level | Meaning |
| --- | --- | --- |
| `EZR001` | Error | A `[Model]` class is not `partial`. |
| `EZR002` | Error | Two models share a singular name. |
| `EZR003` | Error | Two models share a plural name. |
| `EZR004` | Error | A property's type is another `[Model]`. Reference it by id, or mark it `[Nested]`. |
| `EZR005` | Error | `[Nested]` classes contain each other in a loop. Nesting must be a tree. |
| `EZR006` | Error | Two `[Nested]` classes share a singular name. |
| `EZR007` | Error | An `Id` property is not an `int`. Only `int` keys are supported. |
| `EZR008` | Error | A given name is not a valid C# identifier. |
| `EZR009` | Error | Nested items sit in an unsupported collection type. |
| `EZR010` | Error | A class carries two conflicting markers — `[Model]` with `[Nested]`, or `[Model]` with `[Aggregate]`. |
| `EZR011` | Warning | An `int` property named `{X}Id` matches no model `X`. Add the model, or mark it `[Scalar]`. |
| `EZR012` | Error | An `[Aggregate]` does not have exactly one `[Factory]`. |
| `EZR013` | Warning | `Endpoints.Create` without `Endpoints.Read`. |
| `EZR014` | Info | The model is `Endpoints.None`, so it publishes no routes. |
| `EZR015` | Info | The model's `Endpoints` selects no verb. |

## Notes on the interesting ones

**`EZR011` is a warning, never an error.** The check is a guess based on a name,
so a false positive must not break a build. It also stays quiet when the type is
not `int` — `Guid OrderId` with no `Order` model is almost certainly not a key.

**`EZR013` is the only flag combination worth a diagnostic.** Every other
combination just yields a smaller API. This one yields a *wrong* response: the
`Create` handler emits `Location: /books/{id}`, and without `Read` that header
points at a 404, which breaks the `201` contract.

**`EZR014` and `EZR015` are Info, not Warning.** Both describe legitimate,
supported configurations, and a warning on a correct program is noise —
especially for teams building with `TreatWarningsAsErrors`, who could then not
express a model with no routes at all.

They are also the two most likely to be missed. MSBuild's console logger hides
Info diagnostics at the default verbosity, so if a model you expected to have an
API does not, rebuild with `-v detailed`. An IDE shows them normally.

`EZR014` answers the predictable first-run confusion under opt-in defaults — "I
added `[Model]` and no routes appeared" — at the point of the mistake rather
than at runtime. The repository and DTOs *are* there; only the routes are not.

## Planned

The navigation-property work in [relationships.md](relationships.md) reverses
`EZR004` — model-to-model navigation becomes the supported mechanism — and adds
diagnostics for one-to-one shapes, many-to-many shapes, and an ambiguous
inverse. Ids are assigned when that work is planned.
