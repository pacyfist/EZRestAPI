# Models and aggregates

There are two kinds of class. Pick one per class.

- **`[Model]`** — an EF Core model. A table with columns. Plain data.
- **`[Aggregate]`** — a DDD domain entity. A class with rules about its own state.

A class cannot be both (`EZR010`).

## Which one

Ask: **can any mix of field values be wrong?**

- No → `[Model]`. A book, a sensor reading, an audit line.
- Yes → `[Aggregate]`. An order that must not be cancelled after it ships.

If you are unsure, start with `[Model]`. Switching later is a small change.

## `[Model]`

The class matches the table. Properties are columns. It holds no rules, so the
generator can do everything to it:

- Create it from the request body.
- `PUT` overwrites every field.
- Delete it by id.

That is safe because no mix of values is wrong. See [models.md](models.md).

## `[Aggregate]`

The class matches your domain. It holds the rules, so it does the work:

- Create it through one `[Factory]`. That is the only way in.
- Change it through named `[Command]` methods. Each one can refuse.
- Read it through its getters, including ones with no public setter.
- **No `PUT`** — overwriting every field would skip every rule.

Things inside it, like value objects and child lists, travel with it and get no
routes of their own. See [aggregates.md](aggregates.md).

## Side by side

| | `[Model]` | `[Aggregate]` |
| --- | --- | --- |
| Is | An EF Core model | A DDD domain entity |
| Matches | The table | Your domain |
| Properties | Public get **and** set | Public getter, any setter |
| Created by | The request body | One `[Factory]` |
| Changed by | `PUT`, all fields at once | `[Command]` methods, one route each |
| `PUT` route | Yes | **Never** |
| Rules live in | Nowhere — it has none | The class |
| Errors from | Validation attributes → `422` | Thrown exceptions → `422` or `409` |
| Links to others | Foreign keys, nested routes | Not yet supported |
| `Endpoints` flags | Yes | No — all or nothing |

## Both get

An `int Id`, a `DbSet`, `[Nested]` owned data, paged lists, `problem+json`
errors, and OpenAPI. Names must be unique across both (`EZR002`, `EZR003`).

## Using both together

For a real domain you want both. The aggregate holds the rules and is the only
way in over HTTP. The tables around it are `[Model]` with `Endpoints.None`: no
routes, so nothing can go around the aggregate — but they still get a repository
and DTOs, so your own code can use them.

An order priced from a catalog shows the whole shape.

### The catalog — a model with no routes

```csharp
[EZRestAPI.Model("Product", "Products")]   // Endpoints defaults to None
public partial class ProductModel
{
    public required string Sku { get; set; }
    public required decimal Price { get; set; }
    public required bool IsDiscontinued { get; set; }
}
```

Stored and queryable, but unreachable over HTTP. Nobody can reprice it with a
`PUT`.

### The order — an aggregate

`AddLine` takes the sku and price as *values*. It does not look the product up:
a command is a plain method with no database access. Copying is also what you
want here — an order keeps the price it was placed at, not today's price.

```csharp
[EZRestAPI.Nested("OrderLine")]
public class OrderLine
{
    public required string Sku { get; set; }
    public required decimal UnitPrice { get; set; }
    public required int Quantity { get; set; }
}

[EZRestAPI.Aggregate("Order", "Orders")]
public partial class Order
{
    private Order() { }

    [EZRestAPI.Factory]
    public static Order Place(CustomerRef customer) => new() { Customer = customer };

    public string Status { get; private set; } = "Placed";
    public IReadOnlyList<OrderLine> Lines => _lines;
    private readonly List<OrderLine> _lines = [];

    [EZRestAPI.Command]
    public void AddLine(string sku, decimal unitPrice, int quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (Status != "Placed") throw new InvalidOperationException("Order is closed.");

        _lines.Add(new OrderLine { Sku = sku, UnitPrice = unitPrice, Quantity = quantity });
    }
}
```

### The endpoint that joins them

The generated `POST /orders/{id}/add-line` lets the caller name their own price,
which is too much trust for a real catalog. So write one that takes a product
id, and let the two repositories meet:

```csharp
record AddProductBody(int ProductId, int Quantity);

app.MapPost("/orders/{id:int}/add-product", async (
    int id,
    AddProductBody body,
    ProductRepository products,        // from the Endpoints.None model
    OrderRepository orders,
    CancellationToken ct) =>
{
    var product = await products.ReadAsync(body.ProductId, ct);
    if (product is null || product.IsDiscontinued)
    {
        return Results.UnprocessableEntity("Unknown or discontinued product.");
    }

    try
    {
        var order = await orders.ExecuteAddLineAsync(
            id,
            new AddLineOrderRequest
            {
                Sku = product.Sku,
                UnitPrice = product.Price,
                Quantity = body.Quantity,
            },
            ct);

        return order is null ? Results.NotFound() : Results.Ok(order);
    }
    catch (ArgumentException ex) { return Results.UnprocessableEntity(ex.Message); }
    catch (InvalidOperationException ex) { return Results.Conflict(ex.Message); }
});
```

That is the interaction: your code reads the model, decides what the aggregate
is allowed to hear, and hands it over. The aggregate still enforces its own
rules on top — a zero quantity is refused whichever route calls it.

**Catch the domain exceptions yourself.** The `ArgumentException` → 422 and
`InvalidOperationException` → 409 mapping lives in the *generated* endpoint, not
in the repository. A hand-written caller gets a 500 instead unless it does what
the two `catch` arms above do.

### Which to use for each part

| The part | Use |
| --- | --- |
| Belongs to one aggregate, no id of its own | `[Nested]` |
| Own table and id, shared, no routes | `[Model]`, `Endpoints.None` |
| Own table and its own CRUD routes | `[Model]` with endpoints |

### Two limits

- An aggregate cannot link to a model with a real foreign key yet. `int
  ProductId` on an aggregate is a plain column: no constraint, no nested route,
  and no `EZR011`.
- The other way round **does** warn. A `[Model]` with `int OrderId`, where
  `Order` is an aggregate, raises `EZR011`, because only models count as link
  targets. Mark it `[EZRestAPI.Scalar]`.

## Why not one kind

Generated code cannot set a private setter, so a `[Model]`-style create would
not compile against an aggregate. And a generated `PUT` does not know which
changes are allowed — it would happily cancel a shipped order. That is the bug
aggregates exist to stop.
