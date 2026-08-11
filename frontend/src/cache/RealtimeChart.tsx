/** Minimal dependency-free realtime line chart (SVG). Renders a rolling series with a filled area. */
export function RealtimeChart({
  data,
  color = '#2563eb',
  height = 60,
  suffix = '',
}: {
  data: number[]
  color?: string
  height?: number
  suffix?: string
}) {
  const width = 240
  const n = data.length
  const max = Math.max(1, ...data)
  const last = n > 0 ? data[n - 1] : 0

  const points =
    n < 2
      ? ''
      : data
          .map((v, i) => {
            const x = (i / (n - 1)) * width
            const y = height - (v / max) * (height - 6) - 3
            return `${x.toFixed(1)},${y.toFixed(1)}`
          })
          .join(' ')

  const areaId = `area-${color.replace('#', '')}`

  return (
    <div className="rt-chart">
      <svg viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" width="100%" height={height}>
        <defs>
          <linearGradient id={areaId} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={color} stopOpacity="0.25" />
            <stop offset="100%" stopColor={color} stopOpacity="0" />
          </linearGradient>
        </defs>
        {points && (
          <>
            <polygon points={`0,${height} ${points} ${width},${height}`} fill={`url(#${areaId})`} />
            <polyline points={points} fill="none" stroke={color} strokeWidth="2" vectorEffect="non-scaling-stroke" />
          </>
        )}
      </svg>
      <div className="rt-current" style={{ color }}>
        {typeof last === 'number' ? last.toLocaleString(undefined, { maximumFractionDigits: 2 }) : '0'}
        {suffix}
      </div>
    </div>
  )
}
