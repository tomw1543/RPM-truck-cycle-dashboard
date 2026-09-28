import type { LoaderShiftQueueData } from '../api/types'
import { LoaderShiftQueueChart } from './LoaderShiftQueueChart'

interface LoaderShiftQueuePanelProps {
  data: LoaderShiftQueueData
  shiftDate: string   // "YYYY-MM-DD"
  shiftName: string   // "Day" | "Night"
}

function shiftBounds(shiftDate: string, shiftName: string): { start: Date; end: Date } {
  if (shiftName === 'Day') {
    return {
      start: new Date(`${shiftDate}T06:00:00`),
      end: new Date(`${shiftDate}T18:00:00`),
    }
  }
  const nextDate = new Date(`${shiftDate}T00:00:00`)
  nextDate.setDate(nextDate.getDate() + 1)
  const nextStr = nextDate.toISOString().slice(0, 10)
  return {
    start: new Date(`${shiftDate}T18:00:00`),
    end: new Date(`${nextStr}T06:00:00`),
  }
}

export function LoaderShiftQueuePanel({ data, shiftDate, shiftName }: LoaderShiftQueuePanelProps) {
  const { start, end } = shiftBounds(shiftDate, shiftName)
  const sorted = [...data.loaders].sort((a, b) => a.loaderName.localeCompare(b.loaderName))

  return (
    <div className="rounded-lg border border-slate-800 bg-slate-900/60 p-4">
      <h2 className="mb-1 text-sm font-semibold text-slate-200">Loader queues — {shiftDate} {shiftName}</h2>
      <div className="mb-3 flex items-center gap-4 text-xs text-slate-400">
        <span className="flex items-center gap-1">
          <span className="inline-block h-3 w-3 rounded-sm opacity-60" style={{ background: '#7f1d1d' }} />
          Handover (stopped)
        </span>
        <span className="flex items-center gap-1">
          <span className="inline-block h-3 w-3 rounded-sm opacity-60" style={{ background: '#78350f' }} />
          Half-speed spike
        </span>
      </div>
      <div className="flex flex-col gap-6">
        {sorted.map((loader) => (
          <LoaderShiftQueueChart
            key={loader.loaderName}
            loaderName={loader.loaderName}
            arrivals={loader.arrivals}
            delays={loader.delays}
            shiftStart={start}
            shiftEnd={end}
          />
        ))}
      </div>
    </div>
  )
}
