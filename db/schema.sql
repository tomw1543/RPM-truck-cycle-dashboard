/* HaulCycle Insights - database schema (SQL Server / T-SQL)
   Independent learning project on synthetic data. Not affiliated with any company.

   Run this against an existing database (local SQL Server in Docker, or Azure SQL).
   It does not create or select a database, so it works the same way in sqlcmd,
   SSMS, or Azure Data Studio, against any target database.

   Safe to re-run: it drops and recreates everything (a full reset). */

DROP VIEW  IF EXISTS dbo.vw_OptimisedLoaderStatDetail;
DROP VIEW  IF EXISTS dbo.vw_OptimisedAssignmentDetail;
DROP VIEW  IF EXISTS dbo.vw_OptimisedPlanDetail;
DROP VIEW  IF EXISTS dbo.vw_LoaderDelayDetail;
DROP VIEW  IF EXISTS dbo.vw_ScheduleDetail;
DROP VIEW  IF EXISTS dbo.vw_CycleDetail;
DROP TABLE IF EXISTS dbo.OptimisedLoaderStats;
DROP TABLE IF EXISTS dbo.OptimisedAssignments;
DROP TABLE IF EXISTS dbo.OptimisedPlans;
DROP TABLE IF EXISTS dbo.Schedules;
DROP TABLE IF EXISTS dbo.Delays;
DROP TABLE IF EXISTS dbo.Cycles;
DROP TABLE IF EXISTS dbo.LoaderDelays;
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
    BookCycleMin   DECIMAL(5,2)   NOT NULL,
    BookQueueMin   DECIMAL(5,2)   NOT NULL,
    BookLoadMin    DECIMAL(5,2)   NOT NULL,
    BookHaulMin    DECIMAL(5,2)   NOT NULL,
    BookDumpMin    DECIMAL(5,2)   NOT NULL,
    BookReturnMin  DECIMAL(5,2)   NOT NULL,
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

/* One row per period a loader is degraded or stopped: a shift-change handover (fully
   stopped, RateFactor 0.00) or a loader spike (half speed, RateFactor 0.50). Both feed the
   queue-contention model directly - a truck's QueueMin on dbo.Cycles reflects genuinely
   waiting behind these, not an independent random draw. */
CREATE TABLE dbo.LoaderDelays (
    LoaderDelayId  BIGINT        IDENTITY(1,1) NOT NULL PRIMARY KEY,
    LoaderId       INT           NOT NULL REFERENCES dbo.Loaders(LoaderId),
    StartTime      DATETIME2(0)  NOT NULL,
    EndTime        DATETIME2(0)  NOT NULL,
    Reason         NVARCHAR(50)  NOT NULL,
    IsPlanned      BIT           NOT NULL,
    RateFactor     DECIMAL(3,2)  NOT NULL
);

CREATE INDEX IX_LoaderDelays_Loader_Time ON dbo.LoaderDelays (LoaderId, StartTime);

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

/* One row per (shift, plan type) the optimiser produced: the shift's stored Schedules as
   'Original', plus 'MoreOutput' and 'Leaner' candidates from local search. Every outcome is
   scored as the mean over SeedCount (5) independent replays with per-truck deterministic RNG
   streams (see SimulationEngine.SeededRandom); Mean/Min/Max are per-field extrema across those
   replays, not a single coherent replay - a plan's Min TotalTonnes and Min QueueHours can come
   from different seeds. TruckHours and TrucksStoodDown are plan-determined, not random, so their
   Mean/Min/Max are always equal for a given plan - the three columns are kept for a uniform
   shape across every outcome rather than because they vary. Replaced wholesale by every
   --optimise run (TRUNCATE + reinsert), never appended to. */
CREATE TABLE dbo.OptimisedPlans (
    PlanId               INT           IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ShiftDate            DATE          NOT NULL,
    ShiftName            NVARCHAR(5)   NOT NULL CHECK (ShiftName IN ('Day', 'Night')),
    PlanType             NVARCHAR(12)  NOT NULL CHECK (PlanType IN ('Original', 'MoreOutput', 'Leaner')),
    SeedCount            INT           NOT NULL,
    TotalTonnesMean      DECIMAL(9,1)  NOT NULL,
    TotalTonnesMin       DECIMAL(9,1)  NOT NULL,
    TotalTonnesMax       DECIMAL(9,1)  NOT NULL,
    CrusherTonnesMean    DECIMAL(9,1)  NOT NULL,
    CrusherTonnesMin     DECIMAL(9,1)  NOT NULL,
    CrusherTonnesMax     DECIMAL(9,1)  NOT NULL,
    RomTonnesMean        DECIMAL(9,1)  NOT NULL,
    RomTonnesMin         DECIMAL(9,1)  NOT NULL,
    RomTonnesMax         DECIMAL(9,1)  NOT NULL,
    WasteTonnesMean      DECIMAL(9,1)  NOT NULL,
    WasteTonnesMin       DECIMAL(9,1)  NOT NULL,
    WasteTonnesMax       DECIMAL(9,1)  NOT NULL,
    CyclesMean           DECIMAL(7,1)  NOT NULL,
    CyclesMin            DECIMAL(7,1)  NOT NULL,
    CyclesMax            DECIMAL(7,1)  NOT NULL,
    QueueHoursMean       DECIMAL(7,2)  NOT NULL,
    QueueHoursMin        DECIMAL(7,2)  NOT NULL,
    QueueHoursMax        DECIMAL(7,2)  NOT NULL,
    FuelLitresMean       DECIMAL(9,1)  NOT NULL,
    FuelLitresMin        DECIMAL(9,1)  NOT NULL,
    FuelLitresMax        DECIMAL(9,1)  NOT NULL,
    TruckHoursMean       DECIMAL(6,1)  NOT NULL,
    TruckHoursMin        DECIMAL(6,1)  NOT NULL,
    TruckHoursMax        DECIMAL(6,1)  NOT NULL,
    TrucksStoodDownMean  DECIMAL(4,1)  NOT NULL,
    TrucksStoodDownMin   DECIMAL(4,1)  NOT NULL,
    TrucksStoodDownMax   DECIMAL(4,1)  NOT NULL,
    CreatedAt            DATETIME2(0)  NOT NULL,
    CONSTRAINT UQ_OptimisedPlans_Shift_Type UNIQUE (ShiftDate, ShiftName, PlanType)
);

/* One row per truck per plan: its route under that plan (NULL if unavailable or stood down),
   whether the optimiser stood it down (IsStoodDown - a plan decision on a truck that WAS
   available) versus it being unavailable regardless of plan (IsUnavailable - carried over
   unchanged from Schedules.UnavailableReason, never touched by local search).

   MoveReason is dead: the generator now always writes NULL to it. The move-away-from-Original
   sentence shown on the optimiser page is built by the API at read time (MoveReasonCalculator,
   from this table's own rows plus OptimisedLoaderStats and OptimisedPlans), not stored here -
   an owner decision so a wording change never needs a --optimise rerun. Left in place rather
   than dropped now; remove it at the next schema change. */
CREATE TABLE dbo.OptimisedAssignments (
    OptimisedAssignmentId  INT           IDENTITY(1,1) NOT NULL PRIMARY KEY,
    PlanId                 INT           NOT NULL REFERENCES dbo.OptimisedPlans(PlanId),
    TruckId                INT           NOT NULL REFERENCES dbo.Trucks(TruckId),
    RouteId                INT           NULL REFERENCES dbo.Routes(RouteId),
    IsStoodDown            BIT           NOT NULL,
    IsUnavailable          BIT           NOT NULL,
    MoveReason             NVARCHAR(300) NULL  -- dead column, always NULL now; see comment above
);

/* One row per loader per plan: Trucks is the plan's fixed assignment count (not random);
   AvgQueueMin/LoadingMin/Utilisation/MatchFactor are averaged over the plan's SeedCount
   replays. Utilisation = LoadingMin / (720 - loader-stopped minutes in the shift); MatchFactor
   = Trucks x average load time / average truck cycle time at that loader (both zero if the
   loader had no cycles that replay). */
CREATE TABLE dbo.OptimisedLoaderStats (
    OptimisedLoaderStatId  INT           IDENTITY(1,1) NOT NULL PRIMARY KEY,
    PlanId                 INT           NOT NULL REFERENCES dbo.OptimisedPlans(PlanId),
    LoaderId               INT           NOT NULL REFERENCES dbo.Loaders(LoaderId),
    Trucks                 INT           NOT NULL,
    AvgQueueMin            DECIMAL(6,2)  NOT NULL,
    LoadingMin             DECIMAL(7,2)  NOT NULL,
    Utilisation            DECIMAL(6,4)  NOT NULL,
    MatchFactor            DECIMAL(6,3)  NOT NULL
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

/* Pre-joined loader-delay view, with the same ShiftName/ShiftDate derivation as
   vw_CycleDetail plus a duration in minutes. */
CREATE VIEW dbo.vw_LoaderDelayDetail AS
SELECT
    ld.LoaderDelayId,
    l.Name AS LoaderName,
    ld.StartTime,
    ld.EndTime,
    CASE WHEN DATEPART(HOUR, ld.StartTime) BETWEEN 6 AND 17 THEN 'Day' ELSE 'Night' END AS ShiftName,
    CASE WHEN DATEPART(HOUR, ld.StartTime) BETWEEN 0 AND 5
         THEN CAST(DATEADD(DAY, -1, ld.StartTime) AS DATE)
         ELSE CAST(ld.StartTime AS DATE)
    END AS ShiftDate,
    DATEDIFF(SECOND, ld.StartTime, ld.EndTime) / 60.0 AS DurationMin,
    ld.Reason,
    ld.IsPlanned,
    ld.RateFactor
FROM dbo.LoaderDelays AS ld
JOIN dbo.Loaders AS l ON l.LoaderId = ld.LoaderId;
GO

/* Straight pass-through of OptimisedPlans - no joins needed at this grain - kept as a view
   purely so the read-only API (db_datareader only) queries a vw_* the same way it does
   everywhere else. */
CREATE VIEW dbo.vw_OptimisedPlanDetail AS
SELECT
    p.PlanId, p.ShiftDate, p.ShiftName, p.PlanType, p.SeedCount,
    p.TotalTonnesMean, p.TotalTonnesMin, p.TotalTonnesMax,
    p.CrusherTonnesMean, p.CrusherTonnesMin, p.CrusherTonnesMax,
    p.RomTonnesMean, p.RomTonnesMin, p.RomTonnesMax,
    p.WasteTonnesMean, p.WasteTonnesMin, p.WasteTonnesMax,
    p.CyclesMean, p.CyclesMin, p.CyclesMax,
    p.QueueHoursMean, p.QueueHoursMin, p.QueueHoursMax,
    p.FuelLitresMean, p.FuelLitresMin, p.FuelLitresMax,
    p.TruckHoursMean, p.TruckHoursMin, p.TruckHoursMax,
    p.TrucksStoodDownMean, p.TrucksStoodDownMin, p.TrucksStoodDownMax,
    p.CreatedAt
FROM dbo.OptimisedPlans AS p;
GO

/* Pre-joined assignment view with names instead of ids, for the moves list on the optimiser
   page. RouteName/LoaderName/DestinationName are NULL for an unavailable or stood-down truck.
   MoveReason is passed through but always NULL now (see the table comment above) - the API
   doesn't select it. */
CREATE VIEW dbo.vw_OptimisedAssignmentDetail AS
SELECT
    a.OptimisedAssignmentId,
    p.PlanId, p.ShiftDate, p.ShiftName, p.PlanType,
    t.Name AS TruckName,
    r.Name AS RouteName,
    l.Name AS LoaderName,
    d.Name AS DestinationName,
    a.IsStoodDown,
    a.IsUnavailable,
    a.MoveReason
FROM dbo.OptimisedAssignments AS a
JOIN dbo.OptimisedPlans AS p ON p.PlanId = a.PlanId
JOIN dbo.Trucks         AS t ON t.TruckId = a.TruckId
LEFT JOIN dbo.Routes       AS r ON r.RouteId       = a.RouteId
LEFT JOIN dbo.Loaders      AS l ON l.LoaderId      = r.LoaderId
LEFT JOIN dbo.Destinations AS d ON d.DestinationId = r.DestinationId;
GO

/* Pre-joined loader-stat view for the before/after loader table on the optimiser page. */
CREATE VIEW dbo.vw_OptimisedLoaderStatDetail AS
SELECT
    ls.OptimisedLoaderStatId,
    p.PlanId, p.ShiftDate, p.ShiftName, p.PlanType,
    l.Name AS LoaderName,
    ls.Trucks,
    ls.AvgQueueMin,
    ls.LoadingMin,
    ls.Utilisation,
    ls.MatchFactor
FROM dbo.OptimisedLoaderStats AS ls
JOIN dbo.OptimisedPlans AS p ON p.PlanId = ls.PlanId
JOIN dbo.Loaders        AS l ON l.LoaderId = ls.LoaderId;
GO
