import { useMeta, useBottlenecks } from '../api/hooks'
import { ApiError } from '../api/client'
import { BiggestLossesTable } from '../components/BiggestLossesTable'
import { OverBookTable } from '../components/OverBookTable'
import { QueueHotspots } from '../components/QueueHotspots'
import { RecoverablePanel } from '../components/RecoverablePanel'
import { StatusBanner } from '../components/StatusBanner'
import { UnderloadTable } from '../components/UnderloadTable'
import { WindowBar } from '../components/WindowBar'
import { useWindowState } from '../components/useWindowState'

interface LossesProps {
  live: boolean
}

export function Losses({ live }: LossesProps) {
  const [windowState, setWindowState] = useWindowState()
  const meta = useMeta(live)
  const bottlenecks = useBottlenecks(windowState, live)

  const bounds = meta.data ? { first: meta.data.from, last: meta.data.to } : undefined

  const params = new URLSearchParams()
  if (windowState.from) params.set('from', windowState.from)
  if (windowState.to) params.set('to', windowState.to)
  if (windowState.shift) params.set('shift', windowState.shift)
  const windowQuery = params.toString() ? `?${params.toString()}` : ''

  return (
    <div className="mx-auto flex max-w-7xl flex-col gap-4 px-4 py-6">
      <WindowBar state={windowState} onChange={setWindowState} bounds={bounds} />

      {bottlenecks.isLoading && <StatusBanner kind="loading" title="Loading losses…" />}

      {bottlenecks.isError && (
        <StatusBanner
          kind="error"
          title={bottlenecks.error instanceof ApiError ? bottlenecks.error.title : 'Failed to load losses'}
          detail={bottlenecks.error instanceof ApiError ? bottlenecks.error.detail : undefined}
        />
      )}

      {bottlenecks.data &&
        (bottlenecks.data.asOf === null ? (
          <StatusBanner kind="empty" title="No data in this window." detail="Try a different date range, or run the data generator." />
        ) : (
          <>
            <BiggestLossesTable items={bottlenecks.data.data.biggestLosses} windowQuery={windowQuery} />
            <RecoverablePanel data={bottlenecks.data.data.recoverable} windowQuery={windowQuery} />
            <OverBookTable data={bottlenecks.data.data.overBook} />
            <UnderloadTable data={bottlenecks.data.data.underload} windowQuery={windowQuery} />
            <QueueHotspots data={bottlenecks.data.data.hotspots} />
          </>
        ))}
    </div>
  )
}
