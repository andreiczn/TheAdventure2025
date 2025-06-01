using System;
using System.IO;
using System.Text.Json;
using Silk.NET.Maths;

namespace TheAdventure
{
    public class BitmapFont
    {
        private readonly int _glyphWidth;
        private readonly int _glyphHeight;
        private readonly string _chars;
        private readonly int _textureId;

        public BitmapFont(GameRenderer renderer, string fontJsonPath, string fontPngPath)
        {
            var jsonText = File.ReadAllText(fontJsonPath);
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            _glyphWidth = root.GetProperty("glyphWidth").GetInt32();
            _glyphHeight = root.GetProperty("glyphHeight").GetInt32();
            _chars = root.GetProperty("chars").GetString() ?? throw new Exception("font.json: 'chars' field missing or null");

            _textureId = renderer.LoadTexture(fontPngPath, out _);
        }

        public void DrawTextScreen(GameRenderer renderer, string text, int x, int y)
        {
            int cursorX = x;
            int cursorY = y;

            foreach (char c in text)
            {
                int idx = _chars.IndexOf(c);
                if (idx < 0)
                {
                    cursorX += _glyphWidth;
                    continue;
                }

                var src = new Rectangle<int>(
                    idx * _glyphWidth,
                    0,
                    _glyphWidth,
                    _glyphHeight
                );

                var worldCoords = renderer.ToWorldCoordinates(cursorX, cursorY);

                var dst = new Rectangle<int>(
                    worldCoords.X,
                    worldCoords.Y,
                    _glyphWidth,
                    _glyphHeight
                );

                renderer.RenderTexture(_textureId, src, dst);

                cursorX += _glyphWidth;
            }
        }
    }
}
