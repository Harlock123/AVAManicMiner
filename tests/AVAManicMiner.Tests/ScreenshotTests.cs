using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAManicMiner.Engine;
using AVAManicMiner.Views;
using Controls = AVAManicMiner.Engine.Controls;

namespace AVAManicMiner.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set MINER_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avamanicminer-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("MINER_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAManicMiner-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("TOM", 48210, 9);
        g.HighScores.Insert("AVA", 21500, 4);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A cavern played by the computer miner until something worth showing.</summary>
    private static void Play(int cavern, string name, Func<GameEngine, bool> moment, int seconds = 120)
    {
        var (w, c, g) = Open();
        g.Go(cavern);
        g.SetLives(20);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_old_pithead() => Play(0, "pithead", g => g.World.KeysLeft == g.World.Cavern.Keys.Count - 1 && !g.World.Miner.Airborne);

    [AvaloniaFact]
    public void A_jump() => Play(2, "jump", g => g.World.Miner.Airborne && g.World.Miner.Vy > -150 && g.World.Miner.Vy < 0 && g.World.Clock > 3);

    [AvaloniaFact]
    public void The_engine_house() => Play(4, "engine-house", g => g.World.Clock > 6 && !g.World.Miner.Airborne);

    [AvaloniaFact]
    public void Fungus_gallery() => Play(5, "fungus", g => g.World.KeysLeft <= 2 && !g.World.Miner.Airborne);

    [AvaloniaFact]
    public void The_portal_open() => Play(7, "portal", g => g.World.PortalOpen && g.World.Clock > 1);

    [AvaloniaFact]
    public void Deep_seam() => Play(9, "deep-seam", g => g.World.KeysLeft == 4 && !g.World.Miner.Airborne);

    [AvaloniaFact]
    public void Air_counted_in() => Play(3, "air", g => g.Phase == Phase.Out && g.PhaseTime > 0.8);

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go(0);
        g.SetLives(1);
        g.World.Score = 3420;
        Sim.Die(g.World, "test");
        for (int i = 0; i < 600 && !(g.State == GameState.GameOver && g.StateTime > 3.8); i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "game-over");
    }
}
