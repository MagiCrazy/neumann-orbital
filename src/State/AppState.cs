using System.Text.Json;
using Godot;
using NeumannOrbital.Api;
using NeumannOrbital.Api.Models;

namespace NeumannOrbital.State;

public partial class AppState : Node
{
    public static AppState Instance { get; private set; } = null!;

    public Probe? Probe { get; private set; }
    public List<Manny> Mannies { get; private set; } = [];
    public Dictionary<(int X, int Y, int Z), SectorObservation> KnownSectors { get; private set; } = [];

    [Signal] public delegate void ProbeUpdatedEventHandler();
    [Signal] public delegate void ManniesUpdatedEventHandler();
    [Signal] public delegate void SectorDiscoveredEventHandler(int x, int y, int z);
    [Signal] public delegate void SectorUpdatedEventHandler(int x, int y, int z);

    private ApiClient? _api;
    private CancellationTokenSource _cts = new();
    private bool _historyDirty;

    private const double FallbackIntervalSeconds = 60.0;

    // scan_history.json — shared with neumann-cockpit, same format
    private static readonly string[] ScanHistoryPaths =
    [
        AppConfig.XdgConfig("neumann", "scan_history.json"),
        AppConfig.XdgConfig("neumann-cockpit", "scan_history.json"),
    ];

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        Instance = this;

        AppConfig config;
        try { config = AppConfig.Load(); }
        catch (Exception ex)
        {
            GD.PrintErr($"[AppState] Config error: {ex.Message}");
            GetTree().Quit(1);
            return;
        }

        LoadScanHistory();
        _api = new ApiClient(config.BaseUrl, config.ApiKey);
        _ = RunRefreshLoopAsync(_cts.Token);
    }

    public override void _ExitTree()
    {
        _cts.Cancel();
        _api?.Dispose();
        SaveScanHistory();
    }

    // ── Public API ──────────────────────────────────────────────────────────────

    public async Task FetchSectorAsync(int x, int y, int z)
    {
        if (_api is null) return;
        try
        {
            var obs = await _api.GetSectorAsync(x, y, z, _cts.Token);
            StoreSector((x, y, z), obs);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AppState] FetchSector({x},{y},{z}) failed: {ex.Message}");
        }
    }

    public void TriggerRefresh() => _ = RefreshAllAsync(_cts.Token);

    public async Task MoveProbeAsync(int x, int y, int z)
    {
        if (_api is null) return;
        try
        {
            Probe = await _api.MoveProbeAsync(x, y, z, _cts.Token);
            Callable.From(() => EmitSignal(SignalName.ProbeUpdated)).CallDeferred();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AppState] MoveProbe({x},{y},{z}): {ex.Message}");
        }
    }

    public async Task RecallMannyAsync(string mannyId)
    {
        if (_api is null) return;
        try
        {
            var updated = await _api.RecallMannyAsync(mannyId, _cts.Token);
            var idx = Mannies.FindIndex(m => m.Id == mannyId);
            if (idx >= 0) Mannies[idx] = updated;
            Callable.From(() => EmitSignal(SignalName.ManniesUpdated)).CallDeferred();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AppState] RecallManny({mannyId}) failed: {ex.Message}");
        }
    }

    // ── Scan history persistence ────────────────────────────────────────────────

    private void LoadScanHistory()
    {
        var path = Array.Find(ScanHistoryPaths, File.Exists);
        if (path is null) return;
        try
        {
            var history = JsonSerializer.Deserialize<List<SectorObservation>>(
                File.ReadAllText(path), JsonConfig.Options);
            if (history is null) return;
            foreach (var obs in history)
                KnownSectors.TryAdd(ToIntCoords(obs.RelativeCoordinates), obs);
            GD.Print($"[AppState] Loaded {history.Count} sectors from {path}");
        }
        catch (Exception ex) { GD.PrintErr($"[AppState] LoadScanHistory: {ex.Message}"); }
    }

    private void SaveScanHistory()
    {
        if (!_historyDirty) return;
        // Prefer overwriting whichever path already exists; fall back to the cockpit path
        var path = Array.Find(ScanHistoryPaths, p => File.Exists(p))
                   ?? ScanHistoryPaths[1];
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,
                JsonSerializer.Serialize(KnownSectors.Values.ToList(), JsonConfig.Options));
            _historyDirty = false;
        }
        catch (Exception ex) { GD.PrintErr($"[AppState] SaveScanHistory: {ex.Message}"); }
    }

    // ── Refresh loop ────────────────────────────────────────────────────────────

    private async Task RunRefreshLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await RefreshAllAsync(ct);
            var delay = ComputeNextRefresh();
            GD.Print($"[AppState] Next refresh in {delay.TotalSeconds:F0}s");
            try { await Task.Delay(delay, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RefreshAllAsync(CancellationToken ct)
    {
        if (_api is null) return;
        await Task.WhenAll(
            RefreshProbeAsync(ct),
            RefreshManniesAsync(ct),
            RefreshProbeSectorAsync(ct));
    }

    private async Task RefreshProbeAsync(CancellationToken ct)
    {
        try
        {
            Probe = await _api!.GetProbeAsync(ct);
            Callable.From(() => EmitSignal(SignalName.ProbeUpdated)).CallDeferred();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        { GD.PrintErr($"[AppState] RefreshProbe: {ex.Message}"); }
    }

    private async Task RefreshManniesAsync(CancellationToken ct)
    {
        try
        {
            Mannies = await _api!.GetManniesAsync(ct);
            Callable.From(() => EmitSignal(SignalName.ManniesUpdated)).CallDeferred();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        { GD.PrintErr($"[AppState] RefreshMannies: {ex.Message}"); }
    }

    private async Task RefreshProbeSectorAsync(CancellationToken ct)
    {
        try
        {
            var obs = await _api!.GetProbeSectorAsync(ct);
            StoreSector(ToIntCoords(obs.RelativeCoordinates), obs);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        { GD.PrintErr($"[AppState] RefreshProbeSector: {ex.Message}"); }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private void StoreSector((int X, int Y, int Z) coords, SectorObservation obs)
    {
        var isNew = !KnownSectors.ContainsKey(coords);
        KnownSectors[coords] = obs;
        _historyDirty = true;
        if (isNew)
            Callable.From(() => EmitSignal(SignalName.SectorDiscovered, coords.X, coords.Y, coords.Z))
                    .CallDeferred();
        // Always fire SectorUpdated so UI can refresh after a Scan
        Callable.From(() => EmitSignal(SignalName.SectorUpdated, coords.X, coords.Y, coords.Z))
                .CallDeferred();
    }

    private TimeSpan ComputeNextRefresh()
    {
        if (Probe?.Movement is { } mv)
        {
            var remaining = mv.ArrivalAt - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero) return remaining;
        }
        return TimeSpan.FromSeconds(FallbackIntervalSeconds);
    }

    private static (int X, int Y, int Z) ToIntCoords(NeumannOrbital.Api.Models.Vector v) =>
        ((int)Math.Round(v.X), (int)Math.Round(v.Y), (int)Math.Round(v.Z));
}
