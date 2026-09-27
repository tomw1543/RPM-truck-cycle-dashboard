import type { OptimiserOutcome, OptimiserRange } from '../api/types'
import { fmtInt, fmtLitres, fmtNumber, fmtSignedHours, fmtSignedLitres, fmtSignedNumber, fmtSignedTonnes, fmtTonnes } from '../lib/format'

interface OptimiserOutcomeComparisonProps {
  planLabel: string
  original: OptimiserOutcome
  chosen: OptimiserOutcome
}

interface Row {
  label: string
  original: OptimiserRange
  chosen: OptimiserRange
  fmtValue: (v: number) => string
  fmtDelta: (v: number) => string
}

export function OptimiserOutcomeComparison({ planLabel, original, chosen }: OptimiserOutcomeComparisonProps) {
  const rows: Row[] = [
    { label: 'Total tonnes', original: original.totalTonnes, chosen: chosen.totalTonnes, fmtValue: (v) => fmtTonnes(v), fmtDelta: (v) => fmtSignedTonnes(v) },
    { label: 'Crusher tonnes', original: original.crusherTonnes, chosen: chosen.crusherTonnes, fmtValue: (v) => fmtTonnes(v), fmtDelta: (v) => fmtSignedTonnes(v) },
    { label: 'ROM pad tonnes', original: original.romTonnes, chosen: chosen.romTonnes, fmtValue: (v) => fmtTonnes(v), fmtDelta: (v) => fmtSignedTonnes(v) },
    { label: 'Waste dump tonnes', original: original.wasteTonnes, chosen: chosen.wasteTonnes, fmtValue: (v) => fmtTonnes(v), fmtDelta: (v) => fmtSignedTonnes(v) },
    { label: 'Cycles', original: original.cycles, chosen: chosen.cycles, fmtValue: (v) => fmtInt(v), fmtDelta: (v) => fmtSignedNumber(v, 0) },
    { label: 'Queue hours', original: original.queueHours, chosen: chosen.queueHours, fmtValue: (v) => fmtNumber(v), fmtDelta: (v) => fmtSignedHours(v) },
    { label: 'Fuel litres', original: original.fuelLitres, chosen: chosen.fuelLitres, fmtValue: (v) => fmtLitres(v), fmtDelta: (v) => fmtSignedLitres(v) },
    { label: 'Truck-hours', original: original.truckHours, chosen: chosen.truckHours, fmtValue: (v) => fmtNumber(v, 0), fmtDelta: (v) => fmtSignedHours(v, 0) },
    { label: 'Trucks stood down', original: original.trucksStoodDown, chosen: chosen.trucksStoodDown, fmtValue: (v) => fmtInt(v), fmtDelta: (v) => fmtSignedNumber(v, 0) },
  ]

  return (
    <div className="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900/60 p-4">
      <h3 className="text-sm font-semibold text-slate-200">Outcome - Original vs {planLabel}</h3>
      <p className="mt-1 text-xs text-slate-400">Each value is the mean of 5 replays; the range is that plan's min to max across those replays.</p>
      <table className="mt-2 w-full min-w-[600px] text-sm">
        <thead>
          <tr className="border-b border-slate-800 text-left text-xs uppercase tracking-wide text-slate-400">
            <th className="px-3 py-2 font-medium">Outcome</th>
            <th className="px-3 py-2 text-right font-medium">Original</th>
            <th className="px-3 py-2 text-right font-medium">{planLabel} (range)</th>
            <th className="px-3 py-2 text-right font-medium">Delta</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.label} className="border-b border-slate-800/60 last:border-0">
              <td className="px-3 py-2 text-slate-200">{r.label}</td>
              <td className="px-3 py-2 text-right tabular-nums text-slate-400">{r.fmtValue(r.original.mean)}</td>
              <td className="px-3 py-2 text-right tabular-nums text-slate-300">
                {r.fmtValue(r.chosen.mean)}
                <span className="ml-1 text-xs text-slate-500">
                  ({r.fmtValue(r.chosen.min)} to {r.fmtValue(r.chosen.max)})
                </span>
              </td>
              <td className="px-3 py-2 text-right tabular-nums text-slate-200">{r.fmtDelta(r.chosen.mean - r.original.mean)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
