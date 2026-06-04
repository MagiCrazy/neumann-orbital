using Godot;
using NeumannOrbital.Api.Models;
using NeumannOrbital.State;

namespace NeumannOrbital.UI;

/// Full-screen sector map panel, toggled with [M] or a HUD button.
public partial class MapPanel : Control
{
    private MapCanvas _canvas    = null!;
    private Panel    _infoBox    = null!;
    private Label    _infoLabel  = null!;

    private (int X, int Y, int Z)? _selected;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        Visible = false;
        BuildLayout();

        var state = AppState.Instance;
        state.SectorDiscovered += (_, _, _) => _canvas.QueueRedraw();
        state.ProbeUpdated     += ()         => _canvas.QueueRedraw();
    }

    public void Toggle() => Visible = !Visible;

    public override void _Process(double _)
    {
        // Redraw every frame while open and probe is traveling so the marker animates
        if (Visible && AppState.Instance.Probe?.Movement is not null)
            _canvas.QueueRedraw();
    }

    public override void _Input(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is InputEventKey { Pressed: true, Keycode: Key.M or Key.Escape })
        {
            Visible = false;
            GetViewport().SetInputAsHandled();
        }
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private void BuildLayout()
    {
        // Semi-transparent background
        var bg = new Panel();
        bg.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            { BgColor = new Color(0.02f, 0.03f, 0.06f, 0.92f) });
        Anc(bg, 0, 0, 1, 1);
        AddChild(bg);

        // Title
        var title = new Label { Text = "SECTOR MAP   [M / Esc] close" };
        title.AddThemeFontSizeOverride("font_size", 14);
        title.Modulate = new Color(0.5f, 0.6f, 0.8f);
        Anc(title, 0, 0, 1, 0); Off(title, 12, 8, 0, 32);
        title.HorizontalAlignment = HorizontalAlignment.Left;
        AddChild(title);

        // Z-axis note
        var zNote = new Label { Text = "projection X/Y — Z shown as label" };
        zNote.AddThemeFontSizeOverride("font_size", 11);
        zNote.Modulate = new Color(0.35f, 0.4f, 0.5f);
        Anc(zNote, 1, 0, 1, 0); Off(zNote, -280, 8, -12, 32);
        zNote.HorizontalAlignment = HorizontalAlignment.Right;
        AddChild(zNote);

        // Legend
        AddChild(BuildLegend());

        // Drawing canvas
        _canvas = new MapCanvas(this);
        Anc(_canvas, 0, 0, 1, 1); Off(_canvas, 12, 36, -12, -36);
        AddChild(_canvas);

        // Sector info box (hidden until a sector is selected)
        _infoBox = new Panel();
        _infoBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor       = new Color(0.04f, 0.05f, 0.1f, 0.92f),
            BorderColor   = new Color(0.3f, 0.4f, 0.6f, 0.6f),
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop  = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        });
        _infoBox.Visible = false;
        Anc(_infoBox, 1, 0, 1, 0); Off(_infoBox, -280, 36, -12, 220);
        AddChild(_infoBox);

        _infoLabel = new Label { AutowrapMode = TextServer.AutowrapMode.Word };
        _infoLabel.AddThemeFontSizeOverride("font_size", 12);
        Anc(_infoLabel, 0, 0, 1, 1); Off(_infoLabel, 10, 8, -10, -8);
        _infoBox.AddChild(_infoLabel);
    }

    private static Control BuildLegend()
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 16);
        Anc(box, 0, 1, 1, 1); Off(box, 12, -28, 0, 0);

        void Item(string label, Color color)
        {
            var dot = new ColorRect { CustomMinimumSize = new Vector2(10, 10) };
            dot.Color = color;
            box.AddChild(dot);
            var lbl = new Label { Text = label };
            lbl.AddThemeFontSizeOverride("font_size", 11);
            lbl.Modulate = new Color(0.6f, 0.65f, 0.7f);
            box.AddChild(lbl);
        }

        Item("Star",       SectorColor(SectorObjectType.Star));
        Item("Planet",     SectorColor(SectorObjectType.Planet));
        Item("Asteroid",   SectorColor(SectorObjectType.Asteroid));
        Item("System",     SectorColor(SectorObjectType.SolarSystem));
        Item("Unknown",    new Color(0.2f, 0.22f, 0.28f));
        Item("● Probe",    new Color(0.3f, 0.8f, 1.0f));

        return box;
    }

    // ── Selection & info ──────────────────────────────────────────────────────

    internal void SelectSector((int X, int Y, int Z) coords)
    {
        _selected = coords;
        _canvas.QueueRedraw();

        if (!AppState.Instance.KnownSectors.TryGetValue(coords, out var obs))
        {
            _infoBox.Visible = false;
            return;
        }

        var dominant = DominantType(obs);
        var objects  = obs.Objects is { Count: > 0 } o
            ? string.Join("\n", o.Take(4).Select(obj =>
                $"  {obj.ObjectType.ToString().ToLowerInvariant()} — {obj.Name ?? "unnamed"}"))
            : "  (no objects)";

        _infoLabel.Text =
            $"Sector ({coords.X}, {coords.Y}, {coords.Z})\n" +
            $"Type:       {dominant.ToString().ToLowerInvariant()}\n" +
            $"Knowledge:  {obs.KnowledgeLevel.ToString().ToLowerInvariant()}\n" +
            $"Confidence: {obs.Confidence * 100.0:F0}%\n" +
            $"Scan:       {obs.Scan.ScanQuality * 100.0:F0}%\n" +
            $"Distance:   {obs.Distance}\n" +
            $"\nObjects:\n{objects}";

        _infoBox.Visible = true;
    }

    internal (int X, int Y, int Z)? Selected => _selected;

    // ── Drawing helpers (called from _MapCanvas) ───────────────────────────────

    internal static Color SectorColor(SectorObjectType type) => type switch
    {
        SectorObjectType.Star        => new Color(1.0f,  0.85f, 0.2f),
        SectorObjectType.Planet      => new Color(0.2f,  0.6f,  1.0f),
        SectorObjectType.Asteroid    => new Color(0.55f, 0.45f, 0.35f),
        SectorObjectType.SolarSystem => new Color(0.85f, 0.85f, 1.0f),
        SectorObjectType.BlackHole   => new Color(0.5f,  0.1f,  0.7f),
        SectorObjectType.DustCloud   => new Color(0.4f,  0.45f, 0.5f),
        _                            => new Color(0.2f,  0.22f, 0.28f),
    };

    internal static Color SectorColor(SectorObjectType type, float alpha)
    {
        var c = SectorColor(type); return new Color(c.R, c.G, c.B, alpha);
    }

    internal static SectorObjectType DominantType(SectorObservation obs)
    {
        if (obs.Objects is not { Count: > 0 } o) return SectorObjectType.Unknown;
        if (o.Any(x => x.ObjectType == SectorObjectType.Star))        return SectorObjectType.Star;
        if (o.Any(x => x.ObjectType == SectorObjectType.BlackHole))   return SectorObjectType.BlackHole;
        if (o.Any(x => x.ObjectType == SectorObjectType.SolarSystem)) return SectorObjectType.SolarSystem;
        if (o.Any(x => x.ObjectType == SectorObjectType.Planet))      return SectorObjectType.Planet;
        if (o.Any(x => x.ObjectType == SectorObjectType.Asteroid))    return SectorObjectType.Asteroid;
        return SectorObjectType.Unknown;
    }

    // ── Anchor helpers ────────────────────────────────────────────────────────

    private static void Anc(Control c, float l, float t, float r, float b)
        => (c.AnchorLeft, c.AnchorTop, c.AnchorRight, c.AnchorBottom) = (l, t, r, b);

    private static void Off(Control c, float l, float t, float r, float b)
        => (c.OffsetLeft, c.OffsetTop, c.OffsetRight, c.OffsetBottom) = (l, t, r, b);
}

// ── Drawing control (separate Godot partial class, same file) ─────────────────

internal partial class MapCanvas : Control
{
    private readonly MapPanel _panel;

    public MapCanvas(MapPanel panel) => _panel = panel;

    public override void _Draw()
    {
        var sectors = AppState.Instance.KnownSectors;
        if (sectors.Count == 0)
        {
            DrawString(ThemeDB.FallbackFont, Size / 2 - new Vector2(100, 8),
                "No sectors discovered yet", HorizontalAlignment.Left,
                -1, 13, new Color(0.4f, 0.45f, 0.5f));
            return;
        }

        var (transform, cellSize) = ComputeTransform(sectors.Keys);

        // Grid background lines
        DrawGridLines(transform, sectors.Keys, cellSize);

        // Sectors
        foreach (var (coords, obs) in sectors)
        {
            var screen = GridToScreen(coords.X, coords.Y, transform, cellSize);
            var rect   = new Rect2(screen + Vector2.One, new Vector2(cellSize - 2, cellSize - 2));

            var dominant = MapPanel.DominantType(obs);
            var alpha    = (float)Math.Clamp(obs.Confidence * 0.8 + 0.2, 0.2, 1.0);
            DrawRect(rect, MapPanel.SectorColor(dominant, alpha), true);

            // Selection highlight
            if (_panel.Selected == coords)
                DrawRect(rect, new Color(1, 1, 1, 0.5f), false, 2);

            // Z label if not zero (for multi-layer awareness)
            if (coords.Z != 0 && cellSize >= 18)
            {
                DrawString(ThemeDB.FallbackFont, screen + new Vector2(2, cellSize - 4),
                    $"z{coords.Z}", HorizontalAlignment.Left, -1, 10,
                    new Color(1, 1, 1, 0.5f));
            }

            // Knowledge indicator: dim border for low-confidence sectors
            if (obs.KnowledgeLevel == KnowledgeLevel.LongRangeEstimation)
                DrawRect(rect, new Color(0, 0, 0, 0.35f), true);
        }

        // Probe / travel overlay
        var probe = AppState.Instance.Probe;
        if (probe is null) return;

        if (probe.Movement is { } mv && mv.ArrivalAt > DateTimeOffset.UtcNow)
        {
            // ── In transit ───────────────────────────────────────────────────
            var ox = (int)Math.Round(mv.Origin.X);
            var oy = (int)Math.Round(mv.Origin.Y);
            var tx = (int)Math.Round(mv.Target.X);
            var ty = (int)Math.Round(mv.Target.Y);

            var originPt = GridToScreen(ox, oy, transform, cellSize)
                         + new Vector2(cellSize * 0.5f, cellSize * 0.5f);
            var targetPt = GridToScreen(tx, ty, transform, cellSize)
                         + new Vector2(cellSize * 0.5f, cellSize * 0.5f);

            // Dashed travel line
            var dir    = (targetPt - originPt);
            float len  = dir.Length();
            if (len > 0.1f)
            {
                var unit   = dir / len;
                float dash = 8f, gap = 5f, pos = 0f;
                while (pos < len)
                {
                    var from = originPt + unit * pos;
                    var to   = originPt + unit * Math.Min(pos + dash, len);
                    DrawLine(from, to, new Color(0.3f, 0.8f, 1.0f, 0.5f), 1.5f);
                    pos += dash + gap;
                }
            }

            // Destination marker — pulsing ring
            float pulse = (float)(0.5 + 0.5 * Math.Sin(Time.GetTicksMsec() * 0.005));
            DrawRect(
                new Rect2(targetPt - new Vector2(cellSize * 0.5f, cellSize * 0.5f),
                          new Vector2(cellSize, cellSize)),
                new Color(0.3f, 0.8f, 1.0f, 0.15f + 0.15f * pulse), true);
            DrawRect(
                new Rect2(targetPt - new Vector2(cellSize * 0.5f, cellSize * 0.5f),
                          new Vector2(cellSize, cellSize)),
                new Color(0.3f, 0.8f, 1.0f, 0.6f + 0.3f * pulse), false, 2f);

            // Animated probe dot interpolated along the line
            var totalSec = (mv.ArrivalAt - mv.StartedAt).TotalSeconds;
            double progress = totalSec > 0
                ? Math.Clamp((DateTimeOffset.UtcNow - mv.StartedAt).TotalSeconds / totalSec, 0, 1)
                : 1;
            var probePos = originPt.Lerp(targetPt, (float)progress);
            var r = cellSize * 0.28f;
            DrawCircle(probePos, r + 2, new Color(0, 0, 0, 0.6f));
            DrawCircle(probePos, r,     new Color(0.3f, 0.8f, 1.0f));
            // Speed trail
            if (progress > 0.01)
            {
                var trailEnd = originPt.Lerp(targetPt, (float)Math.Max(progress - 0.12, 0));
                DrawLine(probePos, trailEnd, new Color(0.3f, 0.8f, 1.0f, 0.3f), r * 1.5f);
            }
        }
        else if (probe.Sector?.Relative is { } rel)
        {
            // ── At rest ──────────────────────────────────────────────────────
            int px = (int)Math.Round(rel.X), py = (int)Math.Round(rel.Y);
            var center = GridToScreen(px, py, transform, cellSize)
                       + new Vector2(cellSize * 0.5f, cellSize * 0.5f);
            var r = cellSize * 0.28f;
            DrawCircle(center, r + 2, new Color(0, 0, 0, 0.6f));
            DrawCircle(center, r,     new Color(0.3f, 0.8f, 1.0f));
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;

        var sectors = AppState.Instance.KnownSectors;
        if (sectors.Count == 0) return;

        var (transform, cellSize) = ComputeTransform(sectors.Keys);

        foreach (var coords in sectors.Keys)
        {
            var screen = GridToScreen(coords.X, coords.Y, transform, cellSize);
            var rect   = new Rect2(screen, new Vector2(cellSize, cellSize));
            if (rect.HasPoint(mb.Position))
            {
                _panel.SelectSector(coords);
                return;
            }
        }

        // Click on empty space: deselect
        _panel.SelectSector((-9999, -9999, -9999));
    }

    // ── Transform helpers ──────────────────────────────────────────────────────

    private ((float OriginX, float OriginY) Transform, float CellSize) ComputeTransform(
        IEnumerable<(int X, int Y, int Z)> keys)
    {
        var list = keys.ToList();
        int minX = list.Min(k => k.X) - 1, maxX = list.Max(k => k.X) + 1;
        int minY = list.Min(k => k.Y) - 1, maxY = list.Max(k => k.Y) + 1;

        int cols = maxX - minX + 1, rows = maxY - minY + 1;
        float cellSize = Mathf.Clamp(
            Mathf.Min(Size.X / cols, Size.Y / rows),
            8f, 64f);

        float gridW = cols * cellSize, gridH = rows * cellSize;
        float ox = (Size.X - gridW) / 2f - minX * cellSize;
        float oy = (Size.Y - gridH) / 2f + maxY * cellSize;  // Y flipped

        return ((ox, oy), cellSize);
    }

    private static Vector2 GridToScreen(int gx, int gy,
        (float OriginX, float OriginY) t, float cellSize) =>
        new(t.OriginX + gx * cellSize, t.OriginY - gy * cellSize);

    private void DrawGridLines(
        (float OriginX, float OriginY) t,
        IEnumerable<(int X, int Y, int Z)> keys,
        float cellSize)
    {
        if (cellSize < 20) return;  // too small to show grid
        var gridColor = new Color(0.15f, 0.17f, 0.22f, 0.6f);
        var list = keys.ToList();
        int minX = list.Min(k => k.X) - 1, maxX = list.Max(k => k.X) + 1;
        int minY = list.Min(k => k.Y) - 1, maxY = list.Max(k => k.Y) + 1;

        for (int x = minX; x <= maxX + 1; x++)
        {
            var from = GridToScreen(x, minY, t, cellSize);
            var to   = GridToScreen(x, maxY + 1, t, cellSize);
            DrawLine(from, to, gridColor);
        }
        for (int y = minY; y <= maxY + 1; y++)
        {
            var from = GridToScreen(minX, y, t, cellSize);
            var to   = GridToScreen(maxX + 1, y, t, cellSize);
            DrawLine(from, to, gridColor);
        }
    }
}
