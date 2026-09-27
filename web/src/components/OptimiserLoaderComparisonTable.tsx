import type { OptimiserLoaderStat } from '../api/types'
import { fmtInt, fmtNumber, fmtPercentFraction, fmtRatio } from '../lib/format'

interface OptimiserLoaderComparisonTableProps {
  planLabel: string
  original: OptimiserLoaderStat[]
  chosen: OptimiserLoaderStat[]
}

export function OptimiserLoaderComparisonTable({ planLabel, original, chosen }: OptimiserLoaderComparisonTableProps) {
  const chosenByLoader = new Map(chosen.map((l) => [l.loaderName, l]))
  const loaders = [...original].sort((a, b) => a.loaderName.localeCompare(b.loaderName))

  return (
    <div className="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900/60 p-4">
      <h3 className="text-sm font-semibold text-slate-200">Loaders - Original vs {planLabel}</h3>
      <table className="mt-2 w-full min-w-[560px] text-sm">
        <thead>
          <tr className="border-b border-slate-800 text-left text-xs uppercase tracking-wide text-slate-400">
            <th className="px-3 py-2 font-medium">Loader</th>
            <th className="px-3 py-2 text-right font-medium">Trucks (orig / {planLabel.toLowerCase()})</th>
            <th className="px-3 py-2 text-right font-medium">Avg queue min (orig / {planLabel.toLowerCase()})</th>
            <th className="px-3 py-2 text-right font-medium">Utilisation (orig / {planLabel.toLowerCase()})</th>
            <th className="px-3 py-2 text-right font-medium">Match factor (orig / {planLabel.toLowerCase()})</th>
          </tr>
        </thead>
        <tbody>
          {loaders.map((orig) => {
            const c = chosenByLoader.get(orig.loaderName)
            return (
              <tr key={orig.loaderName} className="border-b border-slate-800/60 last:border-0">
                <td className="px-3 py-2 font-medium text-slate-200">{orig.loaderName}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-300">
                  {fmtInt(orig.trucks)} / {c ? fmtInt(c.trucks) : '—'}
                </td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-300">
                  {fmtNumber(orig.avgQueueMin)} / {c ? fmtNumber(c.avgQueueMin) : '—'}
                </td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-300">
                  {fmtPercentFraction(orig.utilisation)} / {c ? fmtPercentFraction(c.utilisation) : '—'}
                </td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-300">
                  {fmtRatio(orig.matchFactor)} / {c ? fmtRatio(c.matchFactor) : '—'}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
