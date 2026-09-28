import { useState } from 'react'
import type { OverTargetData, PhaseMinutes } from '../api/types'
import { fmtNumber, fmtTonnes } from '../lib/format'

interface OverTargetTableProps {
  data: OverTargetData
}

const PHASES: { key: keyof PhaseMinutes; label: string; color: string }[] = [
  { key: 'load', label: 'Load', color: '#22d3ee' },
  { key: 'haul', label: 'Haul', color: '#3b82f6' },
  { key: 'dump', label: 'Dump', color: '#a855f7' },
  { key: 'return', label: 'Return', color: '#f59e0b' },
  { key: 'queue', label: 'Queue', color: '#ef4444' },
  { key: 'unattributed', label: 'Unattributed', color: '#475569' },
]

function PhaseBar({ byPhase, totalMin }: { byPhase: PhaseMinutes; totalMin: number }) {
  if (totalMin <= 0) return null
  return (
    <>
      <div className="mx-3 mt-3 flex h-5 w-auto overflow-hidden rounded-md border border-slate-800">
        {PHASES.map(({ key, label, color }) => {
          const value = byPhase[key]
          if (value <= 0) return null
          const percent = (value / totalMin) * 100
          return (
            <div
              key={key}
              style={{ width: `${percent}%`, backgroundColor: color }}
              title={`${label}: ${fmtNumber(value)} min (${fmtNumber(percent, 0)}%)`}
            />
          )
        })}
      </div>
      <div className="mx-3 mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-400">
        {PHASES.map(({ key, label, color }) => {
          const value = byPhase[key]
          if (value <= 0) return null
          return (
            <span key={key} className="flex items-center gap-1.5">
              <span className="h-2 w-2 rounded-sm" style={{ backgroundColor: color }} />
              {label} {fmtNumber(value)} min
            </span>
          )
        })}
      </div>
    </>
  )
}

export function OverTargetTable({ data }: OverTargetTableProps) {
  const sorted = [...data.byRoute].sort((a, b) => b.totalMin - a.totalMin)
  const [expanded, setExpanded] = useState<Set<string>>(new Set())

  function toggle(routeName: string) {
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(routeName)) next.delete(routeName)
      else next.add(routeName)
      return next
    })
  }

  return (
    <div className="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900/60">
      <h2 className="px-3 pt-3 text-sm font-semibold text-slate-200">
        Minutes over target{' '}
        <span className="font-normal text-slate-400">
          ({fmtNumber(data.totalMin)} min, &asymp; {fmtTonnes(data.totalEquivalentTonnes)})
        </span>
      </h2>
      <p className="px-3 pb-2 text-xs text-slate-400">
        Each cycle&apos;s time above its route&apos;s target cycle time, split across phases.
      </p>

      <PhaseBar byPhase={data.byPhase} totalMin={data.totalMin} />

      {sorted.length === 0 ? (
        <p className="px-3 py-4 text-sm text-slate-400">No minutes over target in this window.</p>
      ) : (
        <table className="mt-3 w-full min-w-[400px] text-sm">
          <thead>
            <tr className="border-b border-slate-800 text-left text-xs uppercase tracking-wide text-slate-400">
              <th className="px-3 py-2 font-medium">Route</th>
              <th className="px-3 py-2 text-right font-medium">Total (min)</th>
              <th className="px-3 py-2 text-right font-medium">&asymp; Tonnes</th>
              <th className="w-8 px-3 py-2" />
            </tr>
          </thead>
          <tbody>
            {sorted.map((r) => {
              const isExpanded = expanded.has(r.routeName)
              return (
                <>
                  <tr key={r.routeName} className="border-b border-slate-800/60 hover:bg-slate-800/30">
                    <td className="px-3 py-2 text-slate-200">{r.routeName}</td>
                    <td className="px-3 py-2 text-right tabular-nums text-slate-200">{fmtNumber(r.totalMin)}</td>
                    <td className="px-3 py-2 text-right tabular-nums text-slate-300">{fmtTonnes(r.equivalentTonnes)}</td>
                    <td className="px-3 py-2 text-right">
                      <button
                        type="button"
                        aria-expanded={isExpanded}
                        aria-label={`${isExpanded ? 'Collapse' : 'Expand'} phase breakdown for ${r.routeName}`}
                        onClick={() => toggle(r.routeName)}
                        className="text-xs text-slate-400 hover:text-slate-200"
                      >
                        {isExpanded ? '▲' : '▼'}
                      </button>
                    </td>
                  </tr>
                  {isExpanded && (
                    <tr key={`${r.routeName}-phases`} className="border-b border-slate-800/60 bg-slate-950/40">
                      <td colSpan={4} className="px-6 py-2">
                        <div className="flex flex-wrap gap-x-5 gap-y-1 text-xs text-slate-400">
                          {PHASES.map(({ key, label, color }) => (
                            <span key={key} className="flex items-center gap-1.5">
                              <span className="h-2 w-2 rounded-sm" style={{ backgroundColor: color }} />
                              {label}: {fmtNumber(r.phases[key])} min
                            </span>
                          ))}
                        </div>
                      </td>
                    </tr>
                  )}
                </>
              )
            })}
          </tbody>
        </table>
      )}
    </div>
  )
}
