using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Angels_Proj;

/// <summary>
/// A menu panel (the debug menu, the options menu): a plain panel with a title and its options grouped under section
/// headings. Each option is a label, an optional value shown on the right, and what choosing it does; the game adds
/// them (Add) so new options only need a line each. Arrow keys or the mouse to pick, Enter, Space or a click to choose,
/// Esc to close. Sits at the top left, or in the middle of a dimmed screen.
/// </summary>
public sealed class MenuPanel
{
    private readonly string _title;
    private readonly bool _centred;

    public MenuPanel(string title, bool centred)
    {
        _title = title;
        _centred = centred;
    }

    private sealed class Item
    {
        public string Label;
        public Func<string> Value;
        public Action Run;
        public bool Header;
    }

    private readonly List<Item> _items = new();
    private int _sel = -1;
    private int _valueChars = 3;        // widest value seen so far, in characters, so the panel doesn't twitch as values change
    public bool Open;

    private static readonly Color Panel = new(18, 20, 24, 232), Edge = new(70, 76, 88);
    private static readonly Color Title = new(220, 224, 232), HeaderCol = new(120, 128, 142), Text = new(200, 205, 214);
    private static readonly Color ValueCol = new(140, 200, 170), Off = new(110, 116, 128), Accent = new(110, 170, 255);

    /// <summary>Starts a new section with this heading.</summary>
    public void Section(string heading) => _items.Add(new Item { Label = heading, Header = true });

    /// <summary>An option: its label, what to show on the right (or null), and what choosing it does.</summary>
    public void Add(string label, Func<string> value, Action run) => _items.Add(new Item { Label = label, Value = value, Run = run });

    /// <summary>"ON" or "OFF", for a toggle's value.</summary>
    public static string OnOff(bool on) => on ? "ON" : "OFF";

    private int Px(float scale) => Math.Max(2, (int)MathF.Round(2f * scale));
    private int RowH(float scale) => 11 * Px(scale);

    private Rectangle PanelRect(Rectangle screen, float scale)
    {
        var px = Px(scale);
        // Wide enough for the longest label and the widest value side by side, with a clear gap between them.
        var labelChars = 19;    // the footer
        foreach (var it in _items)
        {
            if (it.Header) continue;
            labelChars = Math.Max(labelChars, it.Label.Length);
            _valueChars = Math.Max(_valueChars, it.Value?.Invoke()?.Length ?? 0);
        }
        int w = (6 + 4 + 6) * px + (labelChars + 4 + _valueChars) * 6 * px;
        int h = 18 * px + _items.Count * RowH(scale) + 14 * px;
        return _centred ? new Rectangle(screen.X + (screen.Width - w) / 2, screen.Y + (screen.Height - h) / 2, w, h)
            : new Rectangle((int)(16 * scale), (int)(16 * scale), w, h);
    }

    private Rectangle RowRect(Rectangle panel, float scale, int i)
    {
        var px = Px(scale);
        return new Rectangle(panel.X, panel.Y + 16 * px + i * RowH(scale), panel.Width, RowH(scale));
    }

    private void Move(int dir)
    {
        if (_items.Count == 0) return;
        var i = _sel;
        for (var n = 0; n < _items.Count; n++)
        {
            i = (i + dir + _items.Count) % _items.Count;
            if (!_items[i].Header) { _sel = i; return; }
        }
    }

    /// <summary>One tick while open. pressed says whether a key went down this tick; clicked whether the left button did.</summary>
    public void Update(Func<Keys, bool> pressed, MouseState m, bool clicked, Rectangle screen, float scale)
    {
        if (_sel < 0) Move(1);
        if (pressed(Keys.Escape)) { Open = false; return; }
        if (pressed(Keys.Down)) Move(1);
        if (pressed(Keys.Up)) Move(-1);
        if ((pressed(Keys.Enter) || pressed(Keys.Space)) && _sel >= 0) _items[_sel].Run?.Invoke();
        var panel = PanelRect(screen, scale);
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].Header || !RowRect(panel, scale, i).Contains(m.X, m.Y)) continue;
            _sel = i;
            if (clicked) _items[i].Run?.Invoke();
        }
    }

    public void Draw(SpriteBatch sb, Texture2D pixel, Rectangle screen, float scale)
    {
        if (!Open) return;
        var px = Px(scale);
        var panel = PanelRect(screen, scale);
        if (_centred) sb.Draw(pixel, screen, Color.Black * 0.45f);
        sb.Draw(pixel, panel, Panel);
        sb.Draw(pixel, new Rectangle(panel.X, panel.Y, panel.Width, 1), Edge);
        sb.Draw(pixel, new Rectangle(panel.X, panel.Bottom - 1, panel.Width, 1), Edge);
        sb.Draw(pixel, new Rectangle(panel.X, panel.Y, 1, panel.Height), Edge);
        sb.Draw(pixel, new Rectangle(panel.Right - 1, panel.Y, 1, panel.Height), Edge);
        var pad = 6 * px;
        PixelFont.Draw(sb, pixel, _title, new Vector2(panel.X + pad, panel.Y + 5 * px), px, Title);
        sb.Draw(pixel, new Rectangle(panel.X + pad, panel.Y + 14 * px, panel.Width - 2 * pad, 1), Edge);

        for (var i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            var r = RowRect(panel, scale, i);
            var ty = r.Y + (r.Height - 7 * px) / 2;
            if (it.Header)
            {
                var hp = Math.Max(1, px - 1);
                PixelFont.Draw(sb, pixel, it.Label, new Vector2(r.X + pad, r.Bottom - 8 * hp), hp, HeaderCol);
                continue;
            }
            if (i == _sel)
            {
                sb.Draw(pixel, r, new Color(255, 255, 255, 14));
                sb.Draw(pixel, new Rectangle(r.X, r.Y, Math.Max(2, px), r.Height), Accent);
            }
            PixelFont.Draw(sb, pixel, it.Label, new Vector2(r.X + pad + 4 * px, ty), px, Text);
            var v = it.Value?.Invoke();
            if (!string.IsNullOrEmpty(v))
                PixelFont.Draw(sb, pixel, v, new Vector2(r.Right - pad - PixelFont.Measure(v, px), ty), px, v == "OFF" ? Off : ValueCol);
        }
        var hpx = Math.Max(1, px - 1);
        PixelFont.Draw(sb, pixel, "UP/DOWN  ENTER  ESC", new Vector2(panel.X + pad, panel.Bottom - 5 * px - 7 * hpx / 2), hpx, HeaderCol);
    }
}
