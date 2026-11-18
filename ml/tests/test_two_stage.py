import numpy as np
import pandas as pd

from boxbox_ml.backtest import rolling_origin
from boxbox_ml.models import mean_all
from boxbox_ml.two_stage import amount_after_same_kind, amount_two_stage, gap_from_previous_amount


def frame(amounts, gap_days):
    """Build a fill-up table from amounts and the gaps BEFORE each fill-up (gap_days[0] is ignored)."""
    dates = pd.to_datetime("2026-01-01") + pd.to_timedelta(np.cumsum([0, *gap_days[1:]]), unit="D")
    df = pd.DataFrame({"date": dates, "amount": amounts, "litres": np.nan, "trip_km": np.nan})
    df["gap_days"] = df["date"].diff().dt.days
    return df


def test_gap_from_previous_amount_is_aligned_correctly():
    # Exactly 0.1 day per RM: the days until the NEXT fill-up are amount/10 of the fill-up just made.
    amounts = [20, 60, 40, 80, 30, 50, 70, 20, 60, 40, 80, 30, 50, 70]
    gaps_after = [a / 10 for a in amounts]                       # days until the following fill-up
    df = frame(amounts, [0, *gaps_after[:-1]])
    perfect = gap_from_previous_amount(df, "per RM")
    assert perfect.mae < 1e-9                                    # an off-by-one pairing would make this large
    naive_mean = rolling_origin(df["gap_days"].dropna().to_numpy(), mean_all)
    assert perfect.mae < naive_mean.mae
    # ridge shrinks its slope on purpose (regularisation), so it is good but not perfect
    assert gap_from_previous_amount(df, "ridge").mae < naive_mean.mae


def test_gap_model_never_uses_the_gap_it_is_predicting():
    df = frame([30, 50, 40, 60, 20, 80, 30, 50, 40], [0, 3, 5, 4, 6, 2, 8, 3, 5])
    base = gap_from_previous_amount(df, "per RM")
    changed = df.copy()
    changed.loc[changed.index[-1], "date"] += pd.Timedelta(days=30)   # change only the LAST (predicted) gap
    changed["gap_days"] = changed["date"].diff().dt.days
    assert np.allclose(gap_from_previous_amount(changed, "per RM").predicted, base.predicted)


def test_oracle_amount_model_recovers_a_known_relationship():
    gaps_ = [0, 3, 7, 2, 9, 5, 4, 8, 6, 3, 7, 2, 9, 5]
    amounts = [10 + 5 * g for g in gaps_]                        # amount = 10 + 5 * gap exactly
    df = frame(amounts, gaps_)
    oracle = amount_two_stage(df, "ridge on gap", oracle=True)
    # The first origins fall back to a median (fewer than 5 pairs to fit); judge the regression once it is active.
    active = oracle.origins >= 6
    assert np.mean(oracle.abs_errors[active]) < 2.5
    # without peeking at the real gap it cannot be as good
    assert oracle.mae < amount_two_stage(df, "ridge on gap", oracle=False).mae


def test_amount_after_same_kind_learns_a_top_up_then_big_fill_pattern():
    h = np.array([55, 15, 58, 52, 14, 60, 54, 16], dtype=float)  # ... big, top-up: next should be big
    assert amount_after_same_kind()(h) > 50
    h2 = np.array([55, 15, 58, 52, 14, 60, 54, 16, 57], dtype=float)  # last was big: what followed big fills was mixed
    followers = [15, 58, 14, 60, 16]                              # amounts that followed the earlier big fills... median is 16-58
    assert min(followers) <= amount_after_same_kind()(h2) <= max(followers)
    assert amount_after_same_kind()(np.array([1.0, 2.0])) == 1.5  # too short: falls back to the median
