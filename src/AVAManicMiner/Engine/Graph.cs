namespace AVAManicMiner.Engine;

/// <summary>A move from one standing place to another: walking, or a jump (left, up or right).</summary>
public readonly record struct Move(int Dx, bool Jump);

/// <summary>Where the miner stands: the square his body's lower half is in.</summary>
public readonly record struct Spot(int Col, int Row)
{
    public double X => (Col + 0.5) * Field.Tile;
    public double Feet => Field.Top + (Row + 1) * Field.Tile;
    public static Spot Of(Miner m) => new((int)Math.Floor(m.X / Field.Tile), (int)Math.Round((m.Y - Field.Top) / Field.Tile) - 1);
}

/// <summary>
/// Every place the miner can stand in a cavern, and where each move takes him from there, found by trying the
/// moves out (with no guardians about) in the game's own physics. The caverns are kept only if this shows they
/// can be done; the computer player plans its route by it.
/// </summary>
public sealed class Graph
{
    public static readonly Move[] Moves = [new(-1, false), new(1, false), new(-1, true), new(0, true), new(1, true)];
    public readonly Dictionary<Spot, List<(Move Move, Spot To, int Ticks)>> Edges = [];

    public static Graph Build(CavernDef c, World? state = null, HashSet<Spot>? avoid = null)
    {
        var g = new Graph();
        var probe = new World { Probe = true };
        Sim.Start(probe, c, guardians: false);
        if (state != null) probe.Grid = (Tile[,])state.Grid.Clone();
        var grid = probe.Grid;
        for (int col = 1; col < Field.Cols - 1; col++)
        for (int row = 1; row < Field.Rows - 1; row++)
        {
            var s = new Spot(col, row);
            if (!Stand(probe, s) || avoid != null && avoid.Contains(s)) continue;
            var list = new List<(Move, Spot, int)>();
            foreach (var mv in Moves)
                if (Try(probe, grid, s, mv) is { } to && (avoid == null || !avoid.Contains(to.Spot))) list.Add((mv, to.Spot, to.Ticks));
            g.Edges[s] = list;
        }
        return g;
    }

    /// <summary>Whether the miner can stand at a spot: something underfoot, nothing solid or deadly where he'd be.</summary>
    public static bool Stand(World w, Spot s) =>
        CavernBook.Standable(w.At(s.Col, s.Row + 1)) && w.At(s.Col, s.Row) is Tile.Empty && w.At(s.Col, s.Row - 1) is not (Tile.Wall or Tile.Nasty);

    /// <summary>A move tried from a spot: where it ends, standing (and alive), and how long it took.</summary>
    public static (Spot Spot, int Ticks)? Try(World probe, Tile[,] grid, Spot s, Move mv)
    {
        var w = probe;
        w.Grid = (Tile[,])grid.Clone();
        w.Miner = new Miner { X = s.X, Y = s.Feet };
        w.Air = 1e9;
        w.Out = false;
        int ticks = 0;
        foreach (var c in Script(w, mv))
        {
            Sim.Step(w, c);
            ticks++;
            if (w.Miner.Dead || ticks > 240) return null;
        }
        var end = Spot.Of(w.Miner);
        if (end == s && !mv.Jump) return null;
        return (end, ticks);
    }

    /// <summary>
    /// The controls for a move, one step at a time, stopping once he's standing again: a walk goes on to the middle of
    /// the next square (or over the edge and down); a jump goes until he lands.
    /// </summary>
    public static IEnumerable<Controls> Script(World w, Move mv)
    {
        var m = w.Miner;
        if (mv.Jump)
        {
            yield return new Controls(mv.Dx, true);
            for (int i = 0; i < 240 && m.Airborne && !m.Dead; i++) yield return new Controls(mv.Dx, false);
            yield break;
        }
        // On to the middle of the next square (which also puts him back in the middle of one).
        double target = (Math.Floor(m.X / Field.Tile) + mv.Dx + 0.5) * Field.Tile;
        for (int i = 0; i < 240 && !m.Dead; i++)
        {
            if (m.Airborne) { yield return default; continue; }
            if (mv.Dx > 0 ? m.X >= target - 1.3 : m.X <= target + 1.3) yield break;
            double before = m.X;
            yield return new Controls(mv.Dx);
            // Held up (a wall, or a conveyor running the other way).
            if (Math.Abs(m.X - before) < 0.01 && !m.Airborne) yield break;
        }
    }

    /// <summary>Every spot that can be reached from one.</summary>
    public HashSet<Spot> Reach((int Col, int Row) from) => Reach(new Spot(from.Col, from.Row));

    public HashSet<Spot> Reach(Spot from)
    {
        var seen = new HashSet<Spot> { from };
        var q = new Queue<Spot>(seen);
        while (q.Count > 0)
        {
            var s = q.Dequeue();
            if (!Edges.TryGetValue(s, out var list)) continue;
            foreach (var (_, to, _) in list)
                if (seen.Add(to)) q.Enqueue(to);
        }
        return seen;
    }
}
