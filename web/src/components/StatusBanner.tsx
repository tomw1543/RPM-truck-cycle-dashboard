interface StatusBannerProps {
  kind: 'loading' | 'error' | 'empty'
  title: string
  detail?: string
}

const STYLES: Record<StatusBannerProps['kind'], string> = {
  loading: 'border-slate-800 bg-slate-900/40 text-slate-400',
  error: 'border-red-900 bg-red-950/40 text-red-300',
  empty: 'border-slate-800 bg-slate-900/40 text-slate-500',
}

export function StatusBanner({ kind, title, detail }: StatusBannerProps) {
  return (
    <div className={`rounded-lg border px-4 py-6 text-center ${STYLES[kind]}`}>
      <p className="text-sm font-medium">{title}</p>
      {detail && <p className="mt-1 text-xs">{detail}</p>}
    </div>
  )
}
