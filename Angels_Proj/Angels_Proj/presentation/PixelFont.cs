using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Angels_Proj;

/// <summary>Tiny built-in 5x7 bitmap font so the HUD can show text without a content-pipeline font.</summary>
public static class PixelFont
{
    private static readonly Dictionary<char, string[]> Glyphs = Build();

    private static Dictionary<char, string[]> Build()
    {
        var g = new Dictionary<char, string[]>();
        void Add(char c, string rows) => g[c] = rows.Split('/');
        Add('A', ".###./#...#/#...#/#####/#...#/#...#/#...#");
        Add('B', "####./#...#/#...#/####./#...#/#...#/####.");
        Add('C', ".####/#..../#..../#..../#..../#..../.####");
        Add('D', "####./#...#/#...#/#...#/#...#/#...#/####.");
        Add('E', "#####/#..../#..../####./#..../#..../#####");
        Add('F', "#####/#..../#..../####./#..../#..../#....");
        Add('G', ".####/#..../#..../#..##/#...#/#...#/.###.");
        Add('H', "#...#/#...#/#...#/#####/#...#/#...#/#...#");
        Add('I', "#####/..#../..#../..#../..#../..#../#####");
        Add('J', "..###/...#./...#./...#./...#./#..#./.##..");
        Add('K', "#...#/#..#./#.#../##.../#.#../#..#./#...#");
        Add('L', "#..../#..../#..../#..../#..../#..../#####");
        Add('M', "#...#/##.##/#.#.#/#.#.#/#...#/#...#/#...#");
        Add('N', "#...#/##..#/#.#.#/#..##/#...#/#...#/#...#");
        Add('O', ".###./#...#/#...#/#...#/#...#/#...#/.###.");
        Add('P', "####./#...#/#...#/####./#..../#..../#....");
        Add('Q', ".###./#...#/#...#/#...#/#.#.#/#..#./.##.#");
        Add('R', "####./#...#/#...#/####./#.#../#..#./#...#");
        Add('S', ".####/#..../#..../.###./....#/....#/####.");
        Add('T', "#####/..#../..#../..#../..#../..#../..#..");
        Add('U', "#...#/#...#/#...#/#...#/#...#/#...#/.###.");
        Add('V', "#...#/#...#/#...#/#...#/#...#/.#.#./..#..");
        Add('W', "#...#/#...#/#...#/#.#.#/#.#.#/##.##/#...#");
        Add('X', "#...#/#...#/.#.#./..#../.#.#./#...#/#...#");
        Add('Y', "#...#/#...#/.#.#./..#../..#../..#../..#..");
        Add('Z', "#####/....#/...#./..#../.#.../#..../#####");
        Add('0', ".###./#...#/#..##/#.#.#/##..#/#...#/.###.");
        Add('1', "..#../.##../..#../..#../..#../..#../.###.");
        Add('2', ".###./#...#/....#/...#./..#../.#.../#####");
        Add('3', "####./....#/....#/.###./....#/....#/####.");
        Add('4', "...#./..##./.#.#./#..#./#####/...#./...#.");
        Add('5', "#####/#..../####./....#/....#/#...#/.###.");
        Add('6', ".###./#..../#..../####./#...#/#...#/.###.");
        Add('7', "#####/....#/...#./..#../.#.../.#.../.#...");
        Add('8', ".###./#...#/#...#/.###./#...#/#...#/.###.");
        Add('9', ".###./#...#/#...#/.####/....#/....#/.###.");
        Add('.', "...../...../...../...../...../..#../..#..");
        Add('-', "...../...../...../#####/...../...../.....");
        Add('+', "...../..#../..#../#####/..#../..#../.....");
        Add('/', "....#/....#/...#./..#../.#.../#..../#....");
        Add('%', "##..#/##..#/...#./..#../.#.../#..##/#..##");
        Add(':', "...../..#../..#../...../..#../..#../.....");
        Add(' ', "...../...../...../...../...../...../.....");
        return g;
    }

    public const int GlyphW = 5, GlyphH = 7;

    /// <summary>Lit pixels of a string in glyph space (x advances 6 per character), for baking text into textures.</summary>
    public static System.Collections.Generic.IEnumerable<(int x, int y)> LitPixels(string text)
    {
        var x0 = 0;
        foreach (var ch in text)
        {
            if (Glyphs.TryGetValue(char.ToUpperInvariant(ch), out var rows))
                for (var y = 0; y < GlyphH; y++)
                    for (var c = 0; c < GlyphW; c++)
                        if (rows[y][c] == '#') yield return (x0 + c, y);
            x0 += 6;
        }
    }

    public static int Measure(string text, int px) => text.Length * 6 * px - px;

    public static void Draw(SpriteBatch sb, Texture2D pixel, string text, Vector2 pos, int px, Color color)
    {
        var x = (int)pos.X;
        foreach (var ch in text)
        {
            if (Glyphs.TryGetValue(char.ToUpperInvariant(ch), out var rows))
                for (var y = 0; y < GlyphH; y++)
                    for (var c = 0; c < GlyphW; c++)
                        if (rows[y][c] == '#')
                            sb.Draw(pixel, new Rectangle(x + c * px, (int)pos.Y + y * px, px, px), color);
            x += 6 * px;
        }
    }
}
