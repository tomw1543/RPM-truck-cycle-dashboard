import type { OptimiserPlanGain } from '../api/types'
import { KpiTile } from './KpiTile'
import { fmtInt, fmtSignedHours, fmtSignedLitres, fmtSignedTonnes, fmtTonnes } from '../lib/format'

interface OptimiserHeadlineTilesProps {
  shiftsOptimised: number
  shiftsNotOptimised: number
  moreOutput: OptimiserPlanGain
  leaner: OptimiserPlanGain
}

function PlanGainTiles({ label, gain }: { label: string; gain: OptimiserPlanGain }) {
  return (
    <div className="flex flex-col gap-2">
      <h3 className="text-xs font-semibold uppercase tracking-wide text-slate-400">{label}</h3>
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
        <KpiTile
          label="Tonnes gained"
          value={fmtSignedTonnes(gain.tonnesGainedMean)}
          sublabel={`range ${fmtTonnes(gain.tonnesGainedMin)} to ${fmtTonnes(gain.tonnesGainedMax)}`}
        />
        <KpiTile label="Queue hours saved" value={fmtSignedHours(gain.queueHoursSaved)} />
        <KpiTile label="Fuel saved" value={fmtSignedLitres(gain.fuelLitresSaved)} />
        <KpiTile label="Truck-hours saved" value={fmtSignedHours(gain.truckHoursSaved)} />
        <KpiTile label="Trucks stood down" value={fmtInt(gain.trucksStoodDown)} />
      </div>
    </div>
  )
}

export function OptimiserHeadlineTiles({ shiftsOptimised, shiftsNotOptimised, moreOutput, leaner }: OptimiserHeadlineTilesProps) {
  return (
    <div className="flex flex-col gap-4 rounded-lg border border-slate-800 bg-slate-900/60 p-4">
      <p className="text-xs text-slate-400">
        {fmtInt(shiftsOptimised)} shift(s) optimised in this window
        {shiftsNotOptimised > 0 && `, ${fmtInt(shiftsNotOptimised)} not optimised yet`}. Gains are summed across every
        optimised shift, each scored as the mean of 5 independent replays.
      </p>
      <PlanGainTiles label="More output (maximise tonnes)" gain={moreOutput} />
      <PlanGainTiles label="Leaner (minimise truck-hours, then fuel, then queue)" gain={leaner} />
    </div>
  )
}
