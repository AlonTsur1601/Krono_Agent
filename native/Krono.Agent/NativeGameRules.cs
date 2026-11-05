namespace Krono_Agent;

// Pure rules, shared by the native controls and headless verification.
public static class NativeGameRules
{
    public static int[] ReversiFlips(int[] cells, int square, int player)
    {
        if (square is < 0 or >= 64 || cells[square] != 0) return [];
        List<int> result = [];
        for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++) {
            if (dx == 0 && dy == 0) continue;
            List<int> line = []; var x = square % 8 + dx; var y = square / 8 + dy;
            while (x is >= 0 and < 8 && y is >= 0 and < 8 && cells[y * 8 + x] == 3 - player) { line.Add(y * 8 + x); x += dx; y += dy; }
            if (line.Count > 0 && x is >= 0 and < 8 && y is >= 0 and < 8 && cells[y * 8 + x] == player) result.AddRange(line);
        }
        return result.ToArray();
    }
    public static int[] ReversiMoves(int[] cells, int player) => Enumerable.Range(0, 64).Where(i => ReversiFlips(cells, i, player).Length > 0).ToArray();
    public static bool ReversiPlace(int[] cells, int square, int player)
    {
        var flips = ReversiFlips(cells, square, player); if (flips.Length == 0) return false;
        cells[square] = player; foreach (var index in flips) cells[index] = player; return true;
    }
    public static int ReversiBot(int[] cells)
    {
        var moves = ReversiMoves(cells, 2); if (moves.Length == 0) return -1;
        // Corners stay captured. Avoid their neighbors until the adjacent corner is owned.
        int Rank(int square) {
            var copy = (int[])cells.Clone(); ReversiPlace(copy, square, 2);
            var corner = square is 0 or 7 or 56 or 63;
            var danger = new[] { 0, 7, 56, 63 }.Any(c => cells[c] == 0 && Math.Abs(c % 8 - square % 8) <= 1 && Math.Abs(c / 8 - square / 8) <= 1);
            return (corner ? 1000 : 0) - (danger ? 150 : 0) - ReversiMoves(copy, 1).Length * 8 + ReversiFlips(cells, square, 2).Length;
        }
        return moves.OrderByDescending(Rank).ThenBy(square => square).First();
    }
    public static bool SudokuLegal(int[] cells, int square, int digit)
    {
        if (cells.Length != 81 || square is < 0 or >= 81 || digit is < 1 or > 9) return false;
        var x = square % 9; var y = square / 9;
        for (var i = 0; i < 81; i++) if (i != square && cells[i] == digit && (i % 9 == x || i / 9 == y || i % 9 / 3 == x / 3 && i / 9 / 3 == y / 3)) return false;
        return true;
    }
    public static int SudokuSolutions(int[] source, int limit = 2)
    {
        if (source.Length != 81 || source.Where((value, index) => value != 0 && !SudokuLegal(source, index, value)).Any()) return 0;
        var cells = (int[])source.Clone(); return Search();
        int Search() {
            var square = -1; int[] choices = [];
            for (var i = 0; i < 81; i++) if (cells[i] == 0) {
                var options = Enumerable.Range(1, 9).Where(digit => SudokuLegal(cells, i, digit)).ToArray();
                if (options.Length == 0) return 0;
                if (square < 0 || options.Length < choices.Length) { square = i; choices = options; if (choices.Length == 1) break; }
            }
            if (square < 0) return 1;
            var count = 0;
            foreach (var digit in choices) { cells[square] = digit; count += Search(); if (count >= limit) break; }
            cells[square] = 0; return Math.Min(limit, count);
        }
    }
    public static int[] NewSudoku(Random random)
    {
        // Randomize a solved Latin layout, then remove clues only while uniqueness holds.
        var digits = Enumerable.Range(1, 9).OrderBy(_ => random.Next()).ToArray();
        int[] Order() => Enumerable.Range(0, 3).OrderBy(_ => random.Next()).SelectMany(band => Enumerable.Range(0, 3).OrderBy(_ => random.Next()).Select(row => band * 3 + row)).ToArray();
        var rows = Order(); var cols = Order(); var cells = new int[81];
        for (var y = 0; y < 9; y++) for (var x = 0; x < 9; x++) cells[y * 9 + x] = digits[(rows[y] * 3 + rows[y] / 3 + cols[x]) % 9];
        var removed = 0;
        foreach (var square in Enumerable.Range(0, 81).OrderBy(_ => random.Next())) {
            var digit = cells[square]; cells[square] = 0;
            if (SudokuSolutions(cells) != 1) cells[square] = digit;
            else if (++removed == 45) break;
        }
        return cells;
    }
    public readonly record struct StackPlacement(double X, double Width, int Combo, bool Perfect);
    public static StackPlacement StackPlace(double x, double width, double baseX, double baseWidth, int combo)
    {
        var perfect = Math.Abs(x - baseX) <= 3;
        if (perfect) x = baseX;
        var left = Math.Max(x, baseX); var right = Math.Min(x + width, baseX + baseWidth);
        var overlap = Math.Max(0, right - left);
        combo = perfect && overlap > 0 ? combo + 1 : 0;
        if (combo > 0 && combo % 3 == 0) { var expanded = Math.Min(240, overlap + 240 * .08); left = Math.Clamp(left - (expanded - overlap) / 2, 0, 480 - expanded); overlap = expanded; }
        return new(left, overlap, combo, perfect);
    }
    // CIE76 Delta E in D65 CIELAB. Input is standard sRGB, not RGB Euclidean distance.
    public static double DeltaE(uint first, uint second)
    {
        var a = Lab(first); var b = Lab(second);
        return Math.Sqrt(Math.Pow(a.L - b.L, 2) + Math.Pow(a.A - b.A, 2) + Math.Pow(a.B - b.B, 2));
    }
    static (double L, double A, double B) Lab(uint rgb)
    {
        static double Linear(uint channel) { var v = channel / 255.0; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
        static double F(double v) => v > 216.0 / 24389 ? Math.Cbrt(v) : (24389.0 / 27 * v + 16) / 116;
        var r = Linear(rgb >> 16 & 255); var g = Linear(rgb >> 8 & 255); var b = Linear(rgb & 255);
        var x = F((r * .4124564 + g * .3575761 + b * .1804375) / .95047);
        var y = F(r * .2126729 + g * .7151522 + b * .0721750);
        var z = F((r * .0193339 + g * .1191920 + b * .9503041) / 1.08883);
        return (116 * y - 16, 500 * (x - y), 200 * (y - z));
    }
}
