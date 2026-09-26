import { Bar, BarChart, CartesianGrid, Cell, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { ShiftCompliance } from '../api/types'
import { fmtTonnes, fmtTonnesCompact } from '../lib/format'

interface ScheduleChartProps {
  shifts: ShiftCompliance[]
}

function shiftLabel(s: ShiftCompliance): string {
  const [, month, day] = s.shiftDate.split('-')
  return `${day}/${month} ${s.shiftName === 'Day' ? 'D' : 'N'}`
}

function toChartRow(s: ShiftCompliance) {
  return {
    label: shiftLabel(s) + (s.isComplete ? '' : ' *'),
    Actual: s.actualTonnes,
    Planned: s.plannedTonnes ?? 0,
    isComplete: s.isComplete,
  }
}

/** Planned vs actual tonnes per shift. The API returns shifts newest-first; the chart reads
 * left to right oldest-first, like ShiftTonnesChart on the truck detail page. No belowTypical
 * marker here - that distinction lives in the table below. */
export function ScheduleChart({ shifts }: ScheduleChartProps) {
  const rows = [...shifts].reverse().map(toChartRow)
  const hasData = rows.length > 0
  const hasIncompleteShift = shifts.some((s) => !s.isComplete)

  return (
    <div className="rounded-lg border border-slate-800 bg-slate-900/60 p-4">
      <h2 className="mb-3 text-sm font-semibold text-slate-200">Planned vs actual tonnes per shift</h2>

      {hasData ? (
        <div className="h-64">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={rows} margin={{ top: 16, right: 20, left: 0, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#1e293b" />
              <XAxis dataKey="label" stroke="#64748b" fontSize={11} interval={0} angle={-45} textAnchor="end" height={50} />
              <YAxis stroke="#64748b" fontSize={12} tickFormatter={fmtTonnesCompact} />
              <Tooltip
                contentStyle={{ background: '#0f172a', border: '1px solid #1e293b', fontSize: 12 }}
                labelStyle={{ color: '#e2e8f0' }}
                formatter={(value) => fmtTonnes(typeof value === 'number' ? value : null)}
              />
              <Legend wrapperStyle={{ fontSize: 12 }} />
              <Bar dataKey="Actual" fill="#22d3ee" radius={[3, 3, 0, 0]}>
                {rows.map((row, i) => (
                  <Cell key={i} fillOpacity={row.isComplete ? 1 : 0.5} />
                ))}
              </Bar>
              <Bar dataKey="Planned" fill="#475569" radius={[3, 3, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      ) : (
        <p className="py-8 text-center text-sm text-slate-500">No shifts in this window.</p>
      )}

      {hasIncompleteShift && (
        <p className="mt-2 text-xs text-slate-500">* still running - its actual tonnes are partial, not a full-shift total.</p>
      )}
    </div>
  )
}
