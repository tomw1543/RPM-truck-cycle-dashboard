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
  targetMin: number
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
 * the window). vsTarget is a fraction (averageCycleMin / targetCycleMin - 1), null with no cycles
 * in the window. benchmarkCycleMin is the route's all-time 25th-percentile total cycle time. */
export interface RouteData {
  routeName: string
  loaderName: string
  destinationName: string
  material: string
  distanceKm: number
  gradePercent: number
  targetCycleMin: number
  cycles: number
  tonnes: number
  averageCycleMin: number | null
  vsTarget: number | null
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
 * recoverable/over-target minutes, since those minutes come from window cycles that do have a
 * route rate). */
export interface NamedAmount {
  name: string
  minutes: number
  equivalentTonnes: number | null
}

/** BottleneckEndpoints.RoutePhaseMinutesData - one route's over-target (or recoverable-by-route)
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

export interface OverTargetData {
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

/** BottleneckEndpoints.LossItemData. measure is one of "overTarget" | "underload" -
 * the two measures overlap and must never be added together. nativeUnit is "min" or "t". */
export interface LossItem {
  measure: 'overTarget' | 'underload'
  subject: string
  phase: string | null
  nativeAmount: number
  nativeUnit: 'min' | 't'
  equivalentTonnes: number
}

/** BottleneckEndpoints.BottlenecksData - GET /api/bottlenecks. */
export interface BottlenecksData {
  recoverable: RecoverableData
  overTarget: OverTargetData
  underload: UnderloadData
  biggestLosses: LossItem[]
}

/** ScheduleEndpoints.ReasonCountData - one unavailability reason's count within a shift. */
export interface ReasonCount {
  reason: string
  count: number
}

/** ShortfallAttributionCalculator.Buckets, mirrored as ScheduleEndpoints.ShortfallBucketsData.
 * Signed tonnes: a negative bucket means the truck/shift GAINED tonnes there (e.g. loading
 * faster than target), never floored to zero. Buckets sum exactly (to rounding) to the gap they
 * decompose. otherOverTarget groups load/dump/return over target together; haulOverTarget is kept
 * separate since that's where the waste dump ramp and the night effect show. */
export interface ShortfallBuckets {
  payloadShort: number
  unplannedDowntime: number
  queueLoaderDelay: number
  queueOverTrucking: number
  haulOverTarget: number
  otherOverTarget: number
  residual: number
}

/** ScheduleEndpoints.TruckComplianceData - one truck's plan vs actual for one shift.
 * unavailableReason set (and routeName/plannedTonnes null) when the truck had no schedule that
 * shift; belowTypical is only ever true on a complete shift. percentOfPlan/averagePayloadPercent
 * follow the same conventions as elsewhere (0-1 fraction, 0-100 scale respectively). shortfall is
 * null when the truck had no plan that shift (unavailable). */
export interface TruckCompliance {
  truckName: string
  routeName: string | null
  loaderName: string | null
  unavailableReason: string | null
  plannedTonnes: number | null
  actualTonnes: number
  plannedCycles: number | null
  actualCycles: number
  percentOfPlan: number | null
  averagePayloadPercent: number | null
  belowTypical: boolean
  shortfall: ShortfallBuckets | null
}

/** ScheduleEndpoints.ShiftComplianceData - one shift's fleet-wide plan vs actual, plus its
 * per-truck breakdown. belowTypical only applies to complete shifts. shortfall is null when no
 * truck in the shift had a plan. */
export interface ShiftCompliance {
  shiftDate: string
  shiftName: string
  isComplete: boolean
  plannedTonnes: number | null
  actualTonnes: number
  plannedCycles: number | null
  actualCycles: number
  percentOfPlan: number | null
  belowTypical: boolean
  unavailableCount: number
  unavailableReasons: ReasonCount[]
  trucks: TruckCompliance[]
  shortfall: ShortfallBuckets | null
}

/** ScheduleEndpoints.ShiftSummaryData - one shift referenced from the summary (best/worst). */
export interface ShiftSummaryRef {
  shiftDate: string
  shiftName: string
  percentOfPlan: number
}

/** ScheduleEndpoints.ComplianceSummaryData - complete-shift totals for the window. baseline is
 * the tonnes-weighted percent of plan across complete shifts, identical to
 * /api/fleet/summary's planVsActual.percentOfPlan for the same window. */
export interface WindowShortfall {
  gap: number
  buckets: ShortfallBuckets
  shiftCount: number
}

export interface ComplianceSummary {
  plannedTonnes: number | null
  actualTonnes: number
  plannedCycles: number | null
  actualCycles: number
  percentOfPlan: number | null
  baseline: number | null
  shiftCount: number
  best: ShiftSummaryRef | null
  worst: ShiftSummaryRef | null
  shortfall: WindowShortfall | null
}

/** ScheduleEndpoints.ScheduleComplianceData - GET /api/schedule/compliance. Shifts are ordered
 * newest first. */
export interface ScheduleComplianceData {
  summary: ComplianceSummary
  shifts: ShiftCompliance[]
}

/** OptimiserEndpoints.OptimiserPlanGainData - one plan type's whole-window gain vs Original.
 * TonnesGained's min/max are a conservative/optimistic bound (sum of per-shift candidate.Min -
 * original.Max, and candidate.Max - original.Min), not a statistically derived range for the sum
 * - see OptimiserSummaryCalculator's doc comment. QueueHoursSaved/FuelLitresSaved/TruckHoursSaved
 * are Original minus the candidate, summed per shift - positive always means "the candidate used
 * less" (a negative value means it used more, e.g. Leaner can burn more fuel while still saving
 * truck-hours). */
export interface OptimiserPlanGain {
  tonnesGainedMean: number
  tonnesGainedMin: number
  tonnesGainedMax: number
  queueHoursSaved: number
  fuelLitresSaved: number
  truckHoursSaved: number
  trucksStoodDown: number
}

/** OptimiserEndpoints.OptimiserShiftHeadlineData - one shift's row for the shift picker.
 * moreOutputTonnesGainedMean/leanerTruckHoursSavedMean are null when hasResults is false. */
export interface OptimiserShiftHeadline {
  shiftDate: string
  shiftName: string
  hasResults: boolean
  moreOutputTonnesGainedMean: number | null
  leanerTruckHoursSavedMean: number | null
}

/** OptimiserEndpoints.OptimiserSummaryData - GET /api/optimiser/summary. shifts is newest first. */
export interface OptimiserSummaryData {
  shiftsOptimised: number
  shiftsNotOptimised: number
  moreOutput: OptimiserPlanGain
  leaner: OptimiserPlanGain
  shifts: OptimiserShiftHeadline[]
}

/** OptimiserEndpoints.RangeData - mean/min/max over a plan's SeedCount replays. For TruckHours
 * and TrucksStoodDown, min/max always equal mean - those are plan-determined, not random. */
export interface OptimiserRange {
  mean: number
  min: number
  max: number
}

/** OptimiserEndpoints.OptimiserOutcomeData - one plan's outcome, every field as a range. */
export interface OptimiserOutcome {
  totalTonnes: OptimiserRange
  crusherTonnes: OptimiserRange
  romTonnes: OptimiserRange
  wasteTonnes: OptimiserRange
  cycles: OptimiserRange
  queueHours: OptimiserRange
  fuelLitres: OptimiserRange
  truckHours: OptimiserRange
  trucksStoodDown: OptimiserRange
}

/** OptimiserEndpoints.OptimiserAssignmentData - one truck's assignment under one plan.
 * routeName/loaderName/destinationName are null for an unavailable or stood-down truck.
 * moveReason is null for Original and for any truck left on its Original route - otherwise it's
 * built by the API at read time (MoveReasonCalculator), from this plan's own assignments, loader
 * stats and tonnes against Original's, so it only ever states things that are actually true. */
export interface OptimiserAssignment {
  truckName: string
  routeName: string | null
  loaderName: string | null
  destinationName: string | null
  isStoodDown: boolean
  isUnavailable: boolean
  moveReason: string | null
}

/** OptimiserEndpoints.OptimiserLoaderStatData - one loader's stats under one plan, averaged
 * over the plan's replays. utilisation is a 0-1 fraction; matchFactor is a plain ratio. */
export interface OptimiserLoaderStat {
  loaderName: string
  trucks: number
  avgQueueMin: number
  loadingMin: number
  utilisation: number
  matchFactor: number
}

export type OptimiserPlanType = 'Original' | 'MoreOutput' | 'Leaner'

/** OptimiserEndpoints.OptimiserPlanData - one plan (Original/MoreOutput/Leaner) for one shift.
 * planSummary is null for Original (there's no "candidate vs Original" to summarise for Original
 * itself) - otherwise a headline sentence built from the loader stats' before/after truck counts,
 * e.g. "L3 lost 2 trucks (average queue 6.1 to 2.2 min); L1 gained 2 trucks (utilisation 20% to 65%).
 * Stood down 3 trucks." */
export interface OptimiserPlan {
  planType: OptimiserPlanType
  seedCount: number
  planSummary: string | null
  outcome: OptimiserOutcome
  assignments: OptimiserAssignment[]
  loaderStats: OptimiserLoaderStat[]
}

/** OptimiserEndpoints.ActualShiftOutcomeData - the shift as it actually ran (vw_CycleDetail),
 * for the faithfulness comparison against replayed Original. */
export interface ActualShiftOutcome {
  crusherTonnes: number
  romTonnes: number
  wasteTonnes: number
  totalTonnes: number
  cycles: number
  queueHours: number
  fuelLitres: number
}

/** OptimiserEndpoints.OptimiserShiftDetailData - GET /api/optimiser/shifts/{shiftDate}/{shiftName}. */
export interface OptimiserShiftDetailData {
  shiftDate: string
  shiftName: string
  original: OptimiserPlan
  moreOutput: OptimiserPlan
  leaner: OptimiserPlan
  actual: ActualShiftOutcome
}

/** LoaderEndpoints.LoaderShiftData - one loader's stats for one shift. utilisation is a 0-1
 * fraction; matchFactor is a plain ratio (0 with no cycles at that loader that shift). Same
 * definitions as OptimisedLoaderStats/OptimiserLoaderStat, so this page and the optimiser's
 * before/after loader table agree. */
export interface LoaderShift {
  shiftDate: string
  shiftName: string
  isComplete: boolean
  loaderName: string
  trucks: number
  avgQueueMin: number
  loadingMin: number
  stoppedMin: number
  utilisation: number
  matchFactor: number
}

/** LoaderEndpoints.LoaderSummaryData - one loader's window-average figures, over complete
 * shifts only. */
export interface LoaderSummary {
  loaderName: string
  avgUtilisation: number
  avgQueueMin: number
  avgMatchFactor: number
}

/** LoaderEndpoints.LoadersData - GET /api/loaders. shifts is newest first. */
export interface LoadersData {
  shifts: LoaderShift[]
  summary: LoaderSummary[]
}

/** LoaderEndpoints.LoaderArrivalData - one truck's arrival at a loader during a shift. */
export interface LoaderArrival {
  truckName: string
  arrivalTime: string  // ISO datetime (= cycle StartTime)
  queueMin: number
}

/** LoaderEndpoints.LoaderDelayWindowData - one loader delay window during a shift. */
export interface LoaderDelayWindow {
  start: string   // ISO datetime
  end: string
  rateFactor: number  // 0.00 = handover (fully stopped), 0.50 = half-speed spike
}

/** LoaderEndpoints.LoaderQueueData - one loader's arrivals and delay windows for a shift. */
export interface LoaderQueueData {
  loaderName: string
  arrivals: LoaderArrival[]
  delays: LoaderDelayWindow[]
}

/** LoaderEndpoints.LoaderShiftQueueData - GET /api/loaders/shifts/{shiftDate}/{shiftName}. */
export interface LoaderShiftQueueData {
  loaders: LoaderQueueData[]
}
