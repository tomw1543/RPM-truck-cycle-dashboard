import { Link } from 'react-router'
import type { UnderloadData } from '../api/types'
import { fmtInt, fmtNumber, fmtTonnes } from '../lib/format'

interface UnderloadTableProps {
  data: UnderloadData
  windowQuery: string
}

export function UnderloadTable({ data, windowQuery }: UnderloadTableProps) {
  const sorted = [...data.byTruck].sort((a, b) => b.underloadTonnes - a.underloadTonnes)

  return (
    <div className="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900/60">
      <h2 className="px-3 pt-3 text-sm font-semibold text-slate-200">
        Underload <span className="font-normal text-slate-400">({fmtTonnes(data.totalTonnes)})</span>
      </h2>
      <p className="px-3 pb-2 text-xs text-slate-400">
        Tonnes lost to part-filled trucks (capacity minus payload per cycle, floored at zero) - missed by cycle-time
        measures, since a lighter load also loads faster.
      </p>
      {sorted.length === 0 ? (
        <p className="px-3 pb-4 text-sm text-slate-400">No underload in this window.</p>
      ) : (
        <table className="w-full min-w-[520px] text-sm">
          <thead>
            <tr className="border-b border-slate-800 text-left text-xs uppercase tracking-wide text-slate-400">
              <th className="px-3 py-2 font-medium">Truck</th>
              <th className="px-3 py-2 text-right font-medium">Cycles</th>
              <th className="px-3 py-2 text-right font-medium">Underload tonnes</th>
              <th className="px-3 py-2 text-right font-medium">Avg payload %</th>
            </tr>
          </thead>
          <tbody>
            {sorted.map((t) => (
              <tr key={t.truckName} className="border-b border-slate-800/60 last:border-0 hover:bg-slate-800/30">
                <td className="px-3 py-2">
                  <Link to={`/trucks/${t.truckName}${windowQuery}`} className="text-cyan-400 hover:underline">
                    {t.truckName}
                  </Link>
                </td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-300">{fmtInt(t.cycles)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-200">{fmtTonnes(t.underloadTonnes)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-400">{fmtNumber(t.averagePayloadPercent, 0)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
