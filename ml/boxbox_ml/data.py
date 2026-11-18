"""Loading fill-up records and deriving the series the models forecast."""
from __future__ import annotations

from pathlib import Path

import numpy as np
import pandas as pd

# Subsidised BUDI95 RON95 price, RM per litre (the app's default). Used only to turn an amount into litres.
BUDI95_PRICE = 1.99


def load_entries(path: str | Path) -> pd.DataFrame:
    """Read the CSV exported from BoxBox (date, amount, litres, trip_km), oldest first.

    The sort is stable, so several fill-ups on one date keep their file order.
    """
    df = pd.read_csv(path, parse_dates=["date"])
    df = df.sort_values("date", kind="stable").reset_index(drop=True)
    df["gap_days"] = df["date"].diff().dt.days  # days since the previous fill-up; NaN for the first
    return df


def gaps(df: pd.DataFrame) -> np.ndarray:
    """Days between consecutive fill-ups (length n-1): the series behind 'when is the next fill-up'."""
    return df["gap_days"].dropna().to_numpy(dtype=float)


def amounts(df: pd.DataFrame) -> np.ndarray:
    """RM paid at each fill-up (length n)."""
    return df["amount"].to_numpy(dtype=float)


def litres_estimate(df: pd.DataFrame, price: float = BUDI95_PRICE) -> np.ndarray:
    """Recorded litres where known, otherwise amount / price rounded like a pump display."""
    est = (df["amount"] / price).round(2)
    return df["litres"].fillna(est).to_numpy(dtype=float)
