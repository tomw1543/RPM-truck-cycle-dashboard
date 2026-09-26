import { NavLink } from 'react-router'
import { formatAsOf } from '../lib/format'

interface HeaderProps {
  asOf: string | null | undefined
  live: boolean
  onLiveChange: (live: boolean) => void
}

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  `px-3 py-1.5 text-sm rounded-md transition-colors ${
    isActive ? 'bg-slate-800 text-cyan-300' : 'text-slate-400 hover:text-slate-200'
  }`

export function Header({ asOf, live, onLiveChange }: HeaderProps) {
  return (
    <header className="border-b border-slate-800 bg-slate-950">
      <div className="mx-auto flex max-w-7xl flex-wrap items-center gap-4 px-4 py-3">
        <div className="flex items-baseline gap-2">
          <h1 className="text-base font-semibold tracking-tight text-slate-100">HaulCycle Insights</h1>
          <span className="text-xs text-slate-500">As of {formatAsOf(asOf ?? null)}</span>
        </div>

        <nav className="flex gap-1">
          <NavLink to="/" end className={navLinkClass}>
            Overview
          </NavLink>
          <NavLink to="/trucks" className={navLinkClass}>
            Trucks
          </NavLink>
          <NavLink to="/losses" className={navLinkClass}>
            Losses
          </NavLink>
          <NavLink to="/schedule" className={navLinkClass}>
            Schedule
          </NavLink>
        </nav>

        <label className="ml-auto flex items-center gap-2 text-sm text-slate-400">
          <span>Live</span>
          <button
            type="button"
            role="switch"
            aria-checked={live}
            onClick={() => onLiveChange(!live)}
            className={`relative h-5 w-9 rounded-full transition-colors ${live ? 'bg-cyan-600' : 'bg-slate-700'}`}
          >
            <span
              className={`absolute left-0 top-0.5 h-4 w-4 rounded-full bg-slate-100 transition-transform ${
                live ? 'translate-x-4' : 'translate-x-0.5'
              }`}
            />
          </button>
        </label>
      </div>
    </header>
  )
}
