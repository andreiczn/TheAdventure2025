using System;
using System.IO;
using System.Threading.Tasks;
using Silk.NET.SDL;
using System.Runtime.InteropServices;

namespace TheAdventure
{
    
    public static unsafe class AudioManager
    {
        private static Sdl _sdl      = null!;  
        private static AudioSpec _spec;        

        
        private static byte* _bgBuffer       = null!;  
        private static uint  _bgLengthBytes  = 0;       

        private static byte* _bombBuffer     = null!;  
        private static uint  _bombLength     = 0;

        private static byte* _gameOverBuffer = null!; 
        private static uint  _gameOverLength = 0;

        private static uint _deviceId      = 0;   
        private static bool _initialized   = false;

        [DllImport("SDL2.dll", EntryPoint = "SDL_RWFromFile", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_RWFromFile(
            [MarshalAs(UnmanagedType.LPStr)] string file,
            [MarshalAs(UnmanagedType.LPStr)] string mode
        );

        [DllImport("SDL2.dll", EntryPoint = "SDL_LoadWAV_RW", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_LoadWAV_RW(
            IntPtr rw,           // SDL_RWops*
            int freesrc,         // 1 to auto-free the RWops after loading
            out AudioSpec spec,  // fills this with format data
            out IntPtr audioBuf, // Uint8**
            out uint audioLen    // Uint32*
        );

     
        [DllImport("SDL2.dll", EntryPoint = "SDL_FreeWAV", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_FreeWAV(IntPtr audioBuf);

      
        public static void Initialize(Sdl sdl)
        {
            if (_initialized) return;
            _initialized = true;
            _sdl = sdl;

  
            var bgPath       = Path.Combine("Assets", "arcade.wav");
            var bombPath     = Path.Combine("Assets", "bomb.wav");
            var gameOverPath = Path.Combine("Assets", "game_over.wav");

            {
                
                IntPtr rw = SDL_RWFromFile(bgPath, "rb");
                if (rw == IntPtr.Zero)
                {
                    string err = Marshal.PtrToStringUTF8((IntPtr)_sdl.GetError()) ?? "Unknown SDL error";
                    throw new Exception($"SDL_RWFromFile failed to open '{bgPath}': {err}");
                }

                IntPtr ret = SDL_LoadWAV_RW(rw, 1, out _spec, out IntPtr audioBuf, out uint audioLen);
                if (ret == IntPtr.Zero)
                {
                    string err = Marshal.PtrToStringUTF8((IntPtr)_sdl.GetError()) ?? "Unknown SDL error";
                    throw new Exception($"SDL_LoadWAV_RW failed to load '{bgPath}': {err}");
                }

                _bgBuffer      = (byte*)audioBuf;
                _bgLengthBytes = audioLen;
            }

            {
                IntPtr rw = SDL_RWFromFile(bombPath, "rb");
                if (rw == IntPtr.Zero)
                {
                    string err = Marshal.PtrToStringUTF8((IntPtr)_sdl.GetError()) ?? "Unknown SDL error";
                    throw new Exception($"SDL_RWFromFile failed to open '{bombPath}': {err}");
                }

                IntPtr ret = SDL_LoadWAV_RW(rw, 1, out AudioSpec tmpSpec, out IntPtr buf, out uint len);
                if (ret == IntPtr.Zero)
                {
                    string err = Marshal.PtrToStringUTF8((IntPtr)_sdl.GetError()) ?? "Unknown SDL error";
                    throw new Exception($"SDL_LoadWAV_RW failed to load '{bombPath}': {err}");
                }

                _bombBuffer = (byte*)buf;
                _bombLength = len;
            }


            {
                IntPtr rw = SDL_RWFromFile(gameOverPath, "rb");
                if (rw == IntPtr.Zero)
                {
                    string err = Marshal.PtrToStringUTF8((IntPtr)_sdl.GetError()) ?? "Unknown SDL error";
                    throw new Exception($"SDL_RWFromFile failed to open '{gameOverPath}': {err}");
                }

                IntPtr ret = SDL_LoadWAV_RW(rw, 1, out AudioSpec tmpSpec, out IntPtr buf, out uint len);
                if (ret == IntPtr.Zero)
                {
                    string err = Marshal.PtrToStringUTF8((IntPtr)_sdl.GetError()) ?? "Unknown SDL error";
                    throw new Exception($"SDL_LoadWAV_RW failed to load '{gameOverPath}': {err}");
                }

                _gameOverBuffer = (byte*)buf;
                _gameOverLength = len;
            }

            fixed (AudioSpec* wantPtr = &_spec)
            {
                _deviceId = _sdl.OpenAudioDevice(
                    (byte*)0,    // explicitly “null pointer” for the default device
                    0,           // playback
                    wantPtr,     // AudioSpec*
                    null,        // no “obtained” needed
                    0            // allowed format changes = 0
                );
            }

            if (_deviceId == 0)
            {
                string err = Marshal.PtrToStringUTF8((IntPtr)_sdl.GetError()) ?? "Unknown SDL error";
                throw new Exception($"SDL_OpenAudioDevice failed: {err}");
            }


            _sdl.PauseAudioDevice(_deviceId, 0);

            QueueBackgroundLoop();
        }

  
        private static void QueueBackgroundLoop()
        {
  
            _sdl.QueueAudio(_deviceId, _bgBuffer, _bgLengthBytes);


            Task.Run(() =>
            {
                const int loopMs = 18_000;
                while (true)
                {

                    System.Threading.Thread.Sleep(loopMs);
                    _sdl.QueueAudio(_deviceId, _bgBuffer, _bgLengthBytes);
                }
            });
        }

     
        public static void PlayBomb()
        {
            if (!_initialized) return;
            _sdl.QueueAudio(_deviceId, _bombBuffer, _bombLength);
        }


        public static void PlayGameOver()
        {
            if (!_initialized) return;
            _sdl.QueueAudio(_deviceId, _gameOverBuffer, _gameOverLength);
        }

   
        public static void Shutdown()
        {
            if (!_initialized) return;
            _initialized = false;


            _sdl.PauseAudioDevice(_deviceId, 1);
            _sdl.CloseAudioDevice(_deviceId);


            SDL_FreeWAV((IntPtr)_bgBuffer);
            SDL_FreeWAV((IntPtr)_bombBuffer);
            SDL_FreeWAV((IntPtr)_gameOverBuffer);
        }
    }
}
