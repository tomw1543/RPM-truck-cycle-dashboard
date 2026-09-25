import { Link } from 'react-router'
import type { LossItem } from '../api/types'
import { fmtNumber, fmtTonnes } from '../lib/format'

interface BiggestLossesTableProps {
  items: LossItem[]
  windowQuery: string
}

const MEASURE_LABEL: Record<LossItem['measure'], string> = {
  recoverable: 'Recoverable',
  overBook: 'Over book',
  underload: 'Underload',
}

function fmtNative(item: LossItem): string {
  return item.nativeUnit === 'min' ? `${fmtNumber(item.nativeAmount)} min` : fmtTonnes(item.nativeAmount)
}

// Truck names look like "T01".."T12"; route names are longer and contain a space ("L1 to ROM pad").
function isTruckSubject(item: LossItem): boolean {
  return item.measure === 'underload'
}

export function BiggestLossesTable({ items, windowQuery }: BiggestLossesTableProps) {
  return (
    <div className="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900/60">
      <h2 className="px-3 pt-3 text-sm font-semibold text-slate-200">Biggest losses</h2>
      <p className="px-3 pb-2 text-xs text-slate-400">
        The ten largest loss items across three measures that overlap and must not be added together. Equivalent
        tonnes assume the saved time becomes hauling, at that route&apos;s own tonnes-per-minute rate in this window.
      </p>
      {items.length === 0 ? (
        <p className="px-3 pb-4 text-sm text-slate-400">No losses in this window.</p>
      ) : (
        <table className="w-full min-w-[620px] text-sm">
          <thead>
            <tr className="border-b border-slate-800 text-left text-xs uppercase tracking-wide text-slate-400">
              <th className="px-3 py-2 font-medium">Measure</th>
              <th className="px-3 py-2 font-medium">Subject</th>
              <th className="px-3 py-2 font-medium">Phase</th>
              <th className="px-3 py-2 text-right font-medium">Native amount</th>
              <th className="px-3 py-2 text-right font-medium">&asymp; Equivalent tonnes</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item, i) => (
              <tr key={i} className="border-b border-slate-800/60 last:border-0 hover:bg-slate-800/30">
                <td className="px-3 py-2 text-slate-300">{MEASURE_LABEL[item.measure]}</td>
                <td className="px-3 py-2 text-slate-200">
                  {isTruckSubject(item) ? (
                    <Link to={`/trucks/${item.subject}${windowQuery}`} className="text-cyan-400 hover:underline">
                      {item.subject}
                    </Link>
                  ) : (
                    item.subject
                  )}
                </td>
                <td className="px-3 py-2 text-slate-400">{item.phase ?? '—'}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-300">{fmtNative(item)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-slate-200">&asymp; {fmtTonnes(item.equivalentTonnes)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
