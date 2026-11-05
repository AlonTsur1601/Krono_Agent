using Krono_Agent;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var board = new int[64]; board[27] = board[36] = 2; board[28] = board[35] = 1;
Check(NativeGameRules.ReversiMoves(board, 1).SequenceEqual(new[] { 19, 26, 37, 44 }), "Reversi opening moves");
Check(!NativeGameRules.ReversiPlace(board, 0, 1) && board.Count(n => n > 0) == 4, "Illegal move changed the board");
Check(NativeGameRules.ReversiPlace(board, 19, 1) && board[27] == 1 && board.Count(n => n == 1) == 4, "Reversi capture");
var player = 2; var passes = 0;
for (var turn = 0; turn < 128 && passes < 2; turn++) {
    var legal = NativeGameRules.ReversiMoves(board, player);
    var square = player == 2 ? NativeGameRules.ReversiBot(board) : legal.FirstOrDefault(-1);
    if (square < 0) passes++;
    else { passes = 0; var occupied = board.Count(n => n > 0); Check(legal.Contains(square), "Bot picked an illegal move"); Check(NativeGameRules.ReversiPlace(board, square, player) && board.Count(n => n > 0) == occupied + 1, "Reversi move count"); }
    player = 3 - player;
}
Check(passes == 2, "Reversi did not terminate after both players pass");
var corner = new int[64]; corner[1] = 1; corner[2] = 2;
Check(NativeGameRules.ReversiBot(corner) == 0, "Bot failed to capture a legal corner");
for (var seed = 0; seed < 12; seed++) {
    var puzzle = NativeGameRules.NewSudoku(new Random(seed));
    Check(puzzle.Count(n => n > 0) >= 36 && puzzle.Count(n => n == 0) > 0, "Sudoku clue count");
    Check(NativeGameRules.SudokuSolutions(puzzle) == 1, "Generated Sudoku was not unique");
    var clone = (int[])puzzle.Clone(); NativeGameRules.SudokuSolutions(puzzle);
    Check(clone.SequenceEqual(puzzle), "Solver mutated the puzzle");
}
Check(NativeGameRules.SudokuSolutions(new int[81]) == 2, "Sudoku solver did not detect multiple solutions");
var invalid = new int[81]; invalid[0] = invalid[1] = 1;
Check(NativeGameRules.SudokuSolutions(invalid) == 0, "Sudoku accepted conflicting clues");
Check(!NativeGameRules.SudokuLegal(invalid, 2, 1), "Sudoku row conflict");
var placed = NativeGameRules.StackPlace(120, 200, 120, 200, 0);
placed = NativeGameRules.StackPlace(placed.X, placed.Width, placed.X, placed.Width, placed.Combo);
placed = NativeGameRules.StackPlace(placed.X, placed.Width, placed.X, placed.Width, placed.Combo);
Check(placed.Combo == 3 && Math.Abs(placed.Width - 219.2) < .0001, "Stack third-perfect width restore");
Check(NativeGameRules.StackPlace(120, 240, 120, 240, 2).Width == 240, "Stack expansion exceeded original width");
Check(NativeGameRules.StackPlace(250, 100, 120, 100, 3).Width == 0, "Stack missed overlap");
var cut = NativeGameRules.StackPlace(140, 200, 120, 200, 2);
Check(cut.Width == 180 && cut.Combo == 0, "Stack cut or combo reset");
Check(NativeGameRules.DeltaE(0xff808080, 0xff808080) == 0, "Identical colors differ");
Check(Math.Abs(NativeGameRules.DeltaE(0xff000000, 0xffffffff) - 100) < .001, "CIE76 black/white reference");
Check(Math.Abs(NativeGameRules.DeltaE(0xffff0000, 0xff00ff00) - 170.565) < .01, "CIE76 red/green reference");
Check(Math.Abs(NativeGameRules.DeltaE(0xff123456, 0xffabcdef) - NativeGameRules.DeltaE(0xffabcdef, 0xff123456)) < 1e-10, "Color distance symmetry");
Console.WriteLine("NATIVE_RULES_PASS: Reversi legal game/bot/corner, twelve unique Sudoku seeds, Stack clipping/combo/cap, D65 CIE76 reference distances");
