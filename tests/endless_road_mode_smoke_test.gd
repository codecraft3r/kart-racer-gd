extends Node

func _ready() -> void:
    var mode = get_node_or_null("/root/Node3D/EndlessRoadMode")
    if mode == null:
        push_error("EndlessRoadMode not found")
        set_exit_code(1)
        return

    if not Engine.has_singleton("EndlessRoadMode"):
        push_error("EndlessRoadMode singleton is not registered")
        set_exit_code(1)
        return

    var settings = mode.Settings
    if settings == null:
        push_error("EndlessRoadSettings is null")
        set_exit_code(1)
        return

    mode.ResetRun(42)
    mode.StartRun(42)

    var max_frames = 240
    for frame in range(max_frames):
        await get_tree().process_frame

        if mode.State == mode.RunState.Results or mode.State == mode.RunState.GameOver:
            break

    if mode.Score < 0:
        push_error("Score is negative")
        set_exit_code(1)
        return

    if mode.Health < 0 or mode.Health > 110:
        push_error("Health is out of bounds")
        set_exit_code(1)
        return

    if mode.Boost < 0 or mode.Boost > 1.1:
        push_error("Boost is out of bounds")
        set_exit_code(1)
        return

    print("OK EndlessRoad smoke passed")
