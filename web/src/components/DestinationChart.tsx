import { Bar, BarChart, CartesianGrid, LabelList, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { DestinationResult, PlanVsActual } from '../api/types'
import { fmtPercentFraction, fmtTonnes, fmtTonnesCompact } from '../lib/format'

interface DestinationChartProps {
  planVsActual: PlanVsActual
  from: string | null
  to: string | null
}

function toChartRow(d: DestinationResult) {
  return {
    destination: d.destination,
    Actual: d.actualTonnes,
    Planned: d.plannedTonnes ?? 0,
    percentOfPlan: d.percentOfPlan,
  }
}

function formatPercentLabel(value: unknown): string {
  return fmtPercentFraction(typeof value === 'number' ? value : null)
}

export function DestinationChart({ planVsActual, from, to }: DestinationChartProps) {
  const rows = planVsActual.byDestination.map(toChartRow)
  const hasData = rows.length > 0

  return (
    <div className="rounded-lg border border-slate-800 bg-slate-900/60 p-4">
      <div className="mb-3 flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-sm font-semibold text-slate-200">Tonnes by destination vs plan</h2>
        <span className="text-sm text-slate-400">
          Overall: {fmtTonnes(planVsActual.actualTonnes)} / {fmtTonnes(planVsActual.plannedTonnes)} (
          {fmtPercentFraction(planVsActual.percentOfPlan)} of plan)
        </span>
      </div>

      {hasData ? (
        <div className="h-64">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={rows} margin={{ top: 16, right: 8, left: 0, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#1e293b" />
              <XAxis dataKey="destination" stroke="#64748b" fontSize={12} />
              <YAxis stroke="#64748b" fontSize={12} tickFormatter={fmtTonnesCompact} />
              <Tooltip
                contentStyle={{ background: '#0f172a', border: '1px solid #1e293b', fontSize: 12 }}
                labelStyle={{ color: '#e2e8f0' }}
                formatter={(value) => fmtTonnes(typeof value === 'number' ? value : null)}
              />
              <Legend wrapperStyle={{ fontSize: 12 }} />
              <Bar dataKey="Actual" fill="#22d3ee" radius={[3, 3, 0, 0]}>
                <LabelList dataKey="percentOfPlan" position="top" formatter={formatPercentLabel} fill="#94a3b8" fontSize={11} />
              </Bar>
              <Bar dataKey="Planned" fill="#475569" radius={[3, 3, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      ) : (
        <p className="py-8 text-center text-sm text-slate-500">No cycles in this window.</p>
      )}

      <p className="mt-2 text-xs text-slate-500">
        Complete shifts only, on whole calendar dates from {from ?? '—'} to {to ?? '—'} (
        {planVsActual.excludedShiftCount} truck-shift{planVsActual.excludedShiftCount === 1 ? '' : 's'} excluded).
        The Tonnes tile covers the rolling window, so its total can differ.
      </p>
    </div>
  )
}
