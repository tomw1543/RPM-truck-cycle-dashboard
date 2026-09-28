import { useEffect } from 'react'
import { useSearchParams } from 'react-router'
import { ApiError } from '../api/client'
import { useLoaders, useLoaderShiftQueue, useMeta } from '../api/hooks'
import { LoaderQueueScatter } from '../components/LoaderQueueScatter'
import { LoaderShiftQueuePanel } from '../components/LoaderShiftQueuePanel'
import { LoaderTable } from '../components/LoaderTable'
import { StatusBanner } from '../components/StatusBanner'
import { WindowBar } from '../components/WindowBar'
import { useWindowState } from '../components/useWindowState'

interface LoadersProps {
  live: boolean
}

export function Loaders({ live }: LoadersProps) {
  const [windowState, setWindowState] = useWindowState()
  const [searchParams, setSearchParams] = useSearchParams()
  const meta = useMeta(live)
  const loaders = useLoaders(windowState, live)

  const shiftDate = searchParams.get('shiftDate') ?? undefined
  const shiftName = searchParams.get('shiftName') ?? undefined

  // Default to the most recent complete shift from the aggregate data once loaded.
  useEffect(() => {
    if (shiftDate || shiftName) return
    if (!loaders.data?.data?.shifts) return
    const first = loaders.data.data.shifts.find((s) => s.isComplete)
    if (!first) return
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        next.set('shiftDate', first.shiftDate)
        next.set('shiftName', first.shiftName)
        return next
      },
      { replace: true },
    )
  }, [loaders.data, shiftDate, shiftName, setSearchParams])

  const shiftQueue = useLoaderShiftQueue(shiftDate, shiftName, live)

  const bounds = meta.data ? { first: meta.data.from, last: meta.data.to } : undefined

  // Build a sorted list of distinct shift dates for the picker (from the loaded shifts).
  const shiftOptions: { shiftDate: string; shiftName: string }[] = loaders.data?.data?.shifts
    ? [...new Map(
        loaders.data.data.shifts.map((s) => [`${s.shiftDate}-${s.shiftName}`, s]),
      ).values()].map((s) => ({ shiftDate: s.shiftDate, shiftName: s.shiftName }))
    : []

  function handleShiftDateChange(e: React.ChangeEvent<HTMLSelectElement>) {
    const val = e.target.value
    if (!val) return
    const [date, name] = val.split('|')
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        next.set('shiftDate', date)
        next.set('shiftName', name)
        return next
      },
      { replace: true },
    )
  }

  const selectedValue = shiftDate && shiftName ? `${shiftDate}|${shiftName}` : ''

  return (
    <div className="mx-auto flex max-w-7xl flex-col gap-4 px-4 py-6">
      <h1 className="text-base font-semibold text-slate-100">Loaders</h1>

      {/* Shift picker */}
      <div className="flex items-center gap-3">
        <label className="text-xs text-slate-400" htmlFor="loader-shift-picker">
          Shift
        </label>
        <select
          id="loader-shift-picker"
          value={selectedValue}
          onChange={handleShiftDateChange}
          className="rounded border border-slate-700 bg-slate-800 px-2 py-1 text-xs text-slate-200 focus:outline-none focus:ring-1 focus:ring-cyan-600"
        >
          {shiftOptions.length === 0 && <option value="">Loading…</option>}
          {shiftOptions.map((s) => (
            <option key={`${s.shiftDate}|${s.shiftName}`} value={`${s.shiftDate}|${s.shiftName}`}>
              {s.shiftDate} {s.shiftName}
            </option>
          ))}
        </select>
      </div>

      {/* Per-shift queue chart */}
      {shiftQueue.isLoading && <StatusBanner kind="loading" title="Loading shift queue data…" />}
      {shiftQueue.isError && (
        <StatusBanner
          kind="error"
          title={shiftQueue.error instanceof ApiError ? shiftQueue.error.title : 'Failed to load shift queue data'}
          detail={shiftQueue.error instanceof ApiError ? shiftQueue.error.detail : undefined}
        />
      )}
      {shiftQueue.data && shiftDate && shiftName && (
        <LoaderShiftQueuePanel data={shiftQueue.data} shiftDate={shiftDate} shiftName={shiftName} />
      )}

      <WindowBar state={windowState} onChange={setWindowState} bounds={bounds} />

      {loaders.isLoading && <StatusBanner kind="loading" title="Loading loader stats…" />}

      {loaders.isError && (
        <StatusBanner
          kind="error"
          title={loaders.error instanceof ApiError ? loaders.error.title : 'Failed to load loader stats'}
          detail={loaders.error instanceof ApiError ? loaders.error.detail : undefined}
        />
      )}

      {loaders.data &&
        (loaders.data.asOf === null ? (
          <StatusBanner kind="empty" title="No data in this window." detail="Try a different date range, or run the data generator." />
        ) : (
          <>
            <LoaderQueueScatter shifts={loaders.data.data.shifts} />
            <LoaderTable shifts={loaders.data.data.shifts} />
          </>
        ))}
    </div>
  )
}
