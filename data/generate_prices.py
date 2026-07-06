#!/usr/bin/env python3
"""Generate deterministic, cached daily OHLC price data for FiscalFox.

This intentionally uses a seeded geometric-random-walk instead of a live market
API so the project is 100% free and reproducible. The output CSVs mimic the
column layout of Stooq / Yahoo Finance daily exports:

    Date,Open,High,Low,Close,Volume

Run:  python data/generate_prices.py
"""
from __future__ import annotations

import csv
import math
import os
import random
from datetime import date, timedelta

# symbol -> (start_price, annual_drift, annual_vol, seed)
INSTRUMENTS = {
    "VTI":     (180.0, 0.09, 0.16, 101),   # broad US equity ETF
    "BND":     (75.0,  0.03, 0.05, 202),   # US total bond ETF
    "VXUS":    (55.0,  0.06, 0.18, 303),   # ex-US equity ETF
    "GLD":     (185.0, 0.04, 0.14, 404),   # gold ETF
    "BTC-USD": (26000.0, 0.35, 0.65, 505), # crypto (high vol)
}

TRADING_DAYS = 504  # ~2 years of business days
DT = 1.0 / 252.0

OUT_DIR = os.path.join(os.path.dirname(__file__), "prices")


def business_days(n: int, end: date) -> list[date]:
    days: list[date] = []
    d = end
    while len(days) < n:
        if d.weekday() < 5:  # Mon-Fri
            days.append(d)
        d -= timedelta(days=1)
    return list(reversed(days))


def gen_series(start: float, drift: float, vol: float, seed: int) -> list[float]:
    rng = random.Random(seed)
    mu = (drift - 0.5 * vol * vol) * DT
    sigma = vol * math.sqrt(DT)
    price = start
    out = [price]
    for _ in range(TRADING_DAYS - 1):
        shock = rng.gauss(0.0, 1.0)
        price *= math.exp(mu + sigma * shock)
        out.append(round(price, 6))
    return out


def write_csv(symbol: str, closes: list[float], dates: list[date]) -> str:
    safe = symbol.replace("/", "_")
    path = os.path.join(OUT_DIR, f"{safe}.csv")
    rng = random.Random(hash(symbol) & 0xFFFF)
    with open(path, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(["Date", "Open", "High", "Low", "Close", "Volume"])
        prev = closes[0]
        for d, close in zip(dates, closes):
            open_ = round(prev * (1 + rng.uniform(-0.004, 0.004)), 6)
            high = round(max(open_, close) * (1 + rng.uniform(0.0, 0.01)), 6)
            low = round(min(open_, close) * (1 - rng.uniform(0.0, 0.01)), 6)
            volume = rng.randint(500_000, 8_000_000)
            w.writerow([d.isoformat(), open_, high, low, close, volume])
            prev = close
    return path


def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    dates = business_days(TRADING_DAYS, date(2025, 12, 31))
    for symbol, (start, drift, vol, seed) in INSTRUMENTS.items():
        closes = gen_series(start, drift, vol, seed)
        path = write_csv(symbol, closes, dates)
        print(f"wrote {path}  ({len(closes)} bars, last close {closes[-1]:.2f})")


if __name__ == "__main__":
    main()
