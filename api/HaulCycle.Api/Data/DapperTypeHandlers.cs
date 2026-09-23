using System.Data;
using Dapper;

namespace HaulCycle.Api.Data;

/// <summary>Dapper has no built-in mapping for DateOnly (SQL Server DATE columns), so
/// register one at startup. Without this, CycleRow.ShiftDate / ScheduleRow.ShiftDate
/// throw on read.</summary>
public sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value) => parameter.Value = value.ToDateTime(TimeOnly.MinValue);

    public override DateOnly Parse(object value) => DateOnly.FromDateTime((DateTime)value);
}

public static class DapperTypeHandlerRegistration
{
    public static void RegisterAll()
    {
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
    }
}
