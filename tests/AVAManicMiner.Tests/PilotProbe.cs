using AVAManicMiner.Engine;

namespace AVAManicMiner.Tests;

/// <summary>How the computer miner gets on in each cavern (set MINER_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_miner_does()
    {
        if (Environment.GetEnvironmentVariable("MINER_PROBE") is not { } mode) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _ = CavernBook.All;
        output.WriteLine($"caverns built in {sw.ElapsedMilliseconds} ms");
        for (int i = 0; i < CavernBook.All.Length; i++)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "miner-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.Go(i);
            g.SetLives(30);
            var deaths = new Dictionary<string, int>();
            var last = g.Phase;
            int t = 0;
            for (; t < 60 * 400 && g.State == GameState.Playing && g.Cleared == 0; t++)
            {
                var c = mode == "lazy" ? new Controls(1, t % 40 == 0) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Dying && last != Phase.Dying) { var k = $"{g.World.Cause}"; deaths[k] = deaths.GetValueOrDefault(k) + 1; }
                last = g.Phase;
            }
            output.WriteLine($"{i + 1} {CavernBook.All[i].Theme.Name}: {(g.Cleared > 0 ? "CLEARED" : "not cleared")} after {t / 60}s, {deaths.Values.Sum()} deaths ({string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}"))}), keys {CavernBook.All[i].Keys.Count}, guardians {CavernBook.All[i].Guardians.Count}");
        }
        output.WriteLine($"total {sw.ElapsedMilliseconds} ms");
    }
}
