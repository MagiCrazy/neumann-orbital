using Godot;
using NeumannOrbital.State;

namespace NeumannOrbital.UI;

/// Full-screen hyperspace effect shown while the probe is in transit.
public partial class WarpOverlay : Control
{
    private struct Streak
    {
        public float Angle;
        public float Dist;
        public float Speed;
        public float Length;
        public float Alpha;
    }

    private const int Count = 220;
    private const float MinSpeed = 350f;
    private const float MaxSpeed = 1100f;
    private const float MinLen = 50f;
    private const float MaxLen = 220f;

    private readonly Streak[] _streaks = new Streak[Count];
    private readonly RandomNumberGenerator _rng = new() { Seed = 7777 };

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        InitStreaks();
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;

        var size = GetViewportRect().Size;
        float maxR = size.Length() * 0.65f;
        var dt = (float)delta;

        for (int i = 0; i < _streaks.Length; i++)
        {
            _streaks[i].Dist += _streaks[i].Speed * dt;
            if (_streaks[i].Dist > maxR + _streaks[i].Length)
                _streaks[i] = NewStreak(0f);
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size;
        var center = size / 2f;

        // Dark space background
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.01f, 0.02f, 0.08f, 0.92f));

        // Radial streaks
        foreach (var s in _streaks)
        {
            var dir = new Vector2(Mathf.Cos(s.Angle), Mathf.Sin(s.Angle));
            var from = center + dir * s.Dist;
            var to = center + dir * (s.Dist + s.Length);

            // Fade in from centre, bright middle, fade at far edge
            float t = Mathf.Clamp(s.Dist / 120f, 0f, 1f);
            float alpha = s.Alpha * t;
            var inner = new Color(1.0f, 1.0f, 1.0f, alpha);
            var outer = new Color(0.45f, 0.6f, 1.0f, alpha * 0.4f);

            DrawLine(from, to, inner, 1.8f);
            DrawLine(from, to, outer, 3.5f);   // soft glow around streak
        }

        // Central vanishing-point glow
        for (float r = 120f; r > 0f; r -= 12f)
        {
            float a = (1f - r / 120f) * 0.06f;
            DrawCircle(center, r, new Color(0.55f, 0.7f, 1.0f, a));
        }
        DrawCircle(center, 8f, new Color(1.0f, 1.0f, 1.0f, 0.9f));
        DrawCircle(center, 18f, new Color(0.8f, 0.9f, 1.0f, 0.35f));

        // Probe silhouette — simple arrow shape at centre
        DrawProbe(center);

        // Travel info panel at bottom
        DrawTravelInfo(size);
    }

    private void DrawTravelInfo(Vector2 size)
    {
        var probe = AppState.Instance.Probe;
        if (probe?.Movement is not { } mv) return;

        var now = DateTimeOffset.UtcNow;
        var totalSec = (mv.ArrivalAt - mv.StartedAt).TotalSeconds;
        double progress = totalSec > 0
            ? Math.Clamp((now - mv.StartedAt).TotalSeconds / totalSec, 0.0, 1.0)
            : 1.0;
        var remaining = mv.ArrivalAt - now;

        var ox = (int)Math.Round(mv.Origin.X);
        var oy = (int)Math.Round(mv.Origin.Y);
        var oz = (int)Math.Round(mv.Origin.Z);
        var tx = (int)Math.Round(mv.Target.X);
        var ty = (int)Math.Round(mv.Target.Y);
        var tz = (int)Math.Round(mv.Target.Z);

        var font = ThemeDB.FallbackFont;
        float panelW = 480f;
        float panelH = 120f;
        float px = (size.X - panelW) / 2f;
        float py = size.Y - panelH - 30f;

        // Panel background
        DrawRect(new Rect2(px, py, panelW, panelH),
            new Color(0.04f, 0.06f, 0.14f, 0.82f));
        DrawRect(new Rect2(px, py, panelW, panelH),
            new Color(0.3f, 0.5f, 0.9f, 0.35f), false, 1f);

        float lx = px + 16f;
        float font16 = 16f, font12 = 12f, font11 = 11f;

        // Row 1: IN TRANSIT  (ox,oy,oz) → (tx,ty,tz)
        DrawString(font, new Vector2(lx, py + 22f),
            "IN TRANSIT", HorizontalAlignment.Left, -1, (int)font16,
            new Color(0.75f, 0.88f, 1.0f));
        DrawString(font, new Vector2(lx + 140f, py + 22f),
            $"({ox},{oy},{oz})  →  ({tx},{ty},{tz})", HorizontalAlignment.Left,
            -1, (int)font12, new Color(0.55f, 0.70f, 0.95f));

        // Row 2: phase + ETA + speed
        var phase = mv.Phase?.ToString()?.ToLowerInvariant()
                 ?? mv.Status.ToString().ToLowerInvariant();
        var eta = remaining > TimeSpan.Zero ? Helpers.FormatDuration(remaining) : "arriving…";
        var speed = mv.EstimatedVelocityC is { } v ? $"  ·  {v:F3}c" : "";
        DrawString(font, new Vector2(lx, py + 42f),
            $"{phase}  ·  ETA {eta}{speed}", HorizontalAlignment.Left,
            -1, (int)font12, new Color(0.5f, 0.65f, 0.88f));

        // Ship-along-route progress
        float barX = lx;
        float barY = py + 68f;
        float barW = panelW - 32f;
        float shipX = barX + barW * (float)progress;

        // Route line — dim before ship, bright after
        DrawLine(new Vector2(barX, barY), new Vector2(shipX, barY),
            new Color(0.35f, 0.6f, 1.0f, 0.25f), 2f);
        DrawLine(new Vector2(shipX, barY), new Vector2(barX + barW, barY),
            new Color(0.25f, 0.35f, 0.6f, 0.18f), 2f);

        // Tick marks every 10%
        for (int i = 1; i < 10; i++)
        {
            float tx2 = barX + barW * i / 10f;
            DrawLine(new Vector2(tx2, barY - 4f), new Vector2(tx2, barY + 4f),
                new Color(0.3f, 0.4f, 0.6f, 0.3f), 1f);
        }

        // Origin dot
        DrawCircle(new Vector2(barX, barY), 4f, new Color(0.3f, 0.5f, 0.8f, 0.7f));
        // Destination dot — pulsing
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() * 0.005f);
        DrawCircle(new Vector2(barX + barW, barY), 5f + pulse * 2f,
            new Color(0.35f, 0.6f, 1.0f, 0.3f + 0.2f * pulse));
        DrawCircle(new Vector2(barX + barW, barY), 4f,
            new Color(0.5f, 0.75f, 1.0f, 0.9f));

        // Engine trail behind ship
        if (progress > 0.01f)
        {
            float trailLen = Mathf.Min(barW * 0.12f, shipX - barX);
            DrawLine(new Vector2(shipX, barY),
                     new Vector2(shipX - trailLen, barY),
                     new Color(0.4f, 0.65f, 1.0f, 0.45f), 4f);
            DrawLine(new Vector2(shipX, barY),
                     new Vector2(shipX - trailLen * 0.5f, barY),
                     new Color(0.8f, 0.9f, 1.0f, 0.6f), 2f);
        }

        // Ship silhouette — rotated 90° (nose right)
        DrawShipSmall(new Vector2(shipX, barY));

        // Labels under route line
        long traveled = (long)Math.Round(mv.Distance * progress);
        // Departure — left-aligned at bar start
        DrawString(font, new Vector2(barX, barY + 14f),
            $"({ox},{oy},{oz})", HorizontalAlignment.Left, -1, (int)font11,
            new Color(0.4f, 0.5f, 0.7f));
        // Destination — right-aligned, capped 90px before bar end to stay inside panel
        DrawString(font, new Vector2(barX + barW - 88f, barY + 14f),
            $"({tx},{ty},{tz})", HorizontalAlignment.Right, 88f, (int)font11,
            new Color(0.4f, 0.5f, 0.7f));
        // Progress counter — below bar, follows ship
        DrawString(font, new Vector2(shipX, barY + 28f),
            $"{traveled}/{mv.Distance}", HorizontalAlignment.Center, -1, (int)font11,
            new Color(0.7f, 0.85f, 1.0f));
    }

    // ── Public control ────────────────────────────────────────────────────────

    public void ShowWarp() { Visible = true; }
    public void HideWarp() { Visible = false; }

    // ── Internals ─────────────────────────────────────────────────────────────

    private void InitStreaks()
    {
        for (int i = 0; i < _streaks.Length; i++)
            _streaks[i] = NewStreak(1f);   // start already spread across screen
    }

    private Streak NewStreak(float maxDistFraction)
    {
        var size = GetViewportRect().Size;
        float maxR = size.Length() * 0.65f;
        return new Streak
        {
            Angle = _rng.RandfRange(0f, Mathf.Tau),
            Dist = _rng.RandfRange(8f, maxR * maxDistFraction + 8f),
            Speed = _rng.RandfRange(MinSpeed, MaxSpeed),
            Length = _rng.RandfRange(MinLen, MaxLen),
            Alpha = _rng.RandfRange(0.55f, 1.0f),
        };
    }


    private void DrawShipSmall(Vector2 pos)
    {
        // Same delta-wing shape, scaled down, nose pointing right (+X)
        var c = new Color(0.8f, 0.9f, 1.0f, 0.95f);
        DrawColoredPolygon(new Vector2[]
        {
            pos + new Vector2( 12f,  0f),   // nose (right)
            pos + new Vector2( -8f,  7f),   // right wing tip
            pos + new Vector2( -3f,  2f),   // right wing root
            pos + new Vector2( -5f,  0f),   // tail
            pos + new Vector2( -3f, -2f),   // left wing root
            pos + new Vector2( -8f, -7f),   // left wing tip
        }, c);
        // Engine glow at tail
        DrawCircle(pos + new Vector2(-5f, 0f), 3f,
            new Color(0.5f, 0.7f, 1.0f, 0.6f));
    }

    private void DrawProbe(Vector2 center)
    {
        // Minimalist ship silhouette: nose forward (up), delta wings
        var c = new Color(0.75f, 0.85f, 1.0f, 0.85f);
        var pts = new Vector2[]
        {
            center + new Vector2(  0, -22),   // nose
            center + new Vector2( 14,  14),   // right wing tip
            center + new Vector2(  4,   6),   // right wing root
            center + new Vector2(  0,  10),   // tail centre
            center + new Vector2( -4,   6),   // left wing root
            center + new Vector2(-14,  14),   // left wing tip
        };
        DrawColoredPolygon(pts, c);
        // Engine glow
        DrawCircle(center + new Vector2(0, 10), 5f, new Color(0.6f, 0.75f, 1.0f, 0.5f));
    }

}
