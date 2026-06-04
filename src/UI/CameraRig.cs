using Godot;

namespace NeumannOrbital.UI;

public partial class CameraRig : Node3D
{
    private Camera3D _camera = null!;
    private float _yaw      = 0.4f;
    private float _pitch    = 0.3f;
    private float _distance = 80.0f;
    private bool _dragging;

    private const float PanSpeed    = 0.005f;
    private const float ZoomStep    = 8.0f;
    private const float MinDistance = 5.0f;
    private const float MaxDistance = 1000.0f;

    public override void _Ready()
    {
        _camera = new Camera3D { Name = "Camera" };
        AddChild(_camera);
        ApplyTransform();
    }

    public void SnapTo(Vector3 worldPos) => GlobalPosition = worldPos;

    public override void _Process(double delta)
    {
        if (Input.IsKeyPressed(Key.Left))  _yaw += 1.2f * (float)delta;
        if (Input.IsKeyPressed(Key.Right)) _yaw -= 1.2f * (float)delta;
        if (Input.IsKeyPressed(Key.Up))    _pitch = Mathf.Clamp(_pitch + 1.0f * (float)delta, -1.4f, 1.4f);
        if (Input.IsKeyPressed(Key.Down))  _pitch = Mathf.Clamp(_pitch - 1.0f * (float)delta, -1.4f, 1.4f);
        if (Input.IsKeyPressed(Key.Equal)) _distance = Mathf.Clamp(_distance - ZoomStep * (float)delta * 10f, MinDistance, MaxDistance);
        if (Input.IsKeyPressed(Key.Minus)) _distance = Mathf.Clamp(_distance + ZoomStep * (float)delta * 10f, MinDistance, MaxDistance);

        ApplyTransform();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
                _dragging = mb.Pressed;
            if (mb.ButtonIndex == MouseButton.WheelUp)
                _distance = Mathf.Clamp(_distance - ZoomStep, MinDistance, MaxDistance);
            if (mb.ButtonIndex == MouseButton.WheelDown)
                _distance = Mathf.Clamp(_distance + ZoomStep, MinDistance, MaxDistance);
        }

        if (@event is InputEventMouseMotion mm && _dragging)
        {
            _yaw   -= mm.Relative.X * PanSpeed;
            _pitch  = Mathf.Clamp(_pitch + mm.Relative.Y * PanSpeed, -1.4f, 1.4f);
        }
    }

    private void ApplyTransform()
    {
        // Build camera offset via yaw/pitch angles
        var offset = new Vector3(
            Mathf.Sin(_yaw) * Mathf.Cos(_pitch),
            Mathf.Sin(_pitch),
            Mathf.Cos(_yaw) * Mathf.Cos(_pitch)
        ) * _distance;

        _camera.Position = offset;
        _camera.LookAt(GlobalPosition);
    }
}
