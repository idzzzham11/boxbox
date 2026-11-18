"""Candidate one-step-ahead forecasters.

Every predictor has the same shape: history (1-D array, oldest first) -> forecast of the NEXT value.
The first four are exact ports of the models in the C# app (SpendForecaster.cs) so the two can be
compared on identical footing. The rest are the "learned" candidates.
"""
from __future__ import annotations

import warnings
from typing import Callable

import numpy as np
from sklearn.linear_model import Ridge
from sklearn.preprocessing import StandardScaler
from statsmodels.tsa.holtwinters import SimpleExpSmoothing

Predictor = Callable[[np.ndarray], float]


# --- ports of the app's models -------------------------------------------------------------------

def last_value(h: np.ndarray) -> float:
    """Naive: the next value equals the last one. The benchmark every model must beat."""
    return float(h[-1])


def mean_all(h: np.ndarray) -> float:
    return float(np.mean(h))


def median_last6(h: np.ndarray) -> float:
    return float(np.median(h[-6:]))


def ema(alpha: float) -> Predictor:
    """Exponential smoothing with a FIXED alpha (the app uses 0.4)."""
    def predict(h: np.ndarray) -> float:
        level = float(h[0])
        for x in h[1:]:
            level = alpha * float(x) + (1 - alpha) * level
        return level
    return predict


# --- learned candidates --------------------------------------------------------------------------

def ets_fitted_alpha(h: np.ndarray) -> float:
    """Exponential smoothing where alpha is FITTED to the history (statsmodels) instead of fixed.

    With very few points the optimiser can fail or return nonsense, so it falls back to the mean.
    """
    if len(h) < 4:
        return mean_all(h)
    try:
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            fit = SimpleExpSmoothing(h, initialization_method="estimated").fit()
            value = float(fit.forecast(1)[0])
        return value if np.isfinite(value) else mean_all(h)
    except Exception:
        return mean_all(h)


def autoregressive_ridge(lags: int = 3, alpha: float = 1.0, min_rows: int = 6) -> Predictor:
    """Ridge regression of each value on its previous `lags` values (an AR model).

    The honest question it answers: is the next value predictable from the last few, beyond just
    averaging? With a handful of points it is expected NOT to be. It is included so that is measured,
    not assumed. Falls back to the mean while there are too few rows to fit.
    """
    def predict(h: np.ndarray) -> float:
        rows = len(h) - lags
        if rows < min_rows:
            return mean_all(h)
        X = np.array([h[i:i + lags] for i in range(rows)])
        y = h[lags:]
        scaler = StandardScaler().fit(X)
        model = Ridge(alpha=alpha).fit(scaler.transform(X), y)
        return float(model.predict(scaler.transform(h[-lags:].reshape(1, -1)))[0])
    return predict


BASELINES: dict[str, Predictor] = {
    "last value (naive)": last_value,
    "average of all": mean_all,
    "median of last 6": median_last6,
    "exp. smoothing a=0.4": ema(0.4),
}

LEARNED: dict[str, Predictor] = {
    "exp. smoothing, fitted a": ets_fitted_alpha,
    "AR(3) ridge": autoregressive_ridge(lags=3),
}

ALL_MODELS: dict[str, Predictor] = {**BASELINES, **LEARNED}
