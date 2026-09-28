import { ApiError } from '../api/client'
import { useLoaders, useMeta } from '../api/hooks'
import { LoaderQueueScatter } from '../components/LoaderQueueScatter'
import { LoaderTable } from '../components/LoaderTable'
import { StatusBanner } from '../components/StatusBanner'
import { WindowBar } from '../components/WindowBar'
import { useWindowState } from '../components/useWindowState'

interface LoadersProps {
  live: boolean
}

export function Loaders({ live }: LoadersProps) {
  const [windowState, setWindowState] = useWindowState()
  const meta = useMeta(live)
  const loaders = useLoaders(windowState, live)

  const bounds = meta.data ? { first: meta.data.from, last: meta.data.to } : undefined

  return (
    <div className="mx-auto flex max-w-7xl flex-col gap-4 px-4 py-6">
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
