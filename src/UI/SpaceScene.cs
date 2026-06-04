using Godot;
using NeumannOrbital.Api.Models;
using NeumannOrbital.State;
using ApiSectorObject = NeumannOrbital.Api.Models.SectorObject;

namespace NeumannOrbital.UI;

public partial class SpaceScene : Node3D
{
    private Node3D    _starsRoot  = null!;
    private CameraRig _cameraRig  = null!;
    private Node3D?   _binaryRig;
    private readonly List<(Node3D Rig, float Speed)> _orbitRigs = new();
    private (int X, int Y, int Z)? _renderedSector;

    private const float BinaryAngularSpeed = 0.12f; // rad/s ≈ 1 tour/52s

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        BuildEnvironment();
        BuildStarField();

        _starsRoot = new Node3D { Name = "Stars" };
        AddChild(_starsRoot);


        _cameraRig = new CameraRig { Name = "CameraRig" };
        AddChild(_cameraRig);

        var state = AppState.Instance;
        state.ProbeUpdated  += RenderCurrentSystem;
        state.SectorUpdated += OnSectorUpdated;

        RenderCurrentSystem();
    }

    public override void _ExitTree()
    {
        if (AppState.Instance is not { } s) return;
        s.ProbeUpdated  -= RenderCurrentSystem;
        s.SectorUpdated -= OnSectorUpdated;
    }

    private void OnSectorUpdated(int x, int y, int z) => RenderCurrentSystem();

    public override void _Process(double delta)
    {
        if (_binaryRig is not null)
            _binaryRig.RotateY(BinaryAngularSpeed * (float)delta);

        var dt = (float)delta;
        foreach (var (rig, speed) in _orbitRigs)
            rig.RotateY(speed * dt);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key) return;
        switch (key.Keycode)
        {
            case Key.Space: _cameraRig.SnapTo(Vector3.Zero); break;
            case Key.Q:     GetTree().Quit();                break;
            case Key.R:     AppState.Instance.TriggerRefresh(); break;
        }
    }

    // ── Star rendering ────────────────────────────────────────────────────────

    private void RenderCurrentSystem()
    {
        var probe = AppState.Instance.Probe;
        if (probe?.Sector?.Relative is not { } rel) return;

        var coords = ((int)Math.Round(rel.X), (int)Math.Round(rel.Y), (int)Math.Round(rel.Z));

        // Only re-render when the probe enters a new sector
        if (_renderedSector == coords) return;

        foreach (var child in _starsRoot.GetChildren())
            child.QueueFree();

        if (!AppState.Instance.KnownSectors.TryGetValue(coords, out var obs)) return;

        _renderedSector = coords;

        var systems = obs.Objects?
            .Where(o => o.ObjectType == SectorObjectType.SolarSystem)
            .ToList();
        if (systems is null or { Count: 0 }) return;

        _binaryRig = null;
        _orbitRigs.Clear();

        float maxStarR = 0f;

        foreach (var sys in systems)
        {
            // Prefer individual star data from bookmarkTargets, fall back to summary parsing
            var starTargets = sys.BookmarkTargets?
                .Where(b => b.ObjectType == SectorObjectType.Star).ToList();

            if (starTargets is { Count: > 0 })
            {
                maxStarR = RenderStarsFromBookmarks(starTargets);
            }
            else
            {
                int    starCount   = Helpers.ParseStarCount(sys.Summary);
                double sysMass     = sys.Mass ?? 1.0;
                double massPerStar = sysMass / Math.Max(starCount, 1);
                float  starR       = StarRadius(massPerStar);
                float  separation  = Mathf.Max(starR * 3.5f, 4.0f);

                if (starCount == 1)
                {
                    _starsRoot.AddChild(BuildStar(massPerStar));
                    maxStarR = starR;
                }
                else
                {
                    _binaryRig = new Node3D { Name = "BinaryRig" };
                    _binaryRig.RotationDegrees = new Vector3(12f, 0f, 0f);
                    _starsRoot.AddChild(_binaryRig);
                    for (var i = 0; i < starCount; i++)
                    {
                        var starNode = BuildStar(massPerStar);
                        float angle = i * Mathf.Tau / starCount;
                        starNode.Position = new Vector3(
                            Mathf.Sin(angle) * separation, 0f,
                            Mathf.Cos(angle) * separation);
                        _binaryRig.AddChild(starNode);
                    }
                    maxStarR = separation + starR;
                }
            }

            // Orbital bodies from bookmarkTargets, fall back to synthesis
            var bodies = BodiesFromSystem(sys);
            if (bodies.Count > 0) BuildOrbitalBodies(bodies, maxStarR);
        }
    }

    private float RenderStarsFromBookmarks(List<BookmarkTarget> stars)
    {
        float maxR = 0f;
        if (stars.Count == 1)
        {
            float r = StarRadius(stars[0].Mass ?? 1.0);
            _starsRoot.AddChild(BuildStar(stars[0].Mass ?? 1.0));
            return r;
        }

        _binaryRig = new Node3D { Name = "BinaryRig" };
        _binaryRig.RotationDegrees = new Vector3(12f, 0f, 0f);
        _starsRoot.AddChild(_binaryRig);

        for (int i = 0; i < stars.Count; i++)
        {
            float r         = StarRadius(stars[i].Mass ?? 1.0);
            float separation = Mathf.Max(r * 3.5f, 4.0f);
            maxR = Mathf.Max(maxR, separation + r);

            var node  = BuildStar(stars[i].Mass ?? 1.0);
            float angle = i * Mathf.Tau / stars.Count;
            node.Position = new Vector3(
                Mathf.Sin(angle) * separation, 0f,
                Mathf.Cos(angle) * separation);
            _binaryRig.AddChild(node);
        }
        return maxR;
    }

    private static List<ApiSectorObject> BodiesFromSystem(ApiSectorObject sys)
    {
        // Use bookmarkTargets if present (real data)
        if (sys.BookmarkTargets is { Count: > 0 } bk)
        {
            return bk
                .Where(b => b.ObjectType is
                    SectorObjectType.Planet or
                    SectorObjectType.Asteroid or
                    SectorObjectType.BlackHole)
                .Select(b => new ApiSectorObject(
                    b.Id, b.ObjectType, b.Name, false,
                    null, b.Mass, b.Radius, null, null, null, null,
                    null, null, null, null))
                .ToList();
        }

        // Fallback: synthesize from summary + minableTargets
        int total     = Helpers.ParseOrbitalCount(sys.Summary);
        if (total == 0) return [];
        var minables  = sys.MinableTargets ?? [];
        int asteroids = minables.Count;
        int planets   = Math.Max(total - asteroids, 0);

        var list = new List<ApiSectorObject>();
        for (int i = 0; i < planets; i++)
            list.Add(new ApiSectorObject(
                $"synth_planet_{i}", SectorObjectType.Planet, $"Planet {i + 1}",
                true, null, null, null, null, null, null, null,
                null, null, null, null));
        foreach (var mt in minables)
            list.Add(new ApiSectorObject(
                mt.Id, SectorObjectType.Asteroid, mt.Name,
                false, null, mt.Mass, null, null, null, null, [mt],
                null, null, null, null));
        return list;
    }

    private void BuildOrbitalBodies(List<ApiSectorObject> bodies, float innerRadius)
    {
        float orbitR = Mathf.Max(innerRadius * 2.5f, 12f);

        for (int i = 0; i < bodies.Count; i++)
        {
            var   obj   = bodies[i];
            float bodyR = BodyRadius(obj);

            orbitR += bodyR + 3f;

            // Static orbit ring — added to _starsRoot so it doesn't spin
            _starsRoot.AddChild(BuildOrbitRing(orbitR, obj.ObjectType));

            // Orbit pivot — rotates, body placed at (orbitR, 0, 0)
            var orbitRig = new Node3D { Name = $"Orbit_{obj.Name ?? i.ToString()}" };
            orbitRig.RotateY(i * 2.399963f); // golden-angle spread
            _starsRoot.AddChild(orbitRig);

            var body = BuildBody(obj, i);
            body.Position = new Vector3(orbitR, 0f, 0f);
            orbitRig.AddChild(body);

            // ω ∝ r^(-3/2), normalised so r=20 → ~0.08 rad/s
            float speed = 0.08f * Mathf.Pow(20f / orbitR, 1.5f);
            _orbitRigs.Add((orbitRig, speed));

            orbitR += bodyR + 2f;
        }
    }

    private static MeshInstance3D BuildOrbitRing(float radius, SectorObjectType type)
    {
        var color = type switch
        {
            SectorObjectType.Planet    => new Color(0.35f, 0.55f, 0.9f,  0.5f),
            SectorObjectType.Asteroid  => new Color(0.7f,  0.5f,  0.25f, 0.45f),
            SectorObjectType.BlackHole => new Color(0.55f, 0.1f,  0.85f, 0.55f),
            _                          => new Color(0.5f,  0.5f,  0.55f, 0.35f),
        };

        const int segments = 128;
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
        mesh.SurfaceSetColor(color);
        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.Tau / segments;
            mesh.SurfaceAddVertex(new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }
        mesh.SurfaceEnd();

        return new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                ShadingMode            = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency           = BaseMaterial3D.TransparencyEnum.Alpha,
            },
        };
    }

    private static Node3D BuildBody(ApiSectorObject obj, int index) => obj.ObjectType switch
    {
        SectorObjectType.Planet    => BuildPlanet(obj, index),
        SectorObjectType.Asteroid  => BuildAsteroid(obj),
        SectorObjectType.BlackHole => BuildBlackHole(obj),
        _                          => BuildPlanet(obj, index),
    };

    private static Node3D BuildPlanet(ApiSectorObject obj, int index)
    {
        var node = new Node3D { Name = obj.Name ?? $"Planet{index}" };
        float r  = BodyRadius(obj);

        // Pick texture deterministically from name/id hash
        var hash    = Math.Abs((obj.Id ?? obj.Name ?? index.ToString()).GetHashCode());
        var texPath = $"res://resources/planets/planet0{hash % 10}.png";
        var tex     = GD.Load<Texture2D>(texPath);

        node.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh
            {
                Radius = r, Height = r * 2f,
                RadialSegments = 32, Rings = 16,
                Material = new StandardMaterial3D
                {
                    AlbedoTexture = tex,
                },
            },
        });

        // Axial tilt for visual variety
        node.RotationDegrees = new Vector3(
            (hash % 30) - 15f,  // axial tilt -15°…+15°
            0f, 0f);

        return node;
    }

    private static Node3D BuildAsteroid(ApiSectorObject obj)
    {
        var node = new Node3D { Name = obj.Name ?? "Asteroid" };
        float r  = BodyRadius(obj);

        // Irregular look: slightly squashed sphere
        node.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh
            {
                Radius = r, Height = r * 1.4f,
                RadialSegments = 8, Rings = 4,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.42f, 0.36f, 0.28f),
                    Roughness   = 1.0f,
                    Metallic    = 0.0f,
                },
            },
            // Random scale jitter for irregular look
            Scale = new Vector3(
                1.0f + (Math.Abs(obj.GetHashCode()) % 40 - 20) * 0.01f,
                0.7f + (Math.Abs((obj.Name ?? "").GetHashCode()) % 30) * 0.01f,
                1.0f + (Math.Abs((obj.Id  ?? "").GetHashCode()) % 40 - 20) * 0.01f),
        });

        return node;
    }

    private static Node3D BuildBlackHole(ApiSectorObject obj)
    {
        var node = new Node3D { Name = obj.Name ?? "BlackHole" };
        float r  = BodyRadius(obj);

        // Event horizon — pitch black
        node.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh
            {
                Radius = r, Height = r * 2f,
                RadialSegments = 32, Rings = 16,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = Colors.Black,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            },
        });

        // Accretion disk glow — larger semi-transparent purple ring
        node.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh
            {
                Radius = r * 2.0f, Height = r * 0.3f,
                RadialSegments = 32, Rings = 4,
                Material = new StandardMaterial3D
                {
                    AlbedoColor  = new Color(0.5f, 0.1f, 0.8f, 0.35f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode  = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            },
        });

        return node;
    }

    private static float BodyRadius(ApiSectorObject obj)
    {
        if (obj.Radius is > 0.0 and { } r)
            return (float)Math.Clamp(Math.Log10(r + 1.0) * 0.8, 0.2, 2.5);
        if (obj.Mass is > 0.0 and { } m)
            return (float)Math.Clamp(Math.Log10(m + 1.0) * 0.6, 0.2, 2.0);

        return obj.ObjectType switch
        {
            SectorObjectType.Planet    => 0.8f,
            SectorObjectType.Asteroid  => 0.3f,
            SectorObjectType.BlackHole => 1.2f,
            _                          => 0.5f,
        };
    }

    private static Node3D BuildStar(double massPerStar)
    {
        var node  = new Node3D();
        var color = StarColor(massPerStar);
        var r     = StarRadius(massPerStar);

        // Point light at the star's centre — tints the scene with its spectral color
        node.AddChild(new OmniLight3D
        {
            LightColor  = color,
            LightEnergy = 3.0f,
            OmniRange   = 300f,
        });

        // Star body: Unshaded + AlbedoColor = self-luminous, unaffected by external lights
        node.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh
            {
                Radius = r, Height = r * 2f,
                RadialSegments = 32, Rings = 16,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = color,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            },
        });

        // Halo — slightly larger, semi-transparent, same spectral color
        node.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh
            {
                Radius = r * 1.15f, Height = r * 2.3f,
                RadialSegments = 16, Rings = 8,
                Material = new StandardMaterial3D
                {
                    AlbedoColor  = new Color(color.R, color.G, color.B, 0.12f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode  = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            },
        });

        return node;
    }

    // ── Stellar classification ────────────────────────────────────────────────

    /// Colour based on simplified spectral class (O→B→A→F→G→K→M).
    private static Color StarColor(double mass) => mass switch
    {
        >= 30.0 => new Color(0.6f, 0.7f, 1.0f),   // O — blue
        >= 10.0 => new Color(0.75f, 0.85f, 1.0f),  // B — blue-white
        >= 4.0  => new Color(0.95f, 0.97f, 1.0f),  // A — white
        >= 1.5  => new Color(1.0f, 1.0f, 0.9f),    // F — yellow-white
        >= 0.9  => new Color(1.0f, 0.95f, 0.6f),   // G — yellow (sun-like)
        >= 0.5  => new Color(1.0f, 0.7f, 0.35f),   // K — orange
        _       => new Color(1.0f, 0.35f, 0.2f),   // M — red
    };

    /// Visual radius — logarithmic scale so extreme masses (56 M☉+) stay reasonable.
    private static float StarRadius(double mass) =>
        (float)Math.Clamp(Math.Log10(Math.Max(mass, 0.1) + 1.0) * 2.2, 0.3, 3.5);

    // ── Scene construction ────────────────────────────────────────────────────

    private void BuildEnvironment()
    {
        var env = new Godot.Environment();
        env.BackgroundMode  = Godot.Environment.BGMode.Color;
        env.BackgroundColor = new Color(0.005f, 0.005f, 0.02f);
        env.GlowEnabled      = true;
        env.GlowIntensity    = 0.8f;
        env.GlowBloom        = 0.1f;
        env.GlowHdrThreshold = 0.7f;
        AddChild(new WorldEnvironment { Name = "Env", Environment = env });
    }

    private void BuildStarField()
    {
        const int   count  = 3000;
        const float radius = 500f;

        var starMesh = new SphereMesh
        {
            Radius = 0.15f, Height = 0.3f, RadialSegments = 4, Rings = 2,
            Material = new StandardMaterial3D
            {
                AlbedoColor              = Colors.White,
                EmissionEnabled          = true,
                Emission                 = new Color(0.9f, 0.9f, 1.0f),
                EmissionEnergyMultiplier = 1.5f,
            },
        };

        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh            = starMesh,
            InstanceCount   = count,
        };

        var rng = new RandomNumberGenerator { Seed = 42 };
        for (var i = 0; i < count; i++)
        {
            var dir = new Vector3(
                rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f)
            ).Normalized();
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, dir * radius));
        }

        AddChild(new MultiMeshInstance3D { Name = "StarField", Multimesh = mm });
    }

}
