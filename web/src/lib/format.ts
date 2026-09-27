const EMPTY = '—'

/** Renders a nullable number, or EMPTY when it's null/undefined. */
export function fmt(value: number | null | undefined, format: (v: number) => string): string {
  return value === null || value === undefined ? EMPTY : format(value)
}

export function fmtInt(value: number | null | undefined): string {
  return fmt(value, (v) => Math.round(v).toLocaleString())
}

export function fmtNumber(value: number | null | undefined, digits = 1): string {
  return fmt(value, (v) => v.toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits }))
}

/** value is a 0-1 fraction (availability, utilisation, idlePercent, percentOfPlan). */
export function fmtPercentFraction(value: number | null | undefined, digits = 0): string {
  return fmt(value, (v) => `${(v * 100).toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits })}%`)
}

/** value is a signed 0-1 fraction (RouteData.vsBook) - rendered with an explicit +/- sign. */
export function fmtSignedPercentFraction(value: number | null | undefined, digits = 1): string {
  return fmt(value, (v) => `${v >= 0 ? '+' : ''}${(v * 100).toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits })}%`)
}

/** value is already on a 0-100 scale (CycleTimeCalculator.PhaseSplit). */
export function fmtPercentScaled(value: number | null | undefined, digits = 0): string {
  return fmt(value, (v) => `${v.toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits })}%`)
}

export function fmtRatio(value: number | null | undefined, digits = 2): string {
  return fmt(value, (v) => v.toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits }))
}

export function fmtTonnes(value: number | null | undefined): string {
  return fmt(value, (v) => `${Math.round(v).toLocaleString()} t`)
}

/** Compact axis-tick form of a tonnage value, e.g. 380000 -> "380k". Not for tooltips
 * or KPI tiles - those want fmtTonnes' full number with a unit suffix. */
export function fmtTonnesCompact(value: number | null | undefined): string {
  return fmt(value, (v) =>
    Math.abs(v) >= 1000
      ? `${(v / 1000).toLocaleString(undefined, { maximumFractionDigits: 1 })}k`
      : Math.round(v).toLocaleString(),
  )
}

/** value is a signed count/amount (an optimiser gain or saving) - rendered with an explicit
 * +/- sign, no unit. A negative "saved" value means the candidate plan used MORE than Original. */
export function fmtSignedNumber(value: number | null | undefined, digits = 1): string {
  return fmt(value, (v) => `${v >= 0 ? '+' : ''}${v.toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits })}`)
}

export function fmtSignedTonnes(value: number | null | undefined): string {
  return fmt(value, (v) => `${v >= 0 ? '+' : ''}${Math.round(v).toLocaleString()} t`)
}

export function fmtHours(value: number | null | undefined, digits = 1): string {
  return fmt(value, (v) => `${v.toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits })} h`)
}

export function fmtSignedHours(value: number | null | undefined, digits = 1): string {
  return fmt(value, (v) => `${v >= 0 ? '+' : ''}${v.toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits })} h`)
}

export function fmtLitres(value: number | null | undefined): string {
  return fmt(value, (v) => `${Math.round(v).toLocaleString()} L`)
}

export function fmtSignedLitres(value: number | null | undefined): string {
  return fmt(value, (v) => `${v >= 0 ? '+' : ''}${Math.round(v).toLocaleString()} L`)
}

/** asOf comes from the API as a DateTime serialized to an ISO-ish string in mine time
 * (Australia/Brisbane, no offset info we should reinterpret). We must not run it through
 * the browser's local time zone, so this only reformats the literal date/time components. */
export function formatAsOf(asOf: string | null): string {
  if (!asOf) return EMPTY
  const match = /^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})/.exec(asOf)
  if (!match) return asOf
  const [, year, month, day, hour, minute] = match
  return `${day}/${month}/${year} ${hour}:${minute}`
}
