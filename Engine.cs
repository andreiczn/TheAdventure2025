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

        // Game Over state
        private int _gameOverTexId;
        private TextureData _gameOverData;
        private bool _isGameOver;
        private DateTimeOffset _gameOverTimestamp; 

        private readonly Dictionary<int, GameObject> _gameObjects = new();
        private readonly Dictionary<string, TileSet>  _loadedTileSets = new();
        private readonly Dictionary<int, Tile>       _tileIdMap      = new();

        private Level _currentLevel = new();
        private PlayerObject? _player;
        private DateTimeOffset _lastUpdate = DateTimeOffset.Now;

        public Engine(GameRenderer renderer, Input input)
        {
            _renderer = renderer;
            _input    = input;

            // click → bomb or restart
            _input.OnMouseClick += (_, coords) =>
            {
                if (_isGameOver) Reset();
                else             AddBomb(coords.x, coords.y);
            };

            SetupWorld();
        }

        public void SetupWorld()
        {
            _gameObjects.Clear();
            _loadedTileSets.Clear();
            _tileIdMap.Clear();
            _isGameOver = false;

            _gameOverTexId = _renderer.LoadTexture(
                Path.Combine("Assets","image.png"),
                out _gameOverData
            );

            _player = new(SpriteSheet.Load(_renderer, "Player.json", "Assets"), 100, 100);

            var levelJson = File.ReadAllText(Path.Combine("Assets","terrain.tmj"));
            _currentLevel = JsonSerializer.Deserialize<Level>(levelJson)
                            ?? throw new Exception("Failed to load level");


            foreach (var tsRef in _currentLevel.TileSets)
            {
                var tsJson = File.ReadAllText(Path.Combine("Assets", tsRef.Source));
                var tileSet = JsonSerializer.Deserialize<TileSet>(tsJson)
                              ?? throw new Exception("Failed to load tileset");

                foreach (var tile in tileSet.Tiles)
                {
                    if (!tile.Id.HasValue || _tileIdMap.ContainsKey(tile.Id.Value))
                        continue;

                    tile.TextureId = _renderer.LoadTexture(
                        Path.Combine("Assets", tile.Image), out _);
                    _tileIdMap[tile.Id.Value] = tile;
                }
                _loadedTileSets[tileSet.Name] = tileSet;
            }

            int mapW = _currentLevel.Width .Value * _currentLevel.TileWidth .Value;
            int mapH = _currentLevel.Height.Value * _currentLevel.TileHeight.Value;
            _renderer.SetWorldBounds(new Rectangle<int>(0, 0, mapW, mapH));

            _scriptEngine.LoadAll(Path.Combine("Assets","Scripts"));
        }

        public void ProcessFrame()
        {
            // restart on R
            if (_isGameOver)
            {
                var elapsed = (DateTimeOffset.Now - _gameOverTimestamp).TotalSeconds;
                if (elapsed >= 3.0 || _input.IsRestartPressed())
                {
                    Reset();
                }
                return;
            }


            var now = DateTimeOffset.Now;
            var dt  = (now - _lastUpdate).TotalMilliseconds;
            _lastUpdate = now;

            if (_player == null || _isGameOver) return;

            double up    = _input.IsUpPressed()    ? 1 : 0;
            double down  = _input.IsDownPressed()  ? 1 : 0;
            double left  = _input.IsLeftPressed()  ? 1 : 0;
            double right = _input.IsRightPressed() ? 1 : 0;
            bool attack  = _input.IsKeyAPressed() && (up+down+left+right <=1);

            _player.UpdatePosition(up, down, left, right, 48, 48, dt);
            if (attack) _player.Attack();

            _scriptEngine.ExecuteAll(this);

            if (_input.IsKeyBPressed())
                AddBomb(_player.Position.X, _player.Position.Y, false);
        }

        public void RenderFrame()
        {
            _renderer.SetDrawColor(0,0,0,255);
            _renderer.ClearScreen();

            if (_player != null)
                _renderer.CameraLookAt(_player.Position.X, _player.Position.Y);

            RenderTerrain();
            RenderObjects();

            if (_isGameOver)
            {
                _renderer.ApplyDimEffect(0.7f);
                var dims = _renderer.GetScreenDimensions();
                int x = (dims.Width - _gameOverData.Width) / 2;
                int y = (dims.Height - _gameOverData.Height) / 2;

                var src = new Rectangle<int>(0,0,_gameOverData.Width, _gameOverData.Height);
                var dst = new Rectangle<int>(x,y,_gameOverData.Width, _gameOverData.Height);
                _renderer.RenderTextureScreenSpace(_gameOverTexId, src, dst);
            }

            _renderer.PresentFrame();
        }

        private void RenderTerrain()
        {

            int w = _currentLevel.Width .Value;
            int h = _currentLevel.Height.Value;

            foreach (var layer in _currentLevel.Layers)
            {

                int lw = layer.Width.Value;  

                for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                {

                    int raw = layer.Data[j * lw + i].Value;
                    if (raw == 0) 
                        continue;

                    int tid = raw - 1;
                    var tile = _tileIdMap[tid];


                    int tw = tile.ImageWidth .GetValueOrDefault();
                    int th = tile.ImageHeight.GetValueOrDefault();

                    var src = new Rectangle<int>(0, 0, tw, th);
                    var dst = new Rectangle<int>(i * tw, j * th, tw, th);
                    _renderer.RenderTexture(tile.TextureId, src, dst);
                }
            }
        }


        private void RenderObjects()
        {
            var toRem = new List<int>();
            foreach (var obj in GetRenderables())
            {
                obj.Render(_renderer);
                if(obj is TemporaryGameObject tmp && tmp.IsExpired)
                    toRem.Add(tmp.Id);
            }

            foreach (var id in toRem)
            {
                _gameObjects.Remove(id, out var removed);
                if(!_isGameOver && _player != null && removed is TemporaryGameObject b)
                {
                    var dx = Math.Abs(_player.Position.X - b.Position.X);
                    var dy = Math.Abs(_player.Position.Y - b.Position.Y);
                    if (dx < 32 && dy < 32)
                    {
                        _player.GameOver();
                        _isGameOver = true;
                        _gameOverTimestamp = DateTimeOffset.Now;
                    }
                }
            }

            _player?.Render(_renderer);
        }

        public IEnumerable<RenderableGameObject> GetRenderables()
            => _gameObjects.Values.OfType<RenderableGameObject>();

        public (int X,int Y) GetPlayerPosition()
            => _player!.Position;

        public void AddBomb(int X,int Y,bool translate=true)
        {
            var world = translate
                ? _renderer.ToWorldCoordinates(X,Y)
                : new Vector2D<int>(X,Y);

            var sheet = SpriteSheet.Load(_renderer,"BombExploding.json","Assets");
            sheet.ActivateAnimation("Explode");

            var bomb = new TemporaryGameObject(sheet,2.1,(world.X,world.Y));
            _gameObjects[bomb.Id] = bomb;
        }

        private void Reset() => SetupWorld();
    }
}
