"""Read-only checks of the next implementation guide; does not run game code."""
import hashlib
import json
import re
from pathlib import Path
from urllib.parse import unquote

HERE = Path(__file__).resolve().parent


def require(condition, message):
    if not condition:
        raise ValueError(message)


def main():
    plan = json.loads((HERE / "다음_구현_통과기준.json").read_text(encoding="utf-8"))
    source = plan["improvementSource"]
    source_file = (HERE / source["path"]).resolve()
    require(source_file.is_file(), "Missing improvement source: " + str(source_file))
    require(hashlib.sha256(source_file.read_bytes()).hexdigest() == source["sha256"],
            "Improvement source changed: review the plan before updating its hash")

    required_docs = ["README.md", "04_현재_구현_현황.md", "05_다음_구현_계획.md", "06_다음_구현_검증.md",
                     "07_현재_구현_및_차주_계획.md"]
    required_docs += [f"{role}/다음_구현_작업지침.md" for role in "ABCD"]
    for name in required_docs:
        require((HERE / name).is_file(), "Missing document: " + name)

    cases = plan["cases"]
    ids = [case["id"] for case in cases]
    expected_ids = {f"{prefix}{n:02}" for prefix, count in
                    [("T", 4), ("G", 4), ("I", 5), ("S", 4), ("B", 7), ("R", 3), ("N", 12)]
                    for n in range(1, count + 1)}
    require(len(ids) == len(set(ids)) and set(ids) == expected_ids, "Missing/duplicate case IDs")
    for case in cases:
        for field in ["task", "owners", "setup", "action", "expected", "verification"]:
            require(bool(case.get(field)), case["id"] + ": missing " + field)
        require(set(case["owners"]) <= set("ABCD"), case["id"] + ": unknown owner")
        require(case["executionStatus"] == "NOT_RUN", case["id"] + ": keep actual results in separate logs")

    requirements = plan["requirements"]
    require({r["id"] for r in requirements} == {f"IMP-{n:02}" for n in range(1, 15)}
            and len(requirements) == 14, "Coverage must contain IMP-01..14 once")
    for requirement in requirements:
        require(bool(requirement["cases"]) and set(requirement["cases"]) <= set(ids),
                requirement["id"] + ": unknown/empty coverage")

    for name, profile in plan["timingProfiles"].items():
        total = sum(profile[key] for key in ["info", "selection", "investment", "reveal", "result"]) + profile.get("auction", 0)
        require(total == profile["maximum"], name + ": invalid timing total")
    require(plan["timingProfiles"]["nextPlay"]["info"] == 90, "Next INFO must be 90 seconds")
    require(plan["timingProfiles"]["baselineFixture"]["info"] == 50, "Keep original fixture INFO")
    require(plan["timingProfiles"]["nextAuctionProposal"]["maximum"] == 175,
            "Next auction proposal must include its 15-second auction")

    examples = plan["exampleInputs"]
    for entry, exit_ in examples["intervals"]["allowed"]:
        require(0 <= entry < exit_ <= 30 and 5 <= exit_ - entry <= 10, "Invalid allowed interval example")
    for entry, exit_ in examples["intervals"]["rejected"]:
        require(not (0 <= entry < exit_ <= 30 and 5 <= exit_ - entry <= 10), "Invalid rejected interval example")
    graph = {0: 0}
    for bundle in examples["graph"]["visibleBundles"]:
        require(bundle["startDay"] == bundle["bundleIndex"] * 5 + 1
                and len(bundle["dailyChangesBps"]) == 5, "Invalid graph bundle")
        graph.update({bundle["startDay"] + i: value for i, value in enumerate(bundle["dailyChangesBps"])})
    require(graph[10] == -4000 and graph[15] == 3000 and graph[5] == 0 and 16 not in graph,
            "Graph example differs from stated values/hidden dates")
    for seats in examples["seats"]:
        mine = int(seats["ownSelected"])
        require(seats["expected"] == {"empty": seats["remainingSeats"], "mine": mine,
                "occupiedAnonymous": 2 - seats["remainingSeats"] - mine}, "Invalid seat example")

    links = 0
    for document in HERE.rglob("*.md"):
        content = document.read_text(encoding="utf-8")
        require(not re.search(r"[\u3040-\u30ff]", content), "Unexpected text in " + str(document))
        for target in re.findall(r"\]\(([^)]+)\)", content):
            if target.startswith(("http://", "https://", "#")):
                continue
            destination = unquote(target.split("#", 1)[0]).strip("<>")
            require(not destination or (document.parent / destination).exists(),
                    "Broken link: " + str(document) + " -> " + destination)
            links += 1
    json_text = json.dumps(plan, ensure_ascii=False)
    require(not re.search(r"[\u3040-\u30ff]", json_text), "Unexpected text in next plan JSON")
    print(f"PASS: {len(requirements)} requirements, {len(cases)} NOT_RUN specifications, timing/examples/source SHA, {links} guide links.")
    print("Documentation/specification checks only; Unity/BOT/UI features were NOT executed.")


if __name__ == "__main__":
    main()
