using System;

namespace BlackWhale.UI.View
{
    // Optional session actions; the tablet still works with local clients in isolated UI tests.
    public sealed class LobbyActions
    {
        public Action FillBots, RemoveBots, Leave;
        public Func<bool, bool> SetPaused;
        public Func<BlackWhale.UI.Presentation.BotDifficulty, bool> SetDifficulty;
        public bool Busy;
        public string Status = "";
    }
}
