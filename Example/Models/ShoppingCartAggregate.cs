namespace Example.Models;

// An aggregate whose creation entry point is a PUBLIC CONSTRUCTOR marked
// [Factory] (exercising the `new Type(args)` creation path), contrasting
// Order's static-method factory. Reuses the CustomerRef value object.
[EZRestAPI.Aggregate("ShoppingCart", "ShoppingCarts")]
public partial class ShoppingCart
{
    private ShoppingCart() { } // EF materialization ctor

    [EZRestAPI.Factory]
    public ShoppingCart(CustomerRef owner)
    {
        Owner = owner;
        Status = "Open";
    }

    public CustomerRef Owner { get; private set; } = null!;

    public string Status { get; private set; } = "";

    [EZRestAPI.Command]
    public void Checkout()
    {
        if (Status == "CheckedOut")
        {
            throw new System.InvalidOperationException("Cart is already checked out.");
        }

        Status = "CheckedOut";
    }
}
