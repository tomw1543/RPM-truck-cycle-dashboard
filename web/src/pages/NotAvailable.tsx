interface NotAvailableProps {
  title: string
}

export function NotAvailable({ title }: NotAvailableProps) {
  return (
    <div className="mx-auto max-w-7xl px-4 py-16 text-center">
      <h2 className="text-lg font-semibold text-slate-200">{title}</h2>
      <p className="mt-2 text-sm text-slate-500">This data isn't available yet - the endpoint hasn't been built.</p>
    </div>
  )
}
