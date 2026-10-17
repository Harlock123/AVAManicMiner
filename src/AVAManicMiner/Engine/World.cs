namespace AVAManicMiner.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>Miner Tom: where his boots are, which way he faces, and whether he's in the air.</summary>
public sealed class Miner
{
    public const double HalfWidth = 9, Height = 56, Speed = 150, JumpSpeed = 436, Gravity = 1322;
    /// <summary>A fall further than this (from the top of a jump, or the edge he walked off) is the end of him.</summary>
    public const double MaxFall = 4 * Field.Tile + 8;
    public double X, Y, Vy, FallFrom, Walk;
    public int Facing = 1, Jdx;
    public bool Airborne, Dead, JumpPrev;
    public Miner Copy() => (Miner)MemberwiseClone();
}

/// <summary>A guardian on its beat.</summary>
public sealed class Guardian
{
    public GuardianDef Def;
    public double X, Y;
    public int Dir = 1;
    public Guardian Copy() => (Guardian)MemberwiseClone();
    public (double W, double H) Half => Def.Vertical ? (13, 13) : (13, 20);
}

/// <summary>Everything in a cavern that moves or changes, kept together so it can be copied whole.</summary>
public sealed class World
{
    public CavernDef Cavern = null!;
    public Tile[,] Grid = new Tile[Field.Cols, Field.Rows];
    /// <summary>How much each crumbling square has left (in seconds of standing on).</summary>
    public double[,] Crumble = new double[Field.Cols, Field.Rows];
    public Miner Miner = new();
    public List<Guardian> Guardians = [];
    public bool[] Taken = [];
    public double Air, Clock;
    public int Score;
    /// <summary>Into the open portal: the cavern is done.</summary>
    public bool Out;
    public string Cause = "";
    public List<Sfx> Heard = [];
    /// <summary>A world used only to try moves out: keys and the portal are left alone.</summary>
    public bool Probe;

    public bool PortalOpen => Taken.All(t => t);
    public int KeysLeft => Taken.Count(t => !t);

    public World Copy() => new()
    {
        Cavern = Cavern, Grid = (Tile[,])Grid.Clone(), Crumble = (double[,])Crumble.Clone(), Miner = Miner.Copy(),
        Guardians = Guardians.Select(g => g.Copy()).ToList(), Taken = (bool[])Taken.Clone(),
        Air = Air, Clock = Clock, Score = Score, Out = Out, Cause = Cause,
    };

    public Tile At(int c, int r) => c < 0 || c >= Field.Cols || r < 0 || r >= Field.Rows ? Tile.Wall : Grid[c, r];
    public Tile AtPoint(double x, double y) => At((int)Math.Floor(x / Field.Tile), (int)Math.Floor((y - Field.Top) / Field.Tile));
}
