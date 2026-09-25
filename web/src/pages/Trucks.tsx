import { useMeta, useTrucks } from '../api/hooks'
import { ApiError } from '../api/client'
import { StatusBanner } from '../components/StatusBanner'
import { TrucksTable } from '../components/TrucksTable'
import { WindowBar } from '../components/WindowBar'
import { useWindowState } from '../components/useWindowState'

interface TrucksProps {
  live: boolean
}

export function Trucks({ live }: TrucksProps) {
  const [windowState, setWindowState] = useWindowState()
  const meta = useMeta(live)
  const trucks = useTrucks(windowState, live)

  const bounds = meta.data ? { first: meta.data.from, last: meta.data.to } : undefined

  const params = new URLSearchParams()
  if (windowState.from) params.set('from', windowState.from)
  if (windowState.to) params.set('to', windowState.to)
  if (windowState.shift) params.set('shift', windowState.shift)
  const windowQuery = params.toString() ? `?${params.toString()}` : ''

  return (
    <div className="mx-auto flex max-w-7xl flex-col gap-4 px-4 py-6">
      <WindowBar state={windowState} onChange={setWindowState} bounds={bounds} />

      {trucks.isLoading && <StatusBanner kind="loading" title="Loading trucks…" />}

      {trucks.isError && (
        <StatusBanner
          kind="error"
          title={trucks.error instanceof ApiError ? trucks.error.title : 'Failed to load trucks'}
          detail={trucks.error instanceof ApiError ? trucks.error.detail : undefined}
        />
      )}

      {trucks.data &&
        (trucks.data.asOf === null || trucks.data.data.trucks.every((t) => t.cycles === 0) ? (
          <StatusBanner kind="empty" title="No data in this window." detail="Try a different date range, or run the data generator." />
        ) : (
          <TrucksTable fleet={trucks.data.data.fleet} trucks={trucks.data.data.trucks} windowQuery={windowQuery} />
        ))}
    </div>
  )
}
