"""Forecasting the AMOUNT of the next fill-up by first forecasting WHEN it happens.

Spending on fuel accumulates with driving, so a fill-up that comes after a long gap should cost more than one
that comes after a short gap. These models use that structure. At forecast time the next gap is not known, so
the honest version uses a *predicted* gap. The "oracle" version peeks at the real gap and exists only to show
the ceiling: how good this idea could be if timing were perfectly predictable.
"""
from __future__ import annotations

import numpy as np
import pandas as pd
from sklearn.linear_model import Ridge
from sklearn.preprocessing import StandardScaler

from .backtest import MIN_TRAIN, BacktestResult
from .data import amounts, gaps
from .models import ema, median_last6


def _ridge_on_gap(hist_gaps: np.ndarray, hist_amounts: np.ndarray, next_gap: float, min_rows: int = 5) -> float | None:
    """amount ~ gap (Ridge, standardised). None when there are too few points to fit a line."""
    if len(hist_gaps) < min_rows:
        return None
    X = hist_gaps.reshape(-1, 1)
    scaler = StandardScaler().fit(X)
    model = Ridge(alpha=1.0).fit(scaler.transform(X), hist_amounts)
    return float(model.predict(scaler.transform([[next_gap]]))[0])


def _rate_times_gap(hist_gaps: np.ndarray, hist_amounts: np.ndarray, next_gap: float, min_rows: int = 3) -> float | None:
    """Smoothed RM-per-day spend rate times the (predicted) gap."""
    if len(hist_gaps) < min_rows:
        return None
    rate = hist_amounts / np.maximum(hist_gaps, 1.0)
    return ema(0.4)(rate) * next_gap


def amount_two_stage(df: pd.DataFrame, kind: str, oracle: bool, min_train: int = MIN_TRAIN) -> BacktestResult:
    """Rolling-origin backtest of the amount at row k, for the same origins as the plain amount models.

    Row j >= 1 has gap_j = days since row j-1. At origin k the history is rows 0..k-1, so the pairs
    (gap_j, amount_j) for j = 1..k-1 are known, and gap_k is what we predict (or, for the oracle, peek at).
    """
    amt, gp = amounts(df), gaps(df)          # gp[j-1] is gap_j
    fit = {"ridge on gap": _ridge_on_gap, "rate x gap": _rate_times_gap}[kind]
    ks, preds = [], []
    for k in range(min_train, len(amt)):
        hist_gaps = gp[: k - 1]              # gaps of rows 1..k-1
        hist_amts = amt[1:k]                 # amounts of rows 1..k-1
        next_gap = float(gp[k - 1]) if oracle else median_last6(hist_gaps)
        value = fit(hist_gaps, hist_amts, next_gap)
        preds.append(value if value is not None else median_last6(amt[:k]))  # too little data: fall back
        ks.append(k)
    name = f"{kind} ({'oracle gap' if oracle else 'predicted gap'})"
    ks = np.array(ks)
    return BacktestResult(name, ks, amt[ks], np.array(preds))


# --- using the PREVIOUS fill-up to forecast the next one -------------------------------------------

def gap_from_previous_amount(df: pd.DataFrame, kind: str, min_train: int = MIN_TRAIN) -> BacktestResult:
    """Forecast 'days until the next fill-up' from the amount just paid.

    Physical reasoning, decided before looking at results: fuel bought now lasts for some distance, so a small
    top-up should be followed by an earlier refill than a full tank. This is knowable at forecast time (you
    know what you just paid), unlike the next gap itself.

    Pairs are (amount at fill i, days until fill i+1). For each origin only EARLIER pairs are used to fit.
    kind: "ridge" = days ~ amount (line), "per RM" = days per RM paid x amount (one-parameter ratio).
    """
    amt, gp = amounts(df), gaps(df)           # pair i: (amt[i], gp[i])
    ks, preds = [], []
    for k in range(min_train, len(gp)):
        x, y = amt[:k], gp[:k]
        if kind == "ridge" and len(x) >= 5:
            scaler = StandardScaler().fit(x.reshape(-1, 1))
            model = Ridge(alpha=1.0).fit(scaler.transform(x.reshape(-1, 1)), y)
            value = float(model.predict(scaler.transform([[amt[k]]]))[0])
        elif kind == "per RM" and y.sum() > 0 and x.sum() > 0:
            value = float(y.sum() / x.sum() * amt[k])
        else:
            value = float(np.mean(y))
        preds.append(max(value, 0.0))
        ks.append(k)
    ks = np.array(ks)
    return BacktestResult(f"days ~ previous amount ({kind})", ks, gp[ks], np.array(preds))


def amount_after_same_kind(small_share: float = 0.6, min_matches: int = 2):
    """Predictor for the AMOUNT series: median of what followed earlier fill-ups of the same kind.

    A fill-up is 'small' (a top-up) if it is below `small_share` of the median amount so far. The forecast is the
    median of the amounts that came after earlier fill-ups of the same kind as the latest one; with too few
    matches it falls back to the median of the last 6.

    NOTE: this idea was noticed while looking at the whole data set (every top-up was followed by a big fill),
    so a backtest on that same data flatters it. It earns trust only through new fill-ups it has not seen.
    """
    def predict(h: np.ndarray) -> float:
        if len(h) < 4:
            return median_last6(h)
        small = h < small_share * np.median(h)
        followers = [h[i + 1] for i in range(len(h) - 1) if small[i] == small[-1]]
        return float(np.median(followers)) if len(followers) >= min_matches else median_last6(h)
    return predict
