"""Validate fixture consistency and reference rules, not a Unity implementation."""
import copy
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def load(name):
    return json.loads((ROOT / name).read_text(encoding="utf-8"))


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def trunc_div(value, denominator):
    return (-1 if value < 0 else 1) * (abs(value) // denominator)


def validate():
    rules = load("00_rules.json")
    require(rules["rulesVersion"] == "rules-v2", "adopted rules version")
    require(rules["dateSelectionStepDays"] == 1, "daily date selection")
    require(sum(rules["normalPhaseSeconds"].values()) == 120, "normal turn fits 120 seconds")
    require(sum(rules["eventPhaseSeconds"].values()) == 120, "event turn fits 120 seconds")
    require(rules["normalPhaseSeconds"]["SELECTION"] == 8*rules["selectionSecondsPerPick"], "eight pick deadlines")
    market_file = load("B_market_server.json")
    market = {c["companyId"]: c for c in market_file["companies"]}
    ids = [c["companyId"] for c in rules["companies"]]
    require(len(ids) == len(set(ids)) == 6 and set(ids) == set(market), "six company IDs")
    for path in ROOT.glob("*.json"):
        require(load(path.name)["schemaVersion"] == "1.0", path.name + ": schema version")
    for c in market.values():
        require(len(c["dailyChangesBps"]) == 31 and c["dailyChangesBps"][0] == 0, "31 indexed days")
        require(len(set(c["publicBundleIndices"])) == 2, "two public bundles")
        require(all(0 <= b < 6 for b in c["publicBundleIndices"]), "public bundle index")

    def buy_info(setup, req):
        state = copy.deepcopy(setup)
        cid, index = req["payload"]["companyId"], req["payload"]["bundleIndex"]
        code = ""
        if req["turn"] != 1:
            code = "STALE_TURN"
        elif state["phase"] != "INFO":
            code = "WRONG_PHASE"
        elif cid not in market:
            code = "INVALID_COMPANY"
        elif not isinstance(index, int) or not 0 <= index < 6:
            code = "INVALID_BUNDLE"
        elif index in market[cid]["publicBundleIndices"] or {"companyId":cid,"bundleIndex":index} in state["purchasedBundles"]:
            code = "ALREADY_VISIBLE"
        elif state["coins"] < rules["informationBundleCost"]:
            code = "NOT_ENOUGH_COINS"
        else:
            state["coins"] -= rules["informationBundleCost"]
            state["purchasedBundles"].append({"companyId":cid,"bundleIndex":index})
        return {"success":not code,"errorCode":code,"remainingCoins":state["coins"],
                "purchasedBundleCount":len(state["purchasedBundles"])}, state

    def order_error(investments, selected):
        if len(investments) != 2 or len({i["companyId"] for i in investments}) != 2:
            return "DUPLICATE_COMPANY"
        if {i["companyId"] for i in investments} != set(selected):
            return "COMPANY_NOT_SELECTED"
        weights = [i["weightBps"] for i in investments]
        if weights[0] not in rules["firstCompanyWeightOptionsBps"] or sum(weights) != 10000:
            return "INVALID_RATIO"
        for i in investments:
            periods = i["intervals"]
            if len(periods) > rules["maxIntervalsPerCompany"]:
                return "TOO_MANY_INTERVALS"
            if not periods:
                return "MISSING_INTERVAL"
            for p in periods:
                entry, exit_day = p["entryDay"], p["exitDay"]
                if type(entry) is not int or type(exit_day) is not int or not 0 <= entry < exit_day <= rules["daysPerTurn"]:
                    return "INVALID_DAY"
                if not rules["minHoldingDays"] <= exit_day-entry <= rules["maxHoldingDays"]:
                    return "INVALID_HOLDING_DAYS"
            periods = sorted(periods, key=lambda p:p["entryDay"])
            if any(a["exitDay"] > b["entryDay"] for a,b in zip(periods,periods[1:])):
                return "OVERLAPPING_INTERVALS"
        return ""

    cases = load("validation_cases.json")["cases"]
    for case in cases:
        setup, req, expected = case["setup"], case["request"], case["expected"]
        kind = case["operation"]
        if kind.startswith("BUY_INFORMATION"):
            actual, changed = buy_info(setup, req)
            if not actual["success"]:
                require(changed == setup, case["caseId"] + ": rejected purchase mutated state")
            if kind.endswith("REPLAY"):
                # A must return its cached response before invoking C a second time.
                cache_key = (setup["senderPlayerId"], req["turn"], req["requestId"])
                cache = {cache_key:copy.deepcopy(actual)}
                replay = cache[cache_key]
                actual["sameResponse"] = replay == actual
                require(changed["coins"] == 2, "request replay charged twice")
        elif kind == "SELECT_COMPANY":
            cid = req["payload"]["companyId"]
            used = next((x["count"] for x in setup["usedSeats"] if x["companyId"] == cid),0)
            code = ""
            if setup["senderPlayerId"] != setup["currentPlayerId"]:
                code = "NOT_YOUR_TURN"
            elif cid in setup["ownCompanies"]:
                code = "DUPLICATE_COMPANY"
            elif used >= rules["maxPlayersPerCompany"]:
                code = "COMPANY_FULL"
            actual = {"success":not code,"errorCode":code,
                      "remainingSeats":rules["maxPlayersPerCompany"]-used-(0 if code else 1)}
        elif kind == "SUBMIT_INVESTMENT":
            code = order_error(req["payload"]["investments"],setup["selectedCompanies"])
            actual = {"success":not code,"errorCode":code}
        elif kind == "ADVANCE_TURN":
            next_turn = req["nextTurn"]
            grant = rules["lateCoinGrant"] if next_turn >= rules["coinGrantIncreaseTurn"] else rules["earlyCoinGrant"]
            actual = {"success":True,"errorCode":"","remainingCoins":setup["coins"] +
                      (grant if setup["lastGrantedTurn"] < next_turn else 0)}
        else:
            raise AssertionError("Unknown case: " + kind)
        require(actual == expected, case["caseId"] + ": " + repr(actual) + " != " + repr(expected))

    room = load("A_room_initial.json")
    require([p["playerId"] for p in room["players"]] == ["P1","P2","P3","P4"], "player roster")
    chosen = {p["playerId"]:[] for p in room["players"]}
    seats = {cid:2 for cid in ids}
    replay_file = load("A_selection_replay.json")
    replay = replay_file["steps"]
    deadline = replay_file["startSelectionDeadlineAtServerSeconds"]
    for index, step in enumerate(replay):
        pid, cid = step["senderPlayerId"], step["request"]["payload"]["companyId"]
        require(step["requestReceivedAtServerSeconds"] < deadline, "selection received before deadline")
        require(pid == room["selectionOrder"][index], "snake selection order")
        require(cid not in chosen[pid] and seats[cid] > 0, "invalid fixture selection")
        chosen[pid].append(cid)
        seats[cid] -= 1
        pub = step["expectedPublicUpdate"]
        require(pub["serverElapsedSeconds"] == step["requestReceivedAtServerSeconds"], "selection clock")
        deadline = pub["selectionDeadlineAtServerSeconds"]
        require(pub["companySeats"] == [{"companyId":c,"remainingSeats":seats[c]} for c in ids], "seat update")
        require(pub["selectionIndex"] == index+1, "selection index")
        require(pub["currentPlayerId"] == (room["selectionOrder"][index+1] if index < 7 else ""), "next player")
    public_after = load("D_public_after_selection.json")
    require(public_after["companySeats"] == replay[-1]["expectedPublicUpdate"]["companySeats"], "final seats")
    require(public_after["phase"] == "INVESTMENT", "selection end phase")
    require(public_after["serverElapsedSeconds"] == 74 and public_after["phaseEndsAtServerSeconds"] == 97,
            "early selection completion starts investment deadline from actual transition")

    results = {p["playerId"]:p for p in load("B_settlement_expected.json")["players"]}
    for order in load("B_investment_orders.json")["orders"]:
        pid = order["playerId"]
        require(not order_error(order["investments"],chosen[pid]), "invalid final order")
        expected = results[pid]
        profits = []
        allocated_first = trunc_div(rules["initialAssets"]*order["investments"][0]["weightBps"],10000)
        for index, i in enumerate(order["investments"]):
            rates = market[i["companyId"]]["dailyChangesBps"]
            allocated = allocated_first if index == 0 else rules["initialAssets"]-allocated_first
            rate_sum = sum(rates[p["exitDay"]]-rates[p["entryDay"]] for p in i["intervals"])
            rate_sum = max(rules["minimumCompanyReturnBps"],rate_sum)
            profits.append({"companyId":i["companyId"],"profit":trunc_div(allocated*rate_sum,10000)})
        profit = sum(c["profit"] for c in profits)
        require(profits == expected["companyProfits"], pid + ": company settlement")
        require(profit == expected["profit"] and expected["endingAssets"] == 10000+profit, pid + ": total settlement")
        require(expected["turnReturnBps"] == trunc_div(profit*10000,10000), "return rate")

    information_before = load("C_information_before.json")
    information_after = load("C_information_after.json")
    for filename, pid, state in [("D_P1_before_purchase.json","P1",information_before),
                                 ("D_P1_after_purchase.json","P1",information_after),
                                 ("D_P2_after_P1_purchase.json","P2",information_after)]:
        view = load(filename)
        owner = next(p for p in state["players"] if p["playerId"] == pid)
        require(view["recipientPlayerId"] == pid and view["coins"] == owner["coins"], "private recipient")
        require(len(view["companies"]) == 6, "view company coverage")
        require(not view["ownInvestments"], "INFO view contains investment")
        for c in view["companies"]:
            cid = c["companyId"]
            bought = {p["bundleIndex"] for p in owner["purchasedBundles"] if p["companyId"] == cid}
            allowed = set(market[cid]["publicBundleIndices"]) | bought
            require({b["bundleIndex"] for b in c["visibleBundles"]} == allowed, filename + ": leaked/missing bundle")
            for b in c["visibleBundles"]:
                start = b["bundleIndex"]*5+1
                require(b["startDay"] == start and b["dailyChangesBps"] == market[cid]["dailyChangesBps"][start:start+5], "visible daily values")
                require(b["source"] == ("PUBLIC" if b["bundleIndex"] in market[cid]["publicBundleIndices"] else "PURCHASED"), "bundle source")
    require(load("D_P1_before_purchase.json")["companies"] == load("D_P2_after_P1_purchase.json")["companies"], "P2 gained P1 purchase")

    scoreboard = load("D_scoreboard_expected.json")["rows"]
    ranking = sorted(results.values(),key=lambda p:(-p["endingAssets"],p["playerId"]))
    require([p["playerId"] for p in scoreboard] == [p["playerId"] for p in ranking], "ranking")
    for index, row in enumerate(scoreboard):
        p = results[row["playerId"]]
        require(row["rank"] == index+1 and row["totalAssets"] == p["endingAssets"], "scoreboard assets/rank")
        require(row["turnReturnBps"] == row["cumulativeReturnBps"] == p["turnReturnBps"], "scoreboard return")
        require(row["cumulativeProfit"] == p["profit"] and row["returnHistory"][-1]["returnBps"] == p["turnReturnBps"], "scoreboard history")
        require("investments" not in row and "companyProfits" not in row, "scoreboard reveals private order")
    example = load("exchange_examples.json")["examples"][0]
    require(example["recipientPlayerId"] == "P1" and not example["broadcast"], "private purchase routing")
    actual, changed = buy_info(cases[0]["setup"],example["request"])
    payload = example["response"]["payload"]
    require(payload["remainingCoins"] == actual["remainingCoins"] and payload["dailyChangesBps"] == market["AI"]["dailyChangesBps"][6:11], "purchase response")
    require(changed["coins"] == information_after["players"][0]["coins"] and changed["purchasedBundles"] == information_after["players"][0]["purchasedBundles"], "C after snapshot")
    print("PASS:",len(list(ROOT.glob('*.json'))),"JSON files;",len(cases),"reference cases; 8 selections; 4 settlements; private views and scoreboard.")
    print("This validates fixture/reference consistency. Unity and network integration must be tested separately.")


if __name__ == "__main__":
    validate()
