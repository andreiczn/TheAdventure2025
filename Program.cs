// Program.cs
using Silk.NET.SDL;
using System;
using System.IO;
using Thread = System.Threading.Thread;

namespace TheAdventure
{
    public static class Program
    {
        public static void Main()
        {
            var sdl = new Sdl(new SdlContext());
            var initResult = sdl.Init(
                Sdl.InitVideo   |
                Sdl.InitAudio   |
                Sdl.InitEvents  |
                Sdl.InitTimer   |
                Sdl.InitGamecontroller |
                Sdl.InitJoystick
            );
            if (initResult < 0)
                throw new InvalidOperationException("Failed to initialize SDL.");


            using var gameWindow = new GameWindow(sdl);
            var renderer = new GameRenderer(sdl, gameWindow);


            var input = new Input(sdl);
            var engine = new Engine(renderer, input);

     
            const string highscoreFile = "highscore.txt";
            int highscore = 0;
            if (File.Exists(highscoreFile))
            {
                var text = File.ReadAllText(highscoreFile).Trim();
                if (int.TryParse(text, out var stored))
                    highscore = stored;
            }

            bool prevGameOver = false;


            engine.SetupWorld();

    
            bool quit = false;
            while (!quit)
            {

                quit = input.ProcessInput();
                if (quit) break;

                engine.ProcessFrame();

                bool currGameOver = engine.IsGameOver;
                if (currGameOver && !prevGameOver)
                {
                    int finalScore = (int)Math.Floor(engine.ScoreTime);
                    if (finalScore > highscore)
                    {
                        highscore = finalScore;
                        File.WriteAllText(highscoreFile, highscore.ToString());
                    }
                }
                prevGameOver = currGameOver;
                engine.RenderFrame();
                {
                    int currentScore = (int)Math.Floor(engine.ScoreTime);
                    gameWindow.SetTitle(
                        $"The Adventure  —  Score: {currentScore}   Highscore: {highscore}"
                    );
                }

                Thread.Sleep(13);
            }

            sdl.Quit();
        }
    }
}
