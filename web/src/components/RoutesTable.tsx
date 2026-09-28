import { useState } from 'react'
import type { RouteData } from '../api/types'
import { fmtInt, fmtNumber, fmtSignedPercentFraction, fmtTonnes } from '../lib/format'

interface RoutesTableProps {
  routes: RouteData[]
}

// Worst (highest vsTarget) first; routes with no cycles in the window (vsTarget null) sort last.
function sortedByVsTarget(routes: RouteData[]): RouteData[] {
  return [...routes].sort((a, b) => {
    if (a.vsTarget === null && b.vsTarget === null) return 0
    if (a.vsTarget === null) return 1
    if (b.vsTarget === null) return -1
    return b.vsTarget - a.vsTarget
  })
}

export function RoutesTable({ routes }: RoutesTableProps) {
  const sorted = sortedByVsTarget(routes)
  const [expandedRoute, setExpandedRoute] = useState<string | null>(null)

  return (
    <div className="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900/60">
      <h2 className="px-3 pt-3 text-sm font-semibold text-slate-200">Routes</h2>
      <table className="w-full min-w-[520px] text-sm">
        <thead>
          <tr className="border-b border-slate-800 text-left text-xs uppercase tracking-wide text-slate-400">
            <th className="w-8 px-3 py-2" aria-label="Expand" />
            <th className="px-3 py-2 font-medium">Route</th>
            <th className="px-3 py-2 text-right font-medium">Distance (km)</th>
            <th className="px-3 py-2 text-right font-medium">Avg cycle (min)</th>
            <th className="px-3 py-2 text-right font-medium">Vs target</th>
            <th className="px-3 py-2 text-right font-medium">Cycles</th>
            <th className="px-3 py-2 text-right font-medium">Tonnes</th>
          </tr>
        </thead>
        <tbody>
          {sorted.map((route) => {
            const worse = route.vsTarget !== null && route.vsTarget > 0.1
            const isExpanded = expandedRoute === route.routeName
            return (
              <>
                <tr key={route.routeName} className="border-b border-slate-800/60 last:border-0 hover:bg-slate-800/30">
                  <td className="px-3 py-2">
                    <button
                      type="button"
                      className="text-xs text-slate-500 hover:text-slate-300"
                      onClick={() => setExpandedRoute(isExpanded ? null : route.routeName)}
                      aria-expanded={isExpanded}
                      aria-label={isExpanded ? `Collapse ${route.loaderName} to ${route.destinationName}` : `Expand ${route.loaderName} to ${route.destinationName}`}
                    >
                      {isExpanded ? '▼' : '▶'}
                    </button>
                  </td>
                  <td className="px-3 py-2 text-slate-200">{route.loaderName} → {route.destinationName}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-slate-300">{fmtNumber(route.distanceKm, 1)}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-slate-300">{fmtNumber(route.averageCycleMin)}</td>
                  <td
                    className={`px-3 py-2 text-right tabular-nums ${worse ? 'bg-red-950/50 text-red-300' : 'text-slate-300'}`}
                    title={worse ? `${fmtSignedPercentFraction(route.vsTarget)} worse than target` : undefined}
                  >
                    {worse && <span aria-hidden="true">▲ </span>}
                    {fmtSignedPercentFraction(route.vsTarget)}
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums text-slate-300">{fmtInt(route.cycles)}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-slate-300">{fmtTonnes(route.tonnes)}</td>
                </tr>
                {isExpanded && (
                  <tr key={`${route.routeName}-detail`} className="border-b border-slate-800/60">
                    <td colSpan={7} className="px-5 py-3 text-xs text-slate-400">
                      <div className="flex flex-col gap-1">
                        {route.targetCycleMin != null && (
                          <p>Target cycle: {fmtNumber(route.targetCycleMin)} min at full load.</p>
                        )}
                        {route.benchmarkCycleMin != null && (
                          <p>Best quarter of cycles finish in {fmtNumber(route.benchmarkCycleMin)} min or less.</p>
                        )}
                        {route.phases.haul.averageMin != null && route.phases.haul.targetMin != null && (
                          <p>Haul averages {fmtNumber(route.phases.haul.averageMin)} min against a {fmtNumber(route.phases.haul.targetMin)} min target.</p>
                        )}
                        {route.gradePercent != null && (
                          <p>Uphill grade {fmtNumber(route.gradePercent, 1)}%.</p>
                        )}
                      </div>
                    </td>
                  </tr>
                )}
              </>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
