import { useMeta, useFleetSummary } from '../api/hooks'
import { ApiError } from '../api/client'
import { DestinationChart } from '../components/DestinationChart'
import { KpiTile } from '../components/KpiTile'
import { PhaseSplitBar } from '../components/PhaseSplitBar'
import { StatusBanner } from '../components/StatusBanner'
import { WindowBar } from '../components/WindowBar'
import { useWindowState } from '../components/useWindowState'
import { fmtInt, fmtNumber, fmtPercentFraction, fmtRatio, fmtTonnes } from '../lib/format'

interface OverviewProps {
  live: boolean
}

export function Overview({ live }: OverviewProps) {
  const [windowState, setWindowState] = useWindowState()
  const meta = useMeta(live)
  const fleetSummary = useFleetSummary(windowState, live)

  const bounds = meta.data
    ? { first: meta.data.from, last: meta.data.to }
    : undefined

  return (
    <div className="mx-auto flex max-w-7xl flex-col gap-4 px-4 py-6">
      <WindowBar state={windowState} onChange={setWindowState} bounds={bounds} />

      {fleetSummary.isLoading && <StatusBanner kind="loading" title="Loading fleet summary…" />}

      {fleetSummary.isError && (
        <StatusBanner
          kind="error"
          title={fleetSummary.error instanceof ApiError ? fleetSummary.error.title : 'Failed to load fleet summary'}
          detail={fleetSummary.error instanceof ApiError ? fleetSummary.error.detail : undefined}
        />
      )}

      {fleetSummary.data && (
        fleetSummary.data.asOf === null || fleetSummary.data.data.cycles === 0 ? (
          <StatusBanner kind="empty" title="No data in this window." detail="Try a different date range, or run the data generator." />
        ) : (
          <>
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-8">
              <KpiTile label="Cycles" value={fmtInt(fleetSummary.data.data.cycles)} />
              <KpiTile label="Tonnes" value={fmtTonnes(fleetSummary.data.data.tonnes)} />
              <KpiTile
                label="t / operating hr"
                value={fmtNumber(fleetSummary.data.data.tonnesPerOperatingHour)}
              />
              <KpiTile label="Avg cycle" value={`${fmtNumber(fleetSummary.data.data.averageCycleMin)} min`} />
              <KpiTile label="Availability" value={fmtPercentFraction(fleetSummary.data.data.availability)} />
              <KpiTile label="Utilisation" value={fmtPercentFraction(fleetSummary.data.data.utilisation)} />
              <KpiTile label="Match factor" value={fmtRatio(fleetSummary.data.data.matchFactor)} />
              <KpiTile label="Idle %" value={fmtPercentFraction(fleetSummary.data.data.idlePercent)} />
            </div>

            <DestinationChart
              planVsActual={fleetSummary.data.data.planVsActual}
              from={fleetSummary.data.from}
              to={fleetSummary.data.to}
            />

            <PhaseSplitBar
              phaseSplit={fleetSummary.data.data.phaseSplit}
              averageCycleMin={fleetSummary.data.data.averageCycleMin}
            />
          </>
        )
      )}
    </div>
  )
}
