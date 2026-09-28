import { useState } from 'react'

interface ShowMoreProps {
  label: string
  children: React.ReactNode
}

export function ShowMore({ label, children }: ShowMoreProps) {
  const [open, setOpen] = useState(false)

  if (!open) {
    return (
      <button
        type="button"
        className="text-xs text-slate-400 underline-offset-2 hover:text-slate-200 hover:underline"
        onClick={() => setOpen(true)}
      >
        ▼ {label}
      </button>
    )
  }

  return (
    <div className="flex flex-col gap-3">
      {children}
      <button
        type="button"
        className="text-xs text-slate-400 underline-offset-2 hover:text-slate-200 hover:underline"
        onClick={() => setOpen(false)}
      >
        ▲ Show less
      </button>
    </div>
  )
}
