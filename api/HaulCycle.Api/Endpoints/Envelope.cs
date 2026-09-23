namespace HaulCycle.Api.Endpoints;

/// <summary>The `{ asOf, from, to, data }` envelope every data endpoint returns.
/// asOf is the data's as-of time (end of the most recent cycle), not the wall clock.
/// from/to describe the calendar window the response covers.</summary>
public sealed record Envelope<T>(DateTime? AsOf, DateOnly? From, DateOnly? To, T Data);
