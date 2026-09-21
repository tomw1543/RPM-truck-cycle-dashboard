/* HaulCycle Insights - database schema (SQL Server / T-SQL)
   Independent learning project on synthetic data. Not affiliated with any company.

   Run this against an existing database (local SQL Server in Docker, or Azure SQL).
   It does not create or select a database, so it works the same way in sqlcmd,
   SSMS, or Azure Data Studio, against any target database.

   Safe to re-run: it drops and recreates everything (a full reset). */

DROP VIEW  IF EXISTS dbo.vw_CycleDetail;
DROP TABLE IF EXISTS dbo.Delays;
DROP TABLE IF EXISTS dbo.Cycles;
DROP TABLE IF EXISTS dbo.Routes;
DROP TABLE IF EXISTS dbo.Loaders;
DROP TABLE IF EXISTS dbo.Trucks;
GO

CREATE TABLE dbo.Trucks (
    TruckId         INT           NOT NULL PRIMARY KEY,
    Name            NVARCHAR(20)  NOT NULL,
    CapacityTonnes  DECIMAL(6,1)  NOT NULL
);

CREATE TABLE dbo.Loaders (
    LoaderId  INT           NOT NULL PRIMARY KEY,
    Name      NVARCHAR(20)  NOT NULL
);

CREATE TABLE dbo.Routes (
    RouteId       INT            NOT NULL PRIMARY KEY,
    Name          NVARCHAR(50)   NOT NULL,
    DistanceKm    DECIMAL(5,2)   NOT NULL,
    GradePercent  DECIMAL(4,1)   NOT NULL
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
JOIN dbo.Trucks  AS t ON t.TruckId  = c.TruckId
JOIN dbo.Loaders AS l ON l.LoaderId = c.LoaderId
JOIN dbo.Routes  AS r ON r.RouteId  = c.RouteId;
GO
