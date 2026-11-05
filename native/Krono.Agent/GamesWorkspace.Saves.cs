using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace Krono_Agent;

public sealed partial class GamesWorkspace
{
    bool stateReady, disposed;
    string endMessage = "";
    readonly List<TextBlock> saveLabels = [];
    string SaveId(int index) => "native-v1:" + names[index];
    string SaveCaption(int index)
    {
        var saved = workspace.Query("SELECT id FROM game_saves WHERE id=$p0 AND account_id=$p1", SaveId(index), workspace.AccountId).Count > 0;
        return saved ? "SAVED GAME    ↗" : index is 1 or 4 or 7 or 12 ? "VS BOT    ↗" : "SOLO    ↗";
    }
    void OpenGame(int index)
    {
        if (!stateReady || game != index) {
            SaveCurrent(); timer.Stop(); game = index; Reset(); stateReady = true;
            if (!RestoreCurrent()) SaveCurrent();
        }
        if (picker.SelectedIndex != index) picker.SelectedIndex = index;
        if (!paused && !ended && playScreen?.Visibility == Visibility.Visible) timer.Start();
        Draw();
    }
    public void PauseAndSave()
    {
        if (disposed || !stateReady) return;
        paused = true; timer.Stop(); SaveCurrent(); Draw();
    }
    void SaveCurrent()
    {
        if (disposed || !stateReady) return;
        try {
            var state = new GameSave {
                Version = 1, Game = names[game], Score = score, Tick = tick, Moves = moves,
                Ended = ended, EndMessage = endMessage, Turned = turned, Dx = dx, Dy = dy,
                Board = board, Revealed = revealed, Flags = flags, Snake = snake.ToArray(), Food = food,
                Paddle = paddle, Enemy = enemy, BallX = ballX, BallY = ballY, BallDx = ballDx, BallDy = ballDy,
                Bricks = bricks.Select(brick => new[] { brick.X, brick.Y }).ToArray(),
                First = first, Second = second, HideAt = hideAt,
                Sequence = sequence.ToArray(), SimonStart = simonStart, SimonInput = simonInput, SimonWaiting = simonWaiting,
                OddCell = oddCell, OddRound = oddRound, OddBase = Pack(oddBase), OddShade = Pack(oddShade),
                OrbitAngle = orbitAngle, OrbitTarget = orbitTarget, OrbitSpeed = orbitSpeed,
                Sudoku = sudoku, Clues = clues, SelectedSquare = selectedSquare, GameMessage = gameMessage,
                StackX = stackX, StackWidth = stackWidth, StackDirection = stackDirection, StackCombo = stackCombo, StackLevel = stackLevel, Tower = tower.ToArray(),
                TargetColors = targetColors, ChosenColors = chosenColors.ToArray(), ColorStart = colorStart, CurrentColor = CurrentColor
            };
            workspace.Execute("INSERT INTO game_saves(id,account_id,game_id,state,saved_at) VALUES($p0,$p1,$p2,$p3,$p4) ON CONFLICT(id) DO UPDATE SET state=excluded.state,saved_at=excluded.saved_at,synced=0",
                SaveId(game), workspace.AccountId, names[game], JsonSerializer.Serialize(state), Workspace.Now);
            if (game < saveLabels.Count) saveLabels[game].Text = "SAVED GAME    ↗";
        } catch (Exception error) when (error is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException) {
            // Keep the playable state in memory and make a failed disk write visible.
            gameHelp.Text = "Could not save this game. " + error.Message;
        }
    }
    bool RestoreCurrent()
    {
        var row = workspace.Query("SELECT state FROM game_saves WHERE id=$p0 AND account_id=$p1", SaveId(game), workspace.AccountId).FirstOrDefault();
        if (row is null) return false;
        GameSave? state = null;
        try { var json = row["state"]!.ToString(); if (json.Length <= 65536) state = JsonSerializer.Deserialize<GameSave>(json); }
        catch (JsonException) { }
        if (state is null || !Valid(state)) {
            workspace.Execute("DELETE FROM game_saves WHERE id=$p0 AND account_id=$p1", SaveId(game), workspace.AccountId);
            gameHelp.Text = "The previous save was invalid. A new game has been started."; return false;
        }
        score = state.Score; tick = state.Tick; moves = state.Moves; ended = state.Ended; endMessage = state.EndMessage;
        dx = state.Dx; dy = state.Dy; turned = state.Turned; board = state.Board; revealed = state.Revealed; flags = state.Flags;
        snake = state.Snake.ToList(); food = state.Food; paddle = state.Paddle; enemy = state.Enemy;
        ballX = state.BallX; ballY = state.BallY; ballDx = state.BallDx; ballDy = state.BallDy;
        bricks = state.Bricks.Select(brick => (brick[0], brick[1])).ToList(); first = state.First; second = state.Second; hideAt = state.HideAt;
        sequence.Clear(); sequence.AddRange(state.Sequence); simonStart = state.SimonStart; simonInput = state.SimonInput; simonWaiting = state.SimonWaiting; simonVisual = -3;
        oddCell = state.OddCell; oddRound = state.OddRound; oddBase = Unpack(state.OddBase); oddShade = Unpack(state.OddShade);
        orbitAngle = state.OrbitAngle; orbitTarget = state.OrbitTarget; orbitSpeed = state.OrbitSpeed;
        if (game == 13) { sudoku = state.Sudoku; clues = state.Clues; selectedSquare = state.SelectedSquare; }
        gameMessage = state.GameMessage;
        if (game == 14) { stackX = state.StackX; stackWidth = state.StackWidth; stackDirection = state.StackDirection; stackCombo = state.StackCombo; stackLevel = state.StackLevel; tower.Clear(); tower.AddRange(state.Tower); }
        if (game == 15) { targetColors = state.TargetColors; chosenColors.Clear(); chosenColors.AddRange(state.ChosenColors); colorStart = state.ColorStart; SetColor(state.CurrentColor); }
        // Restoration never starts a moving game or advances a pattern without consent.
        paused = true; return true;
    }
    bool Valid(GameSave s)
    {
        if (s.Version != 1 || s.Game != names[game] || s.Score < 0 || s.Tick < 0 || s.Moves < 0 || s.EndMessage is null || s.EndMessage.Length > 160 ||
            s.Board?.Length != 64 || s.Revealed?.Length != 64 || s.Flags?.Length != 64 || s.Snake is null || s.Snake.Length is < 3 or > 400 ||
            s.Snake.Any(cell => cell is < 0 or >= 400) || s.Snake.Distinct().Count() != s.Snake.Length || s.Food is < 0 or >= 400 ||
            Math.Abs((long)s.Dx) + Math.Abs((long)s.Dy) != 1 || s.First is < -1 or >= 16 || s.Second is < -1 or >= 16 || s.Second >= 0 && s.First < 0 ||
            s.Bricks is null || s.Bricks.Length > 32 || s.Bricks.Any(brick => brick?.Length != 2 || brick.Any(v => !double.IsFinite(v) || v < 0 || v > 480)) ||
            new[] { s.BallX, s.BallY, s.BallDx, s.BallDy, s.Enemy, s.OrbitAngle, s.OrbitTarget, s.OrbitSpeed }.Any(v => !double.IsFinite(v) || Math.Abs(v) > 10000) ||
            s.Sequence is null || s.Sequence.Length > 20 || s.Sequence.Any(n => n is < 0 or > 3)) return false;
        return game switch {
            0 => s.Ended || !s.Snake.Contains(s.Food),
            1 or 4 => s.Board.All(value => value is >= 0 and <= 2),
            2 => s.Board.Take(16).All(value => value == 0 || value >= 2 && (value & (value - 1)) == 0),
            3 => s.Board.All(value => value is 0 or -1) && s.Board.Count(value => value == -1) == (s.Moves == 0 ? 0 : 10),
            6 => s.Board.Take(16).GroupBy(value => value).All(group => group.Key is >= 1 and <= 8 && group.Count() == 2),
            8 => s.Board.Take(25).All(value => value is 0 or 1),
            9 => s.Sequence.Length > 0 && s.SimonInput >= 0 && (s.Ended ? s.SimonInput <= s.Sequence.Length : s.SimonInput < s.Sequence.Length) && s.SimonStart >= 0,
            10 => s.OddCell is >= 0 and < 16 && s.OddRound is >= 1 and <= 21,
            11 => s.OrbitSpeed is >= 2.3 and <= 12 && s.OrbitAngle is >= 0 and < 360 && s.OrbitTarget is >= 0 and < 360,
            12 => s.Board.All(value => value is >= 0 and <= 2),
            13 => s.Sudoku?.Length == 81 && s.Clues?.Length == 81 && s.SelectedSquare is >= -1 and < 81 && s.Sudoku.All(value => value is >= 0 and <= 9) && s.Sudoku.Where((value, i) => value > 0 && !NativeGameRules.SudokuLegal(s.Sudoku, i, value)).Count() == 0 && NativeGameRules.SudokuSolutions(s.Sudoku.Select((value, i) => s.Clues[i] ? value : 0).ToArray()) == 1 && s.Clues.Where((fixedCell, i) => fixedCell && s.Sudoku[i] == 0).Count() == 0,
            14 => double.IsFinite(s.StackX) && double.IsFinite(s.StackWidth) && s.StackWidth is > 0 and <= 240 && s.StackX >= 0 && s.StackX + s.StackWidth <= 480 && s.StackDirection is -1 or 1 && s.StackLevel >= 0 && s.StackCombo >= 0 && s.Tower is { Length: > 0 and <= 10 } && s.Tower.All(row => row?.Length == 2 && row.All(double.IsFinite) && row[0] >= 0 && row[1] is > 0 and <= 240 && row[0] + row[1] <= 480),
            15 => s.TargetColors?.Length == 5 && s.ChosenColors is { Length: <= 5 } && s.ColorStart >= 0,
            _ => true
        };
    }
    static uint Pack(Color color) => (uint)(color.A << 24 | color.R << 16 | color.G << 8 | color.B);
    static Color Unpack(uint value) => Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    public sealed class GameSave
    {
        public int Version { get; set; }
        public string Game { get; set; } = "";
        public int Score { get; set; }
        public int Tick { get; set; }
        public int Moves { get; set; }
        public bool Ended { get; set; }
        public string EndMessage { get; set; } = "";
        public bool Turned { get; set; }
        public int Dx { get; set; }
        public int Dy { get; set; }
        public int[] Board { get; set; } = [];
        public bool[] Revealed { get; set; } = [];
        public bool[] Flags { get; set; } = [];
        public int[] Snake { get; set; } = [];
        public int Food { get; set; }
        public int Paddle { get; set; }
        public double Enemy { get; set; }
        public double BallX { get; set; }
        public double BallY { get; set; }
        public double BallDx { get; set; }
        public double BallDy { get; set; }
        public double[][] Bricks { get; set; } = [];
        public int First { get; set; }
        public int Second { get; set; }
        public int HideAt { get; set; }
        public int[] Sequence { get; set; } = [];
        public int SimonStart { get; set; }
        public int SimonInput { get; set; }
        public bool SimonWaiting { get; set; }
        public int OddCell { get; set; }
        public int OddRound { get; set; }
        public uint OddBase { get; set; }
        public uint OddShade { get; set; }
        public double OrbitAngle { get; set; }
        public double OrbitTarget { get; set; }
        public double OrbitSpeed { get; set; }
        public int[] Sudoku { get; set; } = [];
        public bool[] Clues { get; set; } = [];
        public int SelectedSquare { get; set; } = -1;
        public string GameMessage { get; set; } = "";
        public double StackX { get; set; }
        public double StackWidth { get; set; }
        public int StackDirection { get; set; }
        public int StackCombo { get; set; }
        public int StackLevel { get; set; }
        public double[][] Tower { get; set; } = [];
        public uint[] TargetColors { get; set; } = [];
        public uint[] ChosenColors { get; set; } = [];
        public int ColorStart { get; set; }
        public uint CurrentColor { get; set; }
    }
}
