using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SqlAccess.Api.Cache.Hubs;

/// <summary>
/// Live metrics hub. The server pushes a "metrics" event (a <c>MetricsSnapshot</c>) to all
/// connected clients once per second via <c>MetricsBroadcastWorker</c>. Clients only listen.
/// </summary>
[Authorize]
public sealed class CacheMetricsHub : Hub
{
}
