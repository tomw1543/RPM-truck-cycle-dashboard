/* HaulCycle Insights - read-only API login (SQL Server / T-SQL)
   Independent learning project on synthetic data. Not affiliated with any company.

   Creates a SQL login + database user "haul_api" with db_datareader only (no write
   rights of any kind). The API connects as this login, never as sa - see README.md
   for the resulting connection string and HAUL_API_DB_CONN.

   Run this against the local Docker SQL Server with sqlcmd, once, after the schema
   exists. Copy the file into the container first (docker compose exec can't read a
   host path), then pass the password as a sqlcmd variable - the -v value is what
   fills $(HaulApiPassword) below, so it MUST be supplied on every run (there's no
   hardcoded default to fall back to):

     docker compose cp db/readonly-login.sql db:/tmp/readonly-login.sql
     MSYS_NO_PATHCONV=1 docker compose exec db /opt/mssql-tools18/bin/sqlcmd -C \
       -S localhost -U sa -P "<sa password>" \
       -v HaulApiPassword="<value of HAUL_API_PASSWORD from .env>" \
       -i /tmp/readonly-login.sql

   or paste it into SSMS/Azure Data Studio with $(HaulApiPassword) replaced by hand.
   Safe to re-run: it drops and recreates the login/user first.
*/

USE master;
GO

IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'haul_api')
    DROP LOGIN haul_api;
GO

CREATE LOGIN haul_api WITH PASSWORD = '$(HaulApiPassword)', CHECK_POLICY = ON;
GO

USE HaulCycleInsights;
GO

IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'haul_api')
    DROP USER haul_api;
GO

CREATE USER haul_api FOR LOGIN haul_api;
GO

ALTER ROLE db_datareader ADD MEMBER haul_api;
GO

-- No further grants. db_datareader is SELECT-only across the database; haul_api has
-- no INSERT/UPDATE/DELETE/EXECUTE/ALTER rights anywhere.

/* ---------------------------------------------------------------------------------
   Azure SQL variant (commented out).

   Azure SQL has no server-level logins in the same sense as a self-hosted instance -
   auth is per-database via contained database users. Run this instead, connected
   directly to the target database (not master):

   IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'haul_api')
       DROP USER haul_api;
   GO

   CREATE USER haul_api WITH PASSWORD = '$(HaulApiPassword)';
   GO

   ALTER ROLE db_datareader ADD MEMBER haul_api;
   GO
   --------------------------------------------------------------------------------- */
