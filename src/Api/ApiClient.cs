using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using NeumannOrbital.Api.Models;

namespace NeumannOrbital.Api;

public class ApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = JsonConfig.Options;

    private readonly System.Net.Http.HttpClient _http;

    public ApiClient(string baseUrl, string apiKey)
    {
        _http = new System.Net.Http.HttpClient { BaseAddress = new Uri(baseUrl) };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("neumann-orbital/0.1");
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public void Dispose() => _http.Dispose();

    // ── Public API ──────────────────────────────────────────────────────────────

    public async Task<Probe> GetProbeAsync(CancellationToken ct = default)
    {
        var resp = await GetAsync<ProbeWrapper>("/api/probe", ct);
        return resp.Probe;
    }

    public async Task<List<Manny>> GetManniesAsync(CancellationToken ct = default)
    {
        var resp = await GetAsync<ManniesWrapper>("/api/probe/mannies", ct);
        return resp.Mannies;
    }

    public async Task<SectorObservation> GetProbeSectorAsync(CancellationToken ct = default)
    {
        var resp = await GetAsync<SectorWrapper>("/api/probe/sector", ct);
        return resp.Sector;
    }

    public async Task<SectorObservation> GetSectorAsync(int x, int y, int z, CancellationToken ct = default)
    {
        var resp = await GetAsync<SectorWrapper>($"/api/sector?x={x}&y={y}&z={z}", ct);
        return resp.Sector;
    }

    public async Task<Manny> RecallMannyAsync(string mannyId, CancellationToken ct = default)
    {
        var resp = await PostAsync<MannyWrapper>($"/api/probe/mannies/{mannyId}/recall", new { }, ct);
        return resp.Manny;
    }

    public async Task<Probe> MoveProbeAsync(int x, int y, int z, CancellationToken ct = default)
    {
        var resp = await PostAsync<ProbeWrapper>("/api/probe/move",
            new { target = new { x, y, z } }, ct);
        return resp.Probe;
    }

    // ── HTTP primitives ─────────────────────────────────────────────────────────

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        const int maxAttempts = 3;
        var delay = TimeSpan.FromSeconds(1);

        GD.Print($"[API] GET {path}");

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            HttpResponseMessage resp;
            try
            {
                resp = await _http.GetAsync(path, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                GD.PrintErr($"[API] GET {path} attempt {attempt}/{maxAttempts} — network error: {ex.Message}");
                if (attempt == maxAttempts) throw;
                await Task.Delay(delay, ct);
                delay *= 2;
                continue;
            }

            GD.Print($"[API] GET {path} → {(int)resp.StatusCode}");

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("Unauthorized — check api_key in config.toml");

            if ((int)resp.StatusCode >= 500)
            {
                GD.PrintErr($"[API] GET {path} attempt {attempt}/{maxAttempts} — retrying after {delay.TotalSeconds}s");
                if (attempt == maxAttempts)
                    throw new HttpRequestException($"HTTP {(int)resp.StatusCode} on GET {path} after {maxAttempts} attempts");
                await Task.Delay(delay, ct);
                delay *= 2;
                continue;
            }

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException($"HTTP {(int)resp.StatusCode} on GET {path}: {body}");
            }

            var stream = await resp.Content.ReadAsStreamAsync(ct);
            return JsonSerializer.Deserialize<T>(stream, JsonOptions)
                ?? throw new JsonException($"Null response deserializing GET {path}");
        }

        throw new InvalidOperationException("unreachable");
    }

    // ── Response wrappers ───────────────────────────────────────────────────────

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        GD.Print($"[API] POST {path}");

        var json    = JsonSerializer.Serialize(body, JsonOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage resp;
        try { resp = await _http.PostAsync(path, content, ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
            { throw new HttpRequestException($"POST {path}: {ex.Message}", ex); }

        GD.Print($"[API] POST {path} → {(int)resp.StatusCode}");

        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Unauthorized — check api_key in config.toml");
        if (!resp.IsSuccessStatusCode)
        {
            var body2 = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"HTTP {(int)resp.StatusCode} on POST {path}: {body2}");
        }

        var stream = await resp.Content.ReadAsStreamAsync(ct);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new JsonException($"Null response deserializing POST {path}");
    }

    private record ProbeWrapper(Probe Probe);
    private record ManniesWrapper(List<Manny> Mannies);
    private record SectorWrapper(SectorObservation Sector);
    private record MannyWrapper(Manny Manny);
}
