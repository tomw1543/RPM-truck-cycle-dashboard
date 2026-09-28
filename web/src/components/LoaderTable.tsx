import type { LoaderShift } from '../api/types'
import { fmtInt, fmtNumber, fmtPercentFraction, fmtRatio } from '../lib/format'

interface LoaderTableProps {
  shifts: LoaderShift[]
}

/** One row per loader per shift, newest first (matches the API's ordering). */
export function LoaderTable({ shifts }: LoaderTableProps) {
  return (
    <div className="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900/60">
      <h2 className="px-3 pt-3 text-sm font-semibold text-slate-200">Loader-shifts</h2>
      <p className="px-3 pb-2 text-xs text-slate-400">Newest first.</p>
      {shifts.length === 0 ? (
        <p className="px-3 pb-4 text-sm text-slate-400">No loader-shifts in this window.</p>
      ) : (
        <table className="w-full min-w-[720px] text-sm">
          <thead>
            <tr className="border-b border-slate-800 text-left text-xs uppercase tracking-wide text-slate-400">
              <th className="px-3 py-2 font-medium">Shift</th>
              <th className="px-3 py-2 font-medium">Loader</th>
              <th className="px-3 py-2 text-right font-medium">Trucks</th>
              <th className="px-3 py-2 text-right font-medium">Match factor</th>
              <th className="px-3 py-2 text-right font-medium">Avg queue (min)</th>
              <th className="px-3 py-2 text-right font-medium">Utilisation</th>
              <th className="px-3 py-2 text-right font-medium">Stopped (min)</th>
            </tr>
          </thead>
          <tbody>
            {shifts.map((s) => (
              <tr key={`${s.shiftDate}-${s.shiftName}-${s.loaderName}`} className="border-b border-slate-800/60 hover:bg-slate-800/30">
                <td className="px-3 py-1.5 text-slate-300">
                  {s.shiftDate} {s.shiftName}
                  {!s.isComplete && <span className="ml-1 text-xs text-slate-500">(in progress)</span>}
                </td>
                <td className="px-3 py-1.5 text-slate-300">{s.loaderName}</td>
                <td className="px-3 py-1.5 text-right tabular-nums text-slate-300">{fmtInt(s.trucks)}</td>
                <td className="px-3 py-1.5 text-right tabular-nums text-slate-300">{fmtRatio(s.matchFactor)}</td>
                <td className="px-3 py-1.5 text-right tabular-nums text-slate-300">{fmtNumber(s.avgQueueMin)}</td>
                <td className="px-3 py-1.5 text-right tabular-nums text-slate-300">{fmtPercentFraction(s.utilisation)}</td>
                <td className="px-3 py-1.5 text-right tabular-nums text-slate-400">{fmtNumber(s.stoppedMin, 0)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
