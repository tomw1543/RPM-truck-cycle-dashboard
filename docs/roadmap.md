# Roadmap

Phase 1 ships three screens: fleet overview, truck drill-down, and where time is lost.
This file lists what comes after, so the shape of phase 1 leaves room for it.

## Diagnostics to add

**Loader queue heatmap.** Hour of day by loader, coloured by average queue minutes.
Spikes show as blocks. This is the only view that proves a spike happened at a
particular loader at a particular hour, which route-level averages hide.

**Shift comparison.** Day against Night for the same KPIs. Night shifts usually run
fewer cycles. Showing the gap raises the question of why.

**Plan compliance with shortfall attribution.** For each shift, split the gap between
planned and actual tonnes into causes: trucks unavailable, payload short, cycle times
over book, and shifts where the scheduler assigned trucks badly.

**Route scorecard.** Actual cycle time against book time for each of the 9 routes, with
the phase that explains the gap. The waste dump ramp shows up here.

**Payload distribution.** A histogram per truck against its capacity, with control
limits. An average hides a truck that alternates between full and half loads.

**Delay Pareto with MTBF and MTTR.** Downtime ranked by reason, plus mean time between
failures and mean time to repair per truck. Both come from the Delays table.

**Fuel efficiency.** Litres per tonne and per tonne-kilometre, split by phase. Fuel
burnt while queueing is pure waste and worth its own number.

**Trend and anomaly detection.** Rolling 7-day averages per truck, flagging any truck
that drifts more than two standard deviations from the fleet.

**Match factor over time.** Per loader, per shift. A single fleet-wide number hides a
loader that is over-trucked while another sits idle.

**Cycle time percentile bands.** P10, P25, P50 and P90 per route. The P25 benchmark
already drives recoverable minutes, so showing the spread explains where that number
comes from.

**Over-trucking scatter.** Queue minutes against loader utilisation, one point per
loader-shift. Points in the top right mean too many trucks on that loader.

## Implementation work

**New endpoints.** `GET /api/loaders/{id}/queue-series` (hourly), `GET /api/trucks/{id}/payload-distribution`,
`GET /api/delays/pareto`, `GET /api/fuel`, `GET /api/shifts/compare`.

**Optimiser endpoint (Project 2's hook).** `POST /api/schedule/optimise` takes a shift
and returns a better truck-to-route assignment with the tonnes it would gain. The
scheduler assigns trucks randomly on purpose, so there is always a gain to find.

**Live updates without polling.** Server-sent events push new cycles as the generator
writes them, replacing the 10-second refetch. Cheaper against Azure SQL, since the
browser stops asking when nothing has changed.

**Generated TypeScript types.** The API publishes an OpenAPI document. Generating the
client types from it removes the hand-written interfaces in the React app.

**Response caching and ETags.** Aggregates change only when new cycles land. ETags let
the browser skip unchanged payloads, which keeps the Azure database asleep longer.

**Alerts feed.** In live mode, a panel listing what changed in the last 15 minutes:
queue spikes, breakdowns, and trucks falling behind plan.
