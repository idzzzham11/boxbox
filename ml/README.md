# BoxBox ML

Forecasting experiments for BoxBox. The question this folder answers is not "can we build a clever model?"
but "**does anything beat what the app already does, on this much data?**"

One car produces a handful of fill-ups, so every model here is judged by a rolling backtest and compared with
the naive guess "same as last time". A model only deserves to go into the app if it is clearly better.

## Setup

```sh
python3 -m venv ml/.venv && ml/.venv/bin/pip install -r ml/requirements.txt
BOXBOX_HOST=root@my-server ./ml/export_data.sh      # copies your fill-ups to ml/data/entries.csv (git-ignored)
cd ml && ./.venv/bin/python -m boxbox_ml.report data/entries.csv
./.venv/bin/python -m pytest                          # tests use made-up data only
```

(If `python3 -m venv` is unavailable, `python3 -m venv --without-pip` plus the official `get-pip.py` works.)

## How judging works

- **Rolling origin:** for each fill-up k, fit on fill-ups before k only and predict k. Nothing from the future is used.
- **Paired bootstrap:** the difference in error between two models, with a confidence interval. If the interval
  contains 0, the difference is not distinguishable from noise at this sample size.
- **Ranges:** the report checks whether a stated 80% range really contains the truth 80% of the time (conformal
  intervals use only earlier errors).

## Models

| Model | Idea |
|---|---|
| last value, average of all, median of last 6, smoothing (a = 0.4) | Exact ports of the models in the C# app, so results are comparable. The port is verified: errors match the app to the cent |
| smoothing with a **fitted** alpha | Let the data choose how fast to forget. It chooses alpha = 0, i.e. the plain average |
| AR(3) ridge | Predict from the last three values |
| days ~ previous amount | A fill-up buys distance, so the amount just paid should say when the next fill-up is due |
| amount ~ gap (two-stage) | Longer gaps should cost more; shown with predicted gap and with the real gap ("oracle") to reveal the ceiling |
| median after a similar fill-up | Top-ups tend to be followed by full tanks. Noticed in the data, so its backtest is optimistic until new fill-ups confirm it |

## Re-running as data grows

Export the data again and re-run the report. The questions to watch:
1. Does *days ~ previous amount* become clearly better than the average? (correlation is already about 0.45)
2. Does any model's difference interval move away from 0?
3. Do the app's ranges ("typical error") cover the truth as often as they claim?

When one model is clearly better on both a long enough history and fresh fill-ups, port it to the app's
forecaster and keep this backtest as its yardstick.
