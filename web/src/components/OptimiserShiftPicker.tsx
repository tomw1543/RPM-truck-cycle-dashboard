import type { OptimiserShiftHeadline } from '../api/types'
import { fmtSignedHours, fmtSignedTonnes } from '../lib/format'

interface OptimiserShiftPickerProps {
  shifts: OptimiserShiftHeadline[]
  selected: { shiftDate: string; shiftName: string } | undefined
  onSelect: (shiftDate: string, shiftName: string) => void
}

function isSelected(s: OptimiserShiftHeadline, selected: OptimiserShiftPickerProps['selected']): boolean {
  return selected !== undefined && s.shiftDate === selected.shiftDate && s.shiftName === selected.shiftName
}

export function OptimiserShiftPicker({ shifts, selected, onSelect }: OptimiserShiftPickerProps) {
  return (
    <div className="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900/60">
      <h2 className="px-3 pt-3 text-sm font-semibold text-slate-200">Shifts</h2>
      <p className="px-3 pb-2 text-xs text-slate-400">Newest first. Pick a shift to see its plans below.</p>
      {shifts.length === 0 ? (
        <p className="px-3 pb-4 text-sm text-slate-400">No shifts in this window.</p>
      ) : (
        <table className="w-full min-w-[640px] text-sm">
          <thead>
            <tr className="border-b border-slate-800 text-left text-xs uppercase tracking-wide text-slate-400">
              <th className="px-3 py-2 font-medium">Shift</th>
              <th className="px-3 py-2 text-right font-medium">More output gain</th>
              <th className="px-3 py-2 text-right font-medium">Leaner truck-hours saved</th>
              <th className="px-3 py-2 font-medium" />
            </tr>
          </thead>
          <tbody>
            {shifts.map((s) => {
              const key = `${s.shiftDate}-${s.shiftName}`
              const active = isSelected(s, selected)
              return (
                <tr
                  key={key}
                  className={`border-b border-slate-800/60 ${active ? 'bg-cyan-950/30' : 'hover:bg-slate-800/30'}`}
                >
                  <td className="px-3 py-2 text-slate-200">
                    {active && <span aria-hidden="true">&rarr; </span>}
                    {s.shiftDate} {s.shiftName}
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums text-slate-300">
                    {s.hasResults ? fmtSignedTonnes(s.moreOutputTonnesGainedMean) : <span className="text-slate-500">—</span>}
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums text-slate-300">
                    {s.hasResults ? fmtSignedHours(s.leanerTruckHoursSavedMean) : <span className="text-slate-500">—</span>}
                  </td>
                  <td className="px-3 py-2 text-right">
                    <button
                      type="button"
                      onClick={() => onSelect(s.shiftDate, s.shiftName)}
                      disabled={!s.hasResults}
                      className={`rounded-md px-2 py-1 text-xs ${
                        s.hasResults
                          ? active
                            ? 'bg-cyan-600 text-white'
                            : 'bg-slate-800 text-slate-300 hover:bg-slate-700'
                          : 'cursor-not-allowed bg-slate-900 text-slate-600'
                      }`}
                    >
                      {s.hasResults ? (active ? 'Selected' : 'View') : 'Not optimised'}
                    </button>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}
    </div>
  )
}
