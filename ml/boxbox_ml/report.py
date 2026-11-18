"""Backtest report: python -m boxbox_ml.report data/entries.csv"""
from __future__ import annotations

import sys

import numpy as np

from .backtest import BacktestResult, interval_coverage, paired_bootstrap, rolling_origin
from .data import amounts, gaps, litres_estimate, load_entries
from .models import ALL_MODELS, BASELINES
from .two_stage import amount_after_same_kind, amount_two_stage, gap_from_previous_amount

NAIVE = "last value (naive)"


def app_choice(results: list[BacktestResult]) -> BacktestResult:
    """What the app itself would pick: the lowest-MAE of its own four models (ties go to the earlier model)."""
    own = [r for r in results if r.name in BASELINES]
    return min(own, key=lambda r: r.mae)


def _table(title: str, results: list[BacktestResult]) -> BacktestResult:
    by_name = {r.name: r for r in results}
    naive, current = by_name[NAIVE], app_choice(results)
    print(f"\n{title}  (n = {naive.count} forecasts; 'current' = the app's pick: {current.name})")
    print(f"{'model':40}{'MAE':>8}{'vs naive':>10}   {'diff vs current (90% CI)':<28}")
    for r in sorted(results, key=lambda x: x.mae):
        vs_naive = f"{(r.mae / naive.mae - 1) * 100:+.0f}%"
        mean, lo, hi = paired_bootstrap(r.abs_errors - current.abs_errors)
        verdict = "(current)" if r is current else ("better" if hi < 0 else "worse" if lo > 0 else "no clear difference")
        print(f"{r.name:40}{r.mae:8.2f}{vs_naive:>10}   {mean:+6.2f} [{lo:+6.2f}, {hi:+6.2f}]  {verdict}")
    return current


def run(path: str) -> None:
    df = load_entries(path)
    g, a = gaps(df), amounts(df)
    print(f"{len(df)} fill-ups, {df['date'].min().date()} to {df['date'].max().date()}")

    gap_results = [rolling_origin(g, f, n) for n, f in ALL_MODELS.items()]
    gap_results += [gap_from_previous_amount(df, "ridge"), gap_from_previous_amount(df, "per RM")]
    gap_current = _table("DAYS UNTIL NEXT FILL-UP", gap_results)

    amount_results = [rolling_origin(a, f, n) for n, f in ALL_MODELS.items()]
    amount_results.append(rolling_origin(a, amount_after_same_kind(), "median after a similar fill-up (tentative)"))
    for kind in ("ridge on gap", "rate x gap"):
        amount_results += [amount_two_stage(df, kind, oracle=False), amount_two_stage(df, kind, oracle=True)]
    amount_current = _table("RM AT NEXT FILL-UP", amount_results)

    litres = litres_estimate(df)
    litre_results = [rolling_origin(litres, f, n) for n, f in ALL_MODELS.items()]
    _table("LITRES AT NEXT FILL-UP (estimated from RM at the set price)", litre_results)

    print("\nPREDICTION RANGES: does an 80% range really contain the truth 80% of the time?")
    print(f"{'signal / rule':40}{'coverage':>10}{'avg +/-':>10}{'scored':>8}")
    for label, res in (("days", gap_current), ("RM", amount_current)):
        for rule, desc in (("mae", "app style: +/- mean error"), ("conformal", "conformal 80%")):
            cov, width, n = interval_coverage(res, 0.8, rule)
            print(f"{label + ', ' + desc:40}{cov * 100:9.0f}%{width:10.2f}{n:8d}")


if __name__ == "__main__":
    run(sys.argv[1] if len(sys.argv) > 1 else "data/entries.csv")
