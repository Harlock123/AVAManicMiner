using Avalonia.Input;
using AVAManicMiner.Engine;

namespace AVAManicMiner.Tests;

public class GameplayTests
{
    private const double T = Field.Tile;
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avamanicminer-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A bare cavern: brick all round, nothing in it, the portal and a key out of the way up in the corner.</summary>
    private static CavernDef Bare(Action<CavernDef>? build = null)
    {
        var c = new CavernDef { Start = (2, 14), Portal = (28, 1), Air = 60 };
        for (int x = 0; x < Field.Cols; x++)
        for (int y = 0; y < Field.Rows; y++)
            c.Grid[x, y] = x == 0 || x == Field.Cols - 1 || y == 0 || y == Field.Rows - 1 ? Tile.Wall : Tile.Empty;
        c.Keys.Add((26, 1));
        build?.Invoke(c);
        return c;
    }

    private static World In(CavernDef c, double col = 2.5, int row = 14)
    {
        var w = new World();
        Sim.Start(w, c);
        w.Miner.X = col * T;
        w.Miner.Y = Field.Top + (row + 1) * T;
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    private static readonly Controls Right = new(1), Left = new(-1);
    private static readonly double Ground = Field.Top + 15 * T;

    // ───────────────────────────── walking and jumping ─────────────────────────────

    [Fact]
    public void He_walks_at_a_steady_pace()
    {
        var w = In(Bare(), 5.5);
        Run(w, 60, Right);
        Assert.Equal(5.5 * T + Miner.Speed, w.Miner.X, 3);
        Run(w, 60, Left);
        Assert.Equal(5.5 * T, w.Miner.X, 3);
        Assert.Equal(-1, w.Miner.Facing);
    }

    [Fact]
    public void Brick_stops_him()
    {
        var w = In(Bare(c => { c.Grid[10, 14] = Tile.Wall; c.Grid[10, 13] = Tile.Wall; }), 5.5);
        Run(w, 120, Right);
        Assert.InRange(w.Miner.X, 9 * T, 10 * T - Miner.HalfWidth + 0.01);
    }

    [Fact]
    public void A_jump_is_a_fixed_arc_that_pushing_the_other_way_does_not_change()
    {
        var w = In(Bare(), 5.5);
        Sim.Step(w, new Controls(1, true));
        double top = w.Miner.Y;
        int air = 1;
        while (w.Miner.Airborne && air < 200)
        {
            Sim.Step(w, Left);
            top = Math.Min(top, w.Miner.Y);
            air++;
        }
        Assert.False(w.Miner.Dead);
        Assert.Equal(Ground, w.Miner.Y, 3);
        // Up a little over two squares, and three across.
        Assert.InRange(Ground - top, 2 * T, 2.5 * T);
        Assert.InRange(w.Miner.X - 5.5 * T, 2.8 * T, 3.4 * T);
    }

    [Fact]
    public void Holding_jump_jumps_only_once()
    {
        var w = In(Bare(), 5.5);
        int jumps = 0;
        bool was = false;
        for (int i = 0; i < 180; i++)
        {
            Sim.Step(w, new Controls(0, true));
            if (w.Miner.Airborne && !was) jumps++;
            was = w.Miner.Airborne;
        }
        Assert.Equal(1, jumps);
    }

    [Fact]
    public void He_jumps_up_through_a_ledge_and_lands_on_it()
    {
        var w = In(Bare(c => { for (int x = 4; x <= 9; x++) c.Grid[x, 13] = Tile.Floor; }), 6.5);
        Sim.Step(w, new Controls(0, true));
        Run(w, 90);
        Assert.False(w.Miner.Airborne);
        Assert.Equal(Field.Top + 13 * T, w.Miner.Y, 3);
    }

    [Fact]
    public void Brick_overhead_cuts_a_jump_short()
    {
        var w = In(Bare(c => { for (int x = 3; x <= 9; x++) c.Grid[x, 12] = Tile.Wall; }), 6.5);
        Sim.Step(w, new Controls(0, true));
        double top = w.Miner.Y;
        for (int i = 0; i < 60; i++) { Sim.Step(w, default); top = Math.Min(top, w.Miner.Y); }
        Assert.True(Ground - top < 0.5 * T, $"rose {Ground - top}");
        Assert.False(w.Miner.Airborne);
    }

    [Fact]
    public void A_short_drop_is_survived_and_a_long_one_is_not()
    {
        var low = In(Bare(c => { for (int x = 1; x <= 6; x++) c.Grid[x, 11] = Tile.Floor; }), 5.5, 10);
        Run(low, 90, Right);
        Assert.False(low.Miner.Dead);
        Assert.Equal(Ground, low.Miner.Y, 3);

        var high = In(Bare(c => { for (int x = 1; x <= 6; x++) c.Grid[x, 9] = Tile.Floor; }), 5.5, 8);
        Run(high, 120, Right);
        Assert.True(high.Miner.Dead);
        Assert.Equal("a fall", high.Cause);
    }

    // ───────────────────────────── the cavern ─────────────────────────────

    [Fact]
    public void A_conveyor_carries_him_and_pushing_against_it_holds_him_still()
    {
        var w = In(Bare(c => { for (int x = 3; x <= 20; x++) c.Grid[x, 14] = Tile.ConveyorRight; }), 6.5, 13);
        Run(w, 30);
        Assert.Equal(6.5 * T + Miner.Speed / 2, w.Miner.X, 3);
        double x = w.Miner.X;
        Run(w, 30, Left);
        Assert.Equal(x, w.Miner.X, 3);
    }

    [Fact]
    public void A_crumbling_ledge_gives_way_under_him()
    {
        var w = In(Bare(c => { for (int x = 4; x <= 9; x++) c.Grid[x, 13] = Tile.Floor; c.Grid[6, 13] = Tile.Crumble; }), 6.5, 12);
        Run(w, 20);
        Assert.Equal(Tile.Crumble, w.Grid[6, 13]);
        Run(w, 60);
        Assert.Equal(Tile.Empty, w.Grid[6, 13]);
        Assert.Contains(Sfx.Crumble, w.Heard);
        Assert.Equal(Ground, w.Miner.Y, 3);
        Assert.False(w.Miner.Dead);
    }

    [Fact]
    public void A_nasty_is_the_end_of_him()
    {
        var w = In(Bare(c => c.Grid[8, 14] = Tile.Nasty), 3.5);
        Run(w, 120, Right);
        Assert.True(w.Miner.Dead);
        Assert.Equal("a nasty", w.Cause);
    }

    [Fact]
    public void A_guardian_walks_its_beat_and_catches_him()
    {
        var c = Bare(c => c.Guardians.Add(new GuardianDef(false, 12 * T, Ground, 12 * T, 20 * T, 80, 0)));
        var w = In(c, 4.5);
        double min = double.MaxValue, max = 0;
        for (int i = 0; i < 60 * 8; i++)
        {
            Sim.Step(w, default);
            min = Math.Min(min, w.Guardians[0].X);
            max = Math.Max(max, w.Guardians[0].X);
        }
        Assert.Equal(12 * T, min, 3);
        Assert.Equal(20 * T, max, 3);
        Assert.False(w.Miner.Dead);
        Run(w, 240, Right);
        Assert.True(w.Miner.Dead);
        Assert.Equal("a guardian", w.Cause);
    }

    [Fact]
    public void A_guardian_can_be_jumped()
    {
        var c = Bare(c => c.Guardians.Add(new GuardianDef(false, 10 * T, Ground, 10 * T, 10 * T, 0, 0)));
        var w = In(c, 8);
        Sim.Step(w, new Controls(1, true));
        Run(w, 60, Right);
        Assert.False(w.Miner.Dead);
        Assert.True(w.Miner.X > 11 * T);
    }

    [Fact]
    public void The_air_runs_out()
    {
        var c = Bare();
        c.Air = 2;
        var w = In(c);
        Run(w, 100);
        Assert.False(w.Miner.Dead);
        Run(w, 30);
        Assert.True(w.Miner.Dead);
        Assert.Equal("out of air", w.Cause);
    }

    [Fact]
    public void The_portal_opens_only_when_every_key_is_taken()
    {
        var c = Bare(c => { c.Keys.Clear(); c.Keys.Add((6, 13)); c.Keys.Add((20, 13)); c.Portal = (12, 13); });
        var w = In(c);
        for (int i = 0; i < 400 && w.Miner.X < 16 * T; i++) Sim.Step(w, Right);
        Assert.Equal(1, w.KeysLeft);
        Assert.Equal(100, w.Score);
        Assert.False(w.Out);
        Assert.Contains(Sfx.Key, w.Heard);
        for (int i = 0; i < 400 && w.Miner.X < 21 * T; i++) Sim.Step(w, Right);
        Assert.True(w.PortalOpen);
        Assert.Contains(Sfx.PortalOpen, w.Heard);
        for (int i = 0; i < 400 && !w.Out; i++) Sim.Step(w, Left);
        Assert.True(w.Out);
        Assert.InRange(w.Miner.X, 12 * T, 14 * T);
    }

    // ───────────────────────────── the game ─────────────────────────────

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    [Fact]
    public void Caught_he_starts_the_cavern_again_with_its_keys_back()
    {
        var g = Engine();
        g.Go(0);
        g.World.Taken[0] = true;
        Sim.Die(g.World, "test");
        g.Run(1);
        Assert.Equal(Phase.Dying, g.Phase);
        g.Run((int)(60 * GameEngine.DyingSeconds) + 2);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        Assert.Equal(Phase.Start, g.Phase);
        Assert.Equal(0, g.CavernIndex);
        Assert.All(g.World.Taken, t => Assert.False(t));
        Assert.Equal(g.World.Cavern.Air, g.World.Air);
    }

    [Fact]
    public void Through_the_portal_the_air_left_is_counted_in_and_on_to_the_next_cavern()
    {
        var g = Engine();
        g.Go(0);
        g.World.Air = 30;
        g.World.Out = true;
        g.Run(1);
        Assert.Equal(Phase.Out, g.Phase);
        for (int i = 0; i < 600 && g.CavernIndex == 0; i++) g.Run(1);
        Assert.Equal(1, g.CavernIndex);
        Assert.Equal(1, g.Cleared);
        Assert.InRange(g.Score, 29 * GameEngine.AirPoints, 30 * GameEngine.AirPoints);
        Assert.Equal(CavernBook.All[1].Theme.Name, g.Message);
    }

    [Fact]
    public void Every_ten_thousand_points_is_another_miner()
    {
        var g = Engine();
        g.Go(0);
        g.World.Score = 9900;
        g.World.Air = 20;
        g.World.Out = true;
        for (int i = 0; i < 600 && g.CavernIndex == 0; i++) g.Run(1);
        Assert.Equal(GameEngine.Lives0 + 1, g.Lives);
    }

    [Fact]
    public void The_last_miner_lost_is_game_over_under_the_boot_then_the_high_scores()
    {
        var g = Engine();
        var heard = new List<Sfx>();
        g.Silent = false;
        g.Sound += heard.Add;
        g.Go(0);
        g.SetLives(1);
        g.World.Score = 25000;
        Sim.Die(g.World, "test");
        g.Run((int)(60 * GameEngine.DyingSeconds) + 3);
        Assert.Equal(GameState.GameOver, g.State);
        Assert.Contains(Sfx.Stomp, heard);
        g.Run((int)(60 * (GameEngine.StompSeconds + 1)) + 3);
        Assert.Equal(GameState.EnterInitials, g.State);
        g.Initials!.Type('T');
        g.Initials.Type('O');
        g.Initials.Type('M');
        g.Initials.Finish();
        g.Run(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal("TOM", g.HighScores.Entries[g.NewEntryIndex].Initials);
        Assert.Equal(25000, g.HighScores.Entries[g.NewEntryIndex].Score);
    }

    [Fact]
    public void The_title_gives_way_to_the_demonstration_and_enter_starts_a_game()
    {
        var g = Engine();
        Assert.Equal(GameState.Title, g.State);
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 5);
        Assert.True(g.World.Miner.X != (g.World.Cavern.Start.Col + 0.5) * T || g.World.Miner.Airborne, "the demonstration miner should be moving");
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(GameEngine.Lives0, g.Lives);
        Assert.Equal(0, g.CavernIndex);
    }

    [Fact]
    public void Paused_nothing_moves()
    {
        var g = Engine();
        g.Go(0);
        g.Run(30, Right);
        g.Pause();
        double x = g.World.Miner.X, air = g.World.Air;
        g.Run(120, Right);
        Assert.Equal(x, g.World.Miner.X);
        Assert.Equal(air, g.World.Air);
        g.Resume();
        g.Run(10, Right);
        Assert.True(g.World.Miner.X > x);
    }

    [Fact]
    public void The_keys_walk_and_jump_him()
    {
        using var input = new InputHandler();
        input.OnKeyDown(Key.P);
        Assert.Equal(1, input.Player.Dx);
        input.OnKeyUp(Key.P);
        input.OnKeyDown(Key.O);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyUp(Key.O);
        input.OnKeyDown(Key.Right);
        Assert.Equal(1, input.Player.Dx);
        Assert.False(input.Player.Jump);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Jump);

        // And through to the miner.
        var g = Engine();
        g.Go(0);
        double x = g.World.Miner.X;
        g.Run(30, input.Player);
        Assert.True(g.World.Miner.X > x);
    }

    [Fact]
    public void The_air_warning_is_spoken_once()
    {
        var g = Engine();
        var heard = new List<Sfx>();
        g.Silent = false;
        g.Sound += heard.Add;
        g.Go(0);
        g.World.Air = 16;
        g.Run(120);
        Assert.Single(heard, s => s == Sfx.SayAir);
    }
}
