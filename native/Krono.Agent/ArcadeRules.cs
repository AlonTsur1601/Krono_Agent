namespace Krono_Agent;

public static class ArcadeRules
{
    public const int MazeWidth = 15;
    public const int MazeHome = 202;
    public const string Maze = "###############" + "#.............#" + "#.###.#.#.###.#" + "#o....#.#....o#" + "#.##.#...#.##.#" + "#....#.###....#" + "###.##...##.###" + "#.....#.#.....#" + "#.###.#.#.###.#" + "#...#.....#...#" + "#.#.###.###.#.#" + "#.#.........#.#" + "#o###.#.#.###o#" + "#.............#" + "###############";
    public static bool MazeOpen(int cell) => cell >= 0 && cell < Maze.Length && Maze[cell] != '#';
    public static int MazeNext(int cell, int heading) {
        var next = cell + (heading switch { 1 => 1, 2 => 15, 3 => -1, 4 => -15, _ => 0 });
        return MazeOpen(next) ? next : cell;
    }
    public static int GhostMove(int from, int to, bool flee = false)
    {
        if (!MazeOpen(from) || !MazeOpen(to) || from == to) return from;
        // Flood once from the player. The next legal tile with minimum distance is a shortest path.
        var distances = Enumerable.Repeat(int.MaxValue, Maze.Length).ToArray(); distances[to] = 0;
        Queue<int> queue = new(); queue.Enqueue(to);
        while (queue.TryDequeue(out var cell)) for (var dir = 1; dir <= 4; dir++) {
            var next = MazeNext(cell, dir); if (distances[next] != int.MaxValue) continue;
            distances[next] = distances[cell] + 1; queue.Enqueue(next);
        }
        var choices = Enumerable.Range(1, 4).Select(dir => MazeNext(from, dir)).Where(cell => cell != from);
        return (flee ? choices.OrderByDescending(cell => distances[cell]) : choices.OrderBy(cell => distances[cell])).FirstOrDefault(from);
    }
    public static readonly int[] DartSectors = [20, 1, 18, 4, 13, 6, 10, 15, 2, 17, 3, 19, 7, 16, 8, 11, 14, 9, 12, 5];
    public static int DartScore(double x, double y)
    {
        var r = Math.Sqrt(x * x + y * y);
        if (!double.IsFinite(r) || r > 170) return 0;
        if (r <= 6) return 50;
        if (r <= 16) return 25;
        var angle = (Math.Atan2(x, -y) * 180 / Math.PI + 369) % 360;
        var sector = DartSectors[(int)(angle / 18)];
        return sector * (r >= 156 ? 2 : r is >= 96 and <= 108 ? 3 : 1);
    }
    public static bool FlapHits(double y, double pipeX, double gapCenter)
        => pipeX <= 133 && pipeX + 56 >= 107 && (y - 13 < gapCenter - 80 || y + 13 > gapCenter + 80);
}
