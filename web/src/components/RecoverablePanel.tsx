import { Link } from 'react-router'
import type { NamedAmount, PhaseMinutes, RecoverableData } from '../api/types'
import { fmtInt, fmtNumber, fmtTonnes } from '../lib/format'

interface RecoverablePanelProps {
  data: RecoverableData
  windowQuery: string
}

const PHASES: { key: keyof PhaseMinutes; label: string; color: string }[] = [
  { key: 'load', label: 'Load', color: '#22d3ee' },
  { key: 'haul', label: 'Haul', color: '#3b82f6' },
  { key: 'dump', label: 'Dump', color: '#a855f7' },
  { key: 'return', label: 'Return', color: '#f59e0b' },
  { key: 'queue', label: 'Queue', color: '#ef4444' },
  { key: 'unattributed', label: 'Unattributed', color: '#475569' },
]

function AmountTable({ title, rows, windowQuery, linkTrucks }: { title: string; rows: NamedAmount[]; windowQuery: string; linkTrucks?: boolean }) {
  return (
    <div className="overflow-x-auto rounded-lg border border-slate-800 bg-slate-950/40">
      <h3 className="px-3 pt-2 text-xs font-semibold uppercase tracking-wide text-slate-400">{title}</h3>
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-slate-800 text-left text-xs text-slate-400">
            <th className="px-3 py-1.5 font-medium">Name</th>
            <th className="px-3 py-1.5 text-right font-medium">Minutes</th>
            <th className="px-3 py-1.5 text-right font-medium">&asymp; Tonnes</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.name} className="border-b border-slate-800/60 last:border-0">
              <td className="px-3 py-1.5 text-slate-200">
                {linkTrucks ? (
                  <Link to={`/trucks/${r.name}${windowQuery}`} className="text-cyan-400 hover:underline">
                    {r.name}
                  </Link>
                ) : (
                  r.name
                )}
              </td>
              <td className="px-3 py-1.5 text-right tabular-nums text-slate-300">{fmtNumber(r.minutes)}</td>
              <td className="px-3 py-1.5 text-right tabular-nums text-slate-400">{fmtTonnes(r.equivalentTonnes)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

export function RecoverablePanel({ data, windowQuery }: RecoverablePanelProps) {
  return (
    <div className="rounded-lg border border-slate-800 bg-slate-900/60 p-4">
      <h2 className="text-sm font-semibold text-slate-200">
        Recoverable minutes{' '}
        <span className="font-normal text-slate-400">
          ({fmtNumber(data.totalMin)} min, &asymp; {fmtTonnes(data.totalEquivalentTonnes)})
        </span>
      </h2>
      <p className="mt-1 text-xs text-slate-400">
        Each cycle&apos;s time above its route&apos;s all-time 25th-percentile benchmark, split across the phases
        that caused the gap. A gap with no phase excess (percentiles don&apos;t add) falls into Unattributed.
      </p>

      {data.totalMin > 0 ? (
        <>
          <div className="mt-3 flex h-6 w-full overflow-hidden rounded-md border border-slate-800">
            {PHASES.map(({ key, label, color }) => {
              const value = data.byPhase[key]
              if (value <= 0) return null
              const percent = (value / data.totalMin) * 100
              return (
                <div
                  key={key}
                  style={{ width: `${percent}%`, backgroundColor: color }}
                  title={`${label}: ${fmtNumber(value)} min (${fmtNumber(percent, 0)}%)`}
                />
              )
            })}
          </div>
          <div className="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-400">
            {PHASES.map(({ key, label, color }) => (
              <span key={key} className="flex items-center gap-1.5">
                <span className="h-2 w-2 rounded-sm" style={{ backgroundColor: color }} />
                {label} {fmtNumber(data.byPhase[key])} min
              </span>
            ))}
          </div>

          <div className="mt-4 grid gap-3 md:grid-cols-3">
            <AmountTable title="By route" rows={data.byRoute} windowQuery={windowQuery} />
            <AmountTable title="By truck" rows={data.byTruck} windowQuery={windowQuery} linkTrucks />
            <AmountTable title="By loader" rows={data.byLoader} windowQuery={windowQuery} />
          </div>
        </>
      ) : (
        <p className="py-4 text-center text-sm text-slate-400">No recoverable minutes in this window.</p>
      )}
      <p className="mt-3 text-xs text-slate-400">
        {fmtInt(data.byRoute.length)} route{data.byRoute.length === 1 ? '' : 's'} with recoverable minutes in this window.
      </p>
    </div>
  )
}
