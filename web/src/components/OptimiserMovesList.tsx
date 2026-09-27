import type { OptimiserAssignment } from '../api/types'

interface OptimiserMovesListProps {
  planLabel: string
  assignments: OptimiserAssignment[]
}

/** Lists only the trucks the chosen plan actually moved (moveReason is null for Original and for
 * any truck left on its Original route - see OptimisedAssignmentRow's doc comment). */
export function OptimiserMovesList({ planLabel, assignments }: OptimiserMovesListProps) {
  const moves = assignments.filter((a) => a.moveReason !== null)

  return (
    <div className="rounded-lg border border-slate-800 bg-slate-900/60 p-4">
      <h3 className="text-sm font-semibold text-slate-200">Moves - {planLabel}</h3>
      {moves.length === 0 ? (
        <p className="mt-2 text-sm text-slate-400">
          {planLabel === 'Original' ? 'Original is the shift as scheduled - nothing to move.' : 'No moves - the search found nothing better than Original.'}
        </p>
      ) : (
        <ol className="mt-2 flex flex-col gap-2 text-sm">
          {moves.map((a) => (
            <li key={a.truckName} className="flex flex-col gap-0.5 border-b border-slate-800/60 pb-2 last:border-0 last:pb-0">
              <span className="font-medium text-slate-200">
                {a.truckName}
                {a.isStoodDown ? ' - stood down' : a.routeName ? ` - now ${a.routeName}` : ''}
              </span>
              <span className="text-xs text-slate-400">{a.moveReason}</span>
            </li>
          ))}
        </ol>
      )}
    </div>
  )
}
