namespace Krono_Agent;

public sealed class KlondikeRound
{
    public List<int> Stock { get; set; } = [];
    public List<int> Waste { get; set; } = [];
    public List<int>[] Tableau { get; set; } = [[], [], [], [], [], [], []];
    public int[] FaceDown { get; set; } = new int[7];
    public int[] Foundations { get; set; } = new int[4];
    public int SelectedKind { get; set; } = -1;
    public int SelectedPile { get; set; }
    public int SelectedIndex { get; set; }
    public const int WasteKind = 0, TableauKind = 1, FoundationKind = 2;
    public static int Rank(int card) => card % 13 + 1;
    public static int Suit(int card) => card / 13;
    public static bool Red(int card) => Suit(card) % 2 == 1;
    public static bool Below(int top, int next) => Rank(top) == Rank(next) + 1 && Red(top) != Red(next);
    public static KlondikeRound Deal(Random random)
    {
        var deck = Enumerable.Range(0, 52).ToArray(); random.Shuffle(deck.AsSpan()); var offset = 0;
        var round = new KlondikeRound();
        for (var col = 0; col < 7; col++) { round.Tableau[col].AddRange(deck.Skip(offset).Take(col + 1)); round.FaceDown[col] = col; offset += col + 1; }
        round.Stock.AddRange(deck.Skip(offset)); return round;
    }
    public bool DrawStock()
    {
        if (Stock.Count == 0 && Waste.Count == 0) return false;
        if (Stock.Count == 0) { Stock.AddRange(Waste.AsEnumerable().Reverse()); Waste.Clear(); }
        else { Waste.Add(Stock[^1]); Stock.RemoveAt(Stock.Count - 1); }
        ClearSelection(); return true;
    }
    public void Select(int kind, int pile = 0, int index = 0)
    {
        if (SelectedKind == kind && SelectedPile == pile && SelectedIndex == index) { ClearSelection(); return; }
        SelectedKind = kind; SelectedPile = pile; SelectedIndex = index;
        if (Selection().Length == 0) ClearSelection();
    }
    public void ClearSelection() { SelectedKind = -1; SelectedPile = SelectedIndex = 0; }
    public int[] Selection() => SelectedKind switch {
        WasteKind when Waste.Count > 0 => [Waste[^1]],
        TableauKind when SelectedPile is >= 0 and < 7 && SelectedIndex >= FaceDown[SelectedPile] && SelectedIndex < Tableau[SelectedPile].Count => Tableau[SelectedPile].Skip(SelectedIndex).ToArray(),
        FoundationKind when SelectedPile is >= 0 and < 4 && Foundations[SelectedPile] > 0 => [SelectedPile * 13 + Foundations[SelectedPile] - 1],
        _ => []
    };
    public bool ToTableau(int column)
    {
        if (column is < 0 or >= 7 || SelectedKind == TableauKind && SelectedPile == column) return false;
        var cards = Selection(); if (cards.Length == 0) return false;
        var target = Tableau[column];
        if (target.Count == 0 ? Rank(cards[0]) != 13 : !Below(target[^1], cards[0])) return false;
        RemoveSelection(cards.Length); target.AddRange(cards); ClearSelection(); return true;
    }
    public bool ToFoundation(int suit)
    {
        if (suit is < 0 or >= 4 || SelectedKind == FoundationKind) return false;
        var cards = Selection();
        if (cards.Length != 1 || Suit(cards[0]) != suit || Rank(cards[0]) != Foundations[suit] + 1) return false;
        RemoveSelection(1); Foundations[suit]++; ClearSelection(); return true;
    }
    void RemoveSelection(int count)
    {
        if (SelectedKind == WasteKind) Waste.RemoveAt(Waste.Count - 1);
        if (SelectedKind == FoundationKind) Foundations[SelectedPile]--;
        if (SelectedKind == TableauKind) {
            var column = Tableau[SelectedPile]; column.RemoveRange(SelectedIndex, count);
            if (column.Count > 0 && column.Count == FaceDown[SelectedPile]) FaceDown[SelectedPile]--;
        }
    }
    public bool IsValid()
    {
        if (Stock is null || Waste is null || Tableau?.Length != 7 || FaceDown?.Length != 7 || Foundations?.Length != 4 || Foundations.Any(rank => rank is < 0 or > 13) || Tableau.Any(column => column is null || column.Count > 52)) return false;
        var all = Stock.Concat(Waste).Concat(Tableau.SelectMany(column => column)).Concat(Foundations.SelectMany((rank, suit) => Enumerable.Range(suit * 13, rank)));
        if (!all.Order().SequenceEqual(Enumerable.Range(0, 52))) return false;
        for (var i = 0; i < 7; i++) {
            var column = Tableau[i];
            if (FaceDown[i] < 0 || FaceDown[i] > Math.Max(0, column.Count - 1)) return false;
            for (var row = FaceDown[i] + 1; row < column.Count; row++) if (!Below(column[row - 1], column[row])) return false;
        }
        return SelectedKind is >= -1 and <= 2 && (SelectedKind == -1 || Selection().Length > 0);
    }
}
