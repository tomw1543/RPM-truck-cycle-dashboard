import { Fragment, useState } from 'react'
import { Link } from 'react-router'
import type { ShiftCompliance, TruckCompliance } from '../api/types'
import { fmtInt, fmtNumber, fmtPercentFraction, fmtTonnes } from '../lib/format'

interface ComplianceTableProps {
  shifts: ShiftCompliance[]
  windowQuery: string
}

function shiftKey(s: ShiftCompliance): string {
  return `${s.shiftDate}-${s.shiftName}`
}

function reasonsLabel(s: ShiftCompliance): string {
  return s.unavailableReasons.map((r) => `${r.reason} (${r.count})`).join(', ')
}

function TruckRow({ truck, windowQuery }: { truck: TruckCompliance; windowQuery: string }) {
  return (
    <tr className="border-b border-slate-800/40 bg-slate-950/40 last:border-0">
      <td className="px-3 py-1.5 pl-8 text-slate-300">
        <Link to={`/trucks/${truck.truckName}${windowQuery}`} className="text-cyan-400 hover:underline">
          {truck.truckName}
        </Link>
      </td>
      <td className="px-3 py-1.5 text-slate-400">{truck.routeName ?? truck.unavailableReason ?? '—'}</td>
      <td className="px-3 py-1.5 text-right tabular-nums text-slate-400">{fmtTonnes(truck.plannedTonnes)}</td>
      <td className="px-3 py-1.5 text-right tabular-nums text-slate-300">{fmtTonnes(truck.actualTonnes)}</td>
      <td
        className={`px-3 py-1.5 text-right tabular-nums ${truck.belowTypical ? 'bg-red-950/50 text-red-300' : 'text-slate-300'}`}
        title={truck.belowTypical ? 'More than 10 points below the window baseline' : undefined}
      >
        {truck.belowTypical && (
          <>
            <span aria-hidden="true">▲ </span>
            <span className="sr-only">below typical: </span>
          </>
        )}
        {fmtPercentFraction(truck.percentOfPlan)}
      </td>
      <td className="px-3 py-1.5 text-right tabular-nums text-slate-400">
        {fmtInt(truck.actualCycles)} / {fmtInt(truck.plannedCycles)}
      </td>
      <td className="px-3 py-1.5 text-right tabular-nums text-slate-400">{fmtNumber(truck.averagePayloadPercent, 0)}</td>
    </tr>
  )
}

export function ComplianceTable({ shifts, windowQuery }: ComplianceTableProps) {
  const [expanded, setExpanded] = useState<Set<string>>(new Set())

  const toggle = (key: string) => {
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })
  }

  return (
    <div className="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900/60">
      <h2 className="px-3 pt-3 text-sm font-semibold text-slate-200">Shifts</h2>
      <p className="px-3 pb-2 text-xs text-slate-400">
        Newest first. Expand a shift to see each truck's plan vs actual. Rows marked ▲ are more
        than 5 points (shifts) or 10 points (trucks) below the window's baseline percent of plan.
      </p>
      {shifts.length === 0 ? (
        <p className="px-3 pb-4 text-sm text-slate-400">No shifts in this window.</p>
      ) : (
        <table className="w-full min-w-[760px] text-sm">
          <thead>
            <tr className="border-b border-slate-800 text-left text-xs uppercase tracking-wide text-slate-400">
              <th className="px-3 py-2 font-medium">Shift</th>
              <th className="px-3 py-2 font-medium">Unavailable</th>
              <th className="px-3 py-2 text-right font-medium">Planned tonnes</th>
              <th className="px-3 py-2 text-right font-medium">Actual tonnes</th>
              <th className="px-3 py-2 text-right font-medium">% of plan</th>
              <th className="px-3 py-2 text-right font-medium">Cycles (actual / planned)</th>
            </tr>
          </thead>
          <tbody>
            {shifts.map((shift) => {
              const key = shiftKey(shift)
              const isExpanded = expanded.has(key)
              const panelId = `compliance-trucks-${key}`
              return (
                <Fragment key={key}>
                  <tr className="border-b border-slate-800/60 hover:bg-slate-800/30">
                    <td className="px-3 py-2 text-slate-200">
                      <button
                        type="button"
                        onClick={() => toggle(key)}
                        aria-expanded={isExpanded}
                        aria-controls={panelId}
                        aria-label={`${isExpanded ? 'Hide' : 'Show'} trucks for ${shift.shiftDate} ${shift.shiftName}`}
                        className="inline-flex items-center gap-1 hover:text-cyan-300"
                      >
                        <span aria-hidden="true" className="inline-block w-3 text-slate-500">
                          {isExpanded ? '▾' : '▸'}
                        </span>
                        <span>
                          {shift.shiftDate} {shift.shiftName}
                        </span>
                        {!shift.isComplete && <span className="text-xs text-slate-500">(in progress)</span>}
                      </button>
                    </td>
                    <td className={`px-3 py-2 text-xs ${shift.unavailableCount > 0 ? 'text-orange-400/80' : 'text-slate-500'}`}>
                      {shift.unavailableCount > 0 ? `${shift.unavailableCount} — ${reasonsLabel(shift)}` : '—'}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums text-slate-300">{fmtTonnes(shift.plannedTonnes)}</td>
                    <td className="px-3 py-2 text-right tabular-nums text-slate-300">{fmtTonnes(shift.actualTonnes)}</td>
                    <td
                      className={`px-3 py-2 text-right tabular-nums ${shift.belowTypical ? 'bg-red-950/50 text-red-300' : 'text-slate-300'}`}
                      title={shift.belowTypical ? 'More than 5 points below the window baseline' : undefined}
                    >
                      {shift.belowTypical && (
                        <>
                          <span aria-hidden="true">▲ </span>
                          <span className="sr-only">below typical: </span>
                        </>
                      )}
                      {fmtPercentFraction(shift.percentOfPlan)}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums text-slate-300">
                      {fmtInt(shift.actualCycles)} / {fmtInt(shift.plannedCycles)}
                    </td>
                  </tr>
                  {isExpanded && (
                    <tr id={panelId} className="border-b border-slate-800/60">
                      <td colSpan={6} className="p-0">
                        <table className="w-full text-sm">
                          <thead>
                            <tr className="border-b border-slate-800/60 text-left text-xs uppercase tracking-wide text-slate-500">
                              <th className="px-3 py-1.5 pl-8 font-medium">Truck</th>
                              <th className="px-3 py-1.5 font-medium">Route / reason</th>
                              <th className="px-3 py-1.5 text-right font-medium">Planned tonnes</th>
                              <th className="px-3 py-1.5 text-right font-medium">Actual tonnes</th>
                              <th className="px-3 py-1.5 text-right font-medium">% of plan</th>
                              <th className="px-3 py-1.5 text-right font-medium">Cycles (actual / planned)</th>
                              <th className="px-3 py-1.5 text-right font-medium">Avg payload %</th>
                            </tr>
                          </thead>
                          <tbody>
                            {shift.trucks.map((truck) => (
                              <TruckRow key={truck.truckName} truck={truck} windowQuery={windowQuery} />
                            ))}
                          </tbody>
                        </table>
                      </td>
                    </tr>
                  )}
                </Fragment>
              )
            })}
          </tbody>
        </table>
      )}
    </div>
  )
}
