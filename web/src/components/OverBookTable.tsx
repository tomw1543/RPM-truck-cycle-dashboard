import type { OverBookData } from '../api/types'
import { fmtNumber, fmtTonnes } from '../lib/format'

interface OverBookTableProps {
  data: OverBookData
}

export function OverBookTable({ data }: OverBookTableProps) {
  const sorted = [...data.byRoute].sort((a, b) => b.totalMin - a.totalMin)

  return (
    <div className="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900/60">
      <h2 className="px-3 pt-3 text-sm font-semibold text-slate-200">
        Minutes over book{' '}
        <span className="font-normal text-slate-400">
          ({fmtNumber(data.totalMin)} min, &asymp; {fmtTonnes(data.totalEquivalentTonnes)})
        </span>
      </h2>
      <p className="px-3 pb-2 text-xs text-slate-400">
        Each cycle&apos;s time above its route&apos;s book cycle time - slowness that affects every cycle on a
        route, which the P25 benchmark absorbs and so misses. Never added to recoverable minutes; the two overlap.
      </p>
      {sorted.length === 0 ? (
        <p className="px-3 pb-4 text-sm text-slate-400">No minutes over book in this window.</p>
      ) : (
        <table className="w-full min-w-[720px] text-sm">
          <thead>
            <tr className="border-b border-slate-800 text-left text-xs uppercase tracking-wide text-slate-400">
              <th className="px-3 py-2 font-medium">Route</th>
              <th className="px-3 py-2 text-right font-medium">Total (min)</th>
              <th className="px-3 py-2 text-right font-medium">Queue</th>
              <th className="px-3 py-2 text-right font-medium">Load</th>
              <th className="px-3 py-2 text-right font-medium">Haul</th>
              <th className="px-3 py-2 text-right font-medium">Dump</th>
              <th className="px-3 py-2 text-right font-medium">Return</th>
              <th className="px-3 py-2 text-right font-medium">Unattributed</th>
              <th className="px-3 py-2 text-right font-medium">&asymp; Tonnes</th>
            </tr>
          </thead>
          <tbody>
            {sorted.map((r) => (
              <tr key={r.routeName} className="border-b border-slate-800/60 last:border-0 hover:bg-slate-800/30">
                <td className="px-3 py-2 text-slate-200">{r.routeName}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-200">{fmtNumber(r.totalMin)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-400">{fmtNumber(r.phases.queue)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-400">{fmtNumber(r.phases.load)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-400">{fmtNumber(r.phases.haul)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-400">{fmtNumber(r.phases.dump)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-400">{fmtNumber(r.phases.return)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-400">{fmtNumber(r.phases.unattributed)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-300">{fmtTonnes(r.equivalentTonnes)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
