using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace BlockHaven3D;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var gameSettings = GameWindowSettings.Default;
        gameSettings.UpdateFrequency = 60;
        var nativeSettings = new NativeWindowSettings
        {
            ClientSize = new Vector2i(1600, 900),
            Title = "BlockHaven 3D - FPS voxel city + airport simulator",
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core
        };
        using var game = new BlockHavenGame(gameSettings, nativeSettings);
        game.Run();
    }
}

public sealed class BlockHavenGame : GameWindow
{
    private ShaderProgram _shader = null!;
    private CubeRenderer _cube = null!;
    private readonly SaveManager _save = new();
    private readonly WorldManager _world = new(1337);
    private readonly EconomyManager _economy = new();
    private readonly NpcManager _npcs = new();
    private readonly List<Vehicle> _vehicles = [];
    private PlayerState _player = PlayerState.CreateDefault();
    private Camera _camera = null!;
    private bool _creative;
    private bool _paused;
    private float _verticalVelocity;
    private bool _grounded;
    private double _autosaveTimer;
    private double _worldClock = 8.0;
    private float _sun;
    private Vehicle? _drivenVehicle;
    private Vector2 _lastMouse;
    private bool _firstMouse = true;

    public BlockHavenGame(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings)
        : base(gameWindowSettings, nativeWindowSettings)
    {
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        CursorState = CursorState.Grabbed;
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(CullFaceMode.Back);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.ClearColor(0.52f, 0.75f, 0.98f, 1f);

        _shader = new ShaderProgram(VertexShaderSource, FragmentShaderSource);
        _cube = new CubeRenderer(_shader);
        _world.GenerateSpawnCityAndAirport();
        _vehicles.AddRange(VehicleFactory.CreateDefaultFleet());
        _npcs.SpawnPopulation(_world.CityHomes, _world.Workplaces);
        _save.LoadAll(_world, _economy, _npcs, _vehicles, ref _player);
        _camera = new Camera(_player.Position + new Vector3(0, 1.75f, 0));
        _camera.Yaw = _player.Yaw;
        _camera.Pitch = _player.Pitch;
    }

    protected override void OnUnload()
    {
        _player.Position = _camera.Position - new Vector3(0, 1.75f, 0);
        _player.Yaw = _camera.Yaw;
        _player.Pitch = _camera.Pitch;
        _save.SaveAll(_world, _economy, _npcs, _vehicles, _player);
        _cube.Dispose();
        _shader.Dispose();
        base.OnUnload();
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        GL.Viewport(0, 0, Size.X, Size.Y);
        base.OnResize(e);
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);
        var dt = (float)args.Time;
        if (KeyboardState.IsKeyPressed(Keys.Escape))
        {
            _paused = !_paused;
            CursorState = _paused ? CursorState.Normal : CursorState.Grabbed;
        }
        if (_paused) return;

        UpdateMouseLook();
        if (KeyboardState.IsKeyPressed(Keys.F5)) _save.SaveAll(_world, _economy, _npcs, _vehicles, CapturePlayer());
        if (KeyboardState.IsKeyPressed(Keys.F1)) _creative = !_creative;
        if (KeyboardState.IsKeyPressed(Keys.E))
        {
            if (_drivenVehicle is not null) ToggleNearestVehicle();
            else if (!_world.ToggleNearestDoor(_camera.Position)) ToggleNearestVehicle();
        }
        if (KeyboardState.IsKeyPressed(Keys.H)) TryBuyNearestHouse();
        if (KeyboardState.IsKeyPressed(Keys.J)) _economy.PayJobSalary("Livreur", 125);
        if (KeyboardState.IsKeyPressed(Keys.D1)) _player.SelectedBlock = BlockType.Dirt;
        if (KeyboardState.IsKeyPressed(Keys.D2)) _player.SelectedBlock = BlockType.Stone;
        if (KeyboardState.IsKeyPressed(Keys.D3)) _player.SelectedBlock = BlockType.Wood;
        if (KeyboardState.IsKeyPressed(Keys.D4)) _player.SelectedBlock = BlockType.Glass;

        if (_drivenVehicle is null) UpdateWalking(dt); else UpdateVehicleDriving(dt);
        if (MouseState.IsButtonPressed(MouseButton.Left)) BreakBlock();
        if (MouseState.IsButtonPressed(MouseButton.Right)) PlaceBlock();

        _world.UpdateDoors(dt);
        _worldClock = (_worldClock + dt / 90.0) % 24.0;
        _sun = MathF.Max(0.16f, MathF.Sin((float)(_worldClock / 24.0 * MathHelper.TwoPi)) * 0.5f + 0.5f);
        _npcs.Update(dt, _worldClock, _vehicles);
        foreach (var vehicle in _vehicles.Where(v => v.IsTraffic)) vehicle.UpdateTraffic(dt, _world.RoadPoints);

        _autosaveTimer += args.Time;
        if (_autosaveTimer >= 12)
        {
            _autosaveTimer = 0;
            _save.SaveAll(_world, _economy, _npcs, _vehicles, CapturePlayer());
        }
        Title = $"BlockHaven 3D | Argent ${_economy.PlayerMoney} | Mode {(_creative ? "Creatif" : "Survie")} | Bloc {_player.SelectedBlock} | Heure {_worldClock:00.0}h | E: porte/vehicule H: maison F5: save";
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        var sky = new Vector3(0.015f, 0.02f, 0.055f) + (new Vector3(0.56f, 0.75f, 0.96f) - new Vector3(0.015f, 0.02f, 0.055f)) * _sun;
        GL.ClearColor(sky.X, sky.Y, sky.Z, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _shader.Use();
        var projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(74f), Size.X / (float)Size.Y, 0.05f, 600f);
        _shader.SetMatrix4("projection", projection);
        _shader.SetMatrix4("view", _camera.GetViewMatrix());
        _shader.SetVector3("lightDir", Vector3.Normalize(new Vector3(-0.35f, -1f, -0.25f)));
        _shader.SetVector3("viewPos", _camera.Position);
        _shader.SetFloat("dayLight", _sun);
        _shader.SetFloat("time", (float)GLFW.GetTime());

        foreach (var block in _world.VisibleBlocksAround(_camera.Position, 95))
        {
            var color = BlockPalette.GetColor(block.Type);
            if (block.Type == BlockType.Water) color = new Vector4(0.1f, 0.45f + MathF.Sin((float)GLFW.GetTime() * 2f) * 0.06f, 0.9f, 0.72f);
            _cube.Draw(block.Position, Vector3.One, color);
        }
        RenderSunAndMoon();
        RenderCityDetails();
        foreach (var vehicle in _vehicles) RenderVehicle(vehicle);
        foreach (var npc in _npcs.Npcs) RenderNpc(npc);
        SwapBuffers();
    }

    private void UpdateMouseLook()
    {
        var mouse = MouseState.Position;
        if (_firstMouse)
        {
            _lastMouse = mouse;
            _firstMouse = false;
        }
        var delta = mouse - _lastMouse;
        _lastMouse = mouse;
        _camera.Yaw += delta.X * 0.12f;
        _camera.Pitch = Math.Clamp(_camera.Pitch - delta.Y * 0.12f, -88f, 88f);
    }

    private void UpdateWalking(float dt)
    {
        var speed = KeyboardState.IsKeyDown(Keys.LeftShift) ? 11f : 6f;
        var horizontal = Vector3.Zero;
        if (KeyboardState.IsKeyDown(Keys.W) || KeyboardState.IsKeyDown(Keys.Z)) horizontal += _camera.FrontFlat;
        if (KeyboardState.IsKeyDown(Keys.S)) horizontal -= _camera.FrontFlat;
        if (KeyboardState.IsKeyDown(Keys.A) || KeyboardState.IsKeyDown(Keys.Q)) horizontal -= _camera.RightFlat;
        if (KeyboardState.IsKeyDown(Keys.D)) horizontal += _camera.RightFlat;
        if (horizontal.LengthSquared > 0.001f) _camera.Position += Vector3.Normalize(horizontal) * speed * dt;

        if (_creative)
        {
            var fly = 0f;
            if (KeyboardState.IsKeyDown(Keys.Space)) fly += 1f;
            if (KeyboardState.IsKeyDown(Keys.LeftControl)) fly -= 1f;
            if (MathF.Abs(fly) > 0.01f) _camera.Position += Vector3.UnitY * fly * speed * dt;
        }
        else
        {
            if (_grounded && KeyboardState.IsKeyPressed(Keys.Space))
            {
                _verticalVelocity = 8.25f;
                _grounded = false;
            }
            _verticalVelocity -= 24f * dt;
            _verticalVelocity = Math.Max(_verticalVelocity, -42f);
            _camera.Position += Vector3.UnitY * _verticalVelocity * dt;
        }

        var ground = _world.EyeHeightAt(_camera.Position.X, _camera.Position.Z, _camera.Position.Y);
        if (_camera.Position.Y <= ground)
        {
            _camera.Position = new Vector3(_camera.Position.X, ground, _camera.Position.Z);
            _verticalVelocity = 0f;
            _grounded = true;
        }
        else
        {
            _grounded = false;
        }
    }

    private void UpdateVehicleDriving(float dt)
    {
        var vehicle = _drivenVehicle!;
        var throttle = (KeyboardState.IsKeyDown(Keys.W) || KeyboardState.IsKeyDown(Keys.Z)) ? 1f : KeyboardState.IsKeyDown(Keys.S) ? -0.6f : 0f;
        var steer = (KeyboardState.IsKeyDown(Keys.A) || KeyboardState.IsKeyDown(Keys.Q)) ? -1f : KeyboardState.IsKeyDown(Keys.D) ? 1f : 0f;
        vehicle.UpdatePlayerDrive(dt, throttle, steer, KeyboardState.IsKeyDown(Keys.Space));
        _camera.Position = vehicle.Position + vehicle.CameraOffset;
        _camera.Yaw = vehicle.Yaw;
        _camera.Pitch = Math.Clamp(_camera.Pitch, -30f, 35f);
        if (KeyboardState.IsKeyPressed(Keys.R) && vehicle.Kind == VehicleKind.Airplane) vehicle.RequestTakeoff = true;
    }

    private void BreakBlock()
    {
        if (_world.Raycast(_camera.Position, _camera.Front, 8, out var hit, out _))
        {
            var brokenType = _world.GetBlock(hit);
            _world.SetBlock(hit, BlockType.Air, true);
            if (!_creative && brokenType != BlockType.Air) _player.Add(brokenType, 1);
        }
    }

    private void PlaceBlock()
    {
        if (_world.Raycast(_camera.Position, _camera.Front, 8, out _, out var place))
        {
            if (_creative || _player.Consume(_player.SelectedBlock, 1)) _world.SetBlock(place, _player.SelectedBlock, true);
        }
    }

    private void ToggleNearestVehicle()
    {
        if (_drivenVehicle is not null)
        {
            _drivenVehicle.IsPlayerDriven = false;
            _camera.Position = _drivenVehicle.Position + new Vector3(2, 2, 2);
            _drivenVehicle = null;
            return;
        }
        var nearest = _vehicles.Where(v => Vector3.Distance(v.Position, _camera.Position) < 7f).OrderBy(v => Vector3.Distance(v.Position, _camera.Position)).FirstOrDefault();
        if (nearest is not null)
        {
            nearest.IsPlayerDriven = true;
            _drivenVehicle = nearest;
        }
    }

    private void TryBuyNearestHouse()
    {
        var home = _world.CityHomes.OrderBy(h => Vector3.Distance(h.Entrance, _camera.Position)).FirstOrDefault();
        if (home is not null && Vector3.Distance(home.Entrance, _camera.Position) < 8f && !home.OwnedByPlayer && _economy.TrySpend(home.Price))
        {
            home.OwnedByPlayer = true;
        }
    }

    private PlayerState CapturePlayer()
    {
        _player.Position = _camera.Position - new Vector3(0, 1.75f, 0);
        _player.Yaw = _camera.Yaw;
        _player.Pitch = _camera.Pitch;
        return _player;
    }

    private void RenderCityDetails()
    {
        foreach (var sign in _world.Signs) DrawOutlined(sign.Position, sign.Scale, sign.Color, 0f, 0.035f);
        foreach (var door in _world.Doors)
        {
            var swing = 86f * door.OpenAmount;
            var yaw = door.Yaw + swing;
            var offset = new Vector3(MathF.Sin(MathHelper.DegreesToRadians(yaw)) * 0.42f, 0, MathF.Cos(MathHelper.DegreesToRadians(yaw)) * 0.42f);
            DrawOutlined(door.Position + offset, new Vector3(0.12f, 2.15f, 0.86f), door.IsOpen ? new Vector4(0.88f, 0.62f, 0.32f, 1) : new Vector4(0.58f, 0.31f, 0.12f, 1), yaw, 0.045f);
            _cube.DrawRotated(door.Position + offset + new Vector3(0, 0.12f, 0), new Vector3(0.16f, 0.12f, 0.16f), new Vector4(1f, 0.82f, 0.22f, 1), yaw);
        }
    }

    private void RenderSunAndMoon()
    {
        var angle = (float)(_worldClock / 24.0 * MathHelper.TwoPi);
        var sunPos = _camera.Position + new Vector3(MathF.Cos(angle) * 120f, MathF.Sin(angle) * 95f + 30f, -80f);
        var moonPos = _camera.Position - new Vector3(MathF.Cos(angle) * 120f, MathF.Sin(angle) * 95f + 30f, -80f);
        _cube.Draw(sunPos, new Vector3(8f, 8f, 8f), new Vector4(1f, 0.82f, 0.28f, 1));
        _cube.Draw(moonPos, new Vector3(5f, 5f, 5f), new Vector4(0.68f, 0.74f, 0.9f, 1));
    }

    private void RenderVehicle(Vehicle v)
    {
        var body = v.Kind == VehicleKind.Airplane ? new Vector3(3.8f, 0.55f, 1.1f) : v.Kind == VehicleKind.Motorcycle ? new Vector3(1.8f, 0.55f, 0.55f) : new Vector3(2.2f, 0.8f, 1.25f);
        DrawOutlined(v.Position, body, v.Color, v.Yaw, 0.055f);
        if (v.Kind == VehicleKind.Airplane)
        {
            DrawOutlined(v.Position + new Vector3(0, 0.15f, 0), new Vector3(1.0f, 0.12f, 7.5f), new Vector4(0.96f, 0.97f, 1f, 1), v.Yaw, 0.04f);
            DrawOutlined(v.Position + new Vector3(-2.5f, 0.1f, 0), new Vector3(0.2f, 1.2f, 2.2f), new Vector4(0.82f, 0.9f, 1f, 1), v.Yaw, 0.035f);
        }
        else
        {
            DrawOutlined(v.Position + new Vector3(0.55f, 0.55f, 0), new Vector3(0.7f, 0.45f, 1.05f), new Vector4(0.65f, 0.9f, 1f, 0.86f), v.Yaw, 0.035f);
        }
    }

    private void RenderNpc(Npc npc)
    {
        var bob = MathF.Sin((float)GLFW.GetTime() * 5f + npc.Id.GetHashCode()) * 0.035f;
        var p = npc.Position + new Vector3(0, bob, 0);
        var shirt = npc.RoleColor;
        var skin = new Vector4(1.0f, 0.78f, 0.57f, 1);
        DrawOutlined(p + new Vector3(0, 1.03f, 0), new Vector3(0.55f, 0.78f, 0.32f), shirt, 0f, 0.045f);
        DrawOutlined(p + new Vector3(-0.43f, 1.02f, 0), new Vector3(0.18f, 0.72f, 0.24f), new Vector4(shirt.X * 0.9f, shirt.Y * 0.9f, shirt.Z * 0.9f, 1), 0f, 0.035f);
        DrawOutlined(p + new Vector3(0.43f, 1.02f, 0), new Vector3(0.18f, 0.72f, 0.24f), new Vector4(shirt.X * 0.9f, shirt.Y * 0.9f, shirt.Z * 0.9f, 1), 0f, 0.035f);
        DrawOutlined(p + new Vector3(-0.16f, 0.36f, 0), new Vector3(0.2f, 0.65f, 0.24f), new Vector4(0.12f, 0.25f, 0.72f, 1), 0f, 0.035f);
        DrawOutlined(p + new Vector3(0.16f, 0.36f, 0), new Vector3(0.2f, 0.65f, 0.24f), new Vector4(0.12f, 0.25f, 0.72f, 1), 0f, 0.035f);
        DrawOutlined(p + new Vector3(0, 1.63f, 0), new Vector3(0.42f, 0.42f, 0.42f), skin, 0f, 0.045f);
        _cube.Draw(p + new Vector3(-0.09f, 1.67f, -0.215f), new Vector3(0.055f, 0.055f, 0.025f), new Vector4(0.05f, 0.05f, 0.06f, 1));
        _cube.Draw(p + new Vector3(0.09f, 1.67f, -0.215f), new Vector3(0.055f, 0.055f, 0.025f), new Vector4(0.05f, 0.05f, 0.06f, 1));
        _cube.Draw(p + new Vector3(0, 1.52f, -0.225f), new Vector3(0.18f, 0.035f, 0.025f), new Vector4(0.65f, 0.12f, 0.18f, 1));
        DrawOutlined(p + new Vector3(0, 1.91f, 0), new Vector3(0.46f, 0.18f, 0.46f), new Vector4(0.18f, 0.09f, 0.04f, 1), 0f, 0.03f);
    }

    private void DrawOutlined(Vector3 position, Vector3 scale, Vector4 color, float yaw, float outline)
    {
        var outlineScale = scale + new Vector3(outline * 2f, outline * 2f, outline * 2f);
        _cube.DrawRotated(position, outlineScale, new Vector4(0.02f, 0.025f, 0.035f, Math.Min(1f, color.W)), yaw);
        _cube.DrawRotated(position, scale, color, yaw);
    }

    private const string VertexShaderSource = """
#version 330 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aNormal;
uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;
out vec3 Normal;
out vec3 WorldPos;
void main()
{
    vec4 world = model * vec4(aPosition, 1.0);
    WorldPos = world.xyz;
    Normal = mat3(transpose(inverse(model))) * aNormal;
    gl_Position = projection * view * world;
}
""";

    private const string FragmentShaderSource = """
#version 330 core
in vec3 Normal;
in vec3 WorldPos;
out vec4 FragColor;
uniform vec4 objectColor;
uniform vec3 lightDir;
uniform vec3 viewPos;
uniform float dayLight;
uniform float time;
float hash(vec3 p)
{
    return fract(sin(dot(p, vec3(12.9898, 78.233, 37.719))) * 43758.5453);
}

void main()
{
    vec3 n = normalize(Normal);
    vec3 viewDir = normalize(viewPos - WorldPos);
    vec3 halfDir = normalize(-lightDir + viewDir);
    float diffuse = max(dot(n, -lightDir), 0.0);
    float toonDiffuse = floor(diffuse * 4.0 + 0.5) / 4.0;
    float rim = pow(1.0 - max(dot(n, viewDir), 0.0), 2.4) * 0.18;
    float specular = pow(max(dot(n, halfDir), 0.0), 38.0) * 0.34 * objectColor.a;
    float ambient = 0.28 + dayLight * 0.25;
    float grain = hash(floor(WorldPos * 3.0)) * 0.035 - 0.015;
    float edge = smoothstep(0.455, 0.5, max(max(abs(fract(WorldPos.x) - 0.5), abs(fract(WorldPos.y) - 0.5)), abs(fract(WorldPos.z) - 0.5)));
    vec3 baseColor = mix(objectColor.rgb, pow(objectColor.rgb, vec3(0.72)), 0.38);
    vec3 textured = baseColor + vec3(grain) - vec3(edge * 0.075);
    vec3 warmSun = mix(vec3(0.76, 0.84, 1.0), vec3(1.0, 0.94, 0.82), dayLight);
    vec3 lit = textured * (ambient + toonDiffuse * (0.34 + dayLight * 0.55)) * warmSun + vec3(specular + rim);
    float fog = clamp(length(viewPos.xz - WorldPos.xz) / 300.0, 0.0, 0.46);
    vec3 sky = mix(vec3(0.02, 0.025, 0.065), vec3(0.47, 0.72, 1.0), dayLight);
    vec3 graded = pow(max(lit, vec3(0.0)), vec3(1.0 / 2.2));
    graded = mix(vec3(dot(graded, vec3(0.299, 0.587, 0.114))), graded, 1.22);
    FragColor = vec4(mix(graded, sky, fog), objectColor.a);
}
""";
}

public sealed class Camera
{
    public Vector3 Position;
    public float Yaw = -90f;
    public float Pitch;
    public Camera(Vector3 position) => Position = position;
    public Vector3 Front
    {
        get
        {
            var yaw = MathHelper.DegreesToRadians(Yaw);
            var pitch = MathHelper.DegreesToRadians(Pitch);
            return Vector3.Normalize(new Vector3(MathF.Cos(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Sin(yaw) * MathF.Cos(pitch)));
        }
    }
    public Vector3 FrontFlat => Vector3.Normalize(new Vector3(Front.X, 0, Front.Z));
    public Vector3 RightFlat => Vector3.Normalize(Vector3.Cross(FrontFlat, Vector3.UnitY));
    public Matrix4 GetViewMatrix() => Matrix4.LookAt(Position, Position + Front, Vector3.UnitY);
}

public enum BlockType { Air, Grass, Dirt, Stone, Wood, Leaves, Road, Water, Glass, Brick, Concrete, Light, Runway }
public readonly record struct Block(Vector3i Position, BlockType Type);
public sealed class HouseLot
{
    public string Id { get; set; }
    public Vector3 Entrance { get; set; }
    public int Price { get; set; }
    public bool OwnedByPlayer { get; set; }
    public HouseLot(string id, Vector3 entrance, int price, bool ownedByPlayer)
    {
        Id = id;
        Entrance = entrance;
        Price = price;
        OwnedByPlayer = ownedByPlayer;
    }
}
public sealed record WorkPlace(string Id, string Role, Vector3 Position);
public sealed record DetailCube(Vector3 Position, Vector3 Scale, Vector4 Color);
public sealed class Door
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Vector3 Position { get; set; }
    public float Yaw { get; set; }
    public bool IsOpen { get; set; }
    public float OpenAmount { get; set; }
}
public sealed record DoorSave(bool IsOpen, float OpenAmount);

public sealed class WorldManager
{
    private readonly int _seed;
    private readonly Dictionary<Vector3i, BlockType> _overrides = [];
    private readonly HashSet<Vector3i> _cityBlocks = [];
    public List<HouseLot> CityHomes { get; private set; } = [];
    public List<WorkPlace> Workplaces { get; } = [];
    public List<DetailCube> Signs { get; } = [];
    public List<Door> Doors { get; } = [];
    public IReadOnlyList<Vector3> RoadPoints { get; private set; } = [];
    public IReadOnlyDictionary<Vector3i, BlockType> ModifiedBlocks => _overrides;
    public WorldManager(int seed) => _seed = seed;

    public void LoadModifiedBlocks(Dictionary<string, string> data)
    {
        _overrides.Clear();
        foreach (var (key, value) in data)
        {
            var p = key.Split(',');
            if (p.Length == 3 && Enum.TryParse<BlockType>(value, out var type)) _overrides[new Vector3i(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]))] = type;
        }
    }

    public Dictionary<string, string> SaveModifiedBlocks() => _overrides.ToDictionary(k => $"{k.Key.X},{k.Key.Y},{k.Key.Z}", v => v.Value.ToString());

    public void ApplyDoorStates(Dictionary<string, DoorSave>? states)
    {
        if (states is null) return;
        foreach (var door in Doors)
        {
            if (!states.TryGetValue(door.Id, out var state)) continue;
            door.IsOpen = state.IsOpen;
            door.OpenAmount = Math.Clamp(state.OpenAmount, 0f, 1f);
        }
    }

    public Dictionary<string, DoorSave> SaveDoorStates() => Doors.ToDictionary(d => d.Id, d => new DoorSave(d.IsOpen, d.OpenAmount));

    public void UpdateDoors(float dt)
    {
        foreach (var door in Doors)
        {
            var target = door.IsOpen ? 1f : 0f;
            door.OpenAmount = MoveTowards(door.OpenAmount, target, dt * 3.8f);
        }
    }

    public bool ToggleNearestDoor(Vector3 playerPosition)
    {
        var door = Doors.Where(d => Vector3.Distance(d.Position, playerPosition) < 3.2f).OrderBy(d => Vector3.Distance(d.Position, playerPosition)).FirstOrDefault();
        if (door is null) return false;
        door.IsOpen = !door.IsOpen;
        return true;
    }

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        if (MathF.Abs(target - current) <= maxDelta) return target;
        return current + MathF.Sign(target - current) * maxDelta;
    }

    public void GenerateSpawnCityAndAirport()
    {
        BuildRoad(-42, 42, -5, 5);
        BuildRoad(-5, 5, -42, 42);
        BuildDistrictHomes();
        BuildPublicBuilding("hospital", new Vector3i(20, 1, 20), new Vector3i(12, 7, 10), BlockType.Concrete, new Vector4(0.92f, 0.95f, 1f, 1));
        BuildPublicBuilding("police", new Vector3i(-30, 1, 20), new Vector3i(12, 6, 10), BlockType.Brick, new Vector4(0.12f, 0.22f, 0.6f, 1));
        BuildPublicBuilding("store", new Vector3i(21, 1, -26), new Vector3i(16, 5, 12), BlockType.Glass, new Vector4(0.2f, 0.8f, 0.95f, 1));
        BuildAirport();
        BuildStreetLights();
        BuildBrookhavenPolish();
        RoadPoints = Enumerable.Range(-40, 81).Where(i => i % 4 == 0).SelectMany(i => new[] { new Vector3(i, 1.1f, 0), new Vector3(0, 1.1f, i) }).ToList();
        Workplaces.AddRange([
            new WorkPlace("hospital", "Medecin", new Vector3(20, 2, 20)),
            new WorkPlace("police", "Policier", new Vector3(-30, 2, 20)),
            new WorkPlace("store", "Commercant", new Vector3(21, 2, -26)),
            new WorkPlace("airport", "Pilote", new Vector3(92, 2, -18)),
            new WorkPlace("warehouse", "Livreur", new Vector3(78, 2, 26))]);
    }

    public IEnumerable<Block> VisibleBlocksAround(Vector3 center, int radius)
    {
        var cx = (int)MathF.Floor(center.X);
        var cz = (int)MathF.Floor(center.Z);
        for (var x = cx - radius; x <= cx + radius; x++)
        for (var z = cz - radius; z <= cz + radius; z++)
        {
            var h = HeightAt(x, z);
            for (var y = Math.Max(0, h - 2); y <= Math.Min(32, h + 15); y++)
            {
                var pos = new Vector3i(x, y, z);
                var type = GetBlock(pos);
                if (type != BlockType.Air) yield return new Block(pos, type);
            }
        }
    }

    public BlockType GetBlock(Vector3i pos)
    {
        if (_overrides.TryGetValue(pos, out var forced)) return forced;
        if (_cityBlocks.Contains(pos)) return BlockType.Air;
        var h = HeightAt(pos.X, pos.Z);
        if (RiverAt(pos.X, pos.Z) && pos.Y <= 1) return BlockType.Water;
        if (pos.Y > h) return BlockType.Air;
        if (pos.Y == h) return h > 13 ? BlockType.Stone : BlockType.Grass;
        if (pos.Y > h - 3) return BlockType.Dirt;
        return BlockType.Stone;
    }

    public float EyeHeightAt(float x, float z, float currentEyeY)
    {
        var bx = (int)MathF.Floor(x);
        var bz = (int)MathF.Floor(z);
        var start = Math.Clamp((int)MathF.Floor(currentEyeY), 0, 96);
        for (var y = start; y >= 0; y--)
        {
            var type = GetBlock(new Vector3i(bx, y, bz));
            if (type != BlockType.Air && type != BlockType.Water) return y + 1.75f;
        }
        return HeightAt(bx, bz) + 1.75f;
    }

    public void SetBlock(Vector3i pos, BlockType type, bool persistent)
    {
        if (persistent) _overrides[pos] = type;
    }

    public int HeightAt(int x, int z)
    {
        if (Math.Abs(x) < 140 && Math.Abs(z) < 80) return 0;
        var mountain = MathF.Max(0, 18 - MathF.Sqrt((x + 115) * (x + 115) + (z - 70) * (z - 70)) * 0.18f);
        var noise = MathF.Sin((x + _seed) * 0.075f) * 2.4f + MathF.Cos((z - _seed) * 0.061f) * 2.1f;
        return Math.Max(0, (int)MathF.Round(2 + noise + mountain));
    }

    public bool Raycast(Vector3 origin, Vector3 direction, float distance, out Vector3i hit, out Vector3i place)
    {
        var last = new Vector3i((int)MathF.Floor(origin.X), (int)MathF.Floor(origin.Y), (int)MathF.Floor(origin.Z));
        for (float t = 0; t < distance; t += 0.12f)
        {
            var p = origin + direction * t;
            var current = new Vector3i((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));
            if (GetBlock(current) != BlockType.Air)
            {
                hit = current;
                place = last;
                return true;
            }
            last = current;
        }
        hit = default;
        place = default;
        return false;
    }

    public void ApplyHouseOwnership(Dictionary<string, bool>? owned)
    {
        if (owned is null) return;
        foreach (var home in CityHomes) home.OwnedByPlayer = owned.TryGetValue(home.Id, out var v) && v;
    }

    public Dictionary<string, bool> SaveHouseOwnership() => CityHomes.ToDictionary(h => h.Id, h => h.OwnedByPlayer);

    private void BuildRoad(int x0, int x1, int z0, int z1)
    {
        for (var x = x0; x <= x1; x++) for (var z = z0; z <= z1; z++) SetCityBlock(new Vector3i(x, 0, z), BlockType.Road);
    }

    private void BuildDistrictHomes()
    {
        BuildHouse("starter", new Vector3i(-18, 1, -20), true, 0);
        BuildHouse("villa-a", new Vector3i(-38, 1, -22), false, 1250);
        BuildHouse("villa-b", new Vector3i(-18, 1, 28), false, 1800);
        BuildHouse("villa-c", new Vector3i(40, 1, 18), false, 2200);
    }

    private void BuildHouse(string id, Vector3i origin, bool owned, int price)
    {
        for (var x = 0; x < 10; x++) for (var z = 0; z < 8; z++) SetCityBlock(origin + new Vector3i(x, 0, z), BlockType.Wood);
        for (var y = 1; y < 5; y++)
        for (var x = 0; x < 10; x++)
        for (var z = 0; z < 8; z++)
        {
            var wall = x == 0 || z == 0 || x == 9 || z == 7;
            if (wall && !(x == 4 && z == 0 && y < 3)) SetCityBlock(origin + new Vector3i(x, y, z), (x + z + y) % 5 == 0 ? BlockType.Glass : BlockType.Wood);
        }
        for (var x = -1; x <= 10; x++) for (var z = -1; z <= 8; z++) SetCityBlock(origin + new Vector3i(x, 5, z), BlockType.Brick);
        SetCityBlock(origin + new Vector3i(2, 1, 3), BlockType.Wood);
        SetCityBlock(origin + new Vector3i(7, 1, 4), BlockType.Glass);
        Doors.Add(new Door { Id = $"door-{id}", Position = new Vector3(origin.X + 4.5f, 1.05f, origin.Z - 0.38f), Yaw = 0f });
        CityHomes.Add(new HouseLot(id, new Vector3(origin.X + 4, 1, origin.Z - 2), price, owned));
    }

    private void BuildPublicBuilding(string id, Vector3i origin, Vector3i size, BlockType material, Vector4 signColor)
    {
        for (var x = 0; x < size.X; x++) for (var z = 0; z < size.Z; z++) SetCityBlock(origin + new Vector3i(x, 0, z), BlockType.Concrete);
        for (var y = 1; y <= size.Y; y++)
        for (var x = 0; x < size.X; x++)
        for (var z = 0; z < size.Z; z++)
        {
            var edge = x == 0 || z == 0 || x == size.X - 1 || z == size.Z - 1;
            if (edge) SetCityBlock(origin + new Vector3i(x, y, z), y % 3 == 0 ? BlockType.Glass : material);
        }
        for (var x = -1; x <= size.X; x++) for (var z = -1; z <= size.Z; z++) SetCityBlock(origin + new Vector3i(x, size.Y + 1, z), BlockType.Concrete);
        Doors.Add(new Door { Id = $"door-{id}", Position = new Vector3(origin.X + size.X / 2f, 1.05f, origin.Z - 0.38f), Yaw = 0f });
        Signs.Add(new DetailCube(new Vector3(origin.X + size.X / 2f, size.Y + 2.5f, origin.Z - 0.5f), new Vector3(5, 1, 0.25f), signColor));
    }

    private void BuildAirport()
    {
        BuildRoad(56, 132, -4, 4);
        for (var x = 56; x < 132; x++) for (var z = -16; z <= -10; z++) SetCityBlock(new Vector3i(x, 0, z), BlockType.Runway);
        BuildPublicBuilding("airport-terminal", new Vector3i(63, 1, -34), new Vector3i(24, 5, 10), BlockType.Glass, new Vector4(0.4f, 0.8f, 1f, 1));
        BuildPublicBuilding("warehouse-a", new Vector3i(72, 1, 20), new Vector3i(18, 5, 14), BlockType.Concrete, new Vector4(0.75f, 0.75f, 0.7f, 1));
        BuildPublicBuilding("hangar-a", new Vector3i(98, 1, -35), new Vector3i(22, 8, 15), BlockType.Concrete, new Vector4(0.55f, 0.58f, 0.62f, 1));
    }

    private void BuildStreetLights()
    {
        for (var i = -40; i <= 40; i += 10)
        {
            Signs.Add(new DetailCube(new Vector3(i, 3.3f, 7), new Vector3(0.22f, 4.5f, 0.22f), new Vector4(0.15f, 0.15f, 0.16f, 1)));
            Signs.Add(new DetailCube(new Vector3(i, 5.75f, 7), new Vector3(0.9f, 0.35f, 0.9f), new Vector4(1f, 0.88f, 0.45f, 1)));
            Signs.Add(new DetailCube(new Vector3(7, 3.3f, i), new Vector3(0.22f, 4.5f, 0.22f), new Vector4(0.15f, 0.15f, 0.16f, 1)));
            Signs.Add(new DetailCube(new Vector3(7, 5.75f, i), new Vector3(0.9f, 0.35f, 0.9f), new Vector4(1f, 0.88f, 0.45f, 1)));
        }
    }


    private void BuildBrookhavenPolish()
    {
        var sidewalk = new Vector4(0.86f, 0.88f, 0.92f, 1);
        Signs.Add(new DetailCube(new Vector3(0, 0.08f, 8.5f), new Vector3(90f, 0.14f, 2.2f), sidewalk));
        Signs.Add(new DetailCube(new Vector3(0, 0.08f, -8.5f), new Vector3(90f, 0.14f, 2.2f), sidewalk));
        Signs.Add(new DetailCube(new Vector3(8.5f, 0.08f, 0), new Vector3(2.2f, 0.14f, 90f), sidewalk));
        Signs.Add(new DetailCube(new Vector3(-8.5f, 0.08f, 0), new Vector3(2.2f, 0.14f, 90f), sidewalk));

        for (var i = -38; i <= 38; i += 8)
        {
            Signs.Add(new DetailCube(new Vector3(i, 0.16f, 0), new Vector3(3.2f, 0.08f, 0.18f), new Vector4(1f, 0.93f, 0.45f, 1)));
            Signs.Add(new DetailCube(new Vector3(0, 0.16f, i), new Vector3(0.18f, 0.08f, 3.2f), new Vector4(1f, 0.93f, 0.45f, 1)));
        }

        for (var i = -5; i <= 5; i += 2)
        {
            Signs.Add(new DetailCube(new Vector3(i, 0.18f, 6.2f), new Vector3(0.8f, 0.08f, 2.1f), new Vector4(1f, 1f, 1f, 1)));
            Signs.Add(new DetailCube(new Vector3(6.2f, 0.18f, i), new Vector3(2.1f, 0.08f, 0.8f), new Vector4(1f, 1f, 1f, 1)));
        }

        Signs.Add(new DetailCube(new Vector3(-1, 6.8f, -10.2f), new Vector3(13f, 2.2f, 0.28f), new Vector4(0.04f, 0.18f, 0.42f, 1)));
        Signs.Add(new DetailCube(new Vector3(-1, 7.0f, -10.42f), new Vector3(11.8f, 1.25f, 0.18f), new Vector4(0.0f, 0.68f, 1f, 1)));
        Signs.Add(new DetailCube(new Vector3(13, 1.15f, 12), new Vector3(4.0f, 0.55f, 1.4f), new Vector4(0.94f, 0.22f, 0.34f, 1)));
        Signs.Add(new DetailCube(new Vector3(13, 1.85f, 12), new Vector3(0.35f, 1.5f, 1.2f), new Vector4(0.96f, 0.78f, 0.25f, 1)));
        Signs.Add(new DetailCube(new Vector3(-14, 1.05f, 12), new Vector3(3.5f, 0.42f, 1.0f), new Vector4(0.16f, 0.46f, 0.95f, 1)));

        for (var i = 0; i < 8; i++)
        {
            var angle = i / 8f * MathHelper.TwoPi;
            var pos = new Vector3(MathF.Cos(angle) * 4f + 2f, 0.55f, MathF.Sin(angle) * 4f + 14f);
            Signs.Add(new DetailCube(pos, new Vector3(0.55f, 0.65f, 0.55f), new Vector4(0.14f, 0.72f, 0.32f, 1)));
        }
        Signs.Add(new DetailCube(new Vector3(2f, 1.1f, 14f), new Vector3(1.8f, 1.1f, 1.8f), new Vector4(0.3f, 0.72f, 1f, 0.75f)));
    }

    private bool RiverAt(int x, int z) => Math.Abs(z - MathF.Sin(x * 0.07f) * 10f - 92f) < 4f;
    private void SetCityBlock(Vector3i pos, BlockType type)
    {
        _cityBlocks.Add(pos);
        _overrides[pos] = type;
    }
}

public static class BlockPalette
{
    public static Vector4 GetColor(BlockType type) => type switch
    {
        BlockType.Grass => new Vector4(0.38f, 0.82f, 0.32f, 1),
        BlockType.Dirt => new Vector4(0.58f, 0.38f, 0.22f, 1),
        BlockType.Stone => new Vector4(0.63f, 0.65f, 0.7f, 1),
        BlockType.Wood => new Vector4(0.72f, 0.46f, 0.22f, 1),
        BlockType.Leaves => new Vector4(0.18f, 0.72f, 0.24f, 1),
        BlockType.Road => new Vector4(0.12f, 0.13f, 0.16f, 1),
        BlockType.Water => new Vector4(0.14f, 0.62f, 1f, 0.78f),
        BlockType.Glass => new Vector4(0.56f, 0.9f, 1f, 0.64f),
        BlockType.Brick => new Vector4(0.86f, 0.28f, 0.22f, 1),
        BlockType.Concrete => new Vector4(0.86f, 0.86f, 0.84f, 1),
        BlockType.Light => new Vector4(1f, 0.85f, 0.35f, 1),
        BlockType.Runway => new Vector4(0.13f, 0.13f, 0.15f, 1),
        _ => Vector4.Zero
    };
}

public sealed class EconomyManager
{
    public int PlayerMoney { get; private set; } = 2000;
    public int CityTaxes { get; private set; }
    public Dictionary<string, int> DynamicPrices { get; set; } = new() { ["Wood"] = 8, ["Stone"] = 12, ["Vehicle"] = 900, ["FlightLesson"] = 250 };
    public bool TrySpend(int amount)
    {
        if (PlayerMoney < amount) return false;
        PlayerMoney -= amount;
        CityTaxes += Math.Max(1, amount / 20);
        return true;
    }
    public void PayJobSalary(string job, int amount) => PlayerMoney += amount;
    public EconomySave ToSave() => new(PlayerMoney, CityTaxes, DynamicPrices);
    public void FromSave(EconomySave? save)
    {
        if (save is null) return;
        PlayerMoney = save.PlayerMoney;
        CityTaxes = save.CityTaxes;
        DynamicPrices = save.DynamicPrices;
    }
}

public enum VehicleKind { Car, Motorcycle, Airplane }
public sealed class Vehicle
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonConverter(typeof(JsonStringEnumConverter))] public VehicleKind Kind { get; set; }
    public Vector3 Position { get; set; }
    public float Yaw { get; set; }
    public float Speed { get; set; }
    public bool IsPlayerDriven { get; set; }
    public bool IsTraffic { get; set; }
    public bool OwnedByPlayer { get; set; }
    public bool RequestTakeoff { get; set; }
    public Vector4 Color { get; set; } = new(0.9f, 0.1f, 0.1f, 1);
    public Vector3 CameraOffset => Kind == VehicleKind.Airplane ? new Vector3(-8, 3.2f, 0) : new Vector3(-4, 2.4f, 0);

    public void UpdatePlayerDrive(float dt, float throttle, float steer, bool brake)
    {
        var max = Kind == VehicleKind.Airplane ? 70f : Kind == VehicleKind.Motorcycle ? 22f : 18f;
        Speed = Math.Clamp(Speed + throttle * max * 0.85f * dt, -8f, max);
        if (brake) Speed *= MathF.Pow(0.05f, dt);
        Yaw += steer * (Kind == VehicleKind.Airplane ? 30f : 95f) * dt * Math.Clamp(MathF.Abs(Speed) / 8f, 0.2f, 1.4f);
        var dir = new Vector3(MathF.Cos(MathHelper.DegreesToRadians(Yaw)), 0, MathF.Sin(MathHelper.DegreesToRadians(Yaw)));
        Position += dir * Speed * dt;
        if (Kind == VehicleKind.Airplane && (RequestTakeoff || Speed > 38f)) Position += Vector3.UnitY * Math.Clamp((Speed - 32f) * 0.18f, 0, 6f) * dt;
        if (Kind != VehicleKind.Airplane) Position = new Vector3(Position.X, 1.05f, Position.Z);
        Speed *= MathF.Pow(0.92f, dt);
    }

    public void UpdateTraffic(float dt, IReadOnlyList<Vector3> road)
    {
        if (road.Count == 0 || IsPlayerDriven) return;
        var target = road[Math.Abs(Id.GetHashCode() + (int)(GLFW.GetTime() / 5)) % road.Count];
        var delta = target - Position;
        if (delta.LengthSquared < 4f) return;
        var dir = Vector3.Normalize(new Vector3(delta.X, 0, delta.Z));
        Yaw = MathHelper.RadiansToDegrees(MathF.Atan2(dir.Z, dir.X));
        Position += dir * 5.5f * dt;
    }
}

public static class VehicleFactory
{
    public static IEnumerable<Vehicle> CreateDefaultFleet() =>
    [
        new Vehicle { Id = "starter-car", Kind = VehicleKind.Car, Position = new Vector3(-12, 1.1f, -12), OwnedByPlayer = true, Color = new Vector4(0.1f, 0.35f, 1f, 1) },
        new Vehicle { Id = "moto-01", Kind = VehicleKind.Motorcycle, Position = new Vector3(8, 1.1f, -8), Color = new Vector4(0.95f, 0.16f, 0.08f, 1) },
        new Vehicle { Id = "plane-trainer", Kind = VehicleKind.Airplane, Position = new Vector3(62, 1.3f, -13), Yaw = 0, OwnedByPlayer = true, Color = new Vector4(1f, 1f, 1f, 1) },
        new Vehicle { Id = "traffic-a", Kind = VehicleKind.Car, Position = new Vector3(-38, 1.1f, 0), IsTraffic = true, Color = new Vector4(1f, 0.85f, 0.08f, 1) },
        new Vehicle { Id = "traffic-b", Kind = VehicleKind.Car, Position = new Vector3(0, 1.1f, 36), IsTraffic = true, Color = new Vector4(0.2f, 1f, 0.5f, 1) }
    ];
}

public sealed class NpcManager
{
    public List<Npc> Npcs { get; set; } = [];
    private readonly string[] _names = ["Lina", "Noah", "Maya", "Sacha", "Ines", "Adam", "Jade", "Leo", "Nora", "Eli"];
    public void SpawnPopulation(IReadOnlyList<HouseLot> homes, IReadOnlyList<WorkPlace> workplaces)
    {
        if (Npcs.Count > 0 || homes.Count == 0 || workplaces.Count == 0) return;
        for (var i = 0; i < 26; i++)
        {
            var home = homes[i % homes.Count];
            var work = workplaces[i % workplaces.Count];
            Npcs.Add(new Npc
            {
                Id = $"npc-{i:00}",
                Name = _names[i % _names.Length] + " " + (100 + i),
                Role = work.Role,
                Home = home.Entrance,
                Work = work.Position,
                Position = home.Entrance + new Vector3(i % 5, 0, i % 3),
                Money = 300 + i * 17,
                State = NpcState.Travel
            });
        }
    }
    public void Update(float dt, double hour, List<Vehicle> vehicles)
    {
        foreach (var npc in Npcs)
        {
            var target = hour switch
            {
                >= 22 or < 6 => npc.Home,
                >= 8 and < 16 => npc.Work,
                >= 17 and < 19 => new Vector3(24, 1, -24),
                _ => npc.Home + new Vector3(3, 0, 3)
            };
            npc.State = Vector3.Distance(target, npc.Position) < 1.5f ? (hour >= 22 || hour < 6 ? NpcState.Sleep : hour >= 8 && hour < 16 ? NpcState.Work : NpcState.Shop) : NpcState.Travel;
            if (npc.State == NpcState.Travel)
            {
                var delta = target - npc.Position;
                var dir = Vector3.Normalize(new Vector3(delta.X, 0, delta.Z));
                npc.Position += dir * dt * 2.1f;
            }
            npc.Mood = npc.State == NpcState.Sleep ? "tired" : npc.State == NpcState.Shop ? "happy" : "normal";
        }
    }
}

public enum NpcState { Idle, Travel, Work, Shop, Sleep }
public sealed class Npc
{
    public string Id { get; set; } = "npc";
    public string Name { get; set; } = "Citizen";
    public string Role { get; set; } = "Citoyen";
    public Vector3 Position { get; set; }
    public Vector3 Home { get; set; }
    public Vector3 Work { get; set; }
    public int Money { get; set; }
    public string Mood { get; set; } = "normal";
    [JsonConverter(typeof(JsonStringEnumConverter))] public NpcState State { get; set; }
    public Vector4 RoleColor => Role switch
    {
        "Policier" => new Vector4(0.08f, 0.14f, 0.55f, 1),
        "Medecin" => new Vector4(0.95f, 0.95f, 1f, 1),
        "Commercant" => new Vector4(0.2f, 0.75f, 0.45f, 1),
        "Pilote" => new Vector4(0.85f, 0.85f, 0.95f, 1),
        _ => new Vector4(0.9f, 0.45f, 0.2f, 1)
    };
}

public sealed class PlayerState
{
    public Vector3 Position { get; set; }
    public float Yaw { get; set; } = -90f;
    public float Pitch { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))] public BlockType SelectedBlock { get; set; } = BlockType.Wood;
    public Dictionary<BlockType, int> Inventory { get; set; } = new() { [BlockType.Dirt] = 64, [BlockType.Stone] = 64, [BlockType.Wood] = 64, [BlockType.Glass] = 24 };
    public static PlayerState CreateDefault() => new() { Position = new Vector3(-10, 1, -14) };
    public void Add(BlockType type, int amount) => Inventory[type] = Inventory.GetValueOrDefault(type) + amount;
    public bool Consume(BlockType type, int amount)
    {
        if (Inventory.GetValueOrDefault(type) < amount) return false;
        Inventory[type] -= amount;
        return true;
    }
}

public sealed class SaveManager
{
    private readonly string _dir = Path.Combine(AppContext.BaseDirectory, "Saves");
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, IncludeFields = true, Converters = { new JsonStringEnumConverter(), new Vector3JsonConverter(), new Vector4JsonConverter() } };
    public void LoadAll(WorldManager world, EconomyManager economy, NpcManager npcs, List<Vehicle> vehicles, ref PlayerState player)
    {
        Directory.CreateDirectory(_dir);
        var worldSave = Read<WorldSave>("world.json");
        if (worldSave is not null)
        {
            world.LoadModifiedBlocks(worldSave.ModifiedBlocks);
            world.ApplyHouseOwnership(worldSave.HouseOwnership);
            world.ApplyDoorStates(worldSave.DoorStates);
        }
        player = Read<PlayerState>("player.json") ?? player;
        economy.FromSave(Read<EconomySave>("economy.json"));
        var npcSave = Read<List<Npc>>("npcs.json");
        if (npcSave is { Count: > 0 }) npcs.Npcs = npcSave;
        var vehicleSave = Read<List<Vehicle>>("vehicles.json");
        if (vehicleSave is { Count: > 0 })
        {
            vehicles.Clear();
            vehicles.AddRange(vehicleSave);
        }
    }
    public void SaveAll(WorldManager world, EconomyManager economy, NpcManager npcs, List<Vehicle> vehicles, PlayerState player)
    {
        Directory.CreateDirectory(_dir);
        Write("world.json", new WorldSave(world.SaveModifiedBlocks(), world.SaveHouseOwnership(), world.SaveDoorStates()));
        Write("player.json", player);
        Write("economy.json", economy.ToSave());
        Write("npcs.json", npcs.Npcs);
        Write("vehicles.json", vehicles);
    }
    private T? Read<T>(string file)
    {
        var path = Path.Combine(_dir, file);
        return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), _json) : default;
    }
    private void Write<T>(string file, T data) => File.WriteAllText(Path.Combine(_dir, file), JsonSerializer.Serialize(data, _json));
}

public sealed class WorldSave
{
    public Dictionary<string, string> ModifiedBlocks { get; set; } = [];
    public Dictionary<string, bool> HouseOwnership { get; set; } = [];
    public Dictionary<string, DoorSave> DoorStates { get; set; } = [];

    public WorldSave()
    {
    }

    public WorldSave(Dictionary<string, string> modifiedBlocks, Dictionary<string, bool> houseOwnership, Dictionary<string, DoorSave> doorStates)
    {
        ModifiedBlocks = modifiedBlocks;
        HouseOwnership = houseOwnership;
        DoorStates = doorStates;
    }
}
public sealed record EconomySave(int PlayerMoney, int CityTaxes, Dictionary<string, int> DynamicPrices);

public sealed class Vector3JsonConverter : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var values = JsonSerializer.Deserialize<float[]>(ref reader, options) ?? [0, 0, 0];
        return new Vector3(values.ElementAtOrDefault(0), values.ElementAtOrDefault(1), values.ElementAtOrDefault(2));
    }
    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options) => JsonSerializer.Serialize(writer, new[] { value.X, value.Y, value.Z }, options);
}

public sealed class Vector4JsonConverter : JsonConverter<Vector4>
{
    public override Vector4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var values = JsonSerializer.Deserialize<float[]>(ref reader, options) ?? [0, 0, 0, 1];
        return new Vector4(values.ElementAtOrDefault(0), values.ElementAtOrDefault(1), values.ElementAtOrDefault(2), values.ElementAtOrDefault(3));
    }
    public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options) => JsonSerializer.Serialize(writer, new[] { value.X, value.Y, value.Z, value.W }, options);
}

public sealed class ShaderProgram : IDisposable
{
    public int Handle { get; }
    public ShaderProgram(string vertex, string fragment)
    {
        var v = Compile(ShaderType.VertexShader, vertex);
        var f = Compile(ShaderType.FragmentShader, fragment);
        Handle = GL.CreateProgram();
        GL.AttachShader(Handle, v);
        GL.AttachShader(Handle, f);
        GL.LinkProgram(Handle);
        GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out var status);
        if (status == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(Handle));
        GL.DeleteShader(v);
        GL.DeleteShader(f);
    }
    public void Use() => GL.UseProgram(Handle);
    public void SetMatrix4(string name, Matrix4 value) => GL.UniformMatrix4(GL.GetUniformLocation(Handle, name), false, ref value);
    public void SetVector3(string name, Vector3 value) => GL.Uniform3(GL.GetUniformLocation(Handle, name), value);
    public void SetFloat(string name, float value) => GL.Uniform1(GL.GetUniformLocation(Handle, name), value);
    public void SetVector4(string name, Vector4 value) => GL.Uniform4(GL.GetUniformLocation(Handle, name), value);
    public void Dispose() => GL.DeleteProgram(Handle);
    private static int Compile(ShaderType type, string source)
    {
        var shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out var status);
        if (status == 0) throw new InvalidOperationException(GL.GetShaderInfoLog(shader));
        return shader;
    }
}

public sealed class CubeRenderer : IDisposable
{
    private readonly ShaderProgram _shader;
    private readonly int _vao;
    private readonly int _vbo;
    public CubeRenderer(ShaderProgram shader)
    {
        _shader = shader;
        var vertices = CubeVertices();
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
    }
    public void Draw(Vector3 position, Vector3 scale, Vector4 color) => DrawRotated(position, scale, color, 0);
    public void DrawRotated(Vector3 position, Vector3 scale, Vector4 color, float yaw)
    {
        var model = Matrix4.CreateScale(scale) * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(-yaw)) * Matrix4.CreateTranslation(position + new Vector3(0.5f, 0.5f, 0.5f));
        _shader.SetMatrix4("model", model);
        _shader.SetVector4("objectColor", color);
        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
    }
    public void Dispose()
    {
        GL.DeleteBuffer(_vbo);
        GL.DeleteVertexArray(_vao);
    }
    private static float[] CubeVertices() =>
    [
        -0.5f,-0.5f,-0.5f, 0,0,-1, 0.5f,0.5f,-0.5f, 0,0,-1, 0.5f,-0.5f,-0.5f, 0,0,-1, 0.5f,0.5f,-0.5f, 0,0,-1, -0.5f,-0.5f,-0.5f, 0,0,-1, -0.5f,0.5f,-0.5f, 0,0,-1,
        -0.5f,-0.5f,0.5f, 0,0,1, 0.5f,-0.5f,0.5f, 0,0,1, 0.5f,0.5f,0.5f, 0,0,1, 0.5f,0.5f,0.5f, 0,0,1, -0.5f,0.5f,0.5f, 0,0,1, -0.5f,-0.5f,0.5f, 0,0,1,
        -0.5f,0.5f,0.5f, -1,0,0, -0.5f,0.5f,-0.5f, -1,0,0, -0.5f,-0.5f,-0.5f, -1,0,0, -0.5f,-0.5f,-0.5f, -1,0,0, -0.5f,-0.5f,0.5f, -1,0,0, -0.5f,0.5f,0.5f, -1,0,0,
        0.5f,0.5f,0.5f, 1,0,0, 0.5f,-0.5f,-0.5f, 1,0,0, 0.5f,0.5f,-0.5f, 1,0,0, 0.5f,-0.5f,-0.5f, 1,0,0, 0.5f,0.5f,0.5f, 1,0,0, 0.5f,-0.5f,0.5f, 1,0,0,
        -0.5f,-0.5f,-0.5f, 0,-1,0, 0.5f,-0.5f,-0.5f, 0,-1,0, 0.5f,-0.5f,0.5f, 0,-1,0, 0.5f,-0.5f,0.5f, 0,-1,0, -0.5f,-0.5f,0.5f, 0,-1,0, -0.5f,-0.5f,-0.5f, 0,-1,0,
        -0.5f,0.5f,-0.5f, 0,1,0, 0.5f,0.5f,0.5f, 0,1,0, 0.5f,0.5f,-0.5f, 0,1,0, 0.5f,0.5f,0.5f, 0,1,0, -0.5f,0.5f,-0.5f, 0,1,0, -0.5f,0.5f,0.5f, 0,1,0
    ];
}
