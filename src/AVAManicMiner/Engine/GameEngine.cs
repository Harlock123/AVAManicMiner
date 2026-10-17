namespace AVAManicMiner.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>The cavern named; a moment before it starts.</summary>
    Start,
    Play,
    /// <summary>Caught: a moment, then the cavern again from the start (with its keys back), or the game is over.</summary>
    Dying,
    /// <summary>Through the portal: the air left is counted into the score, then the next cavern.</summary>
    Out,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Jump, Land, Key, PortalOpen, Portal, Crumble, Die, ExtraLife, Air, Stomp,
    SayAir,
    Music, MusicStop, TitleTune, GameOver, Victory,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 45;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 1.6, DyingSeconds = 1.6, StompSeconds = 3.5;
    public const int Lives0 = 3, ExtraEvery = 10000, AirPoints = 20;

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true, _music, _airWarned;
    private int _nextExtra;

    public GameEngine(HighScoreTable? table = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Lives { get; private set; }
    public int Score => World.Score;
    public int CavernIndex { get; private set; }
    /// <summary>Caverns done this game.</summary>
    public int Cleared { get; private set; }
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || c.Jump && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > StompSeconds + 1)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Cleared);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        Lives = Lives0;
        Cleared = 0;
        _nextExtra = ExtraEvery;
        Enter(0);
    }

    private void Enter(int index)
    {
        CavernIndex = index;
        int score = World.Score;
        Sim.Start(World, CavernBook.All[index % CavernBook.All.Length]);
        World.Score = score;
        _airWarned = false;
        SetPhase(Phase.Start);
        Message = World.Cavern.Theme.Name;
        MessageTime = StartSeconds;
        Pilot.Reset();
    }

    public void StartGame()
    {
        NewGame();
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        NewGame();
        State = GameState.Attract;
        StateTime = 0;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Music(true);
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds) { SetPhase(Phase.Play); Music(true); }
                break;
            case Phase.Play:
            {
                int before = w.Score;
                Sim.Step(w, c);
                if (w.Score != before) ExtraLives();
                if (!_airWarned && w.Air < 15 && !w.Miner.Dead) { _airWarned = true; Play(Sfx.SayAir); }
                if (w.Miner.Dead) { SetPhase(Phase.Dying); Music(false); }
                else if (w.Out) { SetPhase(Phase.Out); Music(false); }
                break;
            }
            case Phase.Dying:
                if (PhaseTime > DyingSeconds)
                {
                    Lives--;
                    if (Lives <= 0)
                    {
                        Play(Sfx.Stomp);
                        Play(Sfx.GameOver);
                        if (State == GameState.Attract) { ShowHighScores(-1); return; }
                        State = GameState.GameOver;
                        StateTime = 0;
                        return;
                    }
                    Enter(CavernIndex);
                }
                break;
            case Phase.Out:
                // The air left, counted in.
                if (w.Air > 0 && PhaseTime > 0.5)
                {
                    double take = Math.Min(w.Air, dt * 45);
                    int before = (int)w.Air;
                    w.Air -= take;
                    w.Score += (before - (int)w.Air) * AirPoints;
                    if (before != (int)w.Air) Play(Sfx.Air);
                    ExtraLives();
                }
                if (w.Air <= 0 && PhaseTime > 1)
                {
                    Cleared++;
                    if ((CavernIndex + 1) % CavernBook.All.Length == 0) { Play(Sfx.Victory); Message = "EVERY CAVERN CLEARED!"; MessageTime = 3; }
                    Enter(CavernIndex + 1);
                }
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void ExtraLives()
    {
        while (World.Score >= _nextExtra)
        {
            _nextExtra += ExtraEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a cavern, past its name.</summary>
    public void Go(int cavern = 0)
    {
        NewGame();
        State = GameState.Playing;
        Enter(cavern);
        SetPhase(Phase.Play);
        Message = null;
    }

    public void SetLives(int n) => Lives = n;
}
