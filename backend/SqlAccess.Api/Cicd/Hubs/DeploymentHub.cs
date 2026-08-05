using Microsoft.AspNetCore.SignalR;

namespace SqlAccess.Api.Cicd.Hubs;

/// <summary>
/// Live deployment console. Clients join the group for a deployment id and receive
/// "log" and "status" events. Server pushes via IHubContext&lt;DeploymentHub&gt;.
/// </summary>
public class DeploymentHub : Hub
{
    public static string Group(int deploymentId) => $"deployment-{deploymentId}";

    public Task JoinDeployment(int deploymentId)
        => Groups.AddToGroupAsync(Context.ConnectionId, Group(deploymentId));

    public Task LeaveDeployment(int deploymentId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(deploymentId));
}
