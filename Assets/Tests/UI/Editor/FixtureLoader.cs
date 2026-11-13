// 테스트 전용: 프로젝트 루트의 '개발 정리/임시 데이터 및 검증용' fixture를 A의 Shared 형식으로 읽는다.
// Assets 밖에 있으므로 빌드에 포함되지 않는다. 기대값 파일을 복사·수정하지 않는다.
using System;
using System.IO;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.State;
using UnityEngine;

namespace BlackWhale.UI.Tests
{
    internal static class FixtureLoader
    {
        public static string FixtureDir
            => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "개발 정리", "임시 데이터 및 검증용");

        public static T Load<T>(string fileName)
        {
            var path = Path.Combine(FixtureDir, fileName);
            if (!File.Exists(path)) throw new FileNotFoundException("fixture 없음: " + path);
            return JsonUtility.FromJson<T>(File.ReadAllText(path));
        }

        public static PublicState PublicInitial() => Load<PublicState>("D_public_initial.json");
        public static PublicState PublicAfterSelection() => Load<PublicState>("D_public_after_selection.json");
        public static PlayerView P1Before() => Load<PlayerView>("D_P1_before_purchase.json");
        public static PlayerView P1After() => Load<PlayerView>("D_P1_after_purchase.json");
        public static PlayerView P2AfterP1Purchase() => Load<PlayerView>("D_P2_after_P1_purchase.json");
        public static Scoreboard ScoreboardExpected() => Load<Scoreboard>("D_scoreboard_expected.json");
        [Serializable] internal class FixtureOrder { public string playerId; public Investment[] investments; }
        [Serializable] internal class FixtureOrders { public FixtureOrder[] orders; }
        public static FixtureOrder[] Orders() => Load<FixtureOrders>("B_investment_orders.json").orders;

        // ---- exchange_examples.json: 외곽 라우팅 정보 + 안쪽 request/response ----
        [Serializable] internal class Example { public string senderPlayerId; public Command request; public CommandResult response; }
        [Serializable] internal class Examples { public Example[] examples; }

        /// <summary>examples[0]: P1 AI 1번 묶음 구매. examples[1]: P1 주문 확정.</summary>
        public static Example Exchange(int index) => Load<Examples>("exchange_examples.json").examples[index];

        // ---- A_selection_replay.json ----
        [Serializable] internal class SelectionStep { public string senderPlayerId; public Command request; public PublicState expectedPublicUpdate; }
        [Serializable] internal class SelectionReplay { public int turn; public SelectionStep[] steps; }

        public static SelectionReplay Selection() => Load<SelectionReplay>("A_selection_replay.json");

        // ---- validation_cases.json (주문 사례만 사용). 소수 날짜(3.5) 사례가 있어 float 필드를 쓴다. ----
        [Serializable] internal class CaseInterval { public float entryDay; public float exitDay; }
        [Serializable] internal class CaseInvestment { public string companyId; public int weightBps; public CaseInterval[] intervals; }
        [Serializable] internal class CasePayload { public CaseInvestment[] investments; }
        [Serializable] internal class CaseRequest { public string type; public int turn; public CasePayload payload; }
        [Serializable] internal class CaseSetup { public string[] selectedCompanies; }
        [Serializable] internal class CaseExpected { public bool success; public string errorCode; }
        [Serializable] internal class ValidationCase { public string caseId; public CaseSetup setup; public CaseRequest request; public CaseExpected expected; }
        [Serializable] internal class ValidationCases { public ValidationCase[] cases; }

        public static ValidationCase[] Cases() => Load<ValidationCases>("validation_cases.json").cases;

        // ---- ServerMessage 만들기(실제 통신과 같은 모양으로 JSON 한 번 거침) ----
        public static ServerMessage Wire(ServerMessage m) => JsonUtility.FromJson<ServerMessage>(JsonUtility.ToJson(m));

        public static ServerMessage Welcome(string playerId)
            => Wire(new ServerMessage { type = ServerMessageTypes.Welcome, session = new Session { playerId = playerId } });

        public static ServerMessage State(PublicState s) => Wire(new ServerMessage { type = ServerMessageTypes.PublicState, state = s });
        public static ServerMessage View(PlayerView v) => Wire(new ServerMessage { type = ServerMessageTypes.PlayerView, playerView = v });
        public static ServerMessage Result(CommandResult r) => Wire(new ServerMessage { type = ServerMessageTypes.CommandResult, response = r });
        public static ServerMessage Board(Scoreboard b) => Wire(new ServerMessage { type = ServerMessageTypes.Scoreboard, scoreboard = b });

        public static VisibleCompany Company(PlayerView v, string id) => v.companies.First(c => c.companyId == id);
    }
}
