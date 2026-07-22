namespace EZRestAPI.Tests;

public class AggregateProviderTests
{
    private const string OrderAggregate = """
        namespace Tests;

        using System.Collections.Generic;

        [EZRestAPI.Nested("CustomerRef")]
        public class CustomerRef
        {
            public required string Name { get; set; }
        }

        [EZRestAPI.Aggregate("Order", "Orders")]
        public partial class Order
        {
            private Order() { }

            [EZRestAPI.Factory]
            public static Order Place(CustomerRef customer, string note)
            {
                return new Order();
            }

            public CustomerRef Customer { get; private set; } = null!;
            public string Status { get; init; } = "";
            public IReadOnlyList<string> Lines => lines;
            private readonly List<string> lines = new();

            [EZRestAPI.Command]
            public void AddLine(string sku, int quantity) { }

            [EZRestAPI.Command("cancel")]
            public void Cancel() { }
        }
        """;

    [Fact]
    public void GetOnlyAndPrivateSetProperties_AreCollected()
    {
        var probe = GeneratorHarness.RunAggregateProbe(OrderAggregate);

        // { get; private set; }
        Assert.Contains("// PROPERTY Customer :", probe);
        // { get; init; }
        Assert.Contains("// PROPERTY Status :", probe);
        // get-only projection { get; } => lines — dropped by the [Model] rule.
        Assert.Contains("// PROPERTY Lines :", probe);
    }

    [Fact]
    public void PrivateBackingField_IsNotCollectedAsProperty()
    {
        var probe = GeneratorHarness.RunAggregateProbe(OrderAggregate);

        Assert.DoesNotContain("// PROPERTY lines", probe);
    }

    [Fact]
    public void GetOnlyProperty_IsProjectedAsNestedDto_WhenValueObject()
    {
        var probe = GeneratorHarness.RunAggregateProbe(OrderAggregate);

        // Customer is a [Nested] VO -> exposed as its generated Dto type.
        Assert.Contains("// PROPERTY Customer : CustomerRefDto", probe);
    }

    [Fact]
    public void StaticFactory_IsResolvedWithParameters()
    {
        var probe = GeneratorHarness.RunAggregateProbe(OrderAggregate);

        Assert.Contains("// FACTORY StaticMethod Place", probe);
        Assert.Contains("// FACTORY_PARAM customer :", probe);
        Assert.Contains("// FACTORY_PARAM note : string", probe);
    }

    [Fact]
    public void FactoryValueObjectParameter_IsResolvedAsNested()
    {
        var probe = GeneratorHarness.RunAggregateProbe(OrderAggregate);

        Assert.Contains(
            "// FACTORY_PARAM customer : Tests.CustomerRef : Single : CustomerRef",
            probe
        );
    }

    [Fact]
    public void Commands_AreDiscoveredWithKebabRoutes()
    {
        var probe = GeneratorHarness.RunAggregateProbe(OrderAggregate);

        // Method name kebab-cased.
        Assert.Contains("// COMMAND add-line AddLine", probe);
        // Explicit override wins over the method name.
        Assert.Contains("// COMMAND cancel Cancel", probe);
    }

    [Fact]
    public void CommandParameters_AreResolved()
    {
        var probe = GeneratorHarness.RunAggregateProbe(OrderAggregate);

        Assert.Contains("// COMMAND_PARAM AddLine sku : string", probe);
        Assert.Contains("// COMMAND_PARAM AddLine quantity : int", probe);
    }

    [Fact]
    public void PublicConstructorFactory_IsResolvedAsConstructor()
    {
        var probe = GeneratorHarness.RunAggregateProbe(
            """
            namespace Tests;

            [EZRestAPI.Aggregate("Account", "Accounts")]
            public partial class Account
            {
                [EZRestAPI.Factory]
                public Account(string owner) { Owner = owner; }

                public string Owner { get; private set; }
            }
            """
        );

        Assert.Contains("// FACTORY Constructor Account", probe);
        Assert.Contains("// FACTORY_PARAM owner : string", probe);
    }

    [Fact]
    public void UnmarkedInstanceMethod_IsNotACommand()
    {
        var probe = GeneratorHarness.RunAggregateProbe(
            """
            namespace Tests;

            [EZRestAPI.Aggregate("Order", "Orders")]
            public partial class Order
            {
                [EZRestAPI.Factory]
                public static Order Place() => new Order();

                public void NotACommand() { }
            }
            """
        );

        Assert.DoesNotContain("NotACommand", probe);
    }
}
