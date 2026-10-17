namespace AVAManicMiner.Engine;

/// <summary>Left or right, and jump. (Up and down do nothing down a mine.)</summary>
public readonly record struct Controls(int Dx = 0, bool Jump = false);

/// <summary>The screen: the cavern, 32 squares by 16, then its name, the air, the score and the lives below.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768, Top = 40;
    public const double Tile = 32;
    public const int Cols = 32, Rows = 16;
    public const double Bottom = Top + Rows * Tile;
}

/// <summary>What a square of a cavern is.</summary>
public enum Tile : byte
{
    Empty,
    /// <summary>A ledge: stood on from above, jumped up through from below.</summary>
    Floor,
    /// <summary>Brick or rock: nothing passes.</summary>
    Wall,
    /// <summary>A ledge that crumbles away under the miner's boots.</summary>
    Crumble,
    /// <summary>A conveyor belt running left (or right): it carries the miner along.</summary>
    ConveyorLeft,
    ConveyorRight,
    /// <summary>A poisonous plant, a spike, an icicle: touching it is the end.</summary>
    Nasty,
}

/// <summary>A guardian: patrolling to and fro along a ledge, or up and down a shaft.</summary>
public readonly record struct GuardianDef(bool Vertical, double X, double Y, double Min, double Max, double Speed, int Look);

/// <summary>How a cavern looks: its name, its colours, and what its nasties and guardians are.</summary>
public sealed record Theme(string Name, uint Back, uint Floor, uint Wall, uint Accent, int Nasty, int Guardian);

/// <summary>A cavern: its squares, its keys, its portal, where the miner starts, its guardians, and its air.</summary>
public sealed class CavernDef
{
    public int Index;
    public Theme Theme = CavernBook.Themes[0];
    public Tile[,] Grid = new Tile[Field.Cols, Field.Rows];
    /// <summary>The keys, each in a square.</summary>
    public List<(int Col, int Row)> Keys = [];
    /// <summary>The portal's top-left square (it is two by two).</summary>
    public (int Col, int Row) Portal;
    /// <summary>The square the miner starts standing in (his boots on the square below).</summary>
    public (int Col, int Row) Start;
    public List<GuardianDef> Guardians = [];
    public double Air = 90;

    public Tile At(int c, int r) => c < 0 || c >= Field.Cols ? Tile.Wall : r < 0 ? Tile.Wall : r >= Field.Rows ? Tile.Wall : Grid[c, r];
}

/// <summary>The caverns: each built from its own seed, then proved to be solvable before it's kept.</summary>
public static class CavernBook
{
    public static readonly Theme[] Themes =
    [
        new("THE OLD PITHEAD", 0xFF000000, 0xFFB04020, 0xFFC08040, 0xFFFFE040, 0, 0),
        new("FROST HOLLOW", 0xFF000818, 0xFF80C0FF, 0xFF4060A0, 0xFFFFFFFF, 1, 1),
        new("THE LAMP ROOM", 0xFF100800, 0xFFE0A030, 0xFF805020, 0xFFFFFF80, 2, 2),
        new("RAT RUN", 0xFF080808, 0xFF8A6A4A, 0xFF5A4A3A, 0xFFE0E0E0, 0, 3),
        new("THE ENGINE HOUSE", 0xFF000010, 0xFF60A0A0, 0xFF808080, 0xFFFF6040, 2, 4),
        new("FUNGUS GALLERY", 0xFF001000, 0xFF60C040, 0xFF406020, 0xFFFF80FF, 0, 5),
        new("THE SLUICE", 0xFF000818, 0xFF4080E0, 0xFF205080, 0xFF80FFFF, 1, 2),
        new("CLOCKWORK HALL", 0xFF100010, 0xFFC0A0E0, 0xFF6A4A8A, 0xFFFFC040, 2, 4),
        new("THE FURNACE", 0xFF180400, 0xFFFF6020, 0xFF802010, 0xFFFFFF40, 2, 1),
        new("DEEP SEAM", 0xFF000000, 0xFFA0A0A0, 0xFF505050, 0xFFFF4040, 0, 5),
    ];

    /// <summary>The ledges' rows, every two squares up from the floor.</summary>
    private static readonly int[] Tiers = [13, 11, 9, 7, 5, 3];

    public static readonly CavernDef[] All = Enumerable.Range(0, Themes.Length).Select(Build).ToArray();

    private static CavernDef Build(int index)
    {
        for (int attempt = 0; ; attempt++)
        {
            var c = Try(index, 1000 + index * 97 + attempt * 7919);
            if (c != null) return c;
        }
    }

    private static CavernDef? Try(int index, int seed)
    {
        var d = new Dice(seed);
        var s = new CavernDef { Index = index, Theme = Themes[index], Air = 95 - index * 2.5 };
        for (int c = 0; c < Field.Cols; c++)
        for (int r = 0; r < Field.Rows; r++)
            s.Grid[c, r] = c == 0 || c == Field.Cols - 1 || r == 0 || r == Field.Rows - 1 ? Tile.Wall : Tile.Empty;
        // Ledges along every tier, with gaps.
        var segments = new List<(int Row, int C0, int C1)>();
        foreach (int row in Tiers)
        {
            int col = 1 + d.Next(3);
            while (col < Field.Cols - 2)
            {
                int len = 3 + d.Next(6);
                int c1 = Math.Min(Field.Cols - 2, col + len - 1);
                for (int c = col; c <= c1; c++) s.Grid[c, row] = Tile.Floor;
                segments.Add((row, col, c1));
                col = c1 + 2 + d.Next(3);
            }
        }
        // A few brick pillars on the ground, and a brick block or two.
        for (int k = 0; k < 1 + index / 4; k++)
        {
            int c = 6 + d.Next(Field.Cols - 10);
            s.Grid[c, 14] = Tile.Wall;
        }
        // Conveyors and crumbling ledges.
        var ledges = segments.Where(g => g.C1 - g.C0 >= 3).ToList();
        for (int k = 0; k < 1 + index / 3 && ledges.Count > 0; k++)
        {
            var g = ledges[d.Next(ledges.Count)];
            ledges.Remove(g);
            var t = d.Next(2) == 0 ? Tile.ConveyorLeft : Tile.ConveyorRight;
            for (int c = g.C0; c <= g.C1; c++) s.Grid[c, g.Row] = t;
        }
        for (int k = 0; k < 1 + index / 3 && ledges.Count > 0; k++)
        {
            var g = ledges[d.Next(ledges.Count)];
            ledges.Remove(g);
            int a = g.C0 + d.Next(Math.Max(1, g.C1 - g.C0 - 1));
            for (int c = a; c <= Math.Min(g.C1, a + 2); c++) s.Grid[c, g.Row] = Tile.Crumble;
        }
        s.Start = (2, 14);
        // Nasties standing on ledges and the ground.
        for (int k = 0; k < 4 + index / 2; k++)
        {
            int c = 2 + d.Next(Field.Cols - 4), r = Tiers[d.Next(Tiers.Length)] - 1;
            if (d.Next(3) == 0) r = 14;
            if (Math.Abs(c - s.Start.Col) < 4 && r == 14) continue;
            if (Standable(s.At(c, r + 1)) && s.At(c, r) == Tile.Empty) s.Grid[c, r] = Tile.Nasty;
        }
        // Where the miner can get to.
        var graph = Graph.Build(s);
        var from = graph.Reach(s.Start);
        if (from.Count < 30) return null;
        var spots = from.Where(n => n.Row < 14 || n.Col > 8).ToList();
        if (spots.Count < 12) return null;
        // The portal: somewhere reached, well away from the start, with room.
        var portalSpots = spots.Where(n => n.Col < Field.Cols - 2 && Math.Abs(n.Col - s.Start.Col) + Math.Abs(n.Row - s.Start.Row) > 14 && s.At(n.Col + 1, n.Row) == Tile.Empty && s.At(n.Col + 1, n.Row - 1) == Tile.Empty && s.At(n.Col, n.Row - 1) == Tile.Empty).ToList();
        if (portalSpots.Count == 0) return null;
        var portal = portalSpots[d.Next(portalSpots.Count)];
        s.Portal = (portal.Col, portal.Row - 1);
        // Keys: at head height at places reached, spread out.
        int keys = 4 + index / 3;
        for (int tries = 0; s.Keys.Count < keys && tries < 400; tries++)
        {
            var n = spots[d.Next(spots.Count)];
            var k = (n.Col, n.Row - 1);
            if (s.Keys.Any(o => Math.Abs(o.Col - k.Item1) + Math.Abs(o.Row - k.Item2) < 6)) continue;
            if (Math.Abs(k.Item1 - s.Portal.Col) < 4 && Math.Abs(k.Item2 - s.Portal.Row) < 3) continue;
            if (s.At(k.Item1, k.Item2) != Tile.Empty) continue;
            s.Keys.Add(k);
        }
        if (s.Keys.Count < keys) return null;
        // No dead ends: from every key, every other key and the portal can still be reached.
        var targets = s.Keys.Select(k => new Spot(k.Col, k.Row + 1)).Append(portal).ToList();
        foreach (var t in targets)
        {
            var on = graph.Reach(t);
            if (!targets.All(on.Contains)) return null;
        }
        // Guardians: along ledges (not the start's), and up and down open shafts.
        int guards = 2 + index / 3;
        var avoid = new HashSet<Spot>();
        var patrols = segments.Where(g => g.C1 - g.C0 >= 4 && !(g.Row == 13 && g.C0 <= 4)).ToList();
        for (int k = 0; k < guards && patrols.Count > 0; k++)
        {
            var g = patrols[d.Next(patrols.Count)];
            patrols.Remove(g);
            double y = Field.Top + g.Row * Field.Tile;
            double speed = 55 + d.Next(40) + index * 4;
            // Only where it can be kept clear of: every key and the portal still within reach without standing on its beat.
            var beat = new HashSet<Spot>(avoid);
            for (int c = g.C0; c <= g.C1; c++) beat.Add(new Spot(c, g.Row - 1));
            var without = Graph.Build(s, avoid: beat);
            if (!targets.Append(new Spot(s.Start.Col, s.Start.Row)).All(t => targets.All(without.Reach(t).Contains))) { k--; continue; }
            avoid = beat;
            s.Guardians.Add(new GuardianDef(false, (g.C0 + 0.5) * Field.Tile, y, (g.C0 + 0.5) * Field.Tile, (g.C1 + 0.5) * Field.Tile, speed, s.Theme.Guardian));
        }
        if (index >= 2)
        {
            int c = 6 + d.Next(Field.Cols - 12);
            s.Guardians.Add(new GuardianDef(true, (c + 0.5) * Field.Tile, Field.Top + 4 * Field.Tile, Field.Top + 3 * Field.Tile, Field.Top + 11 * Field.Tile, 60 + index * 5, s.Theme.Guardian));
        }
        return s;
    }

    public static bool Standable(Tile t) => t is Tile.Floor or Tile.Wall or Tile.Crumble or Tile.ConveyorLeft or Tile.ConveyorRight;
}
