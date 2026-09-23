using Microsoft.AspNetCore.Http.HttpResults;

namespace HaulCycle.Api.Endpoints;

/// <summary>Resolves the `from`/`to`/`shift` query params into a concrete datetime window.
///
/// With neither from nor to given, the window is the literal last 7 days counting back
/// from as-of (a rolling instant, per the agreed default - not date-truncated).
///
/// With either given, both are treated as whole calendar dates: the window runs from
/// `from` 00:00 up to (not including) the day after `to`, so the `to` date is fully
/// included. Any date left unset defaults to a 7-day span around the one that was given.</summary>
public static class RequestWindow
{
    private static readonly string[] ValidShifts = ["Day", "Night"];

    public static bool TryResolve(
        DateOnly? from,
        DateOnly? to,
        string? shift,
        DateTime asOf,
        out DateTime fromDt,
        out DateTime toDt,
        out DateOnly fromDate,
        out DateOnly toDate,
        out ProblemHttpResult? problem)
    {
        fromDt = default;
        toDt = default;
        fromDate = default;
        toDate = default;
        problem = null;

        if (shift is not null && !ValidShifts.Contains(shift))
        {
            problem = TypedResults.Problem(
                title: "Invalid shift",
                detail: $"shift must be one of: {string.Join(", ", ValidShifts)}.",
                statusCode: StatusCodes.Status400BadRequest);
            return false;
        }

        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            problem = TypedResults.Problem(
                title: "Invalid window",
                detail: "from must not be after to.",
                statusCode: StatusCodes.Status400BadRequest);
            return false;
        }

        if (!from.HasValue && !to.HasValue)
        {
            toDt = asOf;
            fromDt = asOf.AddDays(-7);
            fromDate = DateOnly.FromDateTime(fromDt.Date);
            toDate = DateOnly.FromDateTime(toDt.Date);
            return true;
        }

        toDate = to ?? from!.Value.AddDays(6);
        fromDate = from ?? toDate.AddDays(-6);

        fromDt = fromDate.ToDateTime(TimeOnly.MinValue);
        toDt = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return true;
    }
}
