﻿using System;
using System.Linq;
using MegaMan.Common.Geometry;
using System.IO;
using MegaMan.Common;
using System.Collections.Generic;

namespace MegaMan.Engine
{
    public class StageHandler : GameHandler
    {
        private int playerDeadCount;

        private Action updateFunc;
        private Action<GameRenderEventArgs> drawFunc;

        private string startScreen;
        private int startX, startY;

        private readonly Music music;

        private ScreenHandler _currentScreen;

        private Dictionary<string, ScreenHandler> screens;

        private JoinHandler currentJoin;
        private ScreenHandler nextScreen;

        private StageInfo info;
        
        public HandlerTransfer WinHandler { get; set; }

        public HandlerTransfer LoseHandler { get; set; }

        public PositionComponent PlayerPos;

        # region IGameplayContainer Members

        public GameEntity Player { get; private set; }

        public override IEntityContainer Entities { get { return _currentScreen; } }

        #endregion

        public StageHandler(StageInfo stage)
        {
            info = stage;
            Info = stage;
            startScreen = info.StartScreen;

            if (string.IsNullOrEmpty(startScreen)) startScreen = info.Screens.Keys.First();
            startX = info.PlayerStartX;
            startY = info.PlayerStartY;

            string intropath = (stage.MusicIntroPath != null) ? stage.MusicIntroPath.Absolute : null;
            string looppath = (stage.MusicLoopPath != null) ? stage.MusicLoopPath.Absolute : null;
            if (intropath != null || looppath != null) music = Engine.Instance.SoundSystem.LoadMusic(intropath, looppath, 1);
        }

        public void InitScreens(Dictionary<string, ScreenHandler> screens)
        {
            this.screens = screens;
        }

        public void SetTestingStartPosition(string screen, Point startPosition)
        {
            startScreen = screen;
            startX = startPosition.X;
            startY = startPosition.Y;
        }

        private void Player_Death()
        {
            if (music != null) music.Stop();
            Engine.Instance.SoundSystem.StopMusicNsf();
            
            playerDeadCount = 0;
            updateFunc = DeadUpdate;
            Game.CurrentGame.Player.Lives--;
        }

        private void BeginPlay()
        {
            Player.Start();
            Player.GetComponent<SpriteComponent>().Visible = true;

            StateMessage msg = new StateMessage(null, "Teleport");
            PlayerPos.SetPosition(new PointF(startX, 0));
            Player.SendMessage(msg);
            Action teleport = () => {};
            teleport += () =>
            {
                if (PlayerPos.Position.Y >= startY)
                {
                    PlayerPos.SetPosition(new PointF(startX, startY));
                    Player.SendMessage(new StateMessage(null, "TeleportEnd"));
                    GameThink -= teleport;
                    updateFunc = Update;
                }
            };
            GameThink += teleport;
        }

        private void DrawScreen(GameRenderEventArgs renderArgs)
        {
            _currentScreen.Draw(renderArgs, PlayerPos.Position);
        }

        private void DeadUpdate()
        {
            playerDeadCount++;
            if (playerDeadCount >= Const.MapDeadFrames)
            {
                updateFunc = null;
                Engine.Instance.FadeTransition(Reset);
            }
        }

        private void Reset()
        {
            StopHandler();

            if (Game.CurrentGame.Player.Lives < 0) // game over!
            {
                Finish(LoseHandler);
            }
            else
            {
                // enable respawn for on-death-respawn entities
                foreach (var screen in this.screens.Values)
                {
                    screen.Reset();
                }

                StartHandler();
            }
        }

        // swaps nextscreen for currentscreen and makes necessary adjustments to player
        // does not necessary represent the "end" of a scroll operation (boss doors still have to close)
        private void ScrollDone(JoinHandler join)
        {
            Player.Paused = false;
            join.ScrollDone -= ScrollDone;
            ChangeScreen(nextScreen);

            updateFunc = Update;
            drawFunc = DrawScreen;

            // check for continue points
            if (info.ContinuePoints.ContainsKey(nextScreen.Screen.Name))
            {
                startScreen = nextScreen.Screen.Name;
                startX = info.ContinuePoints[nextScreen.Screen.Name].X;
                startY = info.ContinuePoints[nextScreen.Screen.Name].Y;
            }
        }

        private void ChangeScreen(ScreenHandler nextScreen)
        {
            ScreenHandler oldscreen = _currentScreen;
            _currentScreen = nextScreen;

            oldscreen.Clean();
            StartScreen();
        }

        private void Update()
        {
            _currentScreen.Update();
        }

        private void OnScrollTriggered(JoinHandler join)
        {
            currentJoin = join;

            Player.Paused = true;
            nextScreen = screens[join.NextScreenName];
            join.BeginScroll(nextScreen, PlayerPos.Position);

            updateFunc = () => join.Update(PlayerPos);
            join.ScrollDone += ScrollDone;

            drawFunc = DrawJoin;

            StopScreen();
        }

        private void DrawJoin(GameRenderEventArgs renderArgs)
        {
            _currentScreen.Draw(renderArgs, PlayerPos.Position, 0, 0, currentJoin.OffsetX, currentJoin.OffsetY);
            nextScreen.Draw(renderArgs, PlayerPos.Position, currentJoin.NextScreenX, currentJoin.NextScreenY, currentJoin.NextOffsetX, currentJoin.NextOffsetY);
        }

        private void StartScreen()
        {
            _currentScreen.JoinTriggered += OnScrollTriggered;
            _currentScreen.Teleport += OnTeleport;
            _currentScreen.Start(this, Player);

            RunCommands(_currentScreen.Screen.Commands);
        }

        private void BossDefeated()
        {
            Finish(WinHandler); 
        }

        private void StopScreen()
        {
            _currentScreen.JoinTriggered -= OnScrollTriggered;
            _currentScreen.Teleport -= OnTeleport;
            _currentScreen.Stop();
        }

        private bool teleporting = false;
        private void OnTeleport(TeleportInfo info)
        {
            if (teleporting) return;
            teleporting = true;
            Action<string> setpos = (s) => { };
            if (info.TargetScreen == _currentScreen.Screen.Name)
            {
                setpos = (state) =>
                {
                    PlayerPos.SetPosition(new Point(info.To.X, info.To.Y));
                    (Player.GetComponent<StateComponent>()).StateChanged -= setpos;
                    Player.SendMessage(new StateMessage(null, "TeleportEnd"));
                    teleporting = false;
                    (Player.GetComponent<MovementComponent>()).CanMove = true;
                };
            }
            else
            {
                setpos = state =>
                {
                    (Player.GetComponent<SpriteComponent>()).Visible = false;
                    (Player.GetComponent<StateComponent>()).StateChanged -= setpos;
                    Engine.Instance.FadeTransition(
                        () => 
                    { 
                        StopScreen();
                        ChangeScreen(screens[info.TargetScreen]);
                        PlayerPos.SetPosition(new Point(info.To.X, info.To.Y)); // do it here so drawing is correct for fade-in
                    }, () =>
                    {
                        (Player.GetComponent<SpriteComponent>()).Visible = true;
                        Player.SendMessage(new StateMessage(null, "TeleportEnd"));
                        (Player.GetComponent<MovementComponent>()).CanMove = true;
                        teleporting = false;
                    });
                };
            }
            (Player.GetComponent<MovementComponent>()).CanMove = false;
            Player.SendMessage(new StateMessage(null, "TeleportBlink"));
            (Player.GetComponent<StateComponent>()).StateChanged += setpos;
        }

        #region IHandleGameEvents Members

        public override void StartHandler()
        {
            Player = GameEntity.Get("Player", this);
            PlayerPos = Player.GetComponent<PositionComponent>();

            Player.Death += Player_Death;

            PlayerPos = Player.GetComponent<PositionComponent>();
            PlayerPos.SetPosition(new PointF(startX, 0));

            if (!info.Screens.ContainsKey(startScreen)) throw new GameRunException("The start screen for \""+info.Name+"\" is supposed to be \""+startScreen+"\", but it doesn't exist!");
            _currentScreen = screens[startScreen];
            StartScreen();

            Engine.Instance.SoundSystem.StopMusicNsf();

            if (music != null) music.Play();
            if (info.MusicNsfTrack != 0) Engine.Instance.SoundSystem.PlayMusicNSF((uint)info.MusicNsfTrack);

            // updateFunc isn't set until BeginPlay
            drawFunc = DrawScreen;

            ResumeHandler();
            StartDrawing();

            BeginPlay();

            // make sure we can move
            (Player.GetComponent<InputComponent>()).Paused = false;
        }

        public override void StopHandler()
        {
            Player.Death -= Player_Death;

            // reset gravity and palettes
            Game.CurrentGame.GravityFlip = false;
            PaletteSystem.ResetAll();

            if (_currentScreen != null)
            {
                StopScreen();
                _currentScreen.Clean();
            }

            if (music != null) music.Stop();
            if (info.MusicNsfTrack != 0) Engine.Instance.SoundSystem.StopMusicNsf();

            PauseHandler();
            StopDrawing();

            GameEntity.StopAll();
        }

        private int pauseCount = 1; // starts paused

        public override void PauseHandler()
        {
            if (pauseCount == 0)
            {
                Player.Paused = true;

                Engine.Instance.GameLogicTick -= Tick;
                Engine.Instance.GameInputReceived -= GameInputReceived;
            }

            pauseCount++;
        }

        public override void ResumeHandler()
        {
            if (pauseCount == 0) return;

            pauseCount--;

            if (pauseCount == 0)
            {
                Engine.Instance.GameLogicTick += Tick;
                Engine.Instance.GameInputReceived += GameInputReceived;

                Player.Paused = false;
            }
        }

        protected override void GameInputReceived(GameInputEventArgs e)
        {
            if (updateFunc == null || (Player.GetComponent<InputComponent>()).Paused) return;
        }

        protected override void Tick(GameTickEventArgs e)
        {
            if (updateFunc != null) updateFunc();

            foreach (Tile t in info.Tileset)
            {
                t.Sprite.Update();
            }

            base.Tick(e);
        }

        protected override void GameRender(GameRenderEventArgs e)
        {
            if (drawFunc != null) drawFunc(e);

            base.GameRender(e);
        }

        protected override void RunCommand(SceneCommandInfo cmd)
        {
            // stage handlers only run a subset of all commands
            // entities aren't included because we handle them in a totally different way
            if (cmd.Type == SceneCommands.Call || cmd.Type == SceneCommands.Condition || cmd.Type == SceneCommands.Effect ||
                cmd.Type == SceneCommands.Next || cmd.Type == SceneCommands.PlayMusic || cmd.Type == SceneCommands.Sound || cmd.Type == SceneCommands.StopMusic)
            {
                base.RunCommand(cmd);
            }
        }

        #endregion
    }
}
