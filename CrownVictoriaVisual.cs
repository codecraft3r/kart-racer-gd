using Godot;
using System;
using System.Collections.Generic;

/// <summary>Per-car paint, dent morphs and detachable Crown Victoria panels.</summary>
public partial class CrownVictoriaVisual : Node3D
{
    private static readonly string[] PaintTextures =
    {
        "",
        "res://assets/crown-victoria/taxi_basecolor_midnight.res",
        "res://assets/crown-victoria/taxi_basecolor_ivory.res"
    };

    private readonly Dictionary<string, Node3D> _parts = new();
    private readonly Dictionary<string, (Node Parent, Transform3D Transform)> _rest = new();
    private readonly Dictionary<string, float> _dents = new();
    private readonly Dictionary<string, RigidBody3D> _debris = new();
    private readonly HashSet<string> _pendingDetach = new();
    private readonly HashSet<string> _brokenGlass = new();
    private Kart _kart;
    private Node3D _model;
    private readonly List<MeshInstance3D> _replacementLamps = new();

    public override void _ExitTree()
    {
        foreach (RigidBody3D body in _debris.Values)
            if (GodotObject.IsInstanceValid(body)) body.QueueFree();
        _debris.Clear();
    }

    public void Bind(Kart kart, Node3D model, int paint, bool roofSignVisible)
    {
        _kart = kart;
        _model = model;
        CollectParts(model);
        foreach (var (name, part) in _parts)
            _rest[name] = (part.GetParent(), part.Transform);
        BuildFittedLamps();
        SetPaint(paint);
        SetRoofSignVisible(roofSignVisible);
    }

    private void BuildFittedLamps()
    {
        // The supplied GLB has stray rectangular lamp geometry outside the body.
        // Keep its detachable part hierarchy, but fit compact lenses to the fascia.
        foreach (string id in new[] { "Headlight_L", "Headlight_R", "Taillight_L", "Taillight_R" })
            if (_parts.TryGetValue(id, out Node3D source)) source.Visible = false;

        StandardMaterial3D chrome = new() { AlbedoColor = new Color(0.57f, 0.61f, 0.65f), Metallic = 0.8f, Roughness = 0.24f };
        StandardMaterial3D headlight = new() { AlbedoColor = new Color(0.91f, 0.93f, 0.86f), Roughness = 0.18f,
            EmissionEnabled = true, Emission = new Color(0.35f, 0.34f, 0.27f), EmissionEnergyMultiplier = 0.35f };
        StandardMaterial3D taillight = new() { AlbedoColor = new Color(0.52f, 0.015f, 0.025f), Roughness = 0.17f,
            EmissionEnabled = true, Emission = new Color(0.4f, 0.004f, 0.004f), EmissionEnergyMultiplier = 0.2f };

        foreach (float side in new[] { -1.0f, 1.0f })
        {
            AddLamp("HeadlightRim", new Vector3(side * 0.32f, 0.365f, 1.15f), new Vector3(0.22f, 0.105f, 0.035f), chrome);
            AddLamp("HeadlightLens", new Vector3(side * 0.32f, 0.365f, 1.175f), new Vector3(0.19f, 0.078f, 0.036f), headlight);
            AddLamp("TaillightRim", new Vector3(side * 0.40f, 0.425f, -1.15f), new Vector3(0.115f, 0.13f, 0.035f), chrome);
            AddLamp("TaillightLens", new Vector3(side * 0.40f, 0.425f, -1.175f), new Vector3(0.093f, 0.105f, 0.035f), taillight);
        }
    }

    private void AddLamp(string name, Vector3 position, Vector3 size, Material material)
    {
        MeshInstance3D lamp = new() { Name = name, Position = position,
            Mesh = new BoxMesh { Size = size }, MaterialOverride = material };
        _model.AddChild(lamp);
        _replacementLamps.Add(lamp);
    }

    private void CollectParts(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            string name = child.Name.ToString();
            if (child is Node3D part && name.StartsWith("LOD0_", StringComparison.Ordinal))
                _parts[name.Substring(5)] = part;
            CollectParts(child);
        }
    }

    public void SetPaint(int index)
    {
        if (_model == null) return;
        string path = PaintTextures[Mathf.PosMod(index, PaintTextures.Length)];
        Texture2D texture = path.Length == 0 ? null : GD.Load<Texture2D>(path);
        PaintMeshes(_model, texture);
        foreach (RigidBody3D body in _debris.Values)
            if (GodotObject.IsInstanceValid(body)) PaintMeshes(body, texture);
    }

    private static void PaintMeshes(Node node, Texture2D texture)
    {
        if (node is MeshInstance3D instance && instance.Mesh != null)
        {
            for (int surface = 0; surface < instance.Mesh.GetSurfaceCount(); surface++)
            {
                if (instance.Mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D source || source.AlbedoTexture == null)
                    continue; // Glass has no atlas and should remain transparent.
                if (texture == null)
                {
                    instance.SetSurfaceOverrideMaterial(surface, null);
                    continue;
                }
                StandardMaterial3D material = (StandardMaterial3D)source.Duplicate();
                material.AlbedoTexture = texture;
                instance.SetSurfaceOverrideMaterial(surface, material);
            }
        }
        foreach (Node child in node.GetChildren())
            PaintMeshes(child, texture);
    }

    public void SetRoofSignVisible(bool visible)
    {
        if (_parts.TryGetValue("RoofSign", out Node3D sign) && !_debris.ContainsKey("RoofSign"))
            sign.Visible = visible;
    }

    public void ApplyImpact(Vector3 worldPoint, float impactSpeed)
    {
        if (_model == null || _kart == null) return;
        Vector3 local = _model.ToLocal(worldPoint);
        bool front = local.Z >= 0.0f;
        string lengthKey = front ? "Dent_Front" : "Dent_Rear";
        // The wrapper turns the GLB 180 degrees: source left is kart right.
        string sideKey = local.X < 0.0f ? "Dent_Right" : "Dent_Left";
        AddDent(lengthKey, Mathf.Clamp(impactSpeed / 38.0f, 0.06f, 0.45f));
        if (Mathf.Abs(local.X) > 0.38f)
            AddDent(sideKey, Mathf.Clamp(impactSpeed / 42.0f, 0.05f, 0.38f));
        if (local.Y > 0.55f)
            AddDent("Dent_Roof", Mathf.Clamp(impactSpeed / 65.0f, 0.03f, 0.25f));

        if (impactSpeed < 8.0f) return;
        Vector3 away = (worldPoint - _kart.GlobalPosition).Normalized();
        if (away.LengthSquared() < 0.01f) away = Vector3.Up;
        if (Mathf.Abs(local.X) > 0.55f && Mathf.Abs(local.Z) < 0.75f)
        {
            string door = local.X < 0.0f ? (front ? "Door_FR" : "Door_RR") : (front ? "Door_FL" : "Door_RL");
            if (impactSpeed >= 13.0f) QueueDetach(door, away, impactSpeed);
            else BreakGlass(door.Replace("Door", "Glass"));
        }
        else if (front)
        {
            if (impactSpeed >= 11.0f) QueueDetach("Bumper_Front", away, impactSpeed);
            if (impactSpeed >= 14.0f) QueueDetach("Grille", away, impactSpeed);
            if (impactSpeed >= 17.0f) QueueDetach("Hood", away, impactSpeed);
            if (impactSpeed >= 15.0f) BreakGlass("Glass_Windshield");
            if (impactSpeed >= 14.0f) BreakFittedLamp(true, local.X);
        }
        else
        {
            if (impactSpeed >= 11.0f) QueueDetach("Bumper_Rear", away, impactSpeed);
            if (impactSpeed >= 16.0f) QueueDetach("Trunk", away, impactSpeed);
            if (impactSpeed >= 15.0f) BreakGlass("Glass_Rear");
            if (impactSpeed >= 14.0f) BreakFittedLamp(false, local.X);
        }
    }

    private void BreakFittedLamp(bool front, float side)
    {
        string prefix = front ? "Headlight" : "Taillight";
        foreach (MeshInstance3D lamp in _replacementLamps)
            if (lamp.Name.ToString().StartsWith(prefix, StringComparison.Ordinal) &&
                (Mathf.Abs(side) < 0.12f || lamp.Position.X * side > 0.0f))
                lamp.Visible = false;
    }

    private void AddDent(string key, float amount)
    {
        _dents[key] = Mathf.Clamp(_dents.GetValueOrDefault(key) + amount, 0.0f, 1.0f);
        ApplyDents(_model);
    }

    private void ApplyDents(Node node)
    {
        if (node is MeshInstance3D instance && instance.Mesh is ArrayMesh arrayMesh)
        {
            for (int i = 0; i < arrayMesh.GetBlendShapeCount(); i++)
            {
                string name = arrayMesh.GetBlendShapeName(i).ToString();
                if (_dents.TryGetValue(name, out float weight))
                    instance.SetBlendShapeValue(i, weight);
            }
        }
        foreach (Node child in node.GetChildren()) ApplyDents(child);
    }

    private void BreakGlass(string id)
    {
        if (_brokenGlass.Add(id) && _parts.TryGetValue(id, out Node3D glass))
            glass.Visible = false;
    }

    private void QueueDetach(string id, Vector3 direction, float speed)
    {
        if (!_pendingDetach.Add(id)) return;
        Callable.From(() => Detach(id, direction, speed)).CallDeferred();
    }

    private void Detach(string id, Vector3 direction, float speed)
    {
        _pendingDetach.Remove(id);
        if (_debris.ContainsKey(id) || !_parts.TryGetValue(id, out Node3D part) || !_rest.ContainsKey(id))
            return;

        MeshInstance3D mesh = FindMesh(part);
        if (mesh?.Mesh == null) return;
        RigidBody3D body = new()
        {
            Name = $"CrownDebris_{id}",
            Mass = id.StartsWith("Door", StringComparison.Ordinal) ? 32.0f : 10.0f,
            CollisionLayer = 2,
            CollisionMask = 1,
            ContinuousCd = true
        };
        Node parent = _kart.GetParent() ?? GetTree().CurrentScene;
        parent.AddChild(body);
        Transform3D partWorld = part.GlobalTransform;
        body.GlobalTransform = new Transform3D(partWorld.Basis.Orthonormalized(), partWorld.Origin);
        part.Reparent(body, true);
        CollisionShape3D shape = new() { Name = "DebrisHull", Shape = mesh.Mesh.CreateConvexShape(true, true) };
        body.AddChild(shape);
        shape.GlobalTransform = mesh.GlobalTransform;
        body.AddCollisionExceptionWith(_kart);
        body.LinearVelocity = _kart.LinearVelocity + _kart.AngularVelocity.Cross(body.GlobalPosition - _kart.GlobalPosition);
        body.AngularVelocity = _kart.AngularVelocity;
        body.ApplyCentralImpulse((direction + Vector3.Up * 0.35f).Normalized() * speed * body.Mass * 0.4f);
        _debris[id] = body;
    }

    private static MeshInstance3D FindMesh(Node node)
    {
        if (node is MeshInstance3D mesh) return mesh;
        foreach (Node child in node.GetChildren())
        {
            MeshInstance3D found = FindMesh(child);
            if (found != null) return found;
        }
        return null;
    }

    public void ResetDamage()
    {
        foreach (var (id, body) in _debris)
        {
            if (!GodotObject.IsInstanceValid(body) || !_parts.TryGetValue(id, out Node3D part) || !_rest.TryGetValue(id, out var rest))
                continue;
            part.Reparent(rest.Parent, false);
            part.Transform = rest.Transform;
            body.QueueFree();
        }
        _debris.Clear();
        _pendingDetach.Clear();
        foreach (string id in _brokenGlass)
            if (_parts.TryGetValue(id, out Node3D glass)) glass.Visible = true;
        _brokenGlass.Clear();
        _dents.Clear();
        if (_model != null) ClearDents(_model);
        foreach (MeshInstance3D lamp in _replacementLamps) lamp.Visible = true;
        SetRoofSignVisible(_kart?.CrownRoofSignVisible ?? true);
    }

    private static void ClearDents(Node node)
    {
        if (node is MeshInstance3D instance && instance.Mesh is ArrayMesh arrayMesh)
            for (int i = 0; i < arrayMesh.GetBlendShapeCount(); i++)
                instance.SetBlendShapeValue(i, 0.0f);
        foreach (Node child in node.GetChildren()) ClearDents(child);
    }
}
