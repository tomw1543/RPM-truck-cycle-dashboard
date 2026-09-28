import { Bar, BarChart, CartesianGrid, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { ShortfallBuckets } from '../api/types'
import { fmtSignedTonnes, fmtTonnes } from '../lib/format'

interface ShortfallWaterfallProps {
  gap: number
  buckets: ShortfallBuckets
}

const BUCKETS: { key: keyof ShortfallBuckets; label: string }[] = [
  { key: 'payloadShort', label: 'Payload short' },
  { key: 'unplannedDowntime', label: 'Unplanned downtime' },
  { key: 'queueLoaderDelay', label: 'Queue - loader delay' },
  { key: 'queueOverTrucking', label: 'Queue - over-trucking' },
  { key: 'haulOverTarget', label: 'Haul over target' },
  { key: 'otherOverTarget', label: 'Load/dump/return over target' },
  { key: 'residual', label: 'Idle / unexplained' },
]

/** Window-level shortfall attribution: every bucket that decomposes the plan-vs-actual gap,
 * as a horizontal bar per bucket. Buckets are signed - a negative one (rendered in a different
 * color, labelled "gained" in its tooltip) means that bucket GAINED tonnes rather than losing
 * them, e.g. a truck loading faster than target. Bars sum exactly (to rounding) to the gap shown
 * in the heading. */
export function ShortfallWaterfall({ gap, buckets }: ShortfallWaterfallProps) {
  const rows = BUCKETS.map(({ key, label }) => ({ label, value: buckets[key] }))
  const hasData = rows.some((r) => r.value !== 0)

  return (
    <div className="rounded-lg border border-slate-800 bg-slate-900/60 p-4">
      <h2 className="mb-1 text-sm font-semibold text-slate-200">
        Shortfall attribution <span className="font-normal text-slate-500">(gap: {fmtTonnes(gap)})</span>
      </h2>
      <p className="mb-3 text-xs text-slate-500">
        Decomposes planned minus actual tonnes across complete shifts in the window. Bars sum to the gap above; a bar
        marked "gained" means that bucket added tonnes rather than costing them.
      </p>

      {hasData ? (
        <div className="h-64">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={rows} layout="vertical" margin={{ top: 4, right: 24, left: 0, bottom: 4 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#1e293b" horizontal={false} />
              <XAxis type="number" stroke="#64748b" fontSize={11} tickFormatter={(v) => fmtSignedTonnes(v)} />
              <YAxis type="category" dataKey="label" stroke="#64748b" fontSize={11} width={190} />
              <Tooltip
                contentStyle={{ background: '#0f172a', border: '1px solid #1e293b', fontSize: 12 }}
                labelStyle={{ color: '#e2e8f0' }}
                formatter={(value) => {
                  const v = typeof value === 'number' ? value : 0
                  return [`${fmtSignedTonnes(v)}${v < 0 ? ' (gained)' : ''}`, 'Tonnes']
                }}
              />
              <Bar dataKey="value" radius={[0, 3, 3, 0]}>
                {rows.map((row, i) => (
                  <Cell key={i} fill={row.value < 0 ? '#22c55e' : '#ef4444'} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        </div>
      ) : (
        <p className="py-8 text-center text-sm text-slate-500">No planned shifts in this window.</p>
      )}
    </div>
  )
}
