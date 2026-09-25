import type { RouteData } from '../api/types'
import { fmtInt, fmtNumber, fmtSignedPercentFraction, fmtTonnes } from '../lib/format'

interface RoutesTableProps {
  routes: RouteData[]
}

// Worst (highest vsBook) first; routes with no cycles in the window (vsBook null) sort last.
function sortedByVsBook(routes: RouteData[]): RouteData[] {
  return [...routes].sort((a, b) => {
    if (a.vsBook === null && b.vsBook === null) return 0
    if (a.vsBook === null) return 1
    if (b.vsBook === null) return -1
    return b.vsBook - a.vsBook
  })
}

export function RoutesTable({ routes }: RoutesTableProps) {
  const sorted = sortedByVsBook(routes)

  return (
    <div className="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900/60">
      <h2 className="px-3 pt-3 text-sm font-semibold text-slate-200">Routes</h2>
      <p className="px-3 pb-2 text-xs text-slate-500">
        Vs book compares this window's average cycle time to the route's book rate (full payload,
        no slow ramp). P25 is the route's all-time 25th-percentile cycle time, the benchmark used
        for recoverable minutes - it doesn't change with the date window.
      </p>
      <table className="w-full min-w-[820px] text-sm">
        <thead>
          <tr className="border-b border-slate-800 text-left text-xs uppercase tracking-wide text-slate-500">
            <th className="px-3 py-2 font-medium">Route</th>
            <th className="px-3 py-2 font-medium">Destination</th>
            <th className="px-3 py-2 text-right font-medium">Distance (km)</th>
            <th className="px-3 py-2 text-right font-medium">Grade %</th>
            <th className="px-3 py-2 text-right font-medium">Book (min)</th>
            <th className="px-3 py-2 text-right font-medium">Actual avg (min)</th>
            <th className="px-3 py-2 text-right font-medium">Vs book</th>
            <th className="px-3 py-2 text-right font-medium">P25 (min)</th>
            <th className="px-3 py-2 text-right font-medium">Haul avg (min)</th>
            <th className="px-3 py-2 text-right font-medium">Haul P25 (min)</th>
            <th className="px-3 py-2 text-right font-medium">Cycles</th>
            <th className="px-3 py-2 text-right font-medium">Tonnes</th>
          </tr>
        </thead>
        <tbody>
          {sorted.map((route) => {
            const worse = route.vsBook !== null && route.vsBook > 0.1
            return (
              <tr key={route.routeName} className="border-b border-slate-800/60 last:border-0 hover:bg-slate-800/30">
                <td className="px-3 py-2 text-slate-200">{route.routeName}</td>
                <td className="px-3 py-2 text-slate-400">{route.destinationName}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-300">{fmtNumber(route.distanceKm, 1)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-300">{fmtNumber(route.gradePercent, 1)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-300">{fmtNumber(route.bookCycleMin)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-300">{fmtNumber(route.averageCycleMin)}</td>
                <td className={`px-3 py-2 text-right tabular-nums ${worse ? 'bg-red-950/50 text-red-300' : 'text-slate-300'}`}>
                  {fmtSignedPercentFraction(route.vsBook)}
                </td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-300">{fmtNumber(route.benchmarkCycleMin)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-400">{fmtNumber(route.phases.haul.averageMin)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-400">{fmtNumber(route.phases.haul.benchmarkMin)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-300">{fmtInt(route.cycles)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-300">{fmtTonnes(route.tonnes)}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
