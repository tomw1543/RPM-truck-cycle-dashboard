import type { Shift } from '../api/types'
import type { WindowState } from './useWindowState'

interface WindowBarProps {
  state: WindowState
  onChange: (next: WindowState) => void
  bounds?: { first: string | null; last: string | null }
}

export function WindowBar({ state, onChange, bounds }: WindowBarProps) {
  const isCustom = Boolean(state.from || state.to)

  return (
    <div className="flex flex-wrap items-center gap-3 rounded-lg border border-slate-800 bg-slate-900/40 px-4 py-3">
      <div className="flex gap-1">
        <button
          type="button"
          onClick={() => onChange({ shift: state.shift })}
          className={`rounded-md px-3 py-1.5 text-sm ${
            !isCustom ? 'bg-cyan-600 text-white' : 'bg-slate-800 text-slate-300 hover:bg-slate-700'
          }`}
        >
          Last 7 days
        </button>
        <button
          type="button"
          onClick={() => {
            if (!isCustom) {
              const last = bounds?.last ?? undefined
              onChange({ from: last, to: last, shift: state.shift })
            }
          }}
          className={`rounded-md px-3 py-1.5 text-sm ${
            isCustom ? 'bg-cyan-600 text-white' : 'bg-slate-800 text-slate-300 hover:bg-slate-700'
          }`}
        >
          Custom range
        </button>
      </div>

      {isCustom && (
        <div className="flex items-center gap-2 text-sm text-slate-400">
          <label className="flex items-center gap-1">
            From
            <input
              type="date"
              value={state.from ?? ''}
              min={bounds?.first ?? undefined}
              max={bounds?.last ?? undefined}
              onChange={(e) => onChange({ ...state, from: e.target.value || undefined })}
              className="rounded border border-slate-700 bg-slate-950 px-2 py-1 text-slate-200"
            />
          </label>
          <label className="flex items-center gap-1">
            To
            <input
              type="date"
              value={state.to ?? ''}
              min={bounds?.first ?? undefined}
              max={bounds?.last ?? undefined}
              onChange={(e) => onChange({ ...state, to: e.target.value || undefined })}
              className="rounded border border-slate-700 bg-slate-950 px-2 py-1 text-slate-200"
            />
          </label>
        </div>
      )}

      <label className="ml-auto flex items-center gap-2 text-sm text-slate-400">
        Shift
        <select
          value={state.shift ?? ''}
          onChange={(e) => onChange({ ...state, shift: (e.target.value || undefined) as Shift | undefined })}
          className="rounded border border-slate-700 bg-slate-950 px-2 py-1 text-slate-200"
        >
          <option value="">All</option>
          <option value="Day">Day</option>
          <option value="Night">Night</option>
        </select>
      </label>
    </div>
  )
}
