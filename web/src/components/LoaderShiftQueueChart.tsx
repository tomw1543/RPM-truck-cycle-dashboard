import { ResponsiveContainer, ScatterChart, Scatter, XAxis, YAxis, Tooltip, ReferenceLine, ReferenceArea } from 'recharts'
import type { LoaderArrival, LoaderDelayWindow } from '../api/types'

export interface LoaderShiftQueueChartProps {
  loaderName: string
  arrivals: LoaderArrival[]
  delays: LoaderDelayWindow[]
  shiftStart: Date
  shiftEnd: Date
}

function minutesFrom(base: Date, iso: string): number {
  const ms = new Date(iso).getTime() - base.getTime()
  return ms / 60000
}

function minutesFromDate(base: Date, d: Date): number {
  return (d.getTime() - base.getTime()) / 60000
}

function fmtOffsetMin(base: Date, offsetMin: number): string {
  const d = new Date(base.getTime() + offsetMin * 60000)
  const h = d.getHours().toString().padStart(2, '0')
  const m = d.getMinutes().toString().padStart(2, '0')
  return `${h}:${m}`
}

interface DotPayload {
  truckName: string
  x: number
  y: number
}

function CustomTooltip({ active, payload }: { active?: boolean; payload?: { payload: DotPayload }[] }) {
  if (!active || !payload?.length) return null
  const d = payload[0].payload
  return (
    <div className="rounded border border-slate-700 bg-slate-900 px-2 py-1 text-xs text-slate-200">
      <p className="font-semibold">{d.truckName}</p>
      <p>Queue: {d.y.toFixed(1)} min</p>
    </div>
  )
}

export function LoaderShiftQueueChart({ loaderName, arrivals, delays, shiftStart, shiftEnd }: LoaderShiftQueueChartProps) {
  const maxMin = minutesFromDate(shiftStart, shiftEnd)

  const dots = arrivals.map((a) => ({
    x: minutesFrom(shiftStart, a.arrivalTime),
    y: Number(a.queueMin),
    truckName: a.truckName,
  }))

  return (
    <div>
      <p className="mb-1 text-xs font-semibold text-slate-300">{loaderName}</p>
      <ResponsiveContainer width="100%" height={200}>
        <ScatterChart margin={{ top: 4, right: 16, bottom: 4, left: 40 }}>
          <XAxis
            type="number"
            dataKey="x"
            domain={[0, maxMin]}
            tickCount={7}
            tickFormatter={(v: number) => fmtOffsetMin(shiftStart, v)}
            tick={{ fontSize: 11, fill: '#94a3b8' }}
            stroke="#334155"
          />
          <YAxis
            type="number"
            dataKey="y"
            domain={[0, 'auto']}
            label={{ value: 'Queue (min)', angle: -90, position: 'insideLeft', offset: -28, style: { fontSize: 11, fill: '#94a3b8' } }}
            tick={{ fontSize: 11, fill: '#94a3b8' }}
            stroke="#334155"
          />
          <Tooltip content={<CustomTooltip />} />
          {delays.map((d, i) => (
            <ReferenceArea
              key={i}
              x1={minutesFrom(shiftStart, d.start)}
              x2={minutesFrom(shiftStart, d.end)}
              fill={d.rateFactor === 0 ? '#7f1d1d' : '#78350f'}
              fillOpacity={0.3}
              strokeOpacity={0}
            />
          ))}
          <ReferenceLine
            y={0.8}
            stroke="#475569"
            strokeDasharray="4 2"
            label={{ value: 'target', position: 'right', style: { fontSize: 10, fill: '#64748b' } }}
          />
          <Scatter data={dots} fill="#22d3ee" r={4} />
        </ScatterChart>
      </ResponsiveContainer>
    </div>
  )
}
