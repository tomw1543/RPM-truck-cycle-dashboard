import type { ActualShiftOutcome, OptimiserOutcome } from '../api/types'
import { KpiTile } from './KpiTile'
import { fmtInt, fmtLitres, fmtNumber, fmtTonnes } from '../lib/format'

interface OptimiserActualComparisonProps {
  actual: ActualShiftOutcome
  replayedOriginal: OptimiserOutcome
}

/** The shift as it really ran, next to the replayed Original - the faithfulness check. They
 * rarely match exactly: the replay draws its own independent random numbers (fixed seeds, but a
 * different stream from the one that generated the actual data), and the very first shift in a
 * freshly loaded window can have a partial actual record if the load window started mid-shift.
 * A 2-5% difference in total tonnes is typical; see CONTEXT.md for the measured figure. */
export function OptimiserActualComparison({ actual, replayedOriginal }: OptimiserActualComparisonProps) {
  return (
    <div className="rounded-lg border border-slate-800 bg-slate-900/60 p-4">
      <h3 className="text-sm font-semibold text-slate-200">Actual vs replayed Original</h3>
      <p className="mt-1 text-xs text-slate-400">
        These can differ slightly: the replay draws its own random numbers from a fixed seed, independent of the
        numbers that produced the actual data, and the earliest shift in a freshly loaded window can have a partial
        actual record if the load window started partway through it.
      </p>
      <div className="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-4">
        <KpiTile label="Actual tonnes" value={fmtTonnes(actual.totalTonnes)} sublabel={`Replayed ${fmtTonnes(replayedOriginal.totalTonnes.mean)}`} />
        <KpiTile label="Actual cycles" value={fmtInt(actual.cycles)} sublabel={`Replayed ${fmtNumber(replayedOriginal.cycles.mean, 0)}`} />
        <KpiTile label="Actual queue hours" value={fmtNumber(actual.queueHours)} sublabel={`Replayed ${fmtNumber(replayedOriginal.queueHours.mean)}`} />
        <KpiTile label="Actual fuel" value={fmtLitres(actual.fuelLitres)} sublabel={`Replayed ${fmtLitres(replayedOriginal.fuelLitres.mean)}`} />
      </div>
    </div>
  )
}
