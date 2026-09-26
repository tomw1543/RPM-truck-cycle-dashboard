import type { ComplianceSummary } from '../api/types'
import { KpiTile } from './KpiTile'
import { fmtInt, fmtPercentFraction, fmtTonnes } from '../lib/format'

interface ComplianceSummaryTilesProps {
  summary: ComplianceSummary
}

function shiftLabel(ref: { shiftDate: string; shiftName: string } | null): string {
  if (!ref) return '—'
  const [, month, day] = ref.shiftDate.split('-')
  return `${day}/${month} ${ref.shiftName}`
}

export function ComplianceSummaryTiles({ summary }: ComplianceSummaryTilesProps) {
  return (
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
      <KpiTile label="Planned tonnes" value={fmtTonnes(summary.plannedTonnes)} sublabel={`${fmtInt(summary.shiftCount)} complete shifts`} />
      <KpiTile label="Actual tonnes" value={fmtTonnes(summary.actualTonnes)} />
      <KpiTile label="Planned cycles" value={fmtInt(summary.plannedCycles)} />
      <KpiTile label="Actual cycles" value={fmtInt(summary.actualCycles)} />
      <KpiTile label="% of plan" value={fmtPercentFraction(summary.percentOfPlan)} sublabel={`Baseline ${fmtPercentFraction(summary.baseline)}`} />
      <KpiTile
        label="Best / worst shift"
        value={summary.best ? `${fmtPercentFraction(summary.best.percentOfPlan)}` : '—'}
        sublabel={
          summary.best && summary.worst
            ? `${shiftLabel(summary.best)} best · ${shiftLabel(summary.worst)} worst (${fmtPercentFraction(summary.worst.percentOfPlan)})`
            : undefined
        }
      />
    </div>
  )
}
