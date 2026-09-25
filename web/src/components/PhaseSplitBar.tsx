import type { PhaseSplit } from '../api/types'
import { fmtNumber, fmtPercentScaled } from '../lib/format'

interface PhaseSplitBarProps {
  phaseSplit: PhaseSplit | null
  averageCycleMin: number | null
}

const PHASES: { key: keyof PhaseSplit; label: string; color: string }[] = [
  { key: 'loadPercent', label: 'Load', color: '#22d3ee' },
  { key: 'haulPercent', label: 'Haul', color: '#3b82f6' },
  { key: 'dumpPercent', label: 'Dump', color: '#a855f7' },
  { key: 'returnPercent', label: 'Return', color: '#f59e0b' },
  { key: 'queuePercent', label: 'Queue', color: '#ef4444' },
]

export function PhaseSplitBar({ phaseSplit, averageCycleMin }: PhaseSplitBarProps) {
  return (
    <div className="rounded-lg border border-slate-800 bg-slate-900/60 p-4">
      <h2 className="mb-3 text-sm font-semibold text-slate-200">
        Average phase split <span className="font-normal text-slate-500">({fmtNumber(averageCycleMin)} min/cycle)</span>
      </h2>

      {phaseSplit ? (
        <>
          <div className="flex h-6 w-full overflow-hidden rounded-md border border-slate-800">
            {PHASES.map(({ key, label, color }) => {
              const value = phaseSplit[key]
              if (value <= 0) return null
              return (
                <div
                  key={key}
                  style={{ width: `${value}%`, backgroundColor: color }}
                  title={`${label}: ${fmtPercentScaled(value)}`}
                />
              )
            })}
          </div>

          <div className="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-400">
            {PHASES.map(({ key, label, color }) => (
              <span key={key} className="flex items-center gap-1.5">
                <span className="h-2 w-2 rounded-sm" style={{ backgroundColor: color }} />
                {label} {fmtPercentScaled(phaseSplit[key])}
              </span>
            ))}
          </div>
        </>
      ) : (
        <p className="py-4 text-center text-sm text-slate-500">No cycles in this window.</p>
      )}
    </div>
  )
}
