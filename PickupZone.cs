using Godot;

public partial class PickupZone : Area3D
{
    [Export] public GameManager.CustomerDistance Distance = GameManager.CustomerDistance.Near;
    [Export] public GameManager.CustomerWealth Wealth = GameManager.CustomerWealth.Low;
    [Export] public int MaxAcceptableDamage = 30;
    [Export] public int GroupSize = 1;
    [Export] public float LoadTime = 5.0f;

    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;
    }

    private void OnBodyEntered(Node body)
    {
        if (!Multiplayer.IsServer()) return;
        if (body is not RigidBody3D kart) return;

        // TODO: integrate with GameManager customer selection logic
        GD.Print($"Customer zone triggered by {kart.Name}");
    }

    public GameManager.CustomerData GetCustomerData()
    {
        return new GameManager.CustomerData
        {
            Distance = Distance,
            Wealth = Wealth,
            MaxAcceptableDamage = MaxAcceptableDamage,
            GroupSize = GroupSize,
            LoadTime = LoadTime
        };
    }
}
