import { CartesianGrid, Legend, ResponsiveContainer, Scatter, ScatterChart, Tooltip, XAxis, YAxis, ZAxis } from 'recharts'
import type { LoaderShift } from '../api/types'
import { fmtNumber, fmtPercentFraction } from '../lib/format'

interface LoaderQueueScatterProps {
  shifts: LoaderShift[]
}

const COLORS = ['#22d3ee', '#a855f7', '#f59e0b', '#3b82f6', '#ef4444', '#22c55e']

/** Average queue (y) vs utilisation (x) per loader-shift, one series per loader. Top right
 * (high utilisation, high queue) is where a loader is over-trucked - more trucks are assigned
 * to it than it can clear without a queue building up. */
export function LoaderQueueScatter({ shifts }: LoaderQueueScatterProps) {
  const byLoader = new Map<string, LoaderShift[]>()
  for (const s of shifts) {
    if (s.trucks === 0) continue
    if (!byLoader.has(s.loaderName)) byLoader.set(s.loaderName, [])
    byLoader.get(s.loaderName)!.push(s)
  }
  const loaderNames = [...byLoader.keys()].sort()
  const hasData = loaderNames.length > 0

  return (
    <div className="rounded-lg border border-slate-800 bg-slate-900/60 p-4">
      <h2 className="mb-1 text-sm font-semibold text-slate-200">Queue vs utilisation</h2>
      <p className="mb-3 text-xs text-slate-500">
        Top right (high utilisation, high average queue) is where a loader is over-trucked: more trucks assigned than
        it can clear without a backlog.
      </p>

      {hasData ? (
        <div className="h-80">
          <ResponsiveContainer width="100%" height="100%">
            <ScatterChart margin={{ top: 8, right: 20, left: 0, bottom: 4 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#1e293b" />
              <XAxis
                type="number"
                dataKey="utilisation"
                name="Utilisation"
                domain={[0, 'dataMax']}
                stroke="#64748b"
                fontSize={11}
                tickFormatter={(v) => fmtPercentFraction(v)}
              />
              <YAxis type="number" dataKey="avgQueueMin" name="Avg queue" stroke="#64748b" fontSize={11} tickFormatter={(v) => fmtNumber(v, 0)} />
              <ZAxis type="number" dataKey="trucks" range={[40, 200]} name="Trucks" />
              <Tooltip
                cursor={{ strokeDasharray: '3 3' }}
                contentStyle={{ background: '#0f172a', border: '1px solid #1e293b', fontSize: 12 }}
                labelStyle={{ color: '#e2e8f0' }}
                formatter={(value, name) => {
                  if (name === 'Utilisation') return [fmtPercentFraction(typeof value === 'number' ? value : null), name]
                  if (name === 'Avg queue') return [`${fmtNumber(typeof value === 'number' ? value : null)} min`, name]
                  return [value, name]
                }}
              />
              <Legend wrapperStyle={{ fontSize: 12 }} />
              {loaderNames.map((name, i) => (
                <Scatter key={name} name={name} data={byLoader.get(name)} fill={COLORS[i % COLORS.length]} />
              ))}
            </ScatterChart>
          </ResponsiveContainer>
        </div>
      ) : (
        <p className="py-8 text-center text-sm text-slate-500">No loader-shifts with trucks in this window.</p>
      )}
    </div>
  )
}
