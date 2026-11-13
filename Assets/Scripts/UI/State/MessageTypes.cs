// A(GameRoom)가 사용하는 메시지·명령 type 문자열을 D 쪽에서 한곳에 모아 둔 것.
// 값은 A의 '구현_및_검증.md'와 GameRoom 코드의 문자열과 같다. 새로 만든 값이 아니다.
// [A에게 제안] 이 상수들을 BlackWhale.Shared로 옮기면 A·B·C·D가 같은 선언을 쓸 수 있다.
namespace BlackWhale.UI.State
{
    public static class ServerMessageTypes
    {
        public const string Welcome = "WELCOME";
        public const string PublicState = "PUBLIC_STATE";
        public const string CommandResult = "COMMAND_RESULT";
        public const string PlayerView = "PLAYER_VIEW";
        public const string MarketReveal = "MARKET_REVEAL";
        public const string Scoreboard = "SCOREBOARD";
        public const string JoinRejected = "JOIN_REJECTED";
        public const string RoomEnded = "ROOM_ENDED";
        public const string Disconnected = "DISCONNECTED";
    }

    public static class CommandTypes
    {
        public const string SetReady = "SET_READY";
        public const string StartGame = "START_GAME";
        public const string SetLobbySettings = "SET_LOBBY_SETTINGS";
        public const string CompletePhase = "COMPLETE_PHASE";
        public const string GetPlayerView = "GET_PLAYER_VIEW";
        public const string BuyInformation = "BUY_INFORMATION";
        public const string BuyInformationWindow = "BUY_INFORMATION_WINDOW", RevealInformationWindow = "REVEAL_INFORMATION_WINDOW", UseInformationItem = "USE_INFORMATION_ITEM";
        public const string SelectCompany = "SELECT_COMPANY";
        public const string SubmitInvestment = "SUBMIT_INVESTMENT";
        public const string SaveTradePlan = "SAVE_TRADE_PLAN";
        public const string SelectAbility = "SELECT_ABILITY", SubmitBid = "SUBMIT_BID";

        /// <summary>GameRoom 규칙: 결과 type = 요청 type + "_RESULT".</summary>
        public static string ResultOf(string commandType) => commandType + "_RESULT";
    }

    public static class BundleSources
    {
        public const string Public = "PUBLIC";
        public const string Purchased = "PURCHASED";
        public const string Item = "ITEM";
        public const string Stolen = "STOLEN";
        public const string Ability = "ABILITY";
    }
}
