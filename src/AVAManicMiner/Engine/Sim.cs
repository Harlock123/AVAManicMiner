namespace AVAManicMiner.Engine;

/// <summary>
/// A cavern, step by step: Miner Tom walking, jumping (a fixed arc, as a jump should be) and falling, the conveyors
/// and the crumbling ledges, the guardians on their beats, the air running out, keys, nasties and the portal.
/// </summary>
public static class Sim
{
    private const double Dt = Field.Step, T = Field.Tile;
    /// <summary>How long a crumbling square bears the miner before it goes.</summary>
    public const double CrumbleTime = 0.6;

    public static void Start(World w, CavernDef c, bool guardians = true)
    {
        w.Cavern = c;
        w.Grid = (Tile[,])c.Grid.Clone();
        for (int x = 0; x < Field.Cols; x++)
        for (int y = 0; y < Field.Rows; y++)
            w.Crumble[x, y] = CrumbleTime;
        w.Taken = new bool[c.Keys.Count];
        w.Air = c.Air;
        w.Clock = 0;
        w.Out = false;
        w.Cause = "";
        w.Miner = new Miner { X = (c.Start.Col + 0.5) * T, Y = Field.Top + (c.Start.Row + 1) * T };
        w.Guardians = guardians ? c.Guardians.Select(g => new Guardian { Def = g, X = g.X, Y = g.Y }).ToList() : [];
    }

    public static void Step(World w, Controls c)
    {
        w.Clock += Dt;
        foreach (var g in w.Guardians) Patrol(g);
        var m = w.Miner;
        if (m.Dead || w.Out) return;
        w.Air -= Dt;
        if (w.Air <= 0) { Die(w, "out of air"); return; }
        if (!m.Airborne) Ground(w, m, c);
        if (m.Airborne) Air(w, m);
        m.JumpPrev = c.Jump;
        if (!m.Dead) Touch(w, m);
    }

    private static void Patrol(Guardian g)
    {
        var d = g.Def;
        if (d.Vertical)
        {
            g.Y += g.Dir * d.Speed * Dt;
            if (g.Y > d.Max) { g.Y = d.Max; g.Dir = -1; }
            if (g.Y < d.Min) { g.Y = d.Min; g.Dir = 1; }
        }
        else
        {
            g.X += g.Dir * d.Speed * Dt;
            if (g.X > d.Max) { g.X = d.Max; g.Dir = -1; }
            if (g.X < d.Min) { g.X = d.Min; g.Dir = 1; }
        }
    }

    /// <summary>The square under a foot.</summary>
    private static (int C, int R) Under(double x, double feet) => ((int)Math.Floor(x / T), (int)Math.Floor((feet + 1 - Field.Top) / T));

    /// <summary>Whether there's something to stand on under either foot, exactly at boot level.</summary>
    public static bool Footing(World w, double x, double feet)
    {
        if (Math.Abs((feet - Field.Top) / T - Math.Round((feet - Field.Top) / T)) > 0.001) return false;
        foreach (double fx in new[] { x - 6, x + 6 })
        {
            var (c, r) = Under(fx, feet);
            if (CavernBook.Standable(w.At(c, r))) return true;
        }
        return false;
    }

    private static bool Blocked(World w, double x, double feet)
    {
        foreach (double fx in new[] { x - Miner.HalfWidth, x + Miner.HalfWidth })
            for (double y = feet - Miner.Height + 2; y < feet - 1; y += 12)
                if (w.AtPoint(fx, y) == Tile.Wall) return true;
        return w.AtPoint(x - Miner.HalfWidth, feet - 2) == Tile.Wall || w.AtPoint(x + Miner.HalfWidth, feet - 2) == Tile.Wall;
    }

    private static void Ground(World w, Miner m, Controls c)
    {
        if (!Footing(w, m.X, m.Y))
        {
            // Off the edge: straight down.
            m.Airborne = true;
            m.Vy = 0;
            m.Jdx = 0;
            m.FallFrom = m.Y;
            return;
        }
        if (c.Jump && !m.JumpPrev)
        {
            m.Airborne = true;
            m.Vy = -Miner.JumpSpeed;
            m.Jdx = c.Dx;
            if (c.Dx != 0) m.Facing = c.Dx;
            m.FallFrom = double.NaN;
            w.Heard.Add(Sfx.Jump);
            return;
        }
        // A conveyor carries him; pushing against it only holds him still.
        var (uc, ur) = Under(m.X, m.Y);
        var under = w.At(uc, ur);
        int belt = under == Tile.ConveyorLeft ? -1 : under == Tile.ConveyorRight ? 1 : 0;
        int dx = c.Dx;
        if (belt != 0) dx = dx == -belt ? 0 : belt;
        if (dx != 0)
        {
            m.Facing = c.Dx != 0 ? c.Dx : dx;
            double nx = m.X + dx * Miner.Speed * Dt;
            if (!Blocked(w, nx, m.Y)) { m.X = nx; m.Walk += Dt; }
        }
        // Crumbling ledges give way under him.
        foreach (double fx in new[] { m.X - 6, m.X + 6 })
        {
            var (fc, fr) = Under(fx, m.Y);
            if (w.At(fc, fr) != Tile.Crumble) continue;
            w.Crumble[fc, fr] -= Dt / 2;
            if (w.Crumble[fc, fr] <= 0) { w.Grid[fc, fr] = Tile.Empty; w.Heard.Add(Sfx.Crumble); }
        }
    }

    private static void Air(World w, Miner m)
    {
        double nx = m.X + m.Jdx * Miner.Speed * Dt;
        if (m.Jdx != 0)
        {
            if (Blocked(w, nx, m.Y)) m.Jdx = 0;
            else m.X = nx;
        }
        double vy = m.Vy + Miner.Gravity * Dt, ny = m.Y + vy * Dt;
        if (vy < 0)
        {
            // A head against brick stops the climb.
            double head = ny - Miner.Height;
            if (w.AtPoint(m.X - 6, head) == Tile.Wall || w.AtPoint(m.X + 6, head) == Tile.Wall) { vy = 0; ny = m.Y; }
        }
        if (m.Vy < 0 && vy >= 0 || double.IsNaN(m.FallFrom) && vy >= 0) m.FallFrom = m.Y;
        if (vy > 0)
        {
            // Down onto the first thing to stand on between where his boots were and where they'd be.
            for (int r = (int)Math.Ceiling((m.Y - Field.Top) / T - 0.001); r <= (int)Math.Floor((ny - Field.Top) / T); r++)
            {
                double top = Field.Top + r * T;
                if (top < m.Y - 0.001 || !Footing(w, m.X, top)) continue;
                m.Y = top;
                m.Vy = 0;
                m.Airborne = false;
                if (m.Y - m.FallFrom > Miner.MaxFall) Die(w, "a fall");
                else w.Heard.Add(Sfx.Land);
                return;
            }
        }
        m.Vy = vy;
        m.Y = ny;
        if (m.Y > Field.Bottom + 40) Die(w, "a fall");
    }

    private static void Touch(World w, Miner m)
    {
        double x0 = m.X - Miner.HalfWidth + 3, x1 = m.X + Miner.HalfWidth - 3, y0 = m.Y - Miner.Height + 4, y1 = m.Y - 2;
        // Nasties.
        for (int c = (int)Math.Floor(x0 / T); c <= (int)Math.Floor(x1 / T); c++)
        for (int r = (int)Math.Floor((y0 - Field.Top) / T); r <= (int)Math.Floor((y1 - Field.Top) / T); r++)
            if (w.At(c, r) == Tile.Nasty)
            {
                // The nasty fills the lower part of its square.
                double ny0 = Field.Top + r * T + 10, nx0 = c * T + 6, nx1 = c * T + T - 6;
                if (y1 > ny0 && x1 > nx0 && x0 < nx1) { Die(w, "a nasty"); return; }
            }
        foreach (var g in w.Guardians)
        {
            var (hw, hh) = g.Half;
            double gy = g.Def.Vertical ? g.Y : g.Y - hh;
            if (x1 > g.X - hw + 3 && x0 < g.X + hw - 3 && y1 > gy - hh + 3 && y0 < gy + hh - 3) { Die(w, "a guardian"); return; }
        }
        if (w.Probe) return;
        var keys = w.Cavern.Keys;
        for (int i = 0; i < keys.Count; i++)
        {
            if (w.Taken[i]) continue;
            double kx = (keys[i].Col + 0.5) * T, ky = Field.Top + (keys[i].Row + 0.5) * T;
            if (x1 > kx - 10 && x0 < kx + 10 && y1 > ky - 10 && y0 < ky + 10)
            {
                w.Taken[i] = true;
                w.Score += 100;
                w.Heard.Add(w.PortalOpen ? Sfx.PortalOpen : Sfx.Key);
            }
        }
        if (w.PortalOpen)
        {
            double px = (w.Cavern.Portal.Col + 1) * T, py = Field.Top + (w.Cavern.Portal.Row + 1) * T;
            if (Math.Abs(m.X - px) < 24 && Math.Abs(m.Y - 28 - py) < 22) { w.Out = true; w.Heard.Add(Sfx.Portal); }
        }
    }

    public static void Die(World w, string cause)
    {
        if (w.Miner.Dead) return;
        w.Miner.Dead = true;
        w.Cause = cause;
        w.Heard.Add(Sfx.Die);
    }
}
