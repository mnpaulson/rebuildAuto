using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RebuildOrchestrator.Models;
using RebuildOrchestrator.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5500");

// Configure instant shutdown timeout
builder.Services.Configure<HostOptions>(opts =>
{
    opts.ShutdownTimeout = TimeSpan.FromSeconds(1);
});

// Register Core Services
builder.Services.AddSingleton<WindowManager>();
builder.Services.AddSingleton<ProcessManager>();
builder.Services.AddSingleton<FleetManager>();

var app = builder.Build();

app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles();

var winMgr = app.Services.GetRequiredService<WindowManager>();
var procMgr = app.Services.GetRequiredService<ProcessManager>();
var fleetMgr = app.Services.GetRequiredService<FleetManager>();

var activeClients = new ConcurrentDictionary<WebSocket, ConnectedClient>();
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
};

async Task BroadcastJsonAsync(object obj)
{
    if (activeClients.IsEmpty) return;

    try
    {
        var json = JsonSerializer.Serialize(obj, jsonOptions);
        var buffer = Encoding.UTF8.GetBytes(json);

        var tasks = activeClients.Values.Select(c => c.SendAsync(buffer));
        await Task.WhenAll(tasks);
    }
    catch { }
}

async Task BroadcastFleetStateAsync()
{
    if (activeClients.IsEmpty) return;
    try
    {
        var data = fleetMgr.GetFleetOverview();
        await BroadcastJsonAsync(new { type = "fleet_update", payload = data });
    }
    catch { }
}

fleetMgr.OnFleetUpdated += () => _ = BroadcastFleetStateAsync();

fleetMgr.OnBotLogLine += (profile, line) =>
{
    _ = BroadcastJsonAsync(new
    {
        type = "bot_log",
        payload = new
        {
            profile = profile,
            line = line,
            timestamp = DateTime.UtcNow
        }
    });
};

// Background heartbeat to refresh performance metrics every 1s
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(1000);
        await BroadcastFleetStateAsync();
    }
});

#region REST Endpoints

// 1. Fleet Overview
app.MapGet("/api/fleet", () => Results.Ok(fleetMgr.GetFleetOverview()));

// 2. Bot Process Control
app.MapPost("/api/bot/start", (LaunchBotRequest req) =>
{
    if (procMgr.StartBot(req, out string err))
    {
        return Results.Ok(new { success = true, message = $"Started bot '{req.ProfileName}'" });
    }
    return Results.BadRequest(new { success = false, error = err });
});

app.MapPost("/api/bot/stop/{profile}", (string profile) =>
{
    bool stopped = procMgr.StopBot(profile);
    return Results.Ok(new { success = stopped });
});

app.MapPost("/api/bot/start-all", () =>
{
    var overview = fleetMgr.GetFleetOverview();
    int count = 0;
    foreach (var p in overview.Profiles)
    {
        if (!p.IsRunning)
        {
            procMgr.StartBot(new LaunchBotRequest
            {
                ProfileName = p.ProfileName,
                AccountId = p.AccountId,
                LowSpec = false
            }, out _);
            count++;
        }
    }
    return Results.Ok(new { success = true, startedCount = count });
});

app.MapPost("/api/bot/stop-all", () =>
{
    procMgr.StopAll();
    return Results.Ok(new { success = true });
});

// 3. Win32 Window Arrangement
app.MapPost("/api/windows/tile", (TileLayoutRequest req) =>
{
    var pids = procMgr.GetRunningPids(req.Profiles);
    if (pids.Count == 0)
    {
        return Results.BadRequest(new { success = false, error = "No running bot windows found to arrange." });
    }

    bool success = winMgr.TileWindows(pids, req.LayoutType, req.MonitorIndex);
    fleetMgr.AddLog(new FleetLogEntry
    {
        Level = "Info",
        Message = $"Arranged {pids.Count} bot windows in '{req.LayoutType}' grid on Monitor {req.MonitorIndex}."
    });

    return Results.Ok(new { success = success, windowCount = pids.Count });
});

app.MapPost("/api/windows/focus/{profile}", (string profile) =>
{
    var state = procMgr.GetState(profile);
    if (state != null && state.ProcessId > 0)
    {
        bool focused = winMgr.FocusWindow(state.ProcessId);
        return Results.Ok(new { success = focused });
    }
    return Results.NotFound(new { success = false, error = $"Bot '{profile}' is not running." });
});

app.MapPost("/api/windows/hide/{profile}", (string profile) =>
{
    var state = procMgr.GetState(profile);
    if (state != null && state.ProcessId > 0)
    {
        bool hidden = winMgr.HideWindow(state.ProcessId);
        return Results.Ok(new { success = hidden });
    }
    return Results.NotFound(new { success = false, error = $"Bot '{profile}' is not running." });
});

app.MapPost("/api/windows/show/{profile}", (string profile) =>
{
    var state = procMgr.GetState(profile);
    if (state != null && state.ProcessId > 0)
    {
        bool shown = winMgr.ShowWindowForPid(state.ProcessId);
        return Results.Ok(new { success = shown });
    }
    return Results.NotFound(new { success = false, error = $"Bot '{profile}' is not running." });
});

app.MapPost("/api/windows/hide-all", () =>
{
    var pids = procMgr.GetRunningPids();
    winMgr.HideAll(pids);
    fleetMgr.AddLog(new FleetLogEntry
    {
        Level = "Info",
        Message = $"Hid all {pids.Count} bot windows (Headless background mode)."
    });
    return Results.Ok(new { success = true });
});

app.MapPost("/api/windows/show-all", () =>
{
    var pids = procMgr.GetRunningPids();
    winMgr.ShowAll(pids);
    fleetMgr.AddLog(new FleetLogEntry
    {
        Level = "Info",
        Message = $"Restored all {pids.Count} bot windows."
    });
    return Results.Ok(new { success = true });
});

app.MapPost("/api/windows/minimize-all", () =>
{
    var pids = procMgr.GetRunningPids();
    winMgr.MinimizeAll(pids);
    return Results.Ok(new { success = true });
});

app.MapPost("/api/windows/restore-all", () =>
{
    var pids = procMgr.GetRunningPids();
    winMgr.RestoreAll(pids);
    return Results.Ok(new { success = true });
});

// 4. Bot Config Editor
app.MapGet("/api/bot/{profile}/config", (string profile) =>
{
    string raw = fleetMgr.GetProfileConfigRaw(profile);
    return Results.Content(raw, "application/json");
});

app.MapPost("/api/bot/{profile}/config", async (string profile, HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    string content = await reader.ReadToEndAsync();
    if (fleetMgr.SaveProfileConfigRaw(profile, content, out string error))
    {
        return Results.Ok(new { success = true });
    }
    return Results.BadRequest(new { success = false, error = error });
});

// 5. Discrete Macro Dispatch
app.MapPost("/api/bot/{profile}/macro", (string profile, MacroEnqueueRequest req) =>
{
    if (fleetMgr.EnqueueMacro(profile, req, out string err))
    {
        return Results.Ok(new { success = true, message = $"Enqueued {req.ActionType} macro." });
    }
    return Results.BadRequest(new { success = false, error = err });
});

// 6. Event Logs & Bot Logs
app.MapGet("/api/events", () => Results.Ok(fleetMgr.GetRecentLogs(100)));

app.MapGet("/api/bot/{profile}/logs", (string profile, int? lines) => 
    Results.Ok(fleetMgr.GetRecentBotLogs(profile, lines ?? 200)));

#endregion

#region WebSocket Telemetry Stream

app.Map("/ws/fleet", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var client = new ConnectedClient(webSocket);
        activeClients.TryAdd(webSocket, client);

        var cancelToken = context.RequestAborted;

        try
        {
            // Send initial fleet snapshot immediately via thread-safe client
            var initialData = fleetMgr.GetFleetOverview();
            var initialJson = JsonSerializer.Serialize(new { type = "fleet_update", payload = initialData }, jsonOptions);
            await client.SendAsync(Encoding.UTF8.GetBytes(initialJson), cancelToken);

            var recvBuffer = new byte[1024 * 4];
            while (webSocket.State == WebSocketState.Open && !cancelToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(recvBuffer), cancelToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            activeClients.TryRemove(webSocket, out _);
        }
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

#endregion

// Abort active websockets immediately on Ctrl+C to avoid waiting for timeout
app.Lifetime.ApplicationStopping.Register(() =>
{
    foreach (var client in activeClients.Values)
    {
        try
        {
            client.Socket.Abort();
        }
        catch { }
    }
});

Console.WriteLine("=========================================================");
Console.WriteLine("  RAGNAROK REBUILD FLEET ORCHESTRATOR");
Console.WriteLine("  Dashboard URL: http://localhost:5500");
Console.WriteLine("=========================================================");

app.Run();

public class ConnectedClient
{
    public WebSocket Socket { get; }
    private readonly SemaphoreSlim sendLock = new(1, 1);

    public ConnectedClient(WebSocket socket)
    {
        Socket = socket;
    }

    public async Task SendAsync(byte[] buffer, CancellationToken ct = default)
    {
        if (Socket.State != WebSocketState.Open) return;
        await sendLock.WaitAsync(ct);
        try
        {
            if (Socket.State == WebSocketState.Open)
            {
                await Socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, ct);
            }
        }
        catch { }
        finally
        {
            sendLock.Release();
        }
    }
}
