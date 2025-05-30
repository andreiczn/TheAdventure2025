using Silk.NET.SDL;

namespace TheAdventure
{
    public unsafe class Input
    {
        private readonly Sdl _sdl;

        /// <summary>
        /// Fired on any primary-button mouse click.
        /// Engine will decide whether to plant a bomb or restart.
        /// </summary>
        public EventHandler<(int x, int y)>? OnMouseClick;

        public Input(Sdl sdl)
        {
            _sdl = sdl;
        }

       
        public bool IsLeftPressed()   => GetKeyState(KeyCode.Left);
        public bool IsRightPressed()  => GetKeyState(KeyCode.Right);
        public bool IsUpPressed()     => GetKeyState(KeyCode.Up);
        public bool IsDownPressed()   => GetKeyState(KeyCode.Down);
        public bool IsKeyAPressed()   => GetKeyState(KeyCode.A); // attack
        public bool IsKeyBPressed()   => GetKeyState(KeyCode.B); // plant bomb

        public bool IsRestartPressed() => GetKeyState(KeyCode.R);

        // shared helper
        private bool GetKeyState(KeyCode code)
        {
            ReadOnlySpan<byte> state = new(_sdl.GetKeyboardState(null), (int)KeyCode.Count);
            return state[(int)code] == 1;
        }

        /// <summary>
        /// Polls SDL events, returns true if user tries to quit.
        /// Raises OnMouseClick on primary-button clicks.
        /// </summary>
        public bool ProcessInput()
        {
            Event ev = new();
            while (_sdl.PollEvent(ref ev) != 0)
            {
                if (ev.Type == (uint)EventType.Quit)
                    return true;

                if (ev.Type == (uint)EventType.Mousebuttondown
                    && ev.Button.Button == (byte)MouseButton.Primary)
                {
                    OnMouseClick?.Invoke(this, (ev.Button.X, ev.Button.Y));
                }
            }

            return false;
        }
    }
}
