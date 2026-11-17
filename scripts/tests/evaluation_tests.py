import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("evaluation", Path(__file__).resolve().parents[1] / "evaluate-context.py")
evaluation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evaluation)


class EvaluationTests(unittest.TestCase):
    def test_v2_manifest_and_metric_denominators(self):
        files, cases, _ = evaluation.load_suite(evaluation.ROOT / "evaluation/context-v2")
        self.assertGreaterEqual(len(files), 60)
        self.assertEqual(5, len({c["suite"] for c in cases}))
        result = evaluation.retrieval_metrics({"expectedSources": ["a", "b"]},
                                             [{"path": "import.zip!/a"}, {"path": "a"}, {"path": "wrong"}])
        self.assertEqual((2, 2, 1), (result["retrieved"], result["expected"], result["relevantRetrieved"]))
        self.assertEqual(.5, result["recall"])
        self.assertEqual(.5, result["precision"])
        self.assertTrue(evaluation.retrieval_metrics({"expectedSources": []}, [])["noAnswerCorrectlyEmpty"])

    def test_incomplete_and_duplicate_runs_fail_closed(self):
        row = {"id": "q", "repeat": 0, "elapsedSeconds": 1}
        for rows in ([], [dict(row, error="model failed")], [row, row]):
            with self.assertRaises(ValueError):
                evaluation.compare({"results": rows}, {"results": rows})

    def test_corpus_and_disjoint_questions(self):
        files, cases, _ = evaluation.load_suite()
        self.assertEqual(8, len(files))
        self.assertEqual(36, len(cases))
        self.assertEqual(12, sum(c["split"] == "holdout" for c in cases))

    def test_detects_changed_source(self):
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp) / "suite"
            shutil.copytree(evaluation.DATA, directory)
            source = next((directory / "corpus").rglob("*.cs"))
            source.write_text("changed")
            with self.assertRaisesRegex(ValueError, "Corpus modifié"):
                evaluation.load_suite(directory)

    def test_does_not_call_pattern_hits_correctness(self):
        case = {"factPatterns": ["42"], "forbiddenPatterns": [], "expectedSources": ["src/A.cs"]}
        result = evaluation.indicators(case, "A.cs ne renvoie pas 42")
        self.assertEqual([True], result["factPatternHits"])
        self.assertIsNone(result["humanReview"]["correctFacts"])

    def test_comparison_rejects_different_budget_or_missing_case(self):
        a = {"label": "a", "contextTokens": 8192, "results": [{"id": "q", "repeat": 0, "elapsedSeconds": 1}]}
        with self.assertRaises(ValueError):
            evaluation.compare(a, dict(a, contextTokens=16384))
        with self.assertRaises(ValueError):
            evaluation.compare(a, dict(a, results=[]))
        self.assertTrue(evaluation.compare(a, a)["modelIdentityRequiresReview"])
        with self.assertRaisesRegex(ValueError, "Identité"):
            evaluation.compare(dict(a, modelIdentity="model:one"), dict(a, modelIdentity="model:two"))
