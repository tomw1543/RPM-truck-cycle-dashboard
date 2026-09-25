import { useParams } from 'react-router'
import { ApiError } from '../api/client'
import { useTruckDetail } from '../api/hooks'
import { DelayReasonChart } from '../components/DelayReasonChart'
import { KpiTile } from '../components/KpiTile'
import { PhaseSplitBar } from '../components/PhaseSplitBar'
import { ShiftTonnesChart } from '../components/ShiftTonnesChart'
import { StatusBanner } from '../components/StatusBanner'
import { WindowBar } from '../components/WindowBar'
import { useWindowState } from '../components/useWindowState'
import { fmtInt, fmtNumber, fmtPercentFraction, fmtTonnes } from '../lib/format'

interface TruckDetailProps {
  live: boolean
}

export function TruckDetail({ live }: TruckDetailProps) {
  const { name = '' } = useParams<{ name: string }>()
  const [windowState, setWindowState] = useWindowState()
  const detail = useTruckDetail(name, windowState, live)

  const notFound = detail.error instanceof ApiError && detail.error.status === 404

  return (
    <div className="mx-auto flex max-w-7xl flex-col gap-4 px-4 py-6">
      <WindowBar state={windowState} onChange={setWindowState} />

      {detail.isLoading && <StatusBanner kind="loading" title={`Loading ${name}…`} />}

      {detail.isError && notFound && <StatusBanner kind="empty" title={`No truck named ${name}`} />}

      {detail.isError && !notFound && (
        <StatusBanner
          kind="error"
          title={detail.error instanceof ApiError ? detail.error.title : 'Failed to load truck detail'}
          detail={detail.error instanceof ApiError ? detail.error.detail : undefined}
        />
      )}

      {detail.data &&
        (detail.data.asOf === null || detail.data.data.truck.cycles === 0 ? (
          <StatusBanner kind="empty" title="No data in this window." detail="Try a different date range, or run the data generator." />
        ) : (
          <>
            <h2 className="text-lg font-semibold text-slate-100">{detail.data.data.truck.name}</h2>

            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-8">
              <KpiTile
                label="Cycles"
                value={fmtInt(detail.data.data.truck.cycles)}
                sublabel={`Fleet ${fmtInt(detail.data.data.fleet.cycles)}`}
              />
              <KpiTile
                label="Tonnes"
                value={fmtTonnes(detail.data.data.truck.tonnes)}
                sublabel={`Fleet ${fmtTonnes(detail.data.data.fleet.tonnes)}`}
              />
              <KpiTile
                label="t / operating hr"
                value={fmtNumber(detail.data.data.truck.tonnesPerOperatingHour)}
                sublabel={`Fleet ${fmtNumber(detail.data.data.fleet.tonnesPerOperatingHour)}`}
              />
              <KpiTile
                label="Avg cycle"
                value={`${fmtNumber(detail.data.data.truck.averageCycleMin)} min`}
                sublabel={`Fleet ${fmtNumber(detail.data.data.fleet.averageCycleMin)} min`}
              />
              <KpiTile
                label="Avg payload %"
                value={fmtNumber(detail.data.data.truck.averagePayloadPercent, 0)}
                sublabel={`Fleet ${fmtNumber(detail.data.data.fleet.averagePayloadPercent, 0)}`}
              />
              <KpiTile
                label="Cycles / op hr"
                value={fmtNumber(detail.data.data.truck.cyclesPerOperatingHour, 2)}
                sublabel={`Fleet ${fmtNumber(detail.data.data.fleet.cyclesPerOperatingHour, 2)}`}
              />
              <KpiTile
                label="Availability"
                value={fmtPercentFraction(detail.data.data.truck.availability)}
                sublabel={`Fleet ${fmtPercentFraction(detail.data.data.fleet.availability)}`}
              />
              <KpiTile
                label="Utilisation"
                value={fmtPercentFraction(detail.data.data.truck.utilisation)}
                sublabel={`Fleet ${fmtPercentFraction(detail.data.data.fleet.utilisation)}`}
              />
            </div>

            <ShiftTonnesChart shifts={detail.data.data.shifts} />

            <DelayReasonChart delaysByReason={detail.data.data.delaysByReason} />

            <PhaseSplitBar phaseSplit={detail.data.data.phaseSplit} averageCycleMin={detail.data.data.truck.averageCycleMin} />
          </>
        ))}
    </div>
  )
}
