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
