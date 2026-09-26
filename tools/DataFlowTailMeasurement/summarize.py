"""Audit saved same-run measurements and export raw tables without rerunning analysis."""
import csv
import hashlib
import json
import statistics
import sys
from pathlib import Path


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def audit(directory):
    root = Path(directory)
    rows = [json.loads(line) for line in (root / "raw.jsonl").read_text().splitlines()]
    summary = json.loads((root / "summary.json").read_text())
    process = json.loads((root / "process-result.json").read_text())
    environment = json.loads((root / "environment.json").read_text())
    assert len(rows) == 21 and summary["Status"] == "Complete"
    assert process["ExitCode"] == 0 and process["SourceUnchanged"]
    assert process["ProcessElapsedMs"] < 45000
    assert all(row["SampleElapsedMs"] < 5000 for row in rows)
    assert not list(root.glob("*timeout.json")) and not (root / "failure.json").exists()
    expected = [("Sparse", "Detailed", round_, 1) for round_ in range(4)]
    modes = ["Off", "Coarse", "Detailed"]
    expected += [("Collision", mode, 0, 1) for mode in modes]
    expected += [("Collision", modes[(position + round_ - 1) % 3], round_, 1)
                 for round_ in range(1, 4) for position in range(3)]
    expected += [("JoinLoop", "Detailed", round_, 1) for round_ in range(4)]
    expected += [("Sparse", "Detailed", 4, 2)]
    assert [(r["Fixture"], r["Mode"], r["Round"], r["Dop"]) for r in rows] == expected
    baseline = {}
    counters = {}
    exported = []
    maximum_residual = 0
    categories = ["Location", "Root", "Base", "BasePath"]
    for row in rows:
        d = row["Diagnostic"]
        c = d["Counters"]
        assert d["Frequency"] == environment["Frequency"]
        assert row["Equivalent"] and row["CoveragePassed"] and row["TimingValid"]
        assert d["ExitReason"] == "Complete" and d["Metrics"]["OverflowReason"] == "None"
        assert d["InputHash"] == sha((root / (row["Fixture"] + ".cs")).read_bytes())
        graph = (root / (row["RunId"] + ".graph.json")).read_bytes()
        publication = (root / (row["RunId"] + ".publication.json")).read_bytes()
        assert sha(graph) == row["NormalizedGraphHash"]
        assert sha(publication) == row["PublicationHash"]
        assert len(json.loads(graph)["Nodes"]) == row["Nodes"]
        assert len(json.loads(graph)["Edges"]) == row["Edges"]
        assert len(json.loads(publication)) == d["Metrics"]["UniqueCandidateCount"]
        current = (graph, publication)
        if row["Fixture"] in baseline:
            assert current == baseline[row["Fixture"]]
        baseline[row["Fixture"]] = current
        residual = d["MethodTotalTicks"] - sum(d["PhaseTicks"].values())
        candidate_residual = d["PhaseTicks"]["CandidateLoop"] - sum(d["DetailTicks"].values())
        assert residual >= 0 and candidate_residual >= 0
        assert all(value >= 0 for value in c.values())
        if row["Mode"] != "Off":
            maximum_residual = max(maximum_residual, residual / d["MethodTotalTicks"])
        if row["Mode"] == "Detailed":
            if row["Fixture"] in counters:
                assert c == counters[row["Fixture"]]
            counters[row["Fixture"]] = c
            for category in categories:
                assert c[category + "Contains"] == c[category + "Postings"]
                assert c[category + "Postings"] == (c[category + "Unreachable"] +
                                                    c[category + "SeenChecks"])
                assert c[category + "SeenChecks"] == (c[category + "SeenRejects"] +
                                                       c[category + "Matches"])
                assert c[category + "Queries"] >= c[category + "Hits"]
            assert sum(c[name + "Matches"] for name in categories) == c["ReturnedCandidates"]
            assert sum(c[name + "SeenRejects"] for name in categories) == c["SeenRejects"]
            assert c["TouchedMarks"] == c["TouchedClears"] == c["ReturnedCandidates"]
            assert c["RawCandidates"] == c["UniqueCandidates"] + c["DuplicateCandidates"]
            assert c["CandidateLoopRaw"] + c["ExplicitRaw"] + c["ReturnRaw"] == c["RawCandidates"]
            assert c["CandidateLoopUnique"] + c["ExplicitUnique"] + c["ReturnUnique"] == c["UniqueCandidates"]
            assert c["MaterializedCandidates"] == c["UniqueCandidates"]
            assert c["FactsMatchCalls"] == c["IndexedFactsMatchCalls"] + c["FallbackFactsMatchCalls"]
            assert c["FactsMatchTrue"] == c["IndexedCollectorCalls"] + c["FallbackCollectorCalls"]
            assert c["Dequeues"] == d["Metrics"]["WorklistIterations"] == c["Enqueues"]
        else:
            assert all(value == 0 for value in c.values())
            assert all(value == 0 for value in d["DetailTicks"].values())
        exported.append({
            **{key: row[key] for key in ["RunId", "Fixture", "Mode", "Round", "Dop", "Warmup",
                                        "BuildElapsedMs", "SampleElapsedMs", "Nodes", "Edges",
                                        "DataFlowEdges", "GraphSnapshotVersion", "NormalizedGraphHash",
                                        "PublicationHash", "Equivalent", "CoveragePassed", "TimingValid"]},
            **{key: d[key] for key in ["InputHash", "Document", "MethodSignature", "SpanStart",
                                      "SpanEnd", "StableOrder", "Frequency", "MethodTotalTicks",
                                      "QueueWaitTicks", "CommitTicks", "ExitReason"]},
            **{name + "Ticks": value for name, value in d["PhaseTicks"].items()},
            **{"Detail" + name + "Ticks": value for name, value in d["DetailTicks"].items()},
            "UnattributedTicks": residual, "CandidateLoopResidualTicks": candidate_residual,
            **c,
        })
    with (root / "measurements.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(exported[0]))
        writer.writeheader()
        writer.writerows(exported)
    representatives = []
    for fixture in ["Sparse", "Collision", "JoinLoop"]:
        selected = sorted((r for r in rows if r["Fixture"] == fixture and r["Mode"] == "Detailed"
                           and 0 < r["Round"] < 4), key=lambda r: r["Diagnostic"]["MethodTotalTicks"])[1]
        d = selected["Diagnostic"]
        c = d["Counters"]
        posting = sum(c[name + "Postings"] for name in categories)
        seen_checks = sum(c[name + "SeenChecks"] for name in categories)
        def ratio(numerator, denominator):
            return numerator / denominator if denominator else None
        representatives.append({
            "Fixture": fixture, "RunId": selected["RunId"],
            "TotalMs": d["MethodTotalTicks"] * 1000 / d["Frequency"],
            "PhaseMs": {key: value * 1000 / d["Frequency"] for key, value in d["PhaseTicks"].items()},
            "PhasePercent": {key: value * 100 / d["MethodTotalTicks"] for key, value in d["PhaseTicks"].items()},
            "DetailMs": {key: value * 1000 / d["Frequency"] for key, value in d["DetailTicks"].items()},
            "Posting": posting, "PostingPerUse": ratio(posting, c["UsedFactVisits"]),
            "PostingPerReturnedCandidate": ratio(posting, c["ReturnedCandidates"]),
            "SeenRejectRate": ratio(c["SeenRejects"], seen_checks),
            "UnreachableRate": ratio(sum(c[name + "Unreachable"] for name in categories), posting),
            "MatchPassRate": ratio(c["FactsMatchTrue"], c["FactsMatchCalls"]),
            "NsPerActualFactsMatch_RawUnqualified": ratio(
                d["DetailTicks"]["FactsMatch"] * 1e9 / d["Frequency"], c["FactsMatchCalls"]),
            "UnattributedMs": (d["MethodTotalTicks"] - sum(d["PhaseTicks"].values())) * 1000 / d["Frequency"],
            "CandidateLoopResidualMs": (d["PhaseTicks"]["CandidateLoop"] -
                                        sum(d["DetailTicks"].values())) * 1000 / d["Frequency"],
            "QueueWaitMs": d["QueueWaitTicks"] * 1000 / d["Frequency"],
            "CommitMs": d["CommitTicks"] * 1000 / d["Frequency"],
        })
    coarse = [r["Diagnostic"]["MethodTotalTicks"] for r in rows if r["Fixture"] == "Collision"
              and r["Mode"] == "Coarse" and r["Round"] > 0]
    detailed = [r["Diagnostic"]["MethodTotalTicks"] for r in rows if r["Fixture"] == "Collision"
                and r["Mode"] == "Detailed" and r["Round"] > 0]
    # Observed range crosses 10%; do not infer a pass from an outlier-inflated median.
    lower = min(detailed) / max(coarse) - 1
    upper = max(detailed) / min(coarse) - 1
    gate = "Inconclusive" if lower <= 0.10 < upper else ("Pass" if upper <= 0.10 else "Excessive")
    result = {
        "Status": "Verified", "Records": len(rows), "IndependentByteComparisons": 18,
        "FullMetadataAndPublicationOrderEqual": True, "CountersStableAcrossRunsAndDop": True,
        "MaximumNonOffUnattributedFraction": maximum_residual,
        "CalibrationGate": gate, "ObservedRangeIncrease": [lower, upper],
        "MedianIncreasePercent": (statistics.median(detailed) / statistics.median(coarse) - 1) * 100,
        "Representatives": representatives,
    }
    (root / "audit.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    audit(sys.argv[1])
