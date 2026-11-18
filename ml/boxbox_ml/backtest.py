"""Rolling-origin backtesting and the statistics used to judge it honestly.

Rolling origin: for k = min_train .. n-1, fit on the FIRST k values only and predict value k. Nothing from
the future is ever used, which is what makes the error a fair estimate of real-world performance.
"""
from __future__ import annotations

import math
from dataclasses import dataclass

import numpy as np

from .models import Predictor

MIN_TRAIN = 3  # same warm-up as the app


@dataclass(frozen=True)
class BacktestResult:
    name: str
    origins: np.ndarray      # indices k that were predicted
    actual: np.ndarray
    predicted: np.ndarray

    @property
    def abs_errors(self) -> np.ndarray:
        return np.abs(self.predicted - self.actual)

    @property
    def mae(self) -> float:
        return float(np.mean(self.abs_errors))

    @property
    def count(self) -> int:
        return len(self.origins)


def rolling_origin(series: np.ndarray, predict: Predictor, name: str = "", min_train: int = MIN_TRAIN) -> BacktestResult:
    series = np.asarray(series, dtype=float)
    ks = np.arange(min_train, len(series))
    preds = np.array([predict(series[:k]) for k in ks])  # series[:k] excludes value k: no look-ahead
    return BacktestResult(name, ks, series[ks], preds)


def paired_bootstrap(diff: np.ndarray, resamples: int = 10_000, seed: int = 0, confidence: float = 0.9) -> tuple[float, float, float]:
    """Mean of paired differences with a bootstrap confidence interval.

    `diff` is (error of model A) - (error of model B) per forecast. A negative mean means A is better; if the
    interval contains 0 the difference is not distinguishable from noise at this sample size.
    """
    diff = np.asarray(diff, dtype=float)
    rng = np.random.default_rng(seed)
    idx = rng.integers(0, len(diff), size=(resamples, len(diff)))
    means = diff[idx].mean(axis=1)
    lo, hi = np.quantile(means, [(1 - confidence) / 2, 1 - (1 - confidence) / 2])
    return float(diff.mean()), float(lo), float(hi)


def conformal_halfwidth(past_abs_errors: np.ndarray, coverage: float = 0.8) -> float | None:
    """Split-conformal prediction interval half-width from the errors seen SO FAR.

    The interval prediction +/- this value is expected to contain the truth `coverage` of the time, without
    assuming any error distribution. Needs a few past errors; returns None until there are enough.
    """
    m = len(past_abs_errors)
    if m < 5:
        return None
    rank = min(m, math.ceil((m + 1) * coverage))
    return float(np.sort(past_abs_errors)[rank - 1])


def interval_coverage(result: BacktestResult, coverage: float = 0.8, rule: str = "conformal") -> tuple[float, float, int]:
    """Walk forward: for each forecast use only EARLIER errors to build its interval, then check whether it held.

    rule="conformal": half-width = the `coverage` quantile of past absolute errors.
    rule="mae":       half-width = the mean past absolute error (what the app shows as its range).
    Returns (share of actuals inside the interval, mean half-width, forecasts scored).
    """
    inside, widths = [], []
    errors = result.abs_errors
    for i in range(len(errors)):
        if rule == "conformal":
            hw = conformal_halfwidth(errors[:i], coverage)
        else:
            hw = float(np.mean(errors[:i])) if i >= 5 else None
        if hw is None:
            continue
        inside.append(errors[i] <= hw)
        widths.append(hw)
    if not inside:
        return float("nan"), float("nan"), 0
    return float(np.mean(inside)), float(np.mean(widths)), len(inside)
