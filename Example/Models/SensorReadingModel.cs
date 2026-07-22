namespace Example.Models;

// ExternalId matches the {Singular}Id foreign-key convention (int, "…Id") but
// there is no `External` model, so without an opt-out it would raise EZR011.
// [EZRestAPI.Scalar] keeps it a plain scalar column and suppresses the warning,
// generating NO nested route.
[EZRestAPI.Model("SensorReading", "SensorReadings")]
public partial class SensorReadingModel
{
    [EZRestAPI.Scalar]
    public required int ExternalId { get; set; }

    public required double Value { get; set; }

    public required DateTimeOffset TakenAt { get; set; }
}
