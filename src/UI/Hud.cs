using Godot;
using NeumannOrbital.Api.Models;
using NeumannOrbital.State;

namespace NeumannOrbital.UI;

public partial class Hud : CanvasLayer
{
    // Top bar
    private Label _probeLabel = null!;
    private LineEdit _navX = null!;
    private LineEdit _navY = null!;
    private LineEdit _navZ = null!;

    // Gauge strip
    private Label _fuelLabel = null!;
    private Label _integrityLabel = null!;
    private Label _cargoLabel = null!;
    private Label _etaLabel = null!;

    // Main panels
    private VBoxContainer _currentBox = null!;
    private VBoxContainer _neighborsBox = null!;
    private VBoxContainer _manniesBox = null!;

    private MapPanel _mapPanel = null!;
    private WarpOverlay _warpOverlay = null!;
    private int _scanPending;

    private bool IsScanning => _scanPending > 0;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        Layer = 10;

        _warpOverlay = new WarpOverlay();
        AddChild(_warpOverlay);

        _mapPanel = new MapPanel();
        Anc(_mapPanel, 0, 0, 1, 1);
        AddChild(_mapPanel);

        BuildLayout();

        var s = AppState.Instance;
        s.ProbeUpdated += OnProbeUpdated;
        s.ManniesUpdated += OnManniesUpdated;
        s.SectorDiscovered += OnSectorDiscovered;
        s.SectorUpdated += OnSectorUpdated;

        OnProbeUpdated();
        OnManniesUpdated();
    }

    public override void _ExitTree()
    {
        if (AppState.Instance is not { } s) return;
        s.ProbeUpdated -= OnProbeUpdated;
        s.ManniesUpdated -= OnManniesUpdated;
        s.SectorDiscovered -= OnSectorDiscovered;
        s.SectorUpdated -= OnSectorUpdated;
    }

    private void OnSectorDiscovered(int x, int y, int z) => RenderNeighbors();

    public override void _Process(double _)
    {
        if (AppState.Instance.Probe?.Movement is not { } mv) return;

        var rem = mv.ArrivalAt - DateTimeOffset.UtcNow;

        _etaLabel.Text = rem > TimeSpan.Zero ? $"ETA {Helpers.FormatDuration(rem)}" : "Arriving…";
        _etaLabel.Modulate = new Color(0.9f, 0.85f, 0.4f);
    }

    private void OnProbeUpdated()
    {
        var inTransit = AppState.Instance.Probe?.Movement is { } mv
                        && mv.ArrivalAt > DateTimeOffset.UtcNow;
        if (inTransit) _warpOverlay.ShowWarp(); else _warpOverlay.HideWarp();

        RenderGauges();
        RenderCurrentSector();
        RenderNeighbors();
    }
    private void OnManniesUpdated() => RenderMannies();

    private void ScanSector(int x, int y, int z)
    {
        _scanPending++;
        _ = AppState.Instance.FetchSectorAsync(x, y, z);
        RenderNeighbors();
    }

    private void ScanCurrentSector()
    {
        var c = ProbeCoords();
        if (c is { } p) ScanSector(p.X, p.Y, p.Z);
    }

    private void ScanAllNeighbors()
    {
        var c = ProbeCoords();
        if (c is null) return;
        var neighbors = FccNeighbors(c.Value.X, c.Value.Y, c.Value.Z).ToList();
        _scanPending += neighbors.Count;
        foreach (var (x, y, z) in neighbors)
            _ = AppState.Instance.FetchSectorAsync(x, y, z);
        RenderNeighbors();
    }

    private void OnSectorUpdated(int x, int y, int z)
    {
        var probe = ProbeCoords();
        if (probe is { } p && p.X == x && p.Y == y && p.Z == z)
            RenderCurrentSector();

        if (_scanPending > 0) _scanPending--;
        RenderNeighbors();
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private void BuildLayout()
    {
        const float topH = 40f;
        const float gaugeH = 28f;

        // ── Top bar ──────────────────────────────────────────────────────────
        var topPanel = Panel(new Color(0.03f, 0.04f, 0.08f, 0.85f));
        Anc(topPanel, 0, 0, 1, 0); Off(topPanel, 0, 0, 0, topH);
        AddChild(topPanel);

        var topRow = new HBoxContainer();
        topRow.AddThemeConstantOverride("separation", 8);
        Anc(topRow, 0, 0, 1, 1); Off(topRow, 10, 0, -8, 0);
        topPanel.AddChild(topRow);

        _probeLabel = new Label
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _probeLabel.AddThemeFontSizeOverride("font_size", 13);
        topRow.AddChild(_probeLabel);

        // Navigation controls
        var navLbl = new Label { Text = "GO" };
        navLbl.AddThemeFontSizeOverride("font_size", 11);
        navLbl.Modulate = new Color(0.5f, 0.6f, 0.75f);
        navLbl.VerticalAlignment = VerticalAlignment.Center;
        topRow.AddChild(navLbl);

        _navX = NavInput("X"); topRow.AddChild(_navX);
        _navY = NavInput("Y"); topRow.AddChild(_navY);
        _navZ = NavInput("Z"); topRow.AddChild(_navZ);
        topRow.AddChild(Btn("Go", OnGoPressed));

        topRow.AddChild(Btn("Map [M]", () => _mapPanel.Toggle()));
        topRow.AddChild(Btn("Refresh [R]", () => AppState.Instance.TriggerRefresh()));
        topRow.AddChild(Btn("Quit [Q]", () => GetTree().Quit()));

        // ── Gauge strip ───────────────────────────────────────────────────────
        var gaugePanel = Panel(new Color(0.02f, 0.03f, 0.06f, 0.8f));
        Anc(gaugePanel, 0, 0, 1, 0); Off(gaugePanel, 0, topH + 1f, 0, topH + gaugeH + 1f);
        AddChild(gaugePanel);

        var gaugeRow = new HBoxContainer();
        gaugeRow.AddThemeConstantOverride("separation", 20);
        Anc(gaugeRow, 0, 0, 1, 1); Off(gaugeRow, 12, 0, -12, 0);
        gaugePanel.AddChild(gaugeRow);

        _fuelLabel = GaugeLabel(gaugeRow);
        _integrityLabel = GaugeLabel(gaugeRow);
        _cargoLabel = GaugeLabel(gaugeRow);
        _etaLabel = GaugeLabel(gaugeRow);

        // ── Left panel: current sector + mannies ──────────────────────────────
        // 15% wide, anchored to the left edge, vertically centred (20%–82%)
        var leftPanel = Panel(new Color(0.03f, 0.04f, 0.08f, 0.82f));
        Anc(leftPanel, 0f, 0.20f, 0.15f, 0.82f);
        AddChild(leftPanel);

        var leftScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        Anc(leftScroll, 0, 0, 1, 1); Off(leftScroll, 6, 6, -6, -6);
        leftPanel.AddChild(leftScroll);

        var leftBox = new VBoxContainer();
        leftBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftScroll.AddChild(leftBox);

        _currentBox = AddSection(leftBox, "CURRENT SECTOR");
        AddSeparator(leftBox);
        _manniesBox = AddSection(leftBox, "MANNIES");

        // ── Right panel: neighbors ───────────────────────────────────────────
        // 15% wide, anchored to the right edge, vertically centred (20%–82%)
        var rightPanel = Panel(new Color(0.02f, 0.03f, 0.07f, 0.82f));
        Anc(rightPanel, 0.85f, 0.20f, 1f, 0.82f);
        AddChild(rightPanel);

        var rightScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        Anc(rightScroll, 0, 0, 1, 1); Off(rightScroll, 6, 6, -6, -6);
        rightPanel.AddChild(rightScroll);

        var rightBox = new VBoxContainer();
        rightBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightScroll.AddChild(rightBox);

        _neighborsBox = new VBoxContainer();
        _neighborsBox.AddThemeConstantOverride("separation", 2);
        _neighborsBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightBox.AddChild(_neighborsBox);
    }

    // ── Gauges ────────────────────────────────────────────────────────────────

    private void RenderGauges()
    {
        var probe = AppState.Instance.Probe;
        if (probe is null)
        {
            _probeLabel.Text = "NEUMANN ORBITAL   connecting…";
            _fuelLabel.Text = _integrityLabel.Text = _cargoLabel.Text = _etaLabel.Text = "—";
            return;
        }

        var dot = probe.Status switch
        {
            ProbeStatus.Dead or ProbeStatus.Disabled => "✖",
            ProbeStatus.Idle => "●",
            _ => "◉",
        };
        _probeLabel.Text = $"NEUMANN ORBITAL    {probe.Name}    {dot} {probe.Status.ToString().ToUpperInvariant()}";

        var fuel = Ratio(probe.Fuel.Deuterium ?? 0, 100.0);
        _fuelLabel.Text = $"Fuel  {Bar(fuel, 6)}  {fuel * 100:F0}%";
        _fuelLabel.Modulate = GaugeColor(fuel);

        var integrity = probe.Systems is { } sys ? Ratio(sys.IntegrityPercent ?? 100, 100) : 1.0;
        _integrityLabel.Text = $"Integrity  {Bar(integrity, 6)}  {integrity * 100:F0}%";
        _integrityLabel.Modulate = GaugeColor(integrity);

        var inv = probe.Inventory;
        var cargo = inv.Capacity > 0 ? Ratio(inv.UsedCapacity, inv.Capacity) : 0.0;
        _cargoLabel.Text = $"Cargo  {Bar(cargo, 6)}  {inv.UsedCapacity:F1}/{inv.Capacity:F1}";
        _cargoLabel.Modulate = GaugeColor(cargo);

        if (probe.Movement is null) _etaLabel.Text = "";
    }

    // ── Current sector ────────────────────────────────────────────────────────

    private void RenderCurrentSector()
    {
        Clear(_currentBox);
        var coords = ProbeCoords();
        if (coords is null)
        {
            Lbl(_currentBox, "no position data", Dim);
            return;
        }
        var (cx, cy, cz) = coords.Value;

        if (!AppState.Instance.KnownSectors.TryGetValue(coords.Value, out var obs))
        {
            Lbl(_currentBox, $"({cx},{cy},{cz})  not yet scanned", Dim);
            return;
        }

        // Header
        Lbl(_currentBox, $"({cx},{cy},{cz})  {KnowledgeAbbrev(obs.KnowledgeLevel)}  {Bar(obs.Scan.ScanQuality, 6)}  {obs.Scan.ScanQuality * 100:F0}%",
            KnowledgeColor(obs.KnowledgeLevel));
        if (obs.Scan.RequiredResidenceSeconds > 0)
            Lbl(_currentBox,
                $"Residence: {obs.Scan.CurrentSectorResidenceSeconds}s / {obs.Scan.RequiredResidenceSeconds}s",
                Dim);

        // Objects — sorted: systems/stars first, then planets, asteroids, rest
        if (obs.Objects is { Count: > 0 } objects)
        {
            var sorted = objects.OrderBy(ObjectSortKey);
            foreach (var obj in sorted)
            {
                var spacer = new Control { CustomMinimumSize = new Vector2(0, 4) };
                _currentBox.AddChild(spacer);
                RenderObject(_currentBox, obj, detailed: true);
            }
        }
        else if (obs.EstimatedObjects is { } est)
        {
            RenderEstimated(_currentBox, est);
        }
        else
        {
            Lbl(_currentBox, "no objects detected", Dim);
        }
    }

    // ── Neighbors ─────────────────────────────────────────────────────────────

    private void RenderNeighbors()
    {
        Clear(_neighborsBox);
        var probe = ProbeCoords();
        if (probe is null) { Lbl(_neighborsBox, "no position data", Dim); return; }
        var (px, py, pz) = probe.Value;
        var sectors = AppState.Instance.KnownSectors;

        // ── Current sector scan ───────────────────────────────────────────────
        SectionHeader(_neighborsBox, "CURRENT SECTOR");
        if (sectors.TryGetValue((px, py, pz), out var cur))
        {
            var scanPct = cur.Scan.ScanQuality * 100;
            var curRow = new HBoxContainer();
            curRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _neighborsBox.AddChild(curRow);

            var curLbl = new Label
            {
                Text = $"({px},{py},{pz})  {Bar(cur.Scan.ScanQuality, 5)}  {scanPct:F0}%",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            curLbl.AddThemeFontSizeOverride("font_size", 12);
            curLbl.Modulate = scanPct >= 99.9 ? Dim : KnowledgeColor(cur.KnowledgeLevel);
            curRow.AddChild(curLbl);

            var scanCurBtn = SmallBtn(IsScanning ? "…" : "↺", ScanCurrentSector);
            scanCurBtn.Disabled = IsScanning || scanPct >= 99.9;
            if (scanPct >= 99.9) scanCurBtn.TooltipText = "Already at 100%";
            curRow.AddChild(scanCurBtn);
        }
        else
        {
            Lbl(_neighborsBox, $"({px},{py},{pz})  not scanned", Dim);
        }

        // ── FCC neighbors ─────────────────────────────────────────────────────
        var sep0 = new HSeparator();
        sep0.Modulate = new Color(0.2f, 0.25f, 0.35f, 0.5f);
        _neighborsBox.AddChild(sep0);

        var hdrRow = new HBoxContainer();
        hdrRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _neighborsBox.AddChild(hdrRow);

        var hdrLbl = new Label { Text = "FCC NEIGHBORS", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        hdrLbl.AddThemeFontSizeOverride("font_size", 11);
        hdrLbl.Modulate = new Color(0.45f, 0.55f, 0.75f);
        hdrRow.AddChild(hdrLbl);

        var scanAllBtn = SmallBtn(IsScanning ? "Scanning…" : "Scan All", ScanAllNeighbors);
        scanAllBtn.Disabled = IsScanning;
        hdrRow.AddChild(scanAllBtn);

        var neighbors = FccNeighbors(px, py, pz)
            .OrderBy(c => sectors.TryGetValue(c, out var o) ? SectorSortKey(o) : 99)
            .ToList();

        foreach (var coords in neighbors)
        {
            var rowSep = new HSeparator();
            rowSep.Modulate = new Color(0.15f, 0.18f, 0.28f, 0.5f);
            _neighborsBox.AddChild(rowSep);

            var row = new HBoxContainer();
            row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _neighborsBox.AddChild(row);

            if (sectors.TryGetValue(coords, out var obs))
            {
                var ast = HasAsteroids(obs) ? " ▲" : "";
                var lbl = new Label
                {
                    Text = $"({coords.X},{coords.Y},{coords.Z})  {KnowledgeAbbrev(obs.KnowledgeLevel)}" +
                           $"  {Bar(obs.Confidence, 4)}  {obs.Confidence * 100:F0}%{ast}",
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                };
                lbl.AddThemeFontSizeOverride("font_size", 12);
                lbl.Modulate = HasAsteroids(obs) ? AsteroidColor : KnowledgeColor(obs.KnowledgeLevel);
                row.AddChild(lbl);
            }
            else
            {
                var lbl = new Label
                {
                    Text = $"({coords.X},{coords.Y},{coords.Z})  unknown",
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                };
                lbl.AddThemeFontSizeOverride("font_size", 12);
                lbl.Modulate = Dim;
                row.AddChild(lbl);
            }

            var (cx, cy, cz) = coords;
            var btn = SmallBtn(IsScanning ? "…" : "↺", () => ScanSector(cx, cy, cz));
            btn.Disabled = IsScanning;
            row.AddChild(btn);

            // Compact object detail for known sectors
            if (sectors.TryGetValue(coords, out var obs2) && obs2.Objects is { Count: > 0 } objs)
                foreach (var obj in objs)
                    RenderObject(_neighborsBox, obj, detailed: false);
        }
    }

    // ── Mannies ───────────────────────────────────────────────────────────────

    private void RenderMannies()
    {
        Clear(_manniesBox);
        var mannies = AppState.Instance.Mannies;
        if (mannies.Count == 0) { Lbl(_manniesBox, "no mannies", Dim); return; }

        foreach (var manny in mannies)
        {
            var dot = manny.CurrentTask is null ? "◌" : "●";
            var task = manny.CurrentTask?.ToString().ToLowerInvariant() ?? "idle";
            var prog = manny.CurrentTask is null ? "" : $"  {manny.TaskProgressPercent:F0}%";
            var btn = MannyBtn(manny, $"{dot} {manny.Name}  {task}{prog}");
            btn.Modulate = manny.CurrentTask switch
            {
                MannyTask.Repair => new Color(1.0f, 0.4f, 0.4f),
                null => new Color(0.5f, 0.5f, 0.6f),
                _ => new Color(1.0f, 0.85f, 0.25f),
            };
            _manniesBox.AddChild(btn);
        }
    }

    // ── Object rendering ──────────────────────────────────────────────────────

    private void RenderObject(VBoxContainer box, Api.Models.SectorObject obj, bool detailed)
    {
        var icon = obj.ObjectType switch
        {
            SectorObjectType.SolarSystem => "★",
            SectorObjectType.Star => "✦",
            SectorObjectType.Planet => "◑",
            SectorObjectType.Asteroid => "▲",
            SectorObjectType.BlackHole => "◉",
            SectorObjectType.DustCloud => "∿",
            _ => "·",
        };

        // Name / type header — colour by object type
        Lbl(box, $"{icon}  {obj.Name ?? obj.ObjectType.ToString().ToLowerInvariant()}",
            ObjectTypeColor(obj.ObjectType));

        if (detailed && obj.ObjectType == SectorObjectType.SolarSystem)
        {
            if (obj.BookmarkTargets is { Count: > 0 } bk)
            {
                // Real data from API
                foreach (var b in bk)
                {
                    switch (b.ObjectType)
                    {
                        case SectorObjectType.Star:
                            Lbl(box,
                                $"   ✦  star  {b.Mass:F2} M☉  [{SpectralClass(b.Mass ?? 1.0)}]" +
                                (b.Radius is { } r ? $"  r={r:F2}" : ""),
                                SpectralColor(b.Mass ?? 1.0));
                            break;

                        case SectorObjectType.Planet:
                            Lbl(box,
                                $"   ◑  planet  {b.Mass:F2} M☉" +
                                (b.Radius is { } pr ? $"  r={pr:F2}" : ""),
                                new Color(0.5f, 0.78f, 1.0f));
                            break;

                        case SectorObjectType.Asteroid:
                            var res = obj.MinableTargets?
                                .FirstOrDefault(mt => mt.Id == b.Id)
                                ?.ResourceTypes is { Count: > 0 } rt
                                ? string.Join("+", rt) : "rock";
                            Lbl(box,
                                $"   ▲  asteroid  {b.Mass:F4} M☉  {res}",
                                AsteroidColor);
                            break;

                        case SectorObjectType.BlackHole:
                            Lbl(box, $"   ◉  black hole  {b.Mass:F1} M☉",
                                new Color(0.8f, 0.3f, 1.0f));
                            break;
                    }
                }
            }
            else
            {
                // Fallback: parse summary
                int stars = Helpers.ParseStarCount(obj.Summary);
                int orbital = Helpers.ParseOrbitalCount(obj.Summary);
                double mps = (obj.Mass ?? 2.0) / Math.Max(stars, 1);
                for (int i = 0; i < stars; i++)
                    Lbl(box, $"   ✦  star  {mps:F1} M☉  [{SpectralClass(mps)}]", SpectralColor(mps));
                int asts = obj.MinableTargets?.Count ?? 0;
                int planets = Math.Max(orbital - asts, 0);
                for (int i = 0; i < planets; i++)
                    Lbl(box, "   ◑  planet", new Color(0.5f, 0.78f, 1.0f));
                if (obj.MinableTargets is { Count: > 0 } mt2)
                    foreach (var t in mt2)
                    {
                        var r2 = t.ResourceTypes is { Count: > 0 } rt ? string.Join("+", rt) : "?";
                        Lbl(box, $"   ▲  asteroid  {r2}  {t.Mass:F4}t", AsteroidColor);
                    }
            }

            if (obj.DangerLevel is { } danger && danger != DangerLevel.Unknown)
                Lbl(box, $"   danger: {danger.ToString().ToLowerInvariant()}", DangerColor(danger));
        }
        else if (detailed)
        {
            if (obj.Summary is { } summary)
                Lbl(box, $"   {summary}", new Color(0.65f, 0.7f, 0.85f));
            if (obj.Mass is { } mass)
                Lbl(box, $"   mass {mass:F2}  radius {obj.Radius:F2}", Dim);
            if (obj.DangerLevel is { } danger && danger != DangerLevel.Unknown)
                Lbl(box, $"   danger: {danger.ToString().ToLowerInvariant()}", DangerColor(danger));
            if (obj.MinableTargets is { Count: > 0 } targets)
            {
                foreach (var t in targets)
                {
                    var res = t.ResourceTypes is { Count: > 0 } rt ? string.Join("+", rt) : "?";
                    Lbl(box, $"   ▲  {res}  {t.Mass:F4}t", AsteroidColor);
                }
            }
        }
        else
        {
            // Compact neighbor view
            if (obj.ObjectType == SectorObjectType.SolarSystem)
            {
                if (obj.BookmarkTargets is { Count: > 0 } bk)
                {
                    // Stars — always show individually (usually 1–2)
                    foreach (var s in bk.Where(b => b.ObjectType == SectorObjectType.Star))
                        Lbl(box, $"   ✦ [{SpectralClass(s.Mass ?? 1.0)}]  {s.Mass:F1} M☉",
                            SpectralColor(s.Mass ?? 1.0));

                    // Planets — group as count
                    int pc = bk.Count(b => b.ObjectType == SectorObjectType.Planet);
                    if (pc > 0)
                        Lbl(box, $"   ◑ {pc} planet{(pc > 1 ? "s" : "")}",
                            new Color(0.5f, 0.78f, 1.0f));

                    // Asteroids — show each with resources
                    foreach (var a in bk.Where(b => b.ObjectType == SectorObjectType.Asteroid))
                    {
                        var res = obj.MinableTargets?.FirstOrDefault(mt => mt.Id == a.Id)
                            ?.ResourceTypes is { Count: > 0 } rt ? string.Join("+", rt) : "rock";
                        Lbl(box, $"   ▲ asteroid  {res}", AsteroidColor);
                    }
                }
                else
                {
                    // Fallback to summary parsing
                    int stars = Helpers.ParseStarCount(obj.Summary);
                    int orbital = Helpers.ParseOrbitalCount(obj.Summary);
                    double mps = (obj.Mass ?? 2.0) / Math.Max(stars, 1);
                    for (int i = 0; i < stars; i++)
                        Lbl(box, $"   ✦ [{SpectralClass(mps)}]  {mps:F1} M☉", SpectralColor(mps));
                    int asts = obj.MinableTargets?.Count ?? 0;
                    int pls = Math.Max(orbital - asts, 0);
                    if (pls > 0)
                        Lbl(box, $"   ◑ {pls} planet{(pls > 1 ? "s" : "")}",
                            new Color(0.5f, 0.78f, 1.0f));
                    if (obj.MinableTargets is { Count: > 0 } mt2)
                        foreach (var t in mt2)
                        {
                            var r2 = t.ResourceTypes is { Count: > 0 } rt ? string.Join("+", rt) : "?";
                            Lbl(box, $"   ▲ {r2}", AsteroidColor);
                        }
                }
            }
            else
            {
                // Non-system objects in compact mode
                if (obj.Mass is { } m)
                    Lbl(box, $"   {m:F2} M☉" + (obj.Radius is { } r ? $"  r={r:F2}" : ""), Dim);
                if (obj.MinableTargets is { Count: > 0 } targets)
                    foreach (var t in targets)
                    {
                        var res = t.ResourceTypes is { Count: > 0 } rt ? string.Join("+", rt) : "?";
                        Lbl(box, $"   ▲ {res}", AsteroidColor);
                    }
            }

            if (obj.DangerLevel is { } d && d != DangerLevel.Unknown)
                Lbl(box, $"   {d.ToString().ToLowerInvariant()}", DangerColor(d));
        }
    }

    private void RenderEstimated(VBoxContainer box, EstimatedObjects est)
    {
        if (est.Star == true)
            Lbl(box, "   ✦ star likely", new Color(1.0f, 0.85f, 0.3f));

        if (est.PlanetCountMin is { } pmin && est.PlanetCountMax is { } pmax)
            Lbl(box, $"   ◑ planets: {pmin}–{pmax} estimated", Dim);

        if (est.DangerEstimate is { } d && d != DangerLevel.Unknown)
            Lbl(box, $"   danger: {d.ToString().ToLowerInvariant()}", DangerColor(d));

        if (est.BlackHoleProbability is { } bhp && bhp > 0.1)
            Lbl(box, $"   ◉ black hole: {bhp * 100:F0}% chance", new Color(0.7f, 0.2f, 1.0f));
    }

    // ── Manny popup ───────────────────────────────────────────────────────────

    private void ShowMannyPopup(Manny manny, Vector2 pos)
    {
        var popup = new PopupMenu();
        popup.AddItem($"[ {manny.Name} ]");
        popup.SetItemDisabled(popup.ItemCount - 1, true);
        popup.AddSeparator();
        popup.AddItem($"Task:   {manny.CurrentTask?.ToString().ToLowerInvariant() ?? "idle"}  {(manny.CurrentTask is null ? "" : manny.TaskProgressPercent + "%")}");
        popup.SetItemDisabled(popup.ItemCount - 1, true);
        popup.AddItem($"Cargo:  {manny.Cargo.Deuterium + manny.Cargo.Metals + manny.Cargo.Ice:F1} / {manny.Cargo.Capacity:F1}");
        popup.SetItemDisabled(popup.ItemCount - 1, true);
        popup.AddSeparator("Actions");

        const int IdRecall = 100;
        popup.AddItem("Recall to probe", IdRecall);
        popup.SetItemDisabled(popup.ItemCount - 1,
            manny.Location.LocationType != MannyLocationType.Sector);

        popup.IdPressed += (id) => { if (id == IdRecall) _ = AppState.Instance.RecallMannyAsync(manny.Id); popup.QueueFree(); };
        AddChild(popup);
        popup.Popup(new Rect2I((int)pos.X, (int)pos.Y, 0, 0));
    }

    // ── Widget helpers ────────────────────────────────────────────────────────

    // FCC nearest neighbors: exactly 2 of the 3 delta-coordinates are ±1, one is 0
    private static IEnumerable<(int X, int Y, int Z)> FccNeighbors(int x, int y, int z)
    {
        int[] d = { -1, 1 };
        foreach (var dx in d) foreach (var dy in d) yield return (x + dx, y + dy, z);
        foreach (var dx in d) foreach (var dz in d) yield return (x + dx, y, z + dz);
        foreach (var dy in d) foreach (var dz in d) yield return (x, y + dy, z + dz);
    }

    private static void SectionHeader(VBoxContainer parent, string title)
    {
        var h = new Label { Text = title };
        h.AddThemeFontSizeOverride("font_size", 11);
        h.Modulate = new Color(0.45f, 0.55f, 0.75f);
        parent.AddChild(h);
    }

    private static Button SmallBtn(string text, Action onPress)
    {
        var btn = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        btn.AddThemeFontSizeOverride("font_size", 11);
        btn.AddThemeStyleboxOverride("normal", BtnStyle(new Color(0.1f, 0.15f, 0.28f, 0.7f)));
        btn.AddThemeStyleboxOverride("hover", BtnStyle(new Color(0.2f, 0.3f, 0.5f, 0.85f)));
        btn.AddThemeStyleboxOverride("pressed", BtnStyle(new Color(0.06f, 0.1f, 0.22f, 0.9f)));
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        btn.Pressed += () => onPress();
        return btn;
    }

    private static VBoxContainer AddSection(VBoxContainer parent, string title)
    {
        var header = new Label { Text = title };
        header.AddThemeFontSizeOverride("font_size", 11);
        header.Modulate = new Color(0.45f, 0.55f, 0.75f);
        parent.AddChild(header);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        parent.AddChild(box);
        return box;
    }

    private static void AddSeparator(VBoxContainer parent)
    {
        var spacer = new Control { CustomMinimumSize = new Vector2(0, 6) };
        parent.AddChild(spacer);
        var sep = new HSeparator();
        sep.Modulate = new Color(0.2f, 0.25f, 0.35f, 0.5f);
        parent.AddChild(sep);
        var spacer2 = new Control { CustomMinimumSize = new Vector2(0, 4) };
        parent.AddChild(spacer2);
    }

    private static Label Lbl(VBoxContainer box, string text, Color? color = null)
    {
        var lbl = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.Word };
        lbl.AddThemeFontSizeOverride("font_size", 12);
        if (color.HasValue) lbl.Modulate = color.Value;
        box.AddChild(lbl);
        return lbl;
    }

    private static Label GaugeLabel(HBoxContainer parent)
    {
        var lbl = new Label { VerticalAlignment = VerticalAlignment.Center };
        lbl.AddThemeFontSizeOverride("font_size", 12);
        parent.AddChild(lbl);
        return lbl;
    }

    private Button MannyBtn(Manny manny, string text)
    {
        var btn = new Button
        {
            Text = text,
            Alignment = HorizontalAlignment.Left,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        btn.AddThemeFontSizeOverride("font_size", 12);
        btn.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        btn.AddThemeStyleboxOverride("hover", HoverStyle());
        btn.AddThemeStyleboxOverride("pressed", HoverStyle());
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        btn.Pressed += () => ShowMannyPopup(manny, btn.GlobalPosition + new Vector2(0, btn.Size.Y));
        return btn;
    }

    private static LineEdit NavInput(string placeholder)
    {
        var le = new LineEdit
        {
            PlaceholderText = placeholder,
            CustomMinimumSize = new Vector2(44, 0),
            MaxLength = 5,
            Alignment = HorizontalAlignment.Center,
        };
        le.AddThemeFontSizeOverride("font_size", 12);
        return le;
    }

    private void OnGoPressed()
    {
        if (!int.TryParse(_navX.Text, out var x) ||
            !int.TryParse(_navY.Text, out var y) ||
            !int.TryParse(_navZ.Text, out var z))
            return;
        _ = AppState.Instance.MoveProbeAsync(x, y, z);
    }

    private static Button Btn(string text, Action onPress)
    {
        var btn = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand
        };
        btn.AddThemeFontSizeOverride("font_size", 12);
        btn.AddThemeStyleboxOverride("normal", BtnStyle(new Color(0.12f, 0.18f, 0.3f, 0.7f)));
        btn.AddThemeStyleboxOverride("hover", BtnStyle(new Color(0.2f, 0.3f, 0.5f, 0.85f)));
        btn.AddThemeStyleboxOverride("pressed", BtnStyle(new Color(0.08f, 0.12f, 0.25f, 0.9f)));
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        btn.Pressed += () => onPress();
        return btn;
    }

    private static Panel Panel(Color bg)
    {
        var p = new Panel();
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = new Color(0.18f, 0.25f, 0.42f, 0.5f),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
        });
        return p;
    }

    private static StyleBoxFlat BtnStyle(Color bg) => new()
    {
        BgColor = bg,
        BorderColor = new Color(0.3f, 0.4f, 0.6f, 0.5f),
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        BorderWidthTop = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 3,
        CornerRadiusTopRight = 3,
        CornerRadiusBottomLeft = 3,
        CornerRadiusBottomRight = 3,
        ContentMarginLeft = 10,
        ContentMarginRight = 10,
        ContentMarginTop = 3,
        ContentMarginBottom = 3,
    };

    private static StyleBoxFlat HoverStyle() => new()
    {
        BgColor = new Color(0.2f, 0.3f, 0.5f, 0.25f),
        CornerRadiusTopLeft = 3,
        CornerRadiusTopRight = 3,
        CornerRadiusBottomLeft = 3,
        CornerRadiusBottomRight = 3,
    };

    private static void Clear(VBoxContainer box)
    {
        foreach (var child in box.GetChildren()) child.QueueFree();
    }

    // ── Sorting & colors ──────────────────────────────────────────────────────

    private static int SectorSortKey(SectorObservation obs) =>
        (HasAsteroids(obs) ? 0 : 10) + obs.KnowledgeLevel switch
        {
            KnowledgeLevel.Detailed => 0,
            KnowledgeLevel.NeighborScan => 1,
            KnowledgeLevel.DistantScan => 2,
            KnowledgeLevel.LongRangeEstimation => 3,
            _ => 4,
        };

    private static bool HasAsteroids(SectorObservation obs) =>
        obs.Objects?.Any(o => o.MinableTargets is { Count: > 0 }) == true;

    private static Color AsteroidColor => new(1.0f, 0.7f, 0.2f);
    private static Color Dim => new(0.4f, 0.42f, 0.5f);

    private static Color KnowledgeColor(KnowledgeLevel lvl) => lvl switch
    {
        KnowledgeLevel.Detailed => new Color(0.4f, 0.9f, 0.5f),
        KnowledgeLevel.NeighborScan => new Color(0.6f, 0.8f, 1.0f),
        KnowledgeLevel.DistantScan => new Color(0.6f, 0.65f, 0.8f),
        KnowledgeLevel.LongRangeEstimation => new Color(0.5f, 0.5f, 0.65f),
        _ => Dim,
    };

    private static Color DangerColor(DangerLevel d) => d switch
    {
        DangerLevel.Low => new Color(0.4f, 0.75f, 0.4f),
        DangerLevel.Moderate => new Color(0.9f, 0.75f, 0.2f),
        DangerLevel.Extreme => new Color(0.95f, 0.3f, 0.3f),
        _ => Dim,
    };

    private static Color ObjectTypeColor(SectorObjectType t) => t switch
    {
        SectorObjectType.SolarSystem => new Color(0.95f, 0.9f, 0.5f),  // warm gold
        SectorObjectType.Star => new Color(1.0f, 0.85f, 0.25f), // yellow
        SectorObjectType.Planet => new Color(0.5f, 0.78f, 1.0f),  // blue
        SectorObjectType.Asteroid => AsteroidColor,                   // orange
        SectorObjectType.BlackHole => new Color(0.8f, 0.3f, 1.0f),  // purple
        SectorObjectType.DustCloud => new Color(0.55f, 0.6f, 0.65f), // grey-blue
        _ => new Color(0.75f, 0.8f, 0.85f),
    };

    private static int ObjectSortKey(Api.Models.SectorObject o) => o.ObjectType switch
    {
        SectorObjectType.SolarSystem => 0,
        SectorObjectType.Star => 1,
        SectorObjectType.Planet => 2,
        SectorObjectType.Asteroid => 3,
        SectorObjectType.BlackHole => 4,
        _ => 5,
    };

    private static Color GaugeColor(double r) => r switch
    {
        > 0.5 => new Color(0.3f, 0.9f, 0.3f),
        > 0.25 => new Color(0.9f, 0.8f, 0.2f),
        _ => new Color(0.9f, 0.2f, 0.2f),
    };

    // ── Misc helpers ──────────────────────────────────────────────────────────

    private static (int X, int Y, int Z)? ProbeCoords()
    {
        var rel = AppState.Instance.Probe?.Sector?.Relative;
        if (rel is null) return null;
        return ((int)Math.Round(rel.X), (int)Math.Round(rel.Y), (int)Math.Round(rel.Z));
    }

    private static string Bar(double ratio, int w)
    {
        var f = (int)Math.Round(Math.Clamp(ratio, 0, 1) * w);
        return new string('█', f) + new string('░', w - f);
    }

    private static double Ratio(double v, double max) => max > 0 ? Math.Clamp(v / max, 0, 1) : 0;

    private static string KnowledgeAbbrev(KnowledgeLevel l) => l switch
    {
        KnowledgeLevel.Detailed => "detailed",
        KnowledgeLevel.NeighborScan => "neighbor",
        KnowledgeLevel.DistantScan => "distant",
        KnowledgeLevel.LongRangeEstimation => "estimated",
        _ => "?",
    };

    private static string SpectralClass(double mass) => mass switch
    {
        >= 30.0 => "O",
        >= 10.0 => "B",
        >= 4.0 => "A",
        >= 1.5 => "F",
        >= 0.9 => "G",
        >= 0.5 => "K",
        _ => "M",
    };

    private static Color SpectralColor(double mass) => mass switch
    {
        >= 30.0 => new Color(0.65f, 0.75f, 1.0f),
        >= 10.0 => new Color(0.80f, 0.88f, 1.0f),
        >= 4.0 => new Color(0.98f, 0.98f, 1.0f),
        >= 1.5 => new Color(1.0f, 1.0f, 0.85f),
        >= 0.9 => new Color(1.0f, 0.92f, 0.5f),
        >= 0.5 => new Color(1.0f, 0.65f, 0.3f),
        _ => new Color(1.0f, 0.3f, 0.15f),
    };

    private static void Anc(Control c, float l, float t, float r, float b)
        => (c.AnchorLeft, c.AnchorTop, c.AnchorRight, c.AnchorBottom) = (l, t, r, b);

    private static void Off(Control c, float l, float t, float r, float b)
        => (c.OffsetLeft, c.OffsetTop, c.OffsetRight, c.OffsetBottom) = (l, t, r, b);
}
