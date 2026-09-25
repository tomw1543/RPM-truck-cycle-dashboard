import { Bar, BarChart, CartesianGrid, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { DelayReason } from '../api/types'
import { fmtNumber } from '../lib/format'

interface DelayReasonChartProps {
  delaysByReason: DelayReason[]
}

const PLANNED_COLOR = '#3b82f6'
const UNPLANNED_COLOR = '#ef4444'

export function DelayReasonChart({ delaysByReason }: DelayReasonChartProps) {
  const rows = delaysByReason.map((d) => ({
    reason: d.reason,
    minutes: d.minutes,
    count: d.count,
    isPlanned: d.isPlanned,
  }))
  const hasData = rows.length > 0

  return (
    <div className="rounded-lg border border-slate-800 bg-slate-900/60 p-4">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-sm font-semibold text-slate-200">Delay minutes by reason</h2>
        <div className="flex items-center gap-3 text-xs text-slate-400">
          <span className="flex items-center gap-1.5">
            <span className="h-2 w-2 rounded-sm" style={{ backgroundColor: PLANNED_COLOR }} />
            Planned
          </span>
          <span className="flex items-center gap-1.5">
            <span className="h-2 w-2 rounded-sm" style={{ backgroundColor: UNPLANNED_COLOR }} />
            Unplanned
          </span>
        </div>
      </div>

      {hasData ? (
        <div style={{ height: Math.max(160, rows.length * 36) }}>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={rows} layout="vertical" margin={{ top: 4, right: 24, left: 8, bottom: 4 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#1e293b" horizontal={false} />
              <XAxis type="number" stroke="#64748b" fontSize={12} tickFormatter={(v) => fmtNumber(v, 0)} />
              <YAxis type="category" dataKey="reason" stroke="#64748b" fontSize={12} width={160} />
              <Tooltip
                contentStyle={{ background: '#0f172a', border: '1px solid #1e293b', fontSize: 12 }}
                labelStyle={{ color: '#e2e8f0' }}
                formatter={(value, _name, item) => [
                  `${fmtNumber(typeof value === 'number' ? value : null, 0)} min (${item?.payload?.count ?? 0} delays)`,
                  item?.payload?.isPlanned ? 'Planned' : 'Unplanned',
                ]}
              />
              <Bar dataKey="minutes" radius={[0, 3, 3, 0]}>
                {rows.map((row) => (
                  <Cell key={row.reason} fill={row.isPlanned ? PLANNED_COLOR : UNPLANNED_COLOR} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        </div>
      ) : (
        <p className="py-8 text-center text-sm text-slate-500">No delays in this window.</p>
      )}
    </div>
  )
}
