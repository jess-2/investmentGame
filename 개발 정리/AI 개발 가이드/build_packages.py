"""Extract role-specific AI inputs/expectations from existing fixtures.

--check is read-only. --write replaces only generated role JSON and source_snapshot.json.
Neither mode executes Unity code or validates a live network.
"""
import argparse
import copy
import hashlib
import json
import re
from pathlib import Path
from urllib.parse import unquote

HERE = Path(__file__).resolve().parent
WORKSPACE = HERE.parents[1]
FIXTURES = HERE.parent / "임시 데이터 및 검증용"
PACKAGE_VERSION = "prototype-v2"
PROCESS_DOC = WORKSPACE / "개발 정리/참고사항/전체 프로세스 정리.md"
SOURCES = [
    PROCESS_DOC,
    WORKSPACE / "7주차 발표정리/7주차 정리.md",
    WORKSPACE / "요구사항명세서/요구사항명세서.MD",
    WORKSPACE / "7주차 발표정리/7주차 발표.pptx",
]
COMMON_DOCS = ["README.md", "00_공통_맥락.md", "01_공통_연결_규약.md",
               "02_결정_사항.md", "03_GitHub_병합_지침.md"]


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def record(filename, data=None):
    path = FIXTURES / filename
    return {"sourcePath": path.relative_to(WORKSPACE).as_posix(),
            "sourceSha256": sha(path), "data": load(path) if data is None else data}


def gate(check_id, purpose, setup, action, expected, verification="실제 C# 구현에 입력하여 검증"):
    return {"checkId": check_id, "purpose": purpose, "setup": setup,
            "action": action, "expected": expected, "verification": verification,
            "executionStatus": "NOT_RUN_ON_UNITY_IMPLEMENTATION"}


def extra_checks(role):
    """Concrete additional implementation checks; not assertions of existing behavior."""
    checks = {
        "A": [
            gate("A-connect", "4인 접속과 정원", "실제 방 생성, 아직 참가자 없음", "방장 포함 4명이 참가하고 추가 1명이 참가 시도", {"connectedPlayerCount":4,"extraPlayerRejected":True,"samePublicRoster":True}, "실제 Unity 멀티 인스턴스/실기기 접속"),
            gate("A-routing", "구매 결과 개인 전송", "P1~P4 연결, P1 코인3개, INFO", "P1이 info-001 구매", {"recipientPlayerIds":["P1"],"P1Coins":2,"otherPlayerCoins":[3,3,3],"P2KnowsAiBundle1":False}, "실제 멀티 수신 로그와 개인 화면 비교"),
            gate("A-host-ui", "방장도 개인뷰 사용", "P1 방장, 미구매 AI 1번 묶음", "P1 구매 전후 화면 조회", {"beforeVisible":False,"afterVisible":True,"serverRulesCalled":True}, "호스트 화면·서버 호출 기록"),
            gate("A-identity", "접속 신원 사용", "전송 연결은 P2, 임의 추가 필드가 P1을 주장", "정보 구매 요청", "P1의 코인/구매기록은 변하지 않고 실제 P2로 처리하거나 잘못된 요청으로 거절", "실제 RPC/라우팅 테스트"),
            gate("A-request-conflict", "동일 키의 다른 요청 차단", "info-001 최초처리 완료", "같은 키로 다른 기업 payload 제출", "별도 합의 오류로 거절, 추가 차감/구매 없음", "구현 테스트; 새 오류코드는 공통 계약에 추가"),
            gate("A-concurrent-purchase", "코인 경합 직렬 처리", "P1 코인1개, 미열람 묶음2개", "서로 다른 requestId 구매2개를 동시에 전송", {"successCount":1,"failureCount":1,"remainingCoins":0,"purchasedBundleCount":1}, "실제 서비스 및 네트워크 경합 테스트"),
            gate("A-once-settlement", "정산 중복 적용 방지", "기본4명 주문 확정", "같은1턴 정산을2회 호출", {"assets":{"P1":14280,"P2":11080,"P3":8900,"P4":12750},"appliedCount":1}, "B와 통합 실행"),
            gate("A-disconnect", "호스트 종료 정책", "4명 플레이 중", "P1 호스트 프로세스 종료", "남은 참가자에게 연결 종료 안내, 정상 진행 중인 것처럼 남지 않음; 호스트 이전은 제외", "실기기 또는 독립 프로세스"),
            gate("A-stage-and-time", "최대120초와 자동선택", "수동 또는 실제시계 모드 명시", "단계 전환·전원 완료·마감 실행", "일반 INFO50/선택32/주문23/공개10/결과5초. 입찰턴은 INFO35+입찰15초. 선택초과는 BANK 우선의 가능한 기업, 주문초과는50:50/0~5일. 수동모드는 자동진행만 정지", "실제 서버 타이머와 기본 행동 통합"),
        ],
        "B": [
            gate("B-market-shape", "기업/날짜 단위", "원본 시장 로드", "기업별 조회", {"companyCount":6,"valuesPerCompany":31,"day0":0,"publicBundlesPerCompany":2,"aiDay10":-4000,"aiDay15":3000}),
            gate("B-settlement", "전체 정산", "기본4명 주문", "1턴 정산", {"endingAssets":{"P1":14280,"P2":11080,"P3":8900,"P4":12750},"rankOrder":["P1","P4","P2","P3"]}),
            gate("B-no-partial-mutation", "거절 시 원장 유지", "정원 마감 또는 잘못된 주문", "실패 요청을 적용", "선택원장·주문·자산 모두 요청 전과 동일; 차례도 A가 전진하지 않음"),
            gate("B-rounding", "음수0방향 버림", {"allocatedAssets":101,"rateBps":-150}, "기업 손익 계산", {"profit":-1}),
            gate("B-multi-turn", "누적수익률은 합산 아님", {"gameInitialAssets":10000,"turn1Assets":11000,"turn2Assets":12100}, "2턴 성과 계산", {"turn2ReturnBps":1000,"cumulativeReturnBps":2100}),
            gate("B-ranking-tie", "공동순위", [{"playerId":"P1","assets":10000},{"playerId":"P2","assets":10000},{"playerId":"P3","assets":9000},{"playerId":"P4","assets":8000}], "총자산 정렬", [{"playerId":"P1","rank":1},{"playerId":"P2","rank":1},{"playerId":"P3","rank":3},{"playerId":"P4","rank":4}]),
            gate("B-lock-and-replay", "확정 뒤 변경/재정산 차단", "시장 공개/정산 뒤", "주문 변경과 동일 턴 정산 재호출", "주문은 잠김 유지, 자산은 동일. 합의된 오류/기존 결과 사용"),
            gate("B-empty-interval", "구간누락 방어", "두 기업 중 하나의 intervals가 빈 배열", "주문 제출", "MISSING_INTERVAL로 거절, 상태불변"),
            gate("B-daily-dates", "사용자 확정 날짜", "선택 완료된 기업2개", "각 날짜구간 주문 제출", {"allowed":[[3,8],[3,13],[25,30],[0,5]],"rejected":[[3,7],[3,14],[26,31],[3.5,8.5]]}),
            gate("B-remainder", "잔돈 보존", {"investableAssets":10001,"firstWeightBps":6000}, "기업 원금 배분", {"first":6000,"second":4001}),
        ],
        "C": [
            gate("C-filter-views", "개인 정보 분리", "P1만 AI bundle1 구매", "P1/P2뷰 생성", {"P1Coins":2,"P2Coins":3,"P1AiBundleIndices":[0,1,2],"P2AiBundleIndices":[0,2]}),
            gate("C-atomic-failure", "원본 조회 실패시 무변경", "코인3개, 미구매, 시장 조회가 실패하도록 주입", "구매 처리", {"coins":3,"purchasedBundleCount":0}),
            gate("C-immutable-market", "원본 불변성", "개인뷰 생성 완료", "반환된 배열을 수정", "B 원본 주가와 타인뷰는 변하지 않음"),
            gate("C-turn-reset", "턴별 구매원장", "1턴 P1이 AI bundle1 구매", "2턴으로 이동 후 개인뷰 생성", "코인은 이월+지급, 1턴 구매권으로2턴 비공개 AI bundle1을 보지 못함"),
            gate("C-grant-initialized", "로드시 중복 지급 방지", "C_information_before는이미1턴 지급완료", "1턴 GrantTurnCoins 재호출", {"coinsPerPlayer":3}),
            gate("C-concurrent-buy", "잔액1개 경합", "코인1개, 미열람2묶음", "A를 통해 구매2개 동시처리", {"successCount":1,"purchasedBundleCount":1,"remainingCoins":0}, "A와 직렬 처리 통합"),
            gate("C-shared-public", "동일 무료정보", "시장 시작·전원 미구매", "4명 개인뷰 생성", "각 기업의PUBLIC2묶음 인덱스와 값이 전원 동일; 개인별재추첨없음"),
            gate("C-no-full-market", "반환 공개범위", "미구매 상태", "개인뷰/실패응답 생성", "개인뷰에는 허용된 묶음만, 실패응답에는 미공개 주가 없음"),
        ],
        "D": [
            gate("D-purchase-transition", "응답으로 화면 갱신", "P1 구매전 개인뷰", "구매성공 이벤트 주입", {"coins":2,"aiVisibleBundleIndices":[0,1,2],"aiDay10Percent":-40}, "Unity 화면/PlayMode 테스트"),
            gate("D-hidden-gap", "미열람표시", "P2 비교뷰", "AI 그래프·툴팁·축 표시", "6~10일 실제값/0% 대체표시 없음, 숨김구간 실제선 연결 없음, 축은 비공개 원본을 읽지 않음", "Unity 화면 및 데이터 접근 검토"),
            gate("D-error-state", "실패시 표시/상태 유지", "코인0개 또는이미구매", "각 오류응답 주입", "NOT_ENOUGH_COINS 또는 ALREADY_VISIBLE 안내, 기존 잔액·정보 유지, 대기표시 해제", "Unity UI 테스트"),
            gate("D-response-replay", "성공 응답 중복 적용", "성공 info-001 수신", "같은 응답을한번더수신", {"coins":2,"aiBundle1Count":1}, "Unity UI 테스트"),
            gate("D-stale-response", "늦은응답", "화면은2턴", "1턴 응답 도착", "2턴 코인·주가·선택 상태를1턴 응답으로 덮어쓰지 않음", "Unity UI 테스트"),
            gate("D-seats", "차례별잔석", "공개초기상태", "8회선택 publicUpdate 주입", {"remainingSeats":{"AI":0,"SPACE":1,"FOOD":1,"BANK":1,"BIO":0,"AUTO":1},"phase":"INVESTMENT"}, "Unity UI 테스트"),
            gate("D-scoreboard", "금액과퍼센트", "정산성적주입", "전광판표시", {"rankOrder":["P1","P4","P2","P3"],"P1Assets":14280,"P1ReturnPercent":42.8,"P3ReturnPercent":-11}, "Unity 화면 확인"),
            gate("D-real-service", "Mock교체", "같은 UI와 요청DTO", "A의 실제통신 공급자로 교체", "화면을전면재작성하지않고 요청/응답 연결; 호스트도같은경로", "4인 네트워크 통합"),
            gate("D-timer", "시간표시만담당", "A의현재단계·서버경과/마감시간", "잔여시간·완료버튼표시", "D는단계/정산을임의전진하지않고완료의도만전송", "Unity UI와 A 통합"),
        ],
    }
    return checks[role]


def build():
    original = {p.name:load(p) for p in FIXTURES.glob("*.json")}
    all_cases = original["validation_cases.json"]["cases"]
    case_ids = [c["caseId"] for c in all_cases]
    if len(case_ids) != len(set(case_ids)):
        raise ValueError("Duplicate source case IDs")
    buy_ids = [c["caseId"] for c in all_cases if c["operation"].startswith("BUY_INFORMATION")]
    coin_ids = [c["caseId"] for c in all_cases if c["operation"] == "ADVANCE_TURN"]
    trade_ids = [c["caseId"] for c in all_cases if c["operation"] in {"SELECT_COMPANY","SUBMIT_INVESTMENT"}]
    order_ids = [c["caseId"] for c in all_cases if c["operation"] == "SUBMIT_INVESTMENT"]
    role_cases = {
        "A":["buy-wrong-phase","buy-stale-turn","buy-request-replay","pick-wrong-player","coin-grant-replay"],
        "B":trade_ids,
        "C":buy_ids+coin_ids,
        "D":buy_ids+order_ids,
    }
    input_files = {
        "A":["00_rules.json","A_room_initial.json"],
        "B":["00_rules.json","B_market_server.json","B_investment_orders.json"],
        "C":["00_rules.json","B_market_server.json","C_information_before.json"],
        "D":["00_rules.json","D_public_initial.json","D_P1_before_purchase.json","D_P2_after_P1_purchase.json"],
    }
    output_files = {
        "A":["D_public_after_selection.json","D_scoreboard_expected.json"],
        "B":["B_settlement_expected.json","D_scoreboard_expected.json","D_public_after_selection.json"],
        "C":["C_information_after.json","D_P1_before_purchase.json","D_P1_after_purchase.json","D_P2_after_P1_purchase.json"],
        "D":["D_P1_after_purchase.json","D_P2_after_P1_purchase.json","D_public_after_selection.json","D_scoreboard_expected.json"],
    }
    products = {}
    for role in "ABCD":
        matching = [c for c in all_cases if c["caseId"] in role_cases[role]]
        data = {"packageVersion":PACKAGE_VERSION,"schemaVersion":"1.0","role":role,
                "fixtureId":"basic-turn-v2","rulesVersion":"rules-v2","sourcePathBase":"workspace-root",
                "instructions":"inputs는 실제 입력, referenceCases는 독립 초기 상태와 요청이다. 기대값은 통과기준.json에 있다.",
                "inputs":{n:record(n) for n in input_files[role]},
                "referenceCases":[{k:copy.deepcopy(c[k]) for k in ("caseId","operation","setup","request")} for c in matching]}
        expected = {"packageVersion":PACKAGE_VERSION,"schemaVersion":"1.0","role":role,
                    "fixtureId":"basic-turn-v2","rulesVersion":"rules-v2","sourcePathBase":"workspace-root",
                    "validationScope":"정답 데이터와 앞으로 실행할 점검표다. 실제 Unity/C# 통과 선언이 아니다.",
                    "expectedSnapshots":{n:record(n) for n in output_files[role]},
                    "referenceCases":[{"caseId":c["caseId"],"expected":copy.deepcopy(c["expected"])} for c in matching],
                    "integrationChecks":extra_checks(role)}
        selection = original["A_selection_replay.json"]
        exchanges = original["exchange_examples.json"]
        if role in "ABD":
            data["selectionReplay"] = record("A_selection_replay.json", {
                "turn":selection["turn"],"startPhase":selection["startPhase"],
                "startServerElapsedSeconds":selection["startServerElapsedSeconds"],
                "startPhaseEndsAtServerSeconds":selection["startPhaseEndsAtServerSeconds"],
                "startSelectionDeadlineAtServerSeconds":selection["startSelectionDeadlineAtServerSeconds"],
                "steps":[{"senderPlayerId":s["senderPlayerId"],"requestReceivedAtServerSeconds":s["requestReceivedAtServerSeconds"],
                          "request":s["request"]} for s in selection["steps"]]})
            expected["selectionResults"] = record("A_selection_replay.json", {
                "steps":[{"requestId":s["request"]["requestId"],"expectedPrivateResult":s["expectedPrivateResult"],
                          "expectedPublicUpdate":s["expectedPublicUpdate"]} for s in selection["steps"]]})
        if role in "ACD":
            role_exchanges = exchanges["examples"][:1] if role == "C" else exchanges["examples"]
            data["testActions"] = record("exchange_examples.json", {
                "examples":[{"senderPlayerId":e["senderPlayerId"],"request":e["request"]} for e in role_exchanges]})
            expected["exchangeResults"] = record("exchange_examples.json", {
                "examples":[{"requestId":e["request"]["requestId"],"recipientPlayerId":e["recipientPlayerId"],
                             "response":e["response"],"broadcast":e["broadcast"]} for e in role_exchanges]})
        if role == "D":
            data["incomingEvents"] = {
                "purchaseResult":copy.deepcopy(exchanges["examples"][0]["response"]),
                "investmentResult":copy.deepcopy(exchanges["examples"][1]["response"]),
                "selectionUpdates":[copy.deepcopy(s["expectedPublicUpdate"]) for s in selection["steps"]],
                "scoreboard":copy.deepcopy(original["D_scoreboard_expected.json"])}
        products[HERE/role/"임시데이터.json"] = data
        products[HERE/role/"통과기준.json"] = expected
    # Track original materials and fixture provenance, without modifying any of them.
    tracked = SOURCES + sorted(FIXTURES.glob("*.json")) + [FIXTURES/"generate_fixtures.py",FIXTURES/"validate_fixtures.py"]
    products[HERE/"source_snapshot.json"] = {
        "packageVersion":PACKAGE_VERSION,"rulesVersion":"rules-v2","sourceDate":"2026-10-25",
        "note":"내용이 달라지면 재검토한다. 이 SHA는 파일 출처 검사용이며 실제 네트워크 프로토콜 버전이 아니다.",
        "sources":[{"sourcePath":p.relative_to(WORKSPACE).as_posix(),"sha256":sha(p),
                    "kind":"planning-source" if p in SOURCES else "fixture-source"} for p in tracked],
        "coverage":{r:role_cases[r] for r in "ABCD"}}
    covered = set().union(*(set(ids) for ids in role_cases.values()))
    if covered != set(case_ids):
        raise ValueError("Unassigned or unknown reference cases")
    return products


def check_docs():
    for name in COMMON_DOCS:
        if not (HERE/name).is_file():
            raise ValueError("Missing common document: " + name)
    for role in "ABCD":
        if not (HERE/role/"작업지침.md").is_file():
            raise ValueError("Missing role instructions: " + role)
    count = 0
    for p in sorted(HERE.parent.rglob("*.md")):
        text = p.read_text(encoding="utf-8")
        if re.search("[\u3040-\u30ff]",text):
            raise ValueError("Unexpected Japanese text in Korean guide: " + str(p))
        for target in re.findall(r"\]\(([^)]+)\)",text):
            if target.startswith(("http://","https://","#")):
                continue
            dest = unquote(target.split("#",1)[0]).strip("<>")
            if dest and not (p.parent/dest).exists():
                raise ValueError("Broken link in " + str(p) + ": " + dest)
            count += 1
    return count


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--check", action="store_true")
    mode.add_argument("--write", action="store_true")
    args = parser.parse_args()
    products = build()
    if args.write:
        for p,data in products.items():
            p.parent.mkdir(parents=True,exist_ok=True)
            p.write_text(json.dumps(data,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        print("WROTE:",len(products),"generated package JSON files. Original sources were not modified.")
    else:
        failures = [str(p.relative_to(HERE)) for p,data in products.items() if not p.exists() or load(p) != data]
        if failures:
            raise SystemExit("STALE OR MISSING: " + ", ".join(failures) + ". Review changed sources before --write.")
        links = check_docs()
        for role in "ABCD":
            data = load(HERE/role/"임시데이터.json")
            expected = load(HERE/role/"통과기준.json")
            if [c["caseId"] for c in data["referenceCases"]] != [c["caseId"] for c in expected["referenceCases"]]:
                raise ValueError("Input/expectation case mismatch: " + role)
            print(role + ":",len(data["referenceCases"]),"reference cases;",len(expected["integrationChecks"]),"implementation checks")
        count = len(load(FIXTURES/"validation_cases.json")["cases"])
        print("PASS: package snapshots match source data; all",count,"source cases assigned;",links,"local document links checked.")
        print("Unity/C# execution and live networking have NOT been run by this command.")


if __name__ == "__main__":
    main()
