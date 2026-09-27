namespace HaulCycle.Api.Kpi;

/// <summary>The shift as it actually ran, from vw_CycleDetail - for comparing a replayed
/// Original plan against what really happened (the faithfulness check). Tonnes are split by
/// destination name, since Material alone can't tell ROM pad and Crusher apart (both "Ore").
/// Destination names are fixed reference data (see db/schema.sql's Destinations seed rows /
/// Fleet.Create in the data generator) - not user input, so matching on the literal string is
/// safe and needs no lookup table.</summary>
public static class ActualShiftOutcomeCalculator
{
    public const string CrusherDestination = "Crusher";
    public const string RomPadDestination = "ROM pad";
    public const string WasteDumpDestination = "Waste dump";

    public sealed record Result(
        decimal CrusherTonnes, decimal RomTonnes, decimal WasteTonnes, decimal TotalTonnes,
        int Cycles, decimal QueueHours, decimal FuelLitres);

    public static Result Calculate(IReadOnlyCollection<CycleRow> cycles)
    {
        var crusher = cycles.Where(c => c.DestinationName == CrusherDestination).Sum(c => c.PayloadTonnes);
        var rom = cycles.Where(c => c.DestinationName == RomPadDestination).Sum(c => c.PayloadTonnes);
        var waste = cycles.Where(c => c.DestinationName == WasteDumpDestination).Sum(c => c.PayloadTonnes);

        var queueHours = cycles.Sum(c => c.QueueMin) / 60m;
        var fuel = cycles.Sum(c => c.FuelLitres);

        return new Result(crusher, rom, waste, crusher + rom + waste, cycles.Count, queueHours, fuel);
    }
}
