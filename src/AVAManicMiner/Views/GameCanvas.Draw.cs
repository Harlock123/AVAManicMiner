using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAManicMiner.Engine;

namespace AVAManicMiner.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAManicMiner/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    private const double T = Field.Tile;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            case GameState.GameOver: DrawBoot(dc); break;
            default:
                DrawCavern(dc, g.World);
                DrawPanel(dc);
                if (g.Message is { } msg && g.Phase == Phase.Start)
                    Text(dc, msg, new Point(Field.Width / 2, Field.Top + 250), 56, 0xFFFFFFFF, center: true);
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, Field.Height - 16), 22, Yellow, center: true);
    }

    // ───────────────────────────── the cavern ─────────────────────────────

    private void DrawCavern(DrawingContext dc, World w)
    {
        var th = w.Cavern.Theme;
        dc.FillRectangle(Brush(th.Back), new Rect(0, Field.Top, Field.Width, Field.Rows * T));
        for (int c = 0; c < Field.Cols; c++)
        for (int r = 0; r < Field.Rows; r++)
            DrawTile(dc, w, c, r, th);
        DrawPortal(dc, w);
        var keys = w.Cavern.Keys;
        for (int i = 0; i < keys.Count; i++)
            if (!w.Taken[i]) DrawKey(dc, (keys[i].Col + 0.5) * T, Field.Top + (keys[i].Row + 0.5) * T, i);
        foreach (var gd in w.Guardians) DrawGuardian(dc, gd);
        var m = w.Miner;
        if (!m.Dead || (int)(Engine.PhaseTime * 12) % 2 == 0) DrawMiner(dc, m.X, m.Y, m.Facing, m.Airborne ? 1 : m.Walk, m.Dead);
    }

    private void DrawTile(DrawingContext dc, World w, int c, int r, Theme th)
    {
        var t = w.Grid[c, r];
        double x = c * T, y = Field.Top + r * T;
        switch (t)
        {
            case Tile.Wall:
            {
                dc.FillRectangle(Brush(th.Wall), new Rect(x, y, T, T));
                // Bricks.
                uint mortar = Alpha(0xFF000000, 0.45);
                dc.FillRectangle(Brush(mortar), new Rect(x, y + 15, T, 2));
                dc.FillRectangle(Brush(mortar), new Rect(x, y + 31, T, 1));
                dc.FillRectangle(Brush(mortar), new Rect(x + (r % 2 == 0 ? 8 : 24), y, 2, 15));
                dc.FillRectangle(Brush(mortar), new Rect(x + (r % 2 == 0 ? 24 : 8), y + 16, 2, 15));
                break;
            }
            case Tile.Floor:
                dc.FillRectangle(Brush(th.Floor), new Rect(x, y, T, 12));
                dc.FillRectangle(Brush(Alpha(0xFF000000, 0.35)), new Rect(x, y + 9, T, 3));
                for (int k = 0; k < 4; k++) dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, 0.25)), new Rect(x + 2 + k * 8, y + 2, 4, 3));
                break;
            case Tile.Crumble:
            {
                double left = w.Crumble[c, r] / Sim.CrumbleTime;
                double h = 12 * left;
                dc.FillRectangle(Brush(Alpha(th.Floor, 0.6)), new Rect(x, y, T, h));
                for (int k = 0; k < 6; k++) dc.FillRectangle(Brush(th.Floor), new Rect(x + 1 + k * 5, y + (k % 2) * 3, 3, Math.Max(1, h - 4)));
                break;
            }
            case Tile.ConveyorLeft:
            case Tile.ConveyorRight:
            {
                dc.FillRectangle(Brush(Alpha(th.Floor, 0.85)), new Rect(x, y, T, 12));
                double shift = (_clock * 40 * (t == Tile.ConveyorLeft ? -1 : 1)) % 8;
                if (shift < 0) shift += 8;
                for (int k = -1; k < 5; k++) dc.FillRectangle(Brush(0xFF101010), new Rect(x + k * 8 + shift, y + 3, 3, 6));
                dc.DrawLine(Pen(0xFFFFFFFF, 1.5), new Point(x, y), new Point(x + T, y));
                dc.DrawLine(Pen(0xFFFFFFFF, 1.5), new Point(x, y + 12), new Point(x + T, y + 12));
                break;
            }
            case Tile.Nasty:
                DrawNasty(dc, x, y, th);
                break;
        }
    }

    private void DrawNasty(DrawingContext dc, double x, double y, Theme th)
    {
        switch (th.Nasty)
        {
            case 0:
                // A poisonous bush.
                dc.FillEllipse(x + 16, y + 24, 10, 0xFF20A030);
                dc.FillEllipse(x + 9, y + 22, 6, 0xFF30C040);
                dc.FillEllipse(x + 23, y + 21, 6, 0xFF30C040);
                for (int k = 0; k < 4; k++) dc.FillEllipse(x + 8 + k * 5, y + 16 + (k % 2) * 6, 2, 0xFFFF40FF);
                break;
            case 1:
                // Spikes of ice.
                for (int k = 0; k < 4; k++)
                    Poly(dc, 0xFFC0F0FF, 0xFF6090C0, 1, new Point(x + 2 + k * 7, y + T), new Point(x + 5.5 + k * 7, y + 10 + (k % 2) * 4), new Point(x + 9 + k * 7, y + T));
                break;
            default:
            {
                // A flame.
                double f = Math.Sin(_clock * 18 + x) * 3;
                Poly(dc, 0xFFFF6020, null, 0, new Point(x + 6, y + T), new Point(x + 10 + f, y + 12), new Point(x + 16, y + 4 - f), new Point(x + 22 - f, y + 12), new Point(x + 26, y + T));
                Poly(dc, 0xFFFFE040, null, 0, new Point(x + 11, y + T), new Point(x + 16, y + 14 + f), new Point(x + 21, y + T));
                break;
            }
        }
    }

    private void DrawKey(DrawingContext dc, double x, double y, int i)
    {
        uint[] cycle = [0xFFFFFF40, 0xFF40FFFF, 0xFFFF40FF, 0xFF40FF40];
        uint c = cycle[((int)(_clock * 8) + i) % cycle.Length];
        dc.DrawEllipse(null, Pen(c, 3), new Point(x - 5, y), 5, 5);
        dc.FillRectangle(Brush(c), new Rect(x, y - 1.5, 12, 3));
        dc.FillRectangle(Brush(c), new Rect(x + 7, y, 2.5, 5));
        dc.FillRectangle(Brush(c), new Rect(x + 10, y, 2.5, 4));
    }

    private void DrawPortal(DrawingContext dc, World w)
    {
        var (pc, pr) = w.Cavern.Portal;
        double x = pc * T, y = Field.Top + pr * T;
        bool open = w.PortalOpen;
        bool flash = open && (int)(_clock * 6) % 2 == 0;
        uint frame = flash ? 0xFFFFFFFF : w.Cavern.Theme.Accent, inner = flash ? w.Cavern.Theme.Accent : 0xFF202060;
        dc.DrawRectangle(Brush(inner), Pen(frame, 4), new Rect(x + 2, y + 2, 2 * T - 4, 2 * T - 4));
        for (int k = 0; k < 4; k++)
            dc.DrawRectangle(null, Pen(Alpha(frame, 0.6), 2), new Rect(x + 8 + k * 6, y + 8 + k * 6, 2 * T - 16 - k * 12, 2 * T - 16 - k * 12));
    }

    /// <summary>Miner Tom: a miner's helmet with a lamp, a waistcoat, and legs that stride.</summary>
    private void DrawMiner(DrawingContext dc, double x, double feet, int facing, double walk, bool dead = false)
    {
        double y = feet;
        int frame = (int)(walk * 10) % 4;
        double stride = frame switch { 0 => 0, 1 => 5, 2 => 0, _ => -5 };
        uint skin = 0xFFF0C090, coat = dead ? 0xFFFFFFFF : 0xFF3060E0, trousers = 0xFF2A2A40, hat = 0xFFE0C020;
        // Legs and boots.
        dc.DrawLine(Pen(trousers, 5), new Point(x - 3, y - 18), new Point(x - 3 + stride, y - 3));
        dc.DrawLine(Pen(trousers, 5), new Point(x + 3, y - 18), new Point(x + 3 - stride, y - 3));
        dc.FillRectangle(Brush(0xFF4A2A10), new Rect(x - 6 + stride, y - 4, 8, 4));
        dc.FillRectangle(Brush(0xFF4A2A10), new Rect(x - 2 - stride, y - 4, 8, 4));
        // Body and arms.
        dc.FillRectangle(Brush(coat), new Rect(x - 8, y - 40, 16, 23), 3);
        dc.DrawLine(Pen(coat, 4), new Point(x, y - 34), new Point(x + facing * 10 - stride * 0.6, y - 22));
        // Head, helmet, lamp.
        dc.FillEllipse(x + facing * 1, y - 46, 7, skin);
        dc.FillEllipse(x + facing * 4, y - 47, 1.4, 0xFF101010);
        dc.FillRectangle(Brush(hat), new Rect(x - 9, y - 56, 18, 7), 3);
        dc.FillRectangle(Brush(hat), new Rect(x - 11 + (facing > 0 ? 6 : 0), y - 50, 16, 3));
        dc.FillEllipse(x + facing * 8, y - 53, 3, 0xFFFF4020);
        if (!dead) dc.FillEllipse(x + facing * 8, y - 53, 6, 0x40FFF080);
    }

    private void DrawGuardian(DrawingContext dc, Guardian gd)
    {
        double x = gd.X, y = gd.Def.Vertical ? gd.Y + 13 : gd.Y;
        int f = gd.Dir;
        double bob = Math.Sin(_clock * 12 + x * 0.1) * 2;
        switch (gd.Def.Look)
        {
            case 0:
                // A clanking robot.
                dc.FillRectangle(Brush(0xFFC0C0C8), new Rect(x - 11, y - 34 + bob, 22, 22), 3);
                dc.FillRectangle(Brush(0xFF202028), new Rect(x - 6 + f * 2, y - 29 + bob, 12, 6));
                dc.FillEllipse(x - 2 + f * 3, y - 26 + bob, 2, 0xFFFF2020);
                dc.DrawLine(Pen(0xFF8A8A90, 2), new Point(x, y - 34 + bob), new Point(x, y - 40 + bob));
                dc.FillEllipse(x, y - 41 + bob, 2.5, 0xFFFFE040);
                dc.FillRectangle(Brush(0xFF6A6A70), new Rect(x - 8, y - 12, 6, 12));
                dc.FillRectangle(Brush(0xFF6A6A70), new Rect(x + 2, y - 12, 6, 12));
                break;
            case 1:
                // A penguin.
                dc.DrawEllipse(Brush(0xFF202028), null, new Point(x, y - 18), 11, 17);
                dc.DrawEllipse(Brush(0xFFF0F0F0), null, new Point(x + f * 3, y - 16), 7, 12);
                dc.FillEllipse(x + f * 4, y - 28, 1.6, 0xFF000000);
                Poly(dc, 0xFFFFA020, null, 0, new Point(x + f * 9, y - 26), new Point(x + f * 15, y - 24), new Point(x + f * 9, y - 22));
                dc.FillRectangle(Brush(0xFFFFA020), new Rect(x - 7, y - 3, 6, 3));
                dc.FillRectangle(Brush(0xFFFFA020), new Rect(x + 1, y - 3, 6, 3));
                break;
            case 2:
                // A floating lantern-ghost.
                dc.DrawEllipse(Brush(Alpha(0xFFFFE080, 0.35)), null, new Point(x, y - 22 + bob), 16, 16);
                dc.DrawRectangle(Brush(0xFFFFC040), Pen(0xFF6A4A10, 2), new Rect(x - 8, y - 32 + bob, 16, 20), 3, 3);
                dc.FillEllipse(x - 3, y - 24 + bob, 2, 0xFF402000);
                dc.FillEllipse(x + 3, y - 24 + bob, 2, 0xFF402000);
                break;
            case 3:
                // A rat.
                dc.DrawEllipse(Brush(0xFF8A7A6A), null, new Point(x, y - 9), 13, 8);
                dc.FillEllipse(x + f * 12, y - 11, 6, 0xFF8A7A6A);
                dc.FillEllipse(x + f * 15, y - 13, 1.6, 0xFFFF2020);
                dc.FillEllipse(x + f * 9, y - 17, 3.5, 0xFFE0A0A0);
                dc.DrawLine(Pen(0xFFE0A0A0, 2), new Point(x - f * 12, y - 8), new Point(x - f * 24, y - 14 + bob * 2));
                break;
            case 4:
            {
                // A clockwork cog on legs.
                double a = _clock * 4 * f;
                for (int k = 0; k < 8; k++)
                {
                    double ang = a + k * Math.PI / 4;
                    dc.FillRectangle(Brush(0xFFD0A040), new Rect(x + Math.Cos(ang) * 13 - 3, y - 24 + Math.Sin(ang) * 13 - 3, 6, 6));
                }
                dc.FillEllipse(x, y - 24, 11, 0xFFB08030);
                dc.FillEllipse(x, y - 24, 4, 0xFF302010);
                dc.DrawLine(Pen(0xFF6A5A3A, 3), new Point(x - 5, y - 14), new Point(x - 7 + bob * 2, y));
                dc.DrawLine(Pen(0xFF6A5A3A, 3), new Point(x + 5, y - 14), new Point(x + 7 - bob * 2, y));
                break;
            }
            default:
                // A walking mushroom.
                dc.DrawEllipse(Brush(0xFFE04040), null, new Point(x, y - 26), 14, 10);
                for (int k = 0; k < 3; k++) dc.FillEllipse(x - 7 + k * 7, y - 29 + (k % 2) * 3, 2.5, 0xFFFFFFFF);
                dc.FillRectangle(Brush(0xFFF0E0C0), new Rect(x - 6, y - 18, 12, 14), 3);
                dc.FillEllipse(x + f * 2, y - 13, 1.4, 0xFF000000);
                dc.DrawLine(Pen(0xFFF0E0C0, 3), new Point(x - 3, y - 4), new Point(x - 5 + bob * 2, y));
                dc.DrawLine(Pen(0xFFF0E0C0, 3), new Point(x + 3, y - 4), new Point(x + 5 - bob * 2, y));
                break;
        }
    }

    // ───────────────────────────── the panel ─────────────────────────────

    private void DrawPanel(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        double y = Field.Bottom;
        dc.FillRectangle(Brush(0xFFC0A000), new Rect(0, y + 6, Field.Width, 36));
        Text(dc, w.Cavern.Theme.Name, new Point(Field.Width / 2, y + 24), 32, 0xFF000000, center: true, shadow: false);
        // The air.
        dc.FillRectangle(Brush(0xFFD02020), new Rect(0, y + 48, 120, 26));
        Text(dc, "AIR", new Point(30, y + 61), 26, 0xFFFFFFFF, shadow: false);
        dc.FillRectangle(Brush(0xFF20A020), new Rect(120, y + 48, Field.Width - 120, 26));
        double left = Math.Max(0, w.Air / w.Cavern.Air);
        dc.FillRectangle(Brush(0xFFFFFFFF), new Rect(130, y + 56, (Field.Width - 150) * left, 10));
        Text(dc, $"HIGH SCORE {Math.Max(g.HighScores.TopScore, g.Score):000000}", new Point(30, y + 100), 30, 0xFFFFE040);
        Text(dc, $"SCORE {g.Score:000000}", new Point(Field.Width - 30, y + 100), 30, 0xFFFFE040, right: true);
        Text(dc, $"CAVERN {g.CavernIndex + 1}   KEYS LEFT {w.KeysLeft}", new Point(Field.Width / 2, y + 100), 24, 0xFFB0C0FF, center: true);
        for (int i = 0; i < Math.Min(g.Lives - 1, 8); i++) DrawMiner(dc, 40 + i * 44, y + 200, 1, _clock + i * 0.25);
    }

    // ───────────────────────────── title, game over and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        // A piano, for the music.
        double ky = 420;
        for (int k = 0; k < 28; k++)
        {
            bool lit = (int)(_clock * 6) % 28 == k;
            dc.DrawRectangle(Brush(lit ? 0xFF40C0FF : 0xFFF0F0F0), Pen(0xFF202020, 2), new Rect(64 + k * 32, ky, 32, 140));
        }
        for (int k = 0; k < 27; k++)
            if (k % 7 is not 2 and not 6) dc.FillRectangle(Brush(0xFF101010), new Rect(64 + k * 32 + 22, ky, 20, 86));
        string title = "AVA MANIC MINER";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 28 + 28 + i * 56, ty = 120 + Math.Sin(_clock * 2.4 + i * 0.45) * 4;
            uint[] colours = [0xFFFFE040, 0xFF40FFFF, 0xFFFF40FF, 0xFF40FF40, 0xFFFF4040];
            Text(dc, title[i].ToString(), new Point(tx, ty), 104, colours[i % colours.Length], center: true);
        }
        Text(dc, "AFTER MATTHEW SMITH'S 1983 ZX SPECTRUM GAME", new Point(cx, 192), 24, Dim, center: true);
        Text(dc, "MINER TOM HAS STUMBLED INTO A FORGOTTEN MINE. TO GET OUT, HE MUST", new Point(cx, 248), 26, White, center: true);
        Text(dc, "COLLECT EVERY FLASHING KEY IN EACH CAVERN, THEN REACH THE PORTAL,", new Point(cx, 278), 26, White, center: true);
        Text(dc, "BEFORE THE AIR RUNS OUT. MIND THE GUARDIANS, THE NASTIES, AND THE DROPS.", new Point(cx, 308), 26, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO START", new Point(cx, 368), 40, Yellow, center: true);
        Text(dc, "LEFT/RIGHT (OR O/P): WALK    SPACE, Z OR UP: JUMP    H: PAUSE    M: SOUND", new Point(cx, 600), 20, 0xFFB0A0D0, center: true);
        DrawMiner(dc, (_clock * 80) % 1100 - 40, 700, 1, _clock);
        Text(dc, "MUSIC: THE BLUE DANUBE (STRAUSS, 1866) AND IN THE HALL OF THE MOUNTAIN KING (GRIEG, 1875)", new Point(cx, 740), 18, Dim, center: true);
    }

    /// <summary>Game over: a giant boot comes down on the miner, the way it had to.</summary>
    private void DrawBoot(DrawingContext dc)
    {
        double cx = Field.Width / 2, ground = 560;
        double t = Engine.StateTime;
        double drop = Math.Min(1, t / GameEngine.StompSeconds);
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        dc.FillRectangle(Brush(0xFF404040), new Rect(cx - 120, ground, 240, 40));
        if (drop < 1) DrawMiner(dc, cx, ground, 1, 0);
        double by = 60 + (ground - 60 - 130) * drop;
        // The leg, and the boot.
        dc.FillRectangle(Brush(0xFF2A4A8A), new Rect(cx - 40, 0, 80, by + 20));
        Poly(dc, 0xFF4A2A10, 0xFF1A0A00, 3, new Point(cx - 50, by), new Point(cx + 46, by), new Point(cx + 46, by + 90), new Point(cx + 120, by + 100), new Point(cx + 130, by + 130), new Point(cx - 50, by + 130));
        dc.FillRectangle(Brush(0xFF1A0A00), new Rect(cx - 50, by + 120, 180, 10));
        if (drop >= 1) Text(dc, "GAME OVER", new Point(cx, 220), 96, (int)(_clock * 4) % 2 == 0 ? Red : Yellow, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "THE DEEPEST MINERS", new Point(cx, 120), 64, 0xFFFFE040, center: true);
        Text(dc, "RANK NAME    SCORE  CAVERNS", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
