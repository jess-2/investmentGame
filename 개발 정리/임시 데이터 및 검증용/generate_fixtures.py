"""Rebuild the shared development fixtures. Run validate_fixtures.py afterwards."""
import copy
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
VERSION = "1.0"
IDS = ["AI", "SPACE", "FOOD", "BANK", "BIO", "AUTO"]
PLAYERS = ["P1", "P2", "P3", "P4"]


def save(name, content):
    (ROOT / name).write_text(
        json.dumps({"schemaVersion": VERSION, **content}, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )


def request(kind, number, payload, turn=1):
    return {"type": kind, "requestId": number, "turn": turn, "payload": payload}


def generate():
    rules = {
        "fixtureId": "basic-turn-v2", "rulesVersion": "rules-v2", "playerCount": 4, "totalTurns": 10,
        "daysPerTurn": 30, "bundleSizeDays": 5, "initialAssets": 10000,
        "initialCoinsAfterTurnGrant": 3, "informationBundleCost": 1,
        "maxPlayersPerCompany": 2, "firstCompanyWeightOptionsBps": [5000, 6000, 7000],
        "maxIntervalsPerCompany": 2, "minHoldingDays": 5, "maxHoldingDays": 10,
        "allowOverlappingIntervals": False, "reinvestIntervalProfit": False,
        "coinGrantIncreaseTurn": 6, "earlyCoinGrant": 3, "lateCoinGrant": 5,
        "turnDurationSeconds": 120, "timerMode": "MANUAL_FOR_FIXTURE",
        "dateSelectionStepDays": 1, "minEntryDay": 0, "maxExitDay": 30,
        "secondCompanyGetsRemainder": True, "minimumCompanyReturnBps": -10000,
        "minimumAssets": 0, "eliminateBankruptPlayer": False, "comebackGrant": 1000,
        "normalPhaseSeconds": {"AUCTION":0,"INFO":50,"SELECTION":32,"INVESTMENT":23,"MARKET_REVEAL":10,"RESULT":5},
        "eventPhaseSeconds": {"AUCTION":15,"INFO":35,"SELECTION":32,"INVESTMENT":23,"MARKET_REVEAL":10,"RESULT":5},
        "selectionSecondsPerPick": 4, "defaultCompanyPriority": ["BANK","FOOD","AUTO","AI","SPACE","BIO"],
        "defaultInvestment": {"weightsBps":[5000,5000],"intervals":[{"entryDay":0,"exitDay":5}]},
        "auctionTurns": [3,6,9], "auctionBidCapBps": 1000, "freePassBidMultiplierPercent":125,
        "shopPurchasesPerTurn":1, "inventoryCapacity":3, "inventoryOverflowCoins":1,
        "shopCostBps": {"AMPLIFIER":300,"SHORT":400,"SHIELD":200,"EXPLOSION":500,"BLACKBOX":200,"DISTORT":200,"STEAL":300,"REDUCE_RETURN":400},
        "usesPerTurn": {"FINANCIAL":1,"INFORMATION":1,"ATTACK":1},
        "itemEffects": {"amplifierMultiplier":2,"shieldFlatBandBps":300,"shieldPremiumBps":500,
                        "shieldLossMultiplierBps":5000,"explosionThresholdBps":1500,"explosionBonusBps":5000,
                        "gamblerExplosionBonusBps":6000,"distortSeconds":10,"reduceReturnBps":1000,
                        "combinedAttackCapBps":1500,"hunterReductionBps":500},
        "abilityEffects": {"politicianCooldownTurns":3,"operatorTransferBps":2500,"hunterTransferBps":5000,
                           "gamblerMultiplierBps":12000,"informationBrokerFreeBundlesPerTurn":1,
                           "informationBrokerBlackboxBundles":2},
        "participantReconnectSeconds":30, "maxNewRequestsPerPlayerPerTurn":256,
        "marketGeneration": {"distribution":"UNIFORM_INTEGER_BPS","dailyStepGrowthBpsPerTurn":500,
                             "dailyStepBps":{"AI":800,"SPACE":800,"FOOD":120,"BANK":80,"BIO":1500,"AUTO":350},
                             "absoluteChangeCapBps":{"AI":5000,"SPACE":5000,"FOOD":800,"BANK":600,"BIO":8000,"AUTO":2000}},
        "rateUnit": "BASIS_POINTS", "rounding": "TOWARD_ZERO_PER_COMPANY",
        "settlementFormula": "allocatedAssets * sum(exitBps - entryBps) / 10000",
        "fixtureOnlyPolicies": [
            "Fixed player order, hand-crafted market and free-bundle positions exist only for reproducibility.",
            "MANUAL_FOR_FIXTURE pauses automatic progression; production phase durations and defaults are defined in rules-v2.",
            "No items, abilities, auctions or comeback grants are exercised by the base one-turn fixture.",
            "All other listed game settings are the adopted initial balance rules, not unresolved assumptions."
        ],
        "companies": [
            {"companyId": cid, "displayName": cid, "volatility": vol,
             "gamblerEligible": cid in {"AI", "SPACE", "BIO"}}
            for cid, vol in zip(IDS, ["HIGH", "HIGH", "LOW", "LOW", "EXTREME", "MEDIUM"])
        ],
    }
    save("00_rules.json", rules)

    # Index is the day, including the day-0 baseline. 100 bps = 1%.
    series = {
        "AI": [0,-200,-400,-600,-800,-1000,-1500,-2000,-2500,-3000,-4000,
               -2000,-500,1000,2000,3000,2500,2000,1500,1000,500,800,1100,1400,1700,2000,
               1800,1600,1400,1200,1000],
        "SPACE": [0,-100,-200,-300,-400,-500,-200,100,400,700,1000,
                  1200,1400,1600,1800,2000,1400,800,200,-400,-1000,-600,-200,200,600,1000,
                  1300,1600,1900,2200,2500],
        "FOOD": [0,20,40,60,80,100,140,180,220,260,300,280,260,240,220,200,
                 240,280,320,360,400,380,360,340,320,300,320,340,360,380,400],
        "BANK": [0,10,20,30,40,50,70,90,110,130,150,140,130,120,110,100,
                 130,160,190,220,250,240,230,220,210,200,210,220,230,240,250],
        "BIO": [0,-400,-800,-1200,-1600,-2000,-2600,-3200,-3800,-4400,-5000,
                -3200,-1400,400,2200,4000,3400,2800,2200,1600,1000,0,-1000,-2000,-3000,-4000,
                -2400,-800,800,2400,4000],
        "AUTO": [0,-40,-80,-120,-160,-200,-40,120,280,440,600,500,400,300,200,100,
                 200,300,400,500,600,500,400,300,200,100,180,260,340,420,500],
    }
    free = {"AI": [0,2], "SPACE": [0,3], "FOOD": [1,4],
            "BANK": [0,5], "BIO": [0,2], "AUTO": [1,3]}
    market = {"turn": 1, "serverOnly": True, "companies": [
        {"companyId": cid, "dailyChangesBps": series[cid], "publicBundleIndices": free[cid]}
        for cid in IDS
    ]}
    save("B_market_server.json", market)
    room = {
        "roomId": "DEV-ROOM-001", "turn": 1, "phase": "INFO",
        "hostPlayerId": "P1", "serverElapsedSeconds": 0, "phaseEndsAtServerSeconds": 50,
        "timerMode": "MANUAL_FOR_FIXTURE", "selectionIndex": 0, "selectionDeadlineAtServerSeconds":0,
        "selectionOrder": ["P1","P2","P3","P4","P4","P3","P2","P1"],
        "players": [{"playerId": p, "displayName": "Player " + p[-1],
                     "connected": True, "ready": True} for p in PLAYERS],
        "selections": [],
    }
    save("A_room_initial.json", room)
    before = {"turn": 1, "serverOnly": True, "players": [
        {"playerId": p, "coins": 3, "purchasedBundles": [], "inventory": [], "abilityId": "NONE"}
        for p in PLAYERS
    ]}
    after = copy.deepcopy(before)
    after["players"][0]["coins"] = 2
    after["players"][0]["purchasedBundles"] = [{"companyId": "AI", "bundleIndex": 1}]
    save("C_information_before.json", before)
    save("C_information_after.json", after)

    def player_view(pid, info):
        owner = next(p for p in info["players"] if p["playerId"] == pid)
        purchases = {(x["companyId"], x["bundleIndex"]) for x in owner["purchasedBundles"]}
        companies = []
        for cid in IDS:
            bundles = []
            for index in range(6):
                if index in free[cid] or (cid, index) in purchases:
                    start = index * 5 + 1
                    bundles.append({"bundleIndex": index, "startDay": start,
                                    "source": "PUBLIC" if index in free[cid] else "PURCHASED",
                                    "dailyChangesBps": series[cid][start:start+5]})
            companies.append({"companyId": cid, "visibleBundles": bundles})
        return {"turn": 1, "recipientPlayerId": pid, "assets": 10000,
                "coins": owner["coins"], "ownInvestments": [], "companies": companies}

    save("D_P1_before_purchase.json", player_view("P1", before))
    save("D_P1_after_purchase.json", player_view("P1", after))
    save("D_P2_after_P1_purchase.json", player_view("P2", after))
    public = {
        "roomId": room["roomId"], "turn": 1, "phase": "INFO", "selectionIndex": 0,
        "currentPlayerId": "", "serverElapsedSeconds": 0, "phaseEndsAtServerSeconds": 50,
        "selectionDeadlineAtServerSeconds":0,
        "players": room["players"],
        "companySeats": [{"companyId": cid, "remainingSeats": 2} for cid in IDS],
    }
    save("D_public_initial.json", public)

    picks = list(zip(room["selectionOrder"], ["AI","SPACE","BIO","AI","BIO","AUTO","BANK","FOOD"]))
    selection_steps = []
    for index, (pid, cid) in enumerate(picks):
        seat_update = [{"companyId": c, "remainingSeats": 2-sum(x[1] == c for x in picks[:index+1])}
                       for c in IDS]
        selection_steps.append({
            "senderPlayerId": pid,
            "requestReceivedAtServerSeconds":50+(index+1)*3,
            "request": request("SELECT_COMPANY", "pick-"+str(index+1), {"companyId": cid}),
            "expectedPrivateResult": {"success": True, "errorCode": "", "companyId": cid},
            "expectedPublicUpdate": {"phase": "INVESTMENT" if index == 7 else "SELECTION",
                                     "selectionIndex": index+1,
                                     "currentPlayerId": picks[index+1][0] if index < 7 else "",
                                     "serverElapsedSeconds":50+(index+1)*3,
                                     "phaseEndsAtServerSeconds":97 if index == 7 else 82,
                                     "selectionDeadlineAtServerSeconds":0 if index == 7 else 50+(index+1)*3+4,
                                     "companySeats": seat_update},
        })
    save("A_selection_replay.json", {"turn": 1, "startPhase": "SELECTION", "startServerElapsedSeconds":50,
         "startPhaseEndsAtServerSeconds":82,"startSelectionDeadlineAtServerSeconds":54,"steps": selection_steps})
    selected = copy.deepcopy(public)
    selected.update(selection_steps[-1]["expectedPublicUpdate"])
    save("D_public_after_selection.json", selected)

    def investment(cid, weight, *intervals):
        return {"companyId": cid, "weightBps": weight,
                "intervals": [{"entryDay": a, "exitDay": b} for a,b in intervals]}

    orders = {"turn": 1, "serverOnly": True, "orders": [
        {"playerId": "P1", "investments": [investment("AI",6000,(10,15)),investment("FOOD",4000,(5,10))]},
        {"playerId": "P2", "investments": [investment("SPACE",7000,(5,10)),investment("BANK",3000,(5,10))]},
        {"playerId": "P3", "investments": [investment("BIO",5000,(5,10)),investment("AUTO",5000,(5,10))]},
        {"playerId": "P4", "investments": [investment("AI",5000,(10,15),(20,25)),investment("BIO",5000,(15,20))]},
    ]}
    save("B_investment_orders.json", orders)
    # Manually calculated expected values, independent of validation arithmetic.
    expected = {"turn": 1, "serverOnly": True, "players": [
        {"playerId":"P1","startingAssets":10000,"profit":4280,"endingAssets":14280,"turnReturnBps":4280,
         "companyProfits":[{"companyId":"AI","profit":4200},{"companyId":"FOOD","profit":80}]},
        {"playerId":"P2","startingAssets":10000,"profit":1080,"endingAssets":11080,"turnReturnBps":1080,
         "companyProfits":[{"companyId":"SPACE","profit":1050},{"companyId":"BANK","profit":30}]},
        {"playerId":"P3","startingAssets":10000,"profit":-1100,"endingAssets":8900,"turnReturnBps":-1100,
         "companyProfits":[{"companyId":"BIO","profit":-1500},{"companyId":"AUTO","profit":400}]},
        {"playerId":"P4","startingAssets":10000,"profit":2750,"endingAssets":12750,"turnReturnBps":2750,
         "companyProfits":[{"companyId":"AI","profit":4250},{"companyId":"BIO","profit":-1500}]},
    ]}
    save("B_settlement_expected.json", expected)
    scoreboard = {"turn": 1, "phase": "RESULT", "rankingFinalForTurn": True, "rows": []}
    for rank, pid in enumerate(["P1","P4","P2","P3"], 1):
        p = next(x for x in expected["players"] if x["playerId"] == pid)
        scoreboard["rows"].append({"rank": rank,"playerId":pid,"totalAssets":p["endingAssets"],
                                   "turnReturnBps":p["turnReturnBps"],"cumulativeProfit":p["profit"],
                                   "cumulativeReturnBps":p["turnReturnBps"],
                                   "returnHistory":[{"turn":0,"returnBps":0},{"turn":1,"returnBps":p["turnReturnBps"]}]})
    save("D_scoreboard_expected.json", scoreboard)
    info_req = request("BUY_INFORMATION", "info-001", {"companyId":"AI","bundleIndex":1})
    save("exchange_examples.json", {"transportMetadataIsNotRequestPayload": True, "examples": [
        {"senderPlayerId":"P1", "request":info_req,
         "recipientPlayerId":"P1", "response":{"type":"BUY_INFORMATION_RESULT","requestId":"info-001","turn":1,
             "success":True,"errorCode":"","payload":{"remainingCoins":2,"companyId":"AI","bundleIndex":1,
             "startDay":6,"dailyChangesBps":series["AI"][6:11]}},
         "broadcast":False},
        {"senderPlayerId":"P1", "request":request("SUBMIT_INVESTMENT","order-001",{"investments":orders["orders"][0]["investments"]}),
         "recipientPlayerId":"P1", "response":{"type":"SUBMIT_INVESTMENT_RESULT","requestId":"order-001","turn":1,
             "success":True,"errorCode":"","payload":{"locked":True,"investments":orders["orders"][0]["investments"]}},
         "broadcast":False},
    ]})

    cases = []
    def case(cid, operation, setup, req, success, code, **extra):
        cases.append({"caseId":cid,"operation":operation,"setup":setup,"request":req,
                      "expected":{"success":success,"errorCode":code,**extra}})
    buy = {"senderPlayerId":"P1","phase":"INFO","coins":3,"purchasedBundles":[]}
    case("buy-success","BUY_INFORMATION",buy,info_req,True,"",remainingCoins=2,purchasedBundleCount=1)
    case("buy-no-coins","BUY_INFORMATION",{**buy,"coins":0},info_req,False,"NOT_ENOUGH_COINS",remainingCoins=0,purchasedBundleCount=0)
    case("buy-already-owned","BUY_INFORMATION",{**buy,"coins":2,"purchasedBundles":[{"companyId":"AI","bundleIndex":1}]},info_req,False,"ALREADY_VISIBLE",remainingCoins=2,purchasedBundleCount=1)
    case("buy-public","BUY_INFORMATION",buy,request("BUY_INFORMATION","info-public",{"companyId":"AI","bundleIndex":0}),False,"ALREADY_VISIBLE",remainingCoins=3,purchasedBundleCount=0)
    case("buy-invalid-bundle","BUY_INFORMATION",buy,request("BUY_INFORMATION","info-invalid",{"companyId":"AI","bundleIndex":6}),False,"INVALID_BUNDLE",remainingCoins=3,purchasedBundleCount=0)
    case("buy-invalid-company","BUY_INFORMATION",buy,request("BUY_INFORMATION","info-company",{"companyId":"UNKNOWN","bundleIndex":1}),False,"INVALID_COMPANY",remainingCoins=3,purchasedBundleCount=0)
    case("buy-wrong-phase","BUY_INFORMATION",{**buy,"phase":"RESULT"},info_req,False,"WRONG_PHASE",remainingCoins=3,purchasedBundleCount=0)
    case("buy-stale-turn","BUY_INFORMATION",buy,{**info_req,"turn":0},False,"STALE_TURN",remainingCoins=3,purchasedBundleCount=0)
    case("buy-request-replay","BUY_INFORMATION_REPLAY",buy,info_req,True,"",remainingCoins=2,purchasedBundleCount=1,sameResponse=True)
    pick_setup={"senderPlayerId":"P1","phase":"SELECTION","currentPlayerId":"P1","ownCompanies":[],"usedSeats":[]}
    pick_req=request("SELECT_COMPANY","select-test",{"companyId":"AI"})
    case("pick-success","SELECT_COMPANY",pick_setup,pick_req,True,"",remainingSeats=1)
    case("pick-full","SELECT_COMPANY",{**pick_setup,"usedSeats":[{"companyId":"AI","count":2}]},pick_req,False,"COMPANY_FULL",remainingSeats=0)
    case("pick-duplicate-company","SELECT_COMPANY",{**pick_setup,"ownCompanies":["AI"],"usedSeats":[{"companyId":"AI","count":1}]},pick_req,False,"DUPLICATE_COMPANY",remainingSeats=1)
    case("pick-wrong-player","SELECT_COMPANY",{**pick_setup,"senderPlayerId":"P2"},pick_req,False,"NOT_YOUR_TURN",remainingSeats=2)
    order_setup={"senderPlayerId":"P1","phase":"INVESTMENT","selectedCompanies":["AI","FOOD"]}
    base_order=orders["orders"][0]["investments"]
    def order_case(cid, investments, code):
        case(cid,"SUBMIT_INVESTMENT",order_setup,request("SUBMIT_INVESTMENT",cid,{"investments":investments}),not code,code)
    order_case("order-success",base_order,"")
    bad=copy.deepcopy(base_order);bad[0]["intervals"]=[{"entryDay":10,"exitDay":13}]
    order_case("order-too-short",bad,"INVALID_HOLDING_DAYS")
    bad=copy.deepcopy(base_order);bad[0]["intervals"]=[{"entryDay":0,"exitDay":5},{"entryDay":5,"exitDay":10},{"entryDay":10,"exitDay":15}]
    order_case("order-too-many-intervals",bad,"TOO_MANY_INTERVALS")
    bad=copy.deepcopy(base_order);bad[0]["intervals"]=[{"entryDay":5,"exitDay":15},{"entryDay":10,"exitDay":20}]
    order_case("order-overlap",bad,"OVERLAPPING_INTERVALS")
    bad=copy.deepcopy(base_order);bad[0]["weightBps"]=8000;bad[1]["weightBps"]=2000
    order_case("order-invalid-ratio",bad,"INVALID_RATIO")
    bad=copy.deepcopy(base_order);bad[0]["intervals"]=[{"entryDay":25,"exitDay":31}]
    order_case("order-day-out-of-range",bad,"INVALID_DAY")
    bad=copy.deepcopy(base_order);bad[1]["companyId"]="BANK"
    order_case("order-unselected-company",bad,"COMPANY_NOT_SELECTED")
    # User-confirmed daily entries and inclusive five-to-ten-day holding period.
    for cid, entry, exit_day, code in [
        ("order-daily-3-to-8",3,8,""),
        ("order-daily-3-to-13",3,13,""),
        ("order-daily-3-to-7",3,7,"INVALID_HOLDING_DAYS"),
        ("order-daily-3-to-14",3,14,"INVALID_HOLDING_DAYS"),
        ("order-daily-25-to-30",25,30,""),
        ("order-daily-26-to-31",26,31,"INVALID_DAY"),
        ("order-day-zero-entry",0,5,""),
        ("order-noninteger-day",3.5,8.5,"INVALID_DAY"),
        ("order-exit-before-entry",8,3,"INVALID_DAY"),
    ]:
        bad=copy.deepcopy(base_order);bad[0]["intervals"]=[{"entryDay":entry,"exitDay":exit_day}]
        order_case(cid,bad,code)
    bad=copy.deepcopy(base_order);bad[0]["intervals"]=[]
    order_case("order-missing-interval",bad,"MISSING_INTERVAL")
    bad=copy.deepcopy(base_order);bad[0]["intervals"]=[{"entryDay":3,"exitDay":8},{"entryDay":8,"exitDay":13}]
    order_case("order-touching-intervals",bad,"")
    case("coin-carry-turn-2","ADVANCE_TURN",{"previousTurn":1,"coins":2,"lastGrantedTurn":1},
         {"nextTurn":2},True,"",remainingCoins=5)
    case("coin-grant-switch-turn-6","ADVANCE_TURN",{"previousTurn":5,"coins":2,"lastGrantedTurn":5},
         {"nextTurn":6},True,"",remainingCoins=7)
    case("coin-grant-replay","ADVANCE_TURN",{"previousTurn":1,"coins":5,"lastGrantedTurn":2},
         {"nextTurn":2},True,"",remainingCoins=5)
    save("validation_cases.json", {"independentCases":True,"cases":cases})
    print("Generated", len(list(ROOT.glob('*.json'))), "JSON fixtures in", ROOT)


if __name__ == "__main__":
    generate()
