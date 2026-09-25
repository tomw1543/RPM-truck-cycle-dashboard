import { useState } from 'react'
import { Link } from 'react-router'
import type { TruckKpis } from '../api/types'
import { fmtInt, fmtNumber, fmtPercentFraction, fmtTonnes } from '../lib/format'

interface TrucksTableProps {
  fleet: TruckKpis
  trucks: TruckKpis[]
  windowQuery: string
}

type SortKey = keyof Omit<TruckKpis, 'name' | 'capacityTonnes'>

interface Column {
  key: SortKey
  label: string
  format: (v: number | null) => string
  // Direction that counts as "worse" than the fleet average, for the >10% highlight.
  worseWhen: 'lower' | 'higher' | 'none'
}

const COLUMNS: Column[] = [
  { key: 'cycles', label: 'Cycles', format: fmtInt, worseWhen: 'none' },
  { key: 'tonnes', label: 'Tonnes', format: fmtTonnes, worseWhen: 'lower' },
  { key: 'tonnesPerOperatingHour', label: 't / op hr', format: fmtNumber, worseWhen: 'lower' },
  { key: 'tonnesPerCalendarHour', label: 't / cal hr', format: fmtNumber, worseWhen: 'lower' },
  { key: 'averageCycleMin', label: 'Avg cycle (min)', format: fmtNumber, worseWhen: 'higher' },
  { key: 'averagePayloadPercent', label: 'Avg payload %', format: (v) => fmtNumber(v, 0), worseWhen: 'lower' },
  { key: 'cyclesPerOperatingHour', label: 'Cycles / op hr', format: (v) => fmtNumber(v, 2), worseWhen: 'lower' },
  { key: 'availability', label: 'Availability', format: fmtPercentFraction, worseWhen: 'lower' },
  { key: 'utilisation', label: 'Utilisation', format: fmtPercentFraction, worseWhen: 'lower' },
  { key: 'effectiveUtilisation', label: 'Eff. utilisation', format: fmtPercentFraction, worseWhen: 'lower' },
  { key: 'idlePercent', label: 'Idle %', format: (v) => fmtPercentFraction(v, 1), worseWhen: 'higher' },
]

function isWorse(column: Column, value: number | null, fleetValue: number | null): boolean {
  if (value === null || fleetValue === null || fleetValue === 0 || column.worseWhen === 'none') return false
  if (column.worseWhen === 'lower') return value < fleetValue * 0.9
  return value > fleetValue * 1.1
}

export function TrucksTable({ fleet, trucks, windowQuery }: TrucksTableProps) {
  const [sortKey, setSortKey] = useState<SortKey>('tonnes')
  const [sortDesc, setSortDesc] = useState(true)

  const sortedTrucks = [...trucks].sort((a, b) => {
    const av = a[sortKey]
    const bv = b[sortKey]
    if (av === null && bv === null) return 0
    if (av === null) return 1
    if (bv === null) return -1
    return sortDesc ? bv - av : av - bv
  })

  const onSort = (key: SortKey) => {
    if (key === sortKey) {
      setSortDesc((d) => !d)
    } else {
      setSortKey(key)
      setSortDesc(true)
    }
  }

  return (
    <div className="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900/60">
      <table className="w-full min-w-[900px] text-sm">
        <thead>
          <tr className="border-b border-slate-800 text-left text-xs uppercase tracking-wide text-slate-400">
            <th className="px-3 py-2 font-medium">Truck</th>
            {COLUMNS.map((col) => (
              <th key={col.key} className="px-3 py-2 text-right font-medium">
                <button
                  type="button"
                  onClick={() => onSort(col.key)}
                  className={`inline-flex items-center gap-1 hover:text-slate-300 ${sortKey === col.key ? 'text-cyan-400' : ''}`}
                >
                  {col.label}
                  {sortKey === col.key && <span>{sortDesc ? '↓' : '↑'}</span>}
                </button>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          <tr className="border-b border-slate-800 bg-slate-800/40 font-medium text-slate-200">
            <td className="px-3 py-2">{fleet.name}</td>
            {COLUMNS.map((col) => (
              <td key={col.key} className="px-3 py-2 text-right tabular-nums">
                {col.format(fleet[col.key])}
              </td>
            ))}
          </tr>
          {sortedTrucks.map((truck) => (
            <tr key={truck.name} className="border-b border-slate-800/60 last:border-0 hover:bg-slate-800/30">
              <td className="px-3 py-2">
                <Link to={`/trucks/${truck.name}${windowQuery}`} className="text-cyan-400 hover:underline">
                  {truck.name}
                </Link>
              </td>
              {COLUMNS.map((col) => {
                const value = truck[col.key]
                const fleetValue = fleet[col.key]
                const worse = isWorse(col, value, fleetValue)
                const percentDiff = worse && fleetValue !== null && fleetValue !== 0 && value !== null ? ((value - fleetValue) / Math.abs(fleetValue)) * 100 : null
                return (
                  <td
                    key={col.key}
                    className={`px-3 py-2 text-right tabular-nums ${worse ? 'bg-red-950/50 text-red-300' : 'text-slate-300'}`}
                    title={worse && percentDiff !== null ? `${percentDiff >= 0 ? '+' : ''}${percentDiff.toFixed(0)}% vs fleet average` : undefined}
                  >
                    {worse && <span aria-hidden="true">▲ </span>}
                    {col.format(value)}
                  </td>
                )
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
