export function StatusBadge({ status }: { status?: string | null }) {
  const s = status || 'Queued'
  return (
    <span className={`badge ${s}`}>
      <span className="dot" />
      {s}
    </span>
  )
}
