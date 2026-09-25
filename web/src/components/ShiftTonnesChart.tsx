import { Bar, BarChart, CartesianGrid, LabelList, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { ShiftSeries } from '../api/types'
import { fmtTonnes, fmtTonnesCompact } from '../lib/format'

interface ShiftTonnesChartProps {
  shifts: ShiftSeries[]
}

function shiftLabel(s: ShiftSeries): string {
  const [, month, day] = s.shiftDate.split('-')
  return `${day}/${month} ${s.shiftName === 'Day' ? 'D' : 'N'}`
}

function toChartRow(s: ShiftSeries) {
  return {
    label: shiftLabel(s),
    Actual: s.actualTonnes,
    Planned: s.plannedTonnes ?? 0,
    unavailableReason: s.unavailableReason,
  }
}

function unavailableLabel(entry: unknown): string {
  const row = entry as { unavailableReason?: string | null } | undefined
  return row?.unavailableReason ?? ''
}

export function ShiftTonnesChart({ shifts }: ShiftTonnesChartProps) {
  const rows = shifts.map(toChartRow)
  const hasData = rows.length > 0
  const unavailableShifts = shifts.filter((s) => s.unavailableReason)

  return (
    <div className="rounded-lg border border-slate-800 bg-slate-900/60 p-4">
      <h2 className="mb-3 text-sm font-semibold text-slate-200">Planned vs actual tonnes per shift</h2>

      {hasData ? (
        <div className="h-64">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={rows} margin={{ top: 16, right: 8, left: 0, bottom: 0 }}>
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
                <LabelList dataKey="unavailableReason" position="top" formatter={unavailableLabel} fill="#f97316" fontSize={10} />
              </Bar>
              <Bar dataKey="Planned" fill="#475569" radius={[3, 3, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      ) : (
        <p className="py-8 text-center text-sm text-slate-500">No shifts in this window.</p>
      )}

      {unavailableShifts.length > 0 && (
        <p className="mt-2 text-xs text-orange-400/80">
          Unavailable: {unavailableShifts.map((s) => `${shiftLabel(s)} (${s.unavailableReason})`).join(', ')}
        </p>
      )}
    </div>
  )
}
