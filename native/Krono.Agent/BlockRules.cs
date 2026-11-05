namespace Krono_Agent;

public static class BlockRules
{
    public readonly record struct Cell(int X, int Y);
    static readonly Cell[][] tetrominoes = [
        [new(0,0),new(1,0),new(2,0),new(3,0)], [new(0,0),new(1,0),new(0,1),new(1,1)],
        [new(1,0),new(0,1),new(1,1),new(2,1)], [new(1,0),new(2,0),new(0,1),new(1,1)],
        [new(0,0),new(1,0),new(1,1),new(2,1)], [new(0,0),new(0,1),new(1,1),new(2,1)],
        [new(2,0),new(0,1),new(1,1),new(2,1)]
    ];
    static readonly Cell[][] fitPieces = [
        [new(0,0)], [new(0,0),new(1,0)], [new(0,0),new(0,1)],
        [new(0,0),new(1,0),new(2,0)], [new(0,0),new(0,1),new(0,2)],
        [new(0,0),new(1,0),new(0,1)], [new(0,0),new(1,0),new(1,1)],
        [new(0,0),new(0,1),new(1,1)], [new(1,0),new(0,1),new(1,1)],
        [new(0,0),new(1,0),new(0,1),new(1,1)], [new(0,0),new(1,0),new(2,0),new(3,0)],
        [new(0,0),new(0,1),new(0,2),new(0,3)], [new(0,0),new(1,0),new(2,0),new(0,1),new(0,2)],
        [new(0,0),new(1,0),new(2,0),new(0,1),new(1,1),new(2,1)]
    ];
    public static int FitCount => fitPieces.Length;
    public static Cell[] Fit(int shape) => fitPieces[shape];
    public static Cell[] Falling(int shape, int rotation)
    {
        var cells = tetrominoes[shape];
        for (var i = 0; i < rotation; i++) {
            var maxY = cells.Max(c => c.Y); cells = cells.Select(c => new Cell(maxY - c.Y, c.X)).ToArray();
        }
        return cells;
    }
    public static bool CanPlace(int[] grid, int width, int height, Cell[] cells, int x, int y, int scale = 1)
    {
        foreach (var cell in cells) for (var dy = 0; dy < scale; dy++) for (var dx = 0; dx < scale; dx++) {
            var column = x + cell.X * scale + dx; var row = y + cell.Y * scale + dy;
            if (column < 0 || column >= width || row < 0 || row >= height || grid[row * width + column] != 0) return false;
        }
        return true;
    }
    public static void Place(int[] grid, int width, Cell[] cells, int x, int y, int color, int scale = 1)
    {
        foreach (var cell in cells) for (var dy = 0; dy < scale; dy++) for (var dx = 0; dx < scale; dx++) grid[(y + cell.Y * scale + dy) * width + x + cell.X * scale + dx] = color;
    }
    public static int ClearFit(int[] grid)
    {
        var rows = Enumerable.Range(0, 8).Where(row => Enumerable.Range(0, 8).All(col => grid[row * 8 + col] != 0)).ToArray();
        var cols = Enumerable.Range(0, 8).Where(col => Enumerable.Range(0, 8).All(row => grid[row * 8 + col] != 0)).ToArray();
        for (var i = 0; i < grid.Length; i++) if (rows.Contains(i / 8) || cols.Contains(i % 8)) grid[i] = 0;
        return rows.Length + cols.Length;
    }
    public static bool HasFit(int[] grid, int[] tray) => tray.Any(shape => shape >= 0 && Enumerable.Range(0, 64).Any(i => CanPlace(grid, 8, 8, Fit(shape), i % 8, i / 8)));
    public static int ClearRows(int[] grid, int width, int height)
    {
        var destination = height - 1;
        for (var source = height - 1; source >= 0; source--) {
            if (Enumerable.Range(0, width).All(col => grid[source * width + col] != 0)) continue;
            if (destination != source) Array.Copy(grid, source * width, grid, destination * width, width);
            destination--;
        }
        Array.Clear(grid, 0, (destination + 1) * width); return destination + 1;
    }
    public static bool SettleSand(int[] grid, int width, int height, bool leftFirst)
    {
        var moved = false;
        for (var y = height - 2; y >= 0; y--) for (var column = 0; column < width; column++) {
            var x = leftFirst ? column : width - column - 1; var from = y * width + x;
            if (grid[from] == 0) continue;
            var to = from + width;
            if (grid[to] != 0) {
                var first = leftFirst ? -1 : 1; var second = -first;
                bool Open(int dx) => x + dx >= 0 && x + dx < width && grid[from + dx] == 0 && grid[to + dx] == 0;
                if (Open(first)) to += first; else if (Open(second)) to += second; else continue;
            }
            grid[to] = grid[from]; grid[from] = 0; moved = true;
        }
        return moved;
    }
    public static int ClearSandSpans(int[] grid, int width, int height)
    {
        var visited = new bool[grid.Length]; var cleared = 0;
        for (var y = 0; y < height; y++) {
            var start = y * width; if (visited[start] || grid[start] == 0) continue;
            var color = grid[start]; List<int> connected = [start]; visited[start] = true;
            for (var read = 0; read < connected.Count; read++) {
                var cell = connected[read]; var x = cell % width;
                if (x > 0) Add(cell - 1); if (x < width - 1) Add(cell + 1);
                if (cell >= width) Add(cell - width); if (cell < grid.Length - width) Add(cell + width);
            }
            if (connected.Any(cell => cell % width == width - 1)) { cleared += connected.Count; foreach (var cell in connected) grid[cell] = 0; }
            void Add(int cell) { if (!visited[cell] && grid[cell] == color) { visited[cell] = true; connected.Add(cell); } }
        }
        return cleared;
    }
}
