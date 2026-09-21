/* HaulCycle Insights - database schema (SQL Server / T-SQL)
   Independent learning project on synthetic data. Not affiliated with any company.

   Run this against an existing database (local SQL Server in Docker, or Azure SQL).
   It does not create or select a database, so it works the same way in sqlcmd,
   SSMS, or Azure Data Studio, against any target database.

   Safe to re-run: it drops and recreates everything (a full reset). */

DROP VIEW  IF EXISTS dbo.vw_ScheduleDetail;
DROP VIEW  IF EXISTS dbo.vw_CycleDetail;
DROP TABLE IF EXISTS dbo.Schedules;
DROP TABLE IF EXISTS dbo.Delays;
DROP TABLE IF EXISTS dbo.Cycles;
DROP TABLE IF EXISTS dbo.Routes;
DROP TABLE IF EXISTS dbo.Destinations;
DROP TABLE IF EXISTS dbo.Loaders;
DROP TABLE IF EXISTS dbo.Trucks;
GO

CREATE TABLE dbo.Trucks (
    TruckId          INT           NOT NULL PRIMARY KEY,
    Name             NVARCHAR(20)  NOT NULL,
    CapacityTonnes   DECIMAL(6,1)  NOT NULL,
    EmptyMassTonnes  DECIMAL(6,1)  NOT NULL
);

CREATE TABLE dbo.Loaders (
    LoaderId  INT           NOT NULL PRIMARY KEY,
    Name      NVARCHAR(20)  NOT NULL
);

/* Where trucks dump material. */
CREATE TABLE dbo.Destinations (
    DestinationId  INT           NOT NULL PRIMARY KEY,
    Name           NVARCHAR(30)  NOT NULL,
    Material       NVARCHAR(10)  NOT NULL CHECK (Material IN ('Ore', 'Waste'))
);

/* Every loader x destination pair is a route (3 loaders x 3 destinations = 9 rows). */
CREATE TABLE dbo.Routes (
    RouteId        INT            NOT NULL PRIMARY KEY,
    Name           NVARCHAR(50)   NOT NULL,
    LoaderId       INT            NOT NULL REFERENCES dbo.Loaders(LoaderId),
    DestinationId  INT            NOT NULL REFERENCES dbo.Destinations(DestinationId),
    DistanceKm     DECIMAL(5,2)   NOT NULL,
    GradePercent   DECIMAL(4,1)   NOT NULL,
    CONSTRAINT UQ_Routes_Loader_Destination UNIQUE (LoaderId, DestinationId)
);

/* One row per completed haul cycle: load -> haul -> dump -> return, plus any queueing. */
CREATE TABLE dbo.Cycles (
    CycleId        BIGINT        IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TruckId        INT           NOT NULL REFERENCES dbo.Trucks(TruckId),
    LoaderId       INT           NOT NULL REFERENCES dbo.Loaders(LoaderId),
    RouteId        INT           NOT NULL REFERENCES dbo.Routes(RouteId),
    StartTime      DATETIME2(0)  NOT NULL,
    LoadMin        DECIMAL(6,2)  NOT NULL,
    HaulMin        DECIMAL(6,2)  NOT NULL,
    DumpMin        DECIMAL(6,2)  NOT NULL,
    ReturnMin      DECIMAL(6,2)  NOT NULL,
    QueueMin       DECIMAL(6,2)  NOT NULL,
    PayloadTonnes  DECIMAL(6,1)  NOT NULL,
    FuelLitres     DECIMAL(7,1)  NOT NULL
);

CREATE INDEX IX_Cycles_StartTime  ON dbo.Cycles (StartTime);
CREATE INDEX IX_Cycles_Truck_Time ON dbo.Cycles (TruckId, StartTime);

/* One row per delay: a period when a truck is out of production, planned or unplanned. */
CREATE TABLE dbo.Delays (
    DelayId    BIGINT        IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TruckId    INT           NOT NULL REFERENCES dbo.Trucks(TruckId),
    StartTime  DATETIME2(0)  NOT NULL,
    EndTime    DATETIME2(0)  NOT NULL,
    Reason     NVARCHAR(50)  NOT NULL,
    IsPlanned  BIT           NOT NULL
);

CREATE INDEX IX_Delays_Truck_Time ON dbo.Delays (TruckId, StartTime);

/* One row per truck per shift: its route/loader assignment and planned cycles/tonnes,
   or a reason it's unavailable for the whole shift. Built by the scheduler before the
   shift starts, from book rates, and never changed during it. */
CREATE TABLE dbo.Schedules (
    ScheduleId          INT            IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ShiftDate           DATE           NOT NULL,
    ShiftName           NVARCHAR(5)    NOT NULL CHECK (ShiftName IN ('Day', 'Night')),
    TruckId             INT            NOT NULL REFERENCES dbo.Trucks(TruckId),
    RouteId             INT            NULL REFERENCES dbo.Routes(RouteId),
    LoaderId            INT            NULL REFERENCES dbo.Loaders(LoaderId),
    PlannedCycles       DECIMAL(6,2)   NULL,
    PlannedTonnes       DECIMAL(9,1)   NULL,
    UnavailableReason   NVARCHAR(50)   NULL,
    CONSTRAINT UQ_Schedules_Shift_Truck UNIQUE (ShiftDate, ShiftName, TruckId)
);
GO

/* A friendly, pre-joined view. The API (and later the "Ask your mine plan" tool)
   can query this instead of joining tables every time.

   ShiftName: Day is 06:00-17:59, Night is 18:00-05:59, by StartTime, in mine time.
   ShiftDate: the calendar date the shift STARTED on. Night cycles between 00:00 and
   05:59 belong to the previous day's Night shift, so ShiftDate is StartTime's date
   minus one day for those hours. */
CREATE VIEW dbo.vw_CycleDetail AS
SELECT
    c.CycleId,
    c.StartTime,
    CASE WHEN DATEPART(HOUR, c.StartTime) BETWEEN 6 AND 17 THEN 'Day' ELSE 'Night' END AS ShiftName,
    CASE WHEN DATEPART(HOUR, c.StartTime) BETWEEN 0 AND 5
         THEN CAST(DATEADD(DAY, -1, c.StartTime) AS DATE)
         ELSE CAST(c.StartTime AS DATE)
    END AS ShiftDate,
    t.Name  AS TruckName,
    l.Name  AS LoaderName,
    r.Name  AS RouteName,
    d.Name  AS DestinationName,
    d.Material,
    c.LoadMin,
    c.HaulMin,
    c.DumpMin,
    c.ReturnMin,
    c.QueueMin,
    c.LoadMin + c.HaulMin + c.DumpMin + c.ReturnMin + c.QueueMin AS TotalCycleMin,
    c.PayloadTonnes,
    t.CapacityTonnes,
    CAST(100.0 * c.PayloadTonnes / t.CapacityTonnes AS DECIMAL(5,1)) AS PayloadPercentOfCapacity,
    c.FuelLitres
FROM dbo.Cycles  AS c
JOIN dbo.Trucks       AS t ON t.TruckId       = c.TruckId
JOIN dbo.Loaders      AS l ON l.LoaderId      = c.LoaderId
JOIN dbo.Routes       AS r ON r.RouteId       = c.RouteId
JOIN dbo.Destinations AS d ON d.DestinationId = r.DestinationId;
GO

/* Pre-joined schedule view with names instead of ids. Unavailable trucks have NULL
   route/loader/destination/plan fields. Compliance KPIs (plan vs actual) are left to
   the API, which can join this against vw_CycleDetail. */
CREATE VIEW dbo.vw_ScheduleDetail AS
SELECT
    s.ScheduleId,
    s.ShiftDate,
    s.ShiftName,
    t.Name  AS TruckName,
    r.Name  AS RouteName,
    l.Name  AS LoaderName,
    d.Name  AS DestinationName,
    d.Material,
    s.PlannedCycles,
    s.PlannedTonnes,
    s.UnavailableReason
FROM dbo.Schedules AS s
JOIN dbo.Trucks        AS t ON t.TruckId       = s.TruckId
LEFT JOIN dbo.Routes       AS r ON r.RouteId       = s.RouteId
LEFT JOIN dbo.Loaders      AS l ON l.LoaderId      = s.LoaderId
LEFT JOIN dbo.Destinations AS d ON d.DestinationId = r.DestinationId;
GO
