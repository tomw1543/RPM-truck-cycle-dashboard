#!/usr/bin/env bash
# Reconciliation check for /api/fleet/summary's planVsActual block: this repo has no
# WebApplicationFactory/DB-backed test harness (Kpi/ calculators are unit tested with
# in-memory rows on purpose), so the DB-backed cross-check lives here instead of in
# tests/HaulCycle.Api.Tests. It fails loudly if API window semantics for plan vs
# actual (a shift-grained measure - see PlanVsActualCalculator.cs) drift out of sync with
# a direct SQL aggregate over the same shift set.
#
# Requires: the API running locally (dotnet run --project api/HaulCycle.Api), the `db`
# Docker service up, and python on PATH. Run from the repo root:
#
#   MSYS_NO_PATHCONV=1 API_BASE=http://localhost:5080 bash tests/verify-plan-vs-actual.sh
#
# Exits non-zero (and prints the mismatch) if any window's API percentOfPlan doesn't match
# the direct-SQL figure within TOLERANCE_PCT (rounding only - the two must agree on which
# shifts are in scope, not just be "close").

set -euo pipefail

API_BASE="${API_BASE:-http://localhost:5080}"
TOLERANCE_PCT="${TOLERANCE_PCT:-0.3}"  # percentage points

# .env values (HAUL_API_DB_CONN in particular) contain semicolons, so `source .env` would
# have bash try to run its later fields as commands - read the one line we need instead.
if [ -z "${MSSQL_SA_PASSWORD:-}" ] && [ -f .env ]; then
  MSSQL_SA_PASSWORD="$(grep '^MSSQL_SA_PASSWORD=' .env | cut -d= -f2-)"
fi

SA_PASSWORD="${MSSQL_SA_PASSWORD:?MSSQL_SA_PASSWORD not set (export it or put it in .env)}"

sqlcmd_query() {
  local sql="$1"
  MSYS_NO_PATHCONV=1 docker compose exec -T db /opt/mssql-tools18/bin/sqlcmd -C \
    -S localhost -U sa -P "$SA_PASSWORD" -d HaulCycleInsights -h -1 -W \
    -Q "SET NOCOUNT ON; $sql" 2>&1
}

check_window() {
  local label="$1" query="$2"

  local url="${API_BASE}/api/fleet/summary"
  if [ -n "$query" ]; then url="${url}?${query}"; fi

  local body
  body="$(curl -sf "$url")" || { echo "FAIL [$label]: curl to $url failed"; exit 1; }

  # Pull asOf/from/to/shift-derived SQL bounds and planVsActual figures out of the JSON.
  read -r AS_OF FROM_DATE TO_DATE API_PCT API_ACTUAL API_PLANNED API_EXCLUDED <<PYEOF
$(python -c "
import json, sys
d = json.load(sys.stdin)
pva = d['data']['planVsActual']
print(d['asOf'], d['from'], d['to'], pva['percentOfPlan'], pva['actualTonnes'], pva['plannedTonnes'], pva['excludedShiftCount'])
" <<< "$body")
PYEOF

  local shift_filter="1=1"
  case "$label" in
    *Day*) shift_filter="ShiftName = 'Day'" ;;
    *Night*) shift_filter="ShiftName = 'Night'" ;;
  esac

  # Same shift-grained window as GetCyclesForShiftWindowAsync/GetSchedulesAsync: whole
  # shifts whose ShiftDate falls in [FROM_DATE, TO_DATE], minus any shift not yet complete
  # as of asOf (end time after asOf).
  local sql
  sql=$(cat <<SQL
WITH shifts AS (
  SELECT DISTINCT ShiftDate, ShiftName FROM dbo.vw_CycleDetail WHERE ShiftDate BETWEEN '$FROM_DATE' AND '$TO_DATE'
  UNION
  SELECT DISTINCT ShiftDate, ShiftName FROM dbo.vw_ScheduleDetail WHERE ShiftDate BETWEEN '$FROM_DATE' AND '$TO_DATE'
),
complete AS (
  SELECT ShiftDate, ShiftName FROM shifts
  WHERE ($shift_filter)
    AND DATEADD(HOUR, CASE WHEN ShiftName = 'Day' THEN 18 ELSE 30 END, CAST(ShiftDate AS DATETIME2(0))) <= '$AS_OF'
),
a AS (
  SELECT c.ShiftDate, c.ShiftName, SUM(c.PayloadTonnes) act
  FROM dbo.vw_CycleDetail c JOIN complete x ON x.ShiftDate = c.ShiftDate AND x.ShiftName = c.ShiftName
  GROUP BY c.ShiftDate, c.ShiftName
),
p AS (
  SELECT s.ShiftDate, s.ShiftName, SUM(s.PlannedTonnes) pl
  FROM dbo.vw_ScheduleDetail s JOIN complete x ON x.ShiftDate = s.ShiftDate AND x.ShiftName = s.ShiftName
  WHERE s.PlannedTonnes IS NOT NULL
  GROUP BY s.ShiftDate, s.ShiftName
)
SELECT
  CAST(ISNULL((SELECT SUM(act) FROM a), 0) AS DECIMAL(12,1)) AS ActualTonnes,
  CAST(ISNULL((SELECT SUM(pl) FROM p), 0) AS DECIMAL(12,1)) AS PlannedTonnes,
  (SELECT COUNT(*) FROM shifts WHERE ($shift_filter)) - (SELECT COUNT(*) FROM complete) AS ExcludedShiftCount;
SQL
)

  local result
  result="$(sqlcmd_query "$sql")"
  local sql_actual sql_planned sql_excluded
  read -r sql_actual sql_planned sql_excluded <<< "$(echo "$result" | sed -n '1p')"

  local sql_pct
  sql_pct=$(python -c "
a = $sql_actual
p = $sql_planned
print(round(100.0*a/p, 1) if p > 0 else None)
")

  echo "[$label] window=$FROM_DATE..$TO_DATE asOf=$AS_OF"
  echo "  API:  actual=$API_ACTUAL planned=$API_PLANNED pct=$API_PCT excluded=$API_EXCLUDED"
  echo "  SQL:  actual=$sql_actual planned=$sql_planned pct=$sql_pct excluded=$sql_excluded"

  python -c "
import sys
# API's percentOfPlan is a ratio (0-1); the SQL figure above is a percentage - put both
# on the same percentage-point scale before comparing.
api_pct = 100.0 * $API_PCT if '$API_PCT' != 'None' else None
sql_pct = $sql_pct if '$sql_pct' != 'None' else None
tol = $TOLERANCE_PCT
if api_pct is None and sql_pct is None:
    sys.exit(0)
if api_pct is None or sql_pct is None or abs(api_pct - sql_pct) > tol:
    print(f'MISMATCH [$label]: api={api_pct} sql={sql_pct} tolerance={tol}')
    sys.exit(1)
print('  OK (within tolerance)')
"
}

check_window "default-7day" ""
check_window "30day" "from=$(date -d '-30 days' +%F 2>/dev/null || date -v-30d +%F)&to=$(date +%F)"
check_window "shift-Day" "shift=Day"
check_window "shift-Night" "shift=Night"

echo "All plan-vs-actual reconciliation checks passed."
