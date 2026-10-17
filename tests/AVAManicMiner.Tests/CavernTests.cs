using AVAManicMiner.Engine;

namespace AVAManicMiner.Tests;

/// <summary>The ten caverns: built to be done, and done by the computer miner.</summary>
public class CavernTests(ITestOutputHelper output)
{
    public static TheoryData<int> Caverns() => [.. Enumerable.Range(0, CavernBook.All.Length)];

    [Fact]
    public void Ten_caverns_each_with_its_own_name_and_harder_as_they_go()
    {
        Assert.Equal(10, CavernBook.All.Length);
        Assert.Equal(10, CavernBook.All.Select(c => c.Theme.Name).Distinct().Count());
        for (int i = 1; i < CavernBook.All.Length; i++)
        {
            Assert.True(CavernBook.All[i].Keys.Count >= CavernBook.All[i - 1].Keys.Count);
            Assert.True(CavernBook.All[i].Air <= CavernBook.All[i - 1].Air);
        }
        Assert.True(CavernBook.All[^1].Guardians.Count > CavernBook.All[0].Guardians.Count);
    }

    [Theory]
    [MemberData(nameof(Caverns))]
    public void Every_key_and_the_portal_can_be_reached_from_the_start(int index)
    {
        var c = CavernBook.All[index];
        var reach = Graph.Build(c).Reach(c.Start);
        foreach (var k in c.Keys) Assert.Contains(new Spot(k.Col, k.Row + 1), reach);
        Assert.Contains(new Spot(c.Portal.Col, c.Portal.Row + 1), reach);
        Assert.True(Graph.Stand(new World { Grid = c.Grid }, new Spot(c.Start.Col, c.Start.Row)));
    }

    [Theory]
    [MemberData(nameof(Caverns))]
    public void Every_cavern_is_done_by_the_computer_miner(int index)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avamanicminer-tests-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        g.Go(index);
        g.SetLives(10);
        int t = 0, lost = 0;
        var last = g.Phase;
        for (; t < 60 * 200 && g.Cleared == 0; t++)
        {
            g.Tick(g.Pilot.Decide(g));
            if (g.Phase == Phase.Dying && last != Phase.Dying) lost++;
            last = g.Phase;
        }
        output.WriteLine($"{CavernBook.All[index].Theme.Name}: cleared {g.Cleared} after {t / 60.0:0}s, {lost} lives lost");
        Assert.Equal(1, g.Cleared);
        Assert.True(lost <= 1, $"{lost} lives lost");
    }

    [Fact]
    public void Standing_still_or_running_blindly_gets_nowhere()
    {
        foreach (var lazy in new Func<int, Controls>[] { _ => default, t => new Controls(1, t % 40 == 0) })
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avamanicminer-tests-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.Go(0);
            for (int t = 0; t < 60 * 400 && g.State == GameState.Playing; t++) g.Tick(lazy(t));
            Assert.Equal(0, g.Cleared);
        }
    }
}
