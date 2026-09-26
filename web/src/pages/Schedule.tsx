import { useMeta, useScheduleCompliance } from '../api/hooks'
import { ApiError } from '../api/client'
import { ComplianceSummaryTiles } from '../components/ComplianceSummaryTiles'
import { ComplianceTable } from '../components/ComplianceTable'
import { ScheduleChart } from '../components/ScheduleChart'
import { StatusBanner } from '../components/StatusBanner'
import { WindowBar } from '../components/WindowBar'
import { useWindowState } from '../components/useWindowState'
import { fmtPercentFraction } from '../lib/format'

interface ScheduleProps {
  live: boolean
}

export function Schedule({ live }: ScheduleProps) {
  const [windowState, setWindowState] = useWindowState()
  const meta = useMeta(live)
  const compliance = useScheduleCompliance(windowState, live)

  const bounds = meta.data ? { first: meta.data.from, last: meta.data.to } : undefined

  const params = new URLSearchParams()
  if (windowState.from) params.set('from', windowState.from)
  if (windowState.to) params.set('to', windowState.to)
  if (windowState.shift) params.set('shift', windowState.shift)
  const windowQuery = params.toString() ? `?${params.toString()}` : ''

  const baseline = compliance.data?.data.summary.baseline ?? null

  return (
    <div className="mx-auto flex max-w-7xl flex-col gap-4 px-4 py-6">
      <WindowBar state={windowState} onChange={setWindowState} bounds={bounds} />

      <p className="text-xs text-slate-500">
        Plans use book rates (full payload, minimal queue), so actual tonnes typically run below
        plan{baseline !== null ? `: ${fmtPercentFraction(baseline)} over this window.` : '.'}
      </p>

      {compliance.isLoading && <StatusBanner kind="loading" title="Loading schedule compliance…" />}

      {compliance.isError && (
        <StatusBanner
          kind="error"
          title={compliance.error instanceof ApiError ? compliance.error.title : 'Failed to load schedule compliance'}
          detail={compliance.error instanceof ApiError ? compliance.error.detail : undefined}
        />
      )}

      {compliance.data &&
        (compliance.data.asOf === null ? (
          <StatusBanner kind="empty" title="No data in this window." detail="Try a different date range, or run the data generator." />
        ) : (
          <>
            <ComplianceSummaryTiles summary={compliance.data.data.summary} />
            <ScheduleChart shifts={compliance.data.data.shifts} />
            <ComplianceTable shifts={compliance.data.data.shifts} windowQuery={windowQuery} />
          </>
        ))}
    </div>
  )
}
