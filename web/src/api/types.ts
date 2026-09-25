// Hand-written mirrors of the API's C# records (api/HaulCycle.Api/Endpoints, api/HaulCycle.Api/Kpi).
// JSON is camelCase (Envelope.cs / System.Text.Json defaults).

/** The `{ asOf, from, to, data }` envelope every data endpoint returns.
 * asOf is a DateTime? serialized as an ISO string (or null when there's no data at all).
 * from/to are DateOnly? serialized as "YYYY-MM-DD" strings. */
export interface Envelope<T> {
  asOf: string | null
  from: string | null
  to: string | null
  data: T
}

export interface Meta {
  trucks: number
  loaders: number
  routes: number
  destinations: number
}

/** CycleTimeCalculator.PhaseSplit - each phase's share of total cycle minutes,
 * already expressed on a 0-100 scale (not a 0-1 fraction). */
export interface PhaseSplit {
  loadPercent: number
  haulPercent: number
  dumpPercent: number
  returnPercent: number
  queuePercent: number
}

/** PlanVsActualCalculator.DestinationResult. percentOfPlan is a 0-1 fraction. */
export interface DestinationResult {
  destination: string
  actualTonnes: number
  plannedTonnes: number | null
  percentOfPlan: number | null
}

/** PlanVsActualCalculator.Result. percentOfPlan is a 0-1 fraction. */
export interface PlanVsActual {
  actualTonnes: number
  plannedTonnes: number | null
  percentOfPlan: number | null
  byDestination: DestinationResult[]
  completeShiftsOnly: boolean
  excludedShiftCount: number
}

/** FleetEndpoints.FleetSummaryData.
 *
 * availability, utilisation, effectiveUtilisation and idlePercent are 0-1 fractions
 * (AvailabilityCalculator never multiplies by 100) - multiply by 100 to display as a
 * percentage. phaseSplit's percents are already 0-100. matchFactor is a plain ratio
 * (typically close to 1), not a percentage. */
export interface FleetSummary {
  cycles: number
  tonnes: number
  tonnesPerOperatingHour: number | null
  tonnesPerCalendarHour: number | null
  averageCycleMin: number | null
  phaseSplit: PhaseSplit | null
  availability: number | null
  utilisation: number | null
  effectiveUtilisation: number | null
  idleMinutes: number
  idlePercent: number | null
  matchFactor: number | null
  planVsActual: PlanVsActual
}

export type Shift = 'Day' | 'Night'

export interface FleetSummaryParams {
  from?: string
  to?: string
  shift?: Shift
}

/** TruckEndpoints.TruckKpis. A row is either one truck's own KPIs, or (name = "Fleet average")
 * the fleet's rates alongside cycles/tonnes divided down to a per-truck average. Ratios stay
 * 0-1 fractions; averagePayloadPercent is 0-100, like PhaseSplit's percents. */
export interface TruckKpis {
  name: string
  capacityTonnes: number
  cycles: number
  tonnes: number
  tonnesPerOperatingHour: number | null
  tonnesPerCalendarHour: number | null
  averageCycleMin: number | null
  averagePayloadPercent: number | null
  cyclesPerOperatingHour: number | null
  availability: number | null
  utilisation: number | null
  effectiveUtilisation: number | null
  idlePercent: number | null
}

/** TruckEndpoints.TrucksListData. */
export interface TrucksListData {
  fleet: TruckKpis
  trucks: TruckKpis[]
}

/** TruckEndpoints.DelayReasonData. Minutes are clipped to the requested window (and shift). */
export interface DelayReason {
  reason: string
  isPlanned: boolean
  count: number
  minutes: number
}

/** TruckEndpoints.ShiftSeriesData - one truck's plan vs actual for one shift.
 * unavailableReason is set (and routeName/plannedTonnes null) when the truck had no schedule
 * that shift. */
export interface ShiftSeries {
  shiftDate: string
  shiftName: string
  routeName: string | null
  unavailableReason: string | null
  plannedTonnes: number | null
  actualTonnes: number
  cycles: number
  averagePayloadPercent: number | null
  isComplete: boolean
}

/** TruckEndpoints.TruckDetailData. */
export interface TruckDetailData {
  truck: TruckKpis
  fleet: TruckKpis
  phaseSplit: PhaseSplit | null
  delaysByReason: DelayReason[]
  shifts: ShiftSeries[]
}

export interface TruckWindowParams {
  from?: string
  to?: string
  shift?: Shift
}

/** RouteEndpoints.RoutePhaseData. averageMin is the window average; benchmarkMin is the route's
 * all-time 25th percentile (RouteBenchmarkCalculator) - independent of the requested window. */
export interface RoutePhase {
  averageMin: number | null
  benchmarkMin: number | null
  bookMin: number
}

/** RouteEndpoints.RoutePhasesData. */
export interface RoutePhases {
  queue: RoutePhase
  load: RoutePhase
  haul: RoutePhase
  dump: RoutePhase
  return: RoutePhase
}

/** RouteEndpoints.RouteData. One row per route (all 9, including a route with zero cycles in
 * the window). vsBook is a fraction (averageCycleMin / bookCycleMin - 1), null with no cycles in
 * the window. benchmarkCycleMin is the route's all-time 25th-percentile total cycle time. */
export interface RouteData {
  routeName: string
  loaderName: string
  destinationName: string
  material: string
  distanceKm: number
  gradePercent: number
  bookCycleMin: number
  cycles: number
  tonnes: number
  averageCycleMin: number | null
  vsBook: number | null
  benchmarkCycleMin: number | null
  phases: RoutePhases
}

/** RouteEndpoints.RoutesListData. */
export interface RoutesListData {
  routes: RouteData[]
}

/** BottleneckEndpoints.PhaseMinutesData - minutes attributed to each phase (see CONTEXT.md's
 * Phase attribution), plus an Unattributed bucket for cycles whose gap has no phase excess.
 * Shares + unattributed always sum to the measure's totalMin. */
export interface PhaseMinutes {
  queue: number
  load: number
  haul: number
  dump: number
  return: number
  unattributed: number
}

/** BottleneckEndpoints.NamedAmountData - one route/truck/loader's share of recoverable minutes.
 * equivalentTonnes is null only if the calculator couldn't rate its route (shouldn't happen for
 * recoverable/over-book minutes, since those minutes come from window cycles that do have a
 * route rate). */
export interface NamedAmount {
  name: string
  minutes: number
  equivalentTonnes: number | null
}

/** BottleneckEndpoints.RoutePhaseMinutesData - one route's over-book (or recoverable-by-route)
 * minutes with its own phase split. */
export interface RoutePhaseMinutes {
  routeName: string
  totalMin: number
  equivalentTonnes: number | null
  phases: PhaseMinutes
}

export interface RecoverableData {
  totalMin: number
  totalEquivalentTonnes: number
  byPhase: PhaseMinutes
  byRoute: NamedAmount[]
  byTruck: NamedAmount[]
  byLoader: NamedAmount[]
}

export interface OverBookData {
  totalMin: number
  totalEquivalentTonnes: number
  byPhase: PhaseMinutes
  byRoute: RoutePhaseMinutes[]
}

export interface TruckUnderload {
  truckName: string
  cycles: number
  underloadTonnes: number
  averagePayloadPercent: number | null
}

export interface UnderloadData {
  totalTonnes: number
  byTruck: TruckUnderload[]
  baselinePayloadPercent: number
}

/** BottleneckEndpoints.LossItemData. measure is one of "recoverable" | "overBook" | "underload" -
 * the three measures overlap and must never be added together. nativeUnit is "min" or "t". */
export interface LossItem {
  measure: 'recoverable' | 'overBook' | 'underload'
  subject: string
  phase: string | null
  nativeAmount: number
  nativeUnit: 'min' | 't'
  equivalentTonnes: number
}

export interface HotspotRow {
  loaderName: string
  dateHour: string
  excessMin: number
  cycles: number
  averageQueueMin: number
}

export interface ProfileCell {
  loaderName: string
  hourOfDay: number
  excessMin: number
  cycles: number
}

export interface HotspotsData {
  top: HotspotRow[]
  profile: ProfileCell[]
}

/** BottleneckEndpoints.BottlenecksData - GET /api/bottlenecks. */
export interface BottlenecksData {
  recoverable: RecoverableData
  overBook: OverBookData
  underload: UnderloadData
  biggestLosses: LossItem[]
  hotspots: HotspotsData
}
