import numpy as np

from boxbox_ml.backtest import (BacktestResult, conformal_halfwidth, interval_coverage, paired_bootstrap,
                                rolling_origin)
from boxbox_ml.models import last_value, mean_all


def test_rolling_origin_never_sees_the_value_it_predicts():
    series = np.arange(10, dtype=float)          # 0..9
    seen = []

    def spy(history):
        seen.append(list(history))
        return history[-1]

    result = rolling_origin(series, spy, min_train=3)
    assert list(result.origins) == [3, 4, 5, 6, 7, 8, 9]
    for k, history in zip(result.origins, seen):
        assert len(history) == k                  # exactly the first k values
        assert series[k] not in history           # the target is never in its own history


def test_naive_error_on_a_known_series():
    result = rolling_origin(np.array([1.0, 2.0, 4.0, 7.0, 11.0]), last_value, min_train=3)
    # predicts 4 for 7 (error 3) and 7 for 11 (error 4)
    assert result.mae == 3.5 and result.count == 2


def test_mean_model_on_constant_series_is_perfect():
    assert rolling_origin(np.full(8, 5.0), mean_all).mae == 0.0


def test_paired_bootstrap_of_a_constant_difference_has_a_degenerate_interval():
    mean, lo, hi = paired_bootstrap(np.full(12, -2.0))
    assert mean == lo == hi == -2.0


def test_paired_bootstrap_is_deterministic_for_a_seed_and_includes_zero_for_noise():
    rng = np.random.default_rng(1)
    noise = rng.normal(0, 1, 15)
    assert paired_bootstrap(noise, seed=7) == paired_bootstrap(noise, seed=7)
    _, lo, hi = paired_bootstrap(noise - noise.mean())
    assert lo < 0 < hi


def test_conformal_halfwidth_needs_history_then_uses_the_right_rank():
    assert conformal_halfwidth(np.array([1.0, 2, 3, 4]), 0.8) is None
    errors = np.arange(1.0, 10.0)                 # 9 errors: rank = ceil(10 * 0.8) = 8
    assert conformal_halfwidth(errors, 0.8) == 8.0


def test_conformal_intervals_cover_about_the_nominal_share():
    rng = np.random.default_rng(0)
    actual = np.zeros(600)
    result = BacktestResult("noise", np.arange(600), actual, rng.normal(0, 1, 600))
    coverage, width, scored = interval_coverage(result, 0.8, "conformal")
    assert 0.74 <= coverage <= 0.86 and width > 0 and scored > 500


def test_mean_error_ranges_under_cover_an_80_percent_target():
    rng = np.random.default_rng(0)
    result = BacktestResult("noise", np.arange(600), np.zeros(600), rng.normal(0, 1, 600))
    coverage, _, _ = interval_coverage(result, 0.8, "mae")
    assert coverage < 0.65                        # +/- the mean error is roughly a 58% range for normal noise
