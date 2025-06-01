// Engine.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Silk.NET.Maths;
using TheAdventure.Models;
using TheAdventure.Models.Data;
using TheAdventure.Scripting;

namespace TheAdventure
{
    public class Engine
    {
        private readonly GameRenderer _renderer;
        private readonly Input _input;
        private readonly ScriptEngine _scriptEngine = new();

        private readonly Dictionary<int, GameObject> _gameObjects = new();
        private readonly Dictionary<string, TileSet> _loadedTileSets = new();
        private readonly Dictionary<int, Tile> _tileIdMap = new();

        private Level _currentLevel = new();
        private PlayerObject? _player;

        private DateTimeOffset _lastUpdate = DateTimeOffset.Now;

        private double _scoreTime = 0.0;             
        private readonly BitmapFont _bitmapFont;      


        public Engine(GameRenderer renderer, Input input)
        {
            _renderer = renderer;
            _input = input;

            _bitmapFont = new BitmapFont(
                _renderer,
                Path.Combine("Assets", "font.json"),
                Path.Combine("Assets", "font.png")
            );


            _input.OnMouseClick += (_, coords) =>
            {
                AddBomb(coords.x, coords.y);
            };
        }

        public void SetupWorld()
        {
            _gameObjects.Clear();
            _loadedTileSets.Clear();
            _tileIdMap.Clear();

            // Reset score timer
            _scoreTime = 0.0;
            _lastUpdate = DateTimeOffset.Now;

            // Load player object
            _player = new(SpriteSheet.Load(_renderer, "Player.json", "Assets"), 100, 100);

            // Load level JSON
            var levelContent = File.ReadAllText(Path.Combine("Assets", "terrain.tmj"));
            var level = JsonSerializer.Deserialize<Level>(levelContent);
            if (level == null)
                throw new Exception("Failed to load level");

            // Load each tileset
            foreach (var tileSetRef in level.TileSets)
            {
                var tileSetContent = File.ReadAllText(Path.Combine("Assets", tileSetRef.Source));
                var tileSet = JsonSerializer.Deserialize<TileSet>(tileSetContent);
                if (tileSet == null)
                    throw new Exception("Failed to load tile set");

                foreach (var tile in tileSet.Tiles)
                {
                    // Load tile texture
                    tile.TextureId = _renderer.LoadTexture(
                        Path.Combine("Assets", tile.Image),
                        out _
                    );
                    _tileIdMap.Add(tile.Id!.Value, tile);
                }

                _loadedTileSets.Add(tileSet.Name, tileSet);
            }

            // Validate dimensions
            if (level.Width == null || level.Height == null)
                throw new Exception("Invalid level dimensions");
            if (level.TileWidth == null || level.TileHeight == null)
                throw new Exception("Invalid tile dimensions");

            // Set camera/world bounds
            _renderer.SetWorldBounds(new Rectangle<int>(
                0,
                0,
                level.Width.Value * level.TileWidth.Value,
                level.Height.Value * level.TileHeight.Value
            ));

            _currentLevel = level;

            // Load scripts (e.g. RandomBomb)
            _scriptEngine.LoadAll(Path.Combine("Assets", "Scripts"));
        }

        public void ProcessFrame()
        {
            var now = DateTimeOffset.Now;
            var msSinceLastFrame = (now - _lastUpdate).TotalMilliseconds;
            _lastUpdate = now;

            if (_player == null)
                return;

            // Only accumulate score while player is alive (not in GameOver)
            if (_player.State.State != PlayerObject.PlayerState.GameOver)
            {
                _scoreTime += msSinceLastFrame / 1000.0;
            }

            // Movement + attack + bomb logic
            double up    = _input.IsUpPressed()    ? 1.0 : 0.0;
            double down  = _input.IsDownPressed()  ? 1.0 : 0.0;
            double left  = _input.IsLeftPressed()  ? 1.0 : 0.0;
            double right = _input.IsRightPressed() ? 1.0 : 0.0;
            bool isAttacking = _input.IsKeyAPressed() && (up + down + left + right <= 1);
            bool addBomb    = _input.IsKeyBPressed();

            _player.UpdatePosition(up, down, left, right, 48, 48, msSinceLastFrame);
            if (isAttacking)
                _player.Attack();

            _scriptEngine.ExecuteAll(this);

            if (addBomb)
                AddBomb(_player.Position.X, _player.Position.Y, false);
        }

        public void RenderFrame()
        {
            _renderer.SetDrawColor(0, 0, 0, 255);
            _renderer.ClearScreen();


            var playerPos = _player!.Position;
            _renderer.CameraLookAt(playerPos.X, playerPos.Y);

            RenderTerrain();
            RenderAllObjects();

            {
                string scoreStr = ((int)_scoreTime).ToString();
                _bitmapFont.DrawTextScreen(_renderer, scoreStr, 10, 10);
            }
            _renderer.PresentFrame();
        }

        public void RenderAllObjects()
        {
            var toRemove = new List<int>();
            foreach (var gameObject in GetRenderables())
            {
                gameObject.Render(_renderer);
                if (gameObject is TemporaryGameObject { IsExpired: true } temp)
                    toRemove.Add(temp.Id);
            }

            foreach (var id in toRemove)
            {
                _gameObjects.Remove(id, out var removed);
                if (_player == null)
                    continue;

                var tmp = (TemporaryGameObject)removed!;
                var dx = Math.Abs(_player.Position.X - tmp.Position.X);
                var dy = Math.Abs(_player.Position.Y - tmp.Position.Y);
                if (dx < 32 && dy < 32)
                {
                    _player.GameOver();
                }
            }

            _player?.Render(_renderer);
        }

        public void RenderTerrain()
        {
            foreach (var layer in _currentLevel.Layers)
            {
                int w = layer.Width.Value;
                int h = layer.Height.Value;

                for (int i = 0; i < w; i++)
                {
                    for (int j = 0; j < h; j++)
                    {
                        int rawValue = layer.Data[j * w + i].Value;
                        if (rawValue == 0) 
                            continue;

                        int tileId = rawValue - 1;
                        var tile = _tileIdMap[tileId];

                        int tw = tile.ImageWidth.GetValueOrDefault();
                        int th = tile.ImageHeight.GetValueOrDefault();

                        var src = new Rectangle<int>(0, 0, tw, th);
                        var dst = new Rectangle<int>(i * tw, j * th, tw, th);
                        _renderer.RenderTexture(tile.TextureId, src, dst);
                    }
                }
            }
        }

        public IEnumerable<RenderableGameObject> GetRenderables()
        {
            foreach (var go in _gameObjects.Values)
            {
                if (go is RenderableGameObject rgo)
                    yield return rgo;
            }
        }

        public (int X, int Y) GetPlayerPosition()
            => _player!.Position;

        public void AddBomb(int X, int Y, bool translateCoordinates = true)
        {
            var world = translateCoordinates
                ? _renderer.ToWorldCoordinates(X, Y)
                : new Vector2D<int>(X, Y);

            var sheet = SpriteSheet.Load(_renderer, "BombExploding.json", "Assets");
            sheet.ActivateAnimation("Explode");

            var bomb = new TemporaryGameObject(sheet, 2.1, (world.X, world.Y));
            _gameObjects.Add(bomb.Id, bomb);
        }

     
        public double ScoreTime => _scoreTime;

     
        public bool IsGameOver => (_player?.State.State == PlayerObject.PlayerState.GameOver);
        
    }
}
