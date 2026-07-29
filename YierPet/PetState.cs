namespace YierPet;

/// <summary>Animation states matching the hatch-pet 8×9 atlas contract.</summary>
public enum PetState
{
    Idle,
    RunningRight,
    RunningLeft,
    Waving,
    Jumping,
    Failed,
    Waiting,
    Running,
    Review,
}

public static class PetStateExtensions
{
    public static int Row(this PetState state) => state switch
    {
        PetState.Idle => 0,
        PetState.RunningRight => 1,
        PetState.RunningLeft => 2,
        PetState.Waving => 3,
        PetState.Jumping => 4,
        PetState.Failed => 5,
        PetState.Waiting => 6,
        PetState.Running => 7,
        PetState.Review => 8,
        _ => 0,
    };

    public static int[] DurationsMs(this PetState state) => state switch
    {
        PetState.Idle => [280, 110, 110, 140, 140, 320],
        PetState.RunningRight or PetState.RunningLeft =>
            [120, 120, 120, 120, 120, 120, 120, 220],
        PetState.Waving => [140, 140, 140, 280],
        PetState.Jumping => [140, 140, 140, 140, 280],
        PetState.Failed => [140, 140, 140, 140, 140, 140, 140, 240],
        PetState.Waiting => [150, 150, 150, 150, 150, 260],
        PetState.Running => [120, 120, 120, 120, 120, 220],
        PetState.Review => [150, 150, 150, 150, 150, 280],
        _ => [200],
    };

    public static string DisplayName(this PetState state) => state switch
    {
        PetState.Idle => "待机",
        PetState.RunningRight => "向右跑",
        PetState.RunningLeft => "向左跑",
        PetState.Waving => "挥手",
        PetState.Jumping => "跳跃",
        PetState.Failed => "沮丧",
        PetState.Waiting => "等待",
        PetState.Running => "工作中",
        PetState.Review => "审阅",
        _ => state.ToString(),
    };

    public static string StorageKey(this PetState state) => state switch
    {
        PetState.RunningRight => "running-right",
        PetState.RunningLeft => "running-left",
        _ => state.ToString().ToLowerInvariant(),
    };

    public static PetState? FromStorageKey(string raw) => raw switch
    {
        "idle" => PetState.Idle,
        "running-right" => PetState.RunningRight,
        "running-left" => PetState.RunningLeft,
        "waving" => PetState.Waving,
        "jumping" => PetState.Jumping,
        "failed" => PetState.Failed,
        "waiting" => PetState.Waiting,
        "running" => PetState.Running,
        "review" => PetState.Review,
        _ => null,
    };

    public static PetState[] All { get; } =
    [
        PetState.Idle, PetState.RunningRight, PetState.RunningLeft,
        PetState.Waving, PetState.Jumping, PetState.Failed,
        PetState.Waiting, PetState.Running, PetState.Review,
    ];
}
