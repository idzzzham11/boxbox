import numpy as np
import pandas as pd

from boxbox_ml.data import gaps, litres_estimate, load_entries
from boxbox_ml.models import autoregressive_ridge, ema, ets_fitted_alpha, last_value, mean_all, median_last6


def test_ports_of_the_apps_models_match_hand_calculations():
    h = np.arange(1.0, 11.0)
    assert last_value(h) == 10 and mean_all(h) == 5.5
    assert median_last6(h) == 7.5                          # median of 5..10
    assert ema(0.4)(np.array([10.0, 20.0])) == 0.4 * 20 + 0.6 * 10


def test_fitted_smoothing_handles_constant_and_short_series():
    assert abs(ets_fitted_alpha(np.full(8, 3.0)) - 3.0) < 1e-6
    assert ets_fitted_alpha(np.array([1.0, 2.0, 3.0])) == 2.0   # < 4 points: falls back to the mean


def test_autoregressive_ridge_falls_back_to_the_mean_with_too_little_data():
    h = np.array([1.0, 5.0, 2.0, 8.0])
    assert autoregressive_ridge()(h) == mean_all(h)


def test_load_entries_sorts_stably_and_computes_gaps(tmp_path):
    csv = tmp_path / "e.csv"
    csv.write_text("date,amount,litres,trip_km\n2026-01-10,40,,\n2026-01-01,30,15.0,\n2026-01-10,50,,\n")
    df = load_entries(csv)
    assert list(df["amount"]) == [30, 40, 50]               # the two 10 Jan rows keep file order
    assert list(gaps(df)) == [9.0, 0.0]


def test_litres_estimate_prefers_recorded_litres_and_rounds_like_a_pump(tmp_path):
    csv = tmp_path / "e.csv"
    csv.write_text("date,amount,litres,trip_km\n2026-01-01,59.7,29.4,\n2026-01-02,50,,\n")
    est = litres_estimate(load_entries(csv))
    assert est[0] == 29.4 and est[1] == round(50 / 1.99, 2)
