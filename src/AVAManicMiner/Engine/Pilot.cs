namespace AVAManicMiner.Engine;

/// <summary>
/// The computer miner, for the demonstration and the tests. It knows every place it can stand and where each walk
/// or jump takes it (the cavern's <see cref="Graph"/>), and plans the quickest way to the nearest key, then the
/// portal. Before each move it plays the move out on a copy of the cavern, guardians and all, and if a guardian
/// would get it, it waits for the moment it wouldn't (or tries another way).
/// </summary>
public sealed class Pilot
{
    private readonly Queue<Controls> _plan = new();
    private Graph? _full;
    private HashSet<Spot> _beats = [];
    /// <summary>What a step onto a guardian's beat counts as, against going round.</summary>
    public const int BeatCost = 900;
    private CavernDef? _for;
    private int _crumbled = -1;
    public const int WaitStep = 6, MaxWait = 360, Settle = 40;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _plan.Clear();
        _full = null;
        _for = null;
        Doing = "";
    }

    public Controls Decide(GameEngine g)
    {
        var w = g.World;
        var m = w.Miner;
        if (g.Phase != Phase.Play || m.Dead) { _plan.Clear(); return default; }
        if (_plan.Count > 0) return _plan.Dequeue();
        if (m.Airborne) return default;
        // The map, again if a ledge has crumbled.
        int crumbled = 0;
        foreach (var t in w.Grid) if (t == Tile.Empty) crumbled++;
        if (_full == null || _for != w.Cavern || crumbled != _crumbled)
        {
            // The guardians' beats: gone along only when there's no other way (a crumbled ledge can leave none).
            var beats = _beats = new HashSet<Spot>();
            foreach (var gd in w.Cavern.Guardians)
                if (!gd.Vertical)
                    for (int c = (int)(gd.Min / Field.Tile); c <= (int)(gd.Max / Field.Tile); c++)
                        beats.Add(new Spot(c, (int)Math.Round((gd.Y - Field.Top) / Field.Tile) - 1));
            _full = Graph.Build(w.Cavern, w);
            _for = w.Cavern;
            _crumbled = crumbled;
        }
        var here = Spot.Of(m);
        if (!_full!.Edges.ContainsKey(here))
        {
            // Off the map (one boot on the end of a ledge): onto the middle of the square underfoot.
            var near = new[] { here with { Col = here.Col - 1 }, here with { Col = here.Col + 1 } }.Where(_full.Edges.ContainsKey).OrderBy(s => Math.Abs(s.X - m.X)).FirstOrDefault();
            if (near != default)
            {
                Doing = "stepping on";
                int dx = Math.Sign(near.X - m.X);
                for (int i = 0; i < 12 && Math.Abs(m.X + dx * (i + 1) * Miner.Speed * Field.Step - near.X) < Math.Abs(m.X + dx * i * Miner.Speed * Field.Step - near.X); i++) _plan.Enqueue(new Controls(dx));
                if (_plan.Count > 0) return _plan.Dequeue();
            }
        }
        var targets = new List<Spot>();
        for (int i = 0; i < w.Cavern.Keys.Count; i++)
            if (!w.Taken[i]) targets.Add(new Spot(w.Cavern.Keys[i].Col, w.Cavern.Keys[i].Row + 1));
        if (targets.Count == 0) targets.Add(new Spot(w.Cavern.Portal.Col, w.Cavern.Portal.Row + 1));
        // How far every spot is from the nearest target.
        var far = Distances(_full, targets, _beats);
        int now = far.TryGetValue(here, out int h) ? h : int.MaxValue;
        if (now == 0 && Math.Abs(m.X - here.X) > 2)
        {
            // On the target's square but off its middle (a jump can land him anywhere in it): into the middle.
            Doing = "lining up";
            int dx = Math.Sign(here.X - m.X);
            for (int i = 0; i < 12 && Math.Abs(m.X + dx * (i + 1) * Miner.Speed * Field.Step - here.X) < Math.Abs(m.X + dx * i * Miner.Speed * Field.Step - here.X); i++) _plan.Enqueue(new Controls(dx));
            if (_plan.Count > 0) return _plan.Dequeue();
        }
        // The moves from here, best first, each tried now and after waits.
        var options = new List<(Move Move, int Cost)>();
        if (_full.Edges.TryGetValue(here, out var edges))
            foreach (var (mv, to, ticks) in edges)
                // Only moves that get nearer (a step back and forth would never get past a guardian).
                if (far.TryGetValue(to, out int d) && (d < now || now == int.MaxValue)) options.Add((mv, ticks + d + (_beats.Contains(to) ? BeatCost : 0)));
        options.Sort((a, b) => a.Cost.CompareTo(b.Cost));
        int best = int.MaxValue;
        List<Controls>? pick = null;
        int pickWait = 0;
        // (If nothing is safe: the move that keeps him alive longest.)
        int longest = -1;
        List<Controls>? escape = null;
        foreach (var (mv, cost) in options.Take(4))
        {
            for (int wait = 0; wait <= MaxWait && wait + cost < best; wait += WaitStep)
            {
                var (run, alive) = Rehearse(w, mv, wait);
                if (run == null)
                {
                    if (wait == 0 && alive > longest && escapeRun != null) { longest = alive; escape = escapeRun; }
                    continue;
                }
                if (wait + cost < best) { best = wait + cost; pick = run; pickWait = wait; }
                break;
            }
        }
        if (pick == null)
        {
            // Every move from here on the way is deadly: any move at all that buys time.
            if (_full.Edges.TryGetValue(here, out var all))
                foreach (var (mv, _, _) in all)
                {
                    var (run, alive) = Rehearse(w, mv, 0);
                    if (run != null) { pick = run; break; }
                    if (alive > longest && escapeRun != null) { longest = alive; escape = escapeRun; }
                }
            if (pick == null && escape != null && longest > HoldAlive(w)) pick = escape;
        }
        if (pick == null)
        {
            Doing = "stuck";
            for (int i = 0; i < WaitStep; i++) _plan.Enqueue(Hold(w));
            return _plan.Dequeue();
        }
        Doing = pickWait > 0 ? "waiting" : "going";
        if (pickWait > 0)
        {
            for (int i = 0; i < Math.Min(pickWait, WaitStep); i++) _plan.Enqueue(Hold(w));
            return _plan.Dequeue();
        }
        foreach (var c in pick) _plan.Enqueue(c);
        return _plan.Count > 0 ? _plan.Dequeue() : default;
    }

    private List<Controls>? escapeRun;

    /// <summary>How long he'd live just standing here.</summary>
    private static int HoldAlive(World start)
    {
        var w = start.Copy();
        for (int i = 0; i < 240; i++)
        {
            Sim.Step(w, Hold(w));
            if (w.Miner.Dead) return i;
        }
        return 240;
    }

    /// <summary>Standing still: on a conveyor, that means pushing against it.</summary>
    private static Controls Hold(World w)
    {
        var m = w.Miner;
        var under = w.AtPoint(m.X, m.Y + 1);
        return new Controls(under == Tile.ConveyorLeft ? 1 : under == Tile.ConveyorRight ? -1 : 0);
    }

    /// <summary>
    /// A move played out on a copy after a wait, then a moment standing: its controls if he comes through it; and,
    /// either way, how many steps he lived.
    /// </summary>
    private (List<Controls>? Run, int Alive) Rehearse(World start, Move mv, int wait)
    {
        var w = start.Copy();
        int alive = 0;
        escapeRun = null;
        for (int i = 0; i < wait; i++)
        {
            Sim.Step(w, Hold(w));
            alive++;
            if (w.Miner.Dead || w.Miner.Airborne) return (null, alive);
        }
        var controls = new List<Controls>();
        foreach (var c in Graph.Script(w, mv))
        {
            controls.Add(c);
            Sim.Step(w, c);
            alive++;
            if (w.Miner.Dead) { escapeRun = controls; return (null, alive); }
            if (w.Out) return (controls, alive);
        }
        for (int i = 0; i < Settle; i++)
        {
            Sim.Step(w, Hold(w));
            alive++;
            if (w.Miner.Dead) { escapeRun = controls; return (null, alive); }
            if (w.Out) break;
        }
        return (controls, alive);
    }

    /// <summary>How long (in steps) from every spot to the nearest of the targets, going backwards from them.</summary>
    private static Dictionary<Spot, int> Distances(Graph g, List<Spot> targets, HashSet<Spot> beats)
    {
        var back = new Dictionary<Spot, List<(Spot From, int Ticks)>>();
        foreach (var (from, list) in g.Edges)
            foreach (var (_, to, ticks) in list)
            {
                if (!back.TryGetValue(to, out var l)) back[to] = l = [];
                l.Add((from, ticks));
            }
        var dist = new Dictionary<Spot, int>();
        var pq = new PriorityQueue<Spot, int>();
        foreach (var t in targets) { dist[t] = 0; pq.Enqueue(t, 0); }
        while (pq.TryDequeue(out var s, out int d))
        {
            if (d > dist[s]) continue;
            if (!back.TryGetValue(s, out var l)) continue;
            foreach (var (from, ticks) in l)
            {
                int nd = d + ticks + (beats.Contains(s) ? BeatCost : 0);
                if (dist.TryGetValue(from, out int od) && od <= nd) continue;
                dist[from] = nd;
                pq.Enqueue(from, nd);
            }
        }
        return dist;
    }
}
