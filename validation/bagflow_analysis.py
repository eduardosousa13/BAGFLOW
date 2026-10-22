"""BAGFLOW M5 - loading and statistical validation of Unity exports.

Pure analysis code (NumPy/SciPy/pandas, no Unity). The theoretical expressions follow docs/MATH_MODELS.md
literally; SciPy is used only as an independent cross-check in the tests and for p-values.

Data sources (all produced by the Unity C# exporter, see docs/M5_VALIDATION_EXPORT.md):
  * single export folder:  bags.csv, events.csv, config.json   (docs/CONTRACTS.md format)
  * batch folder:          batch.json, runs.csv, batch_bags.csv[.gz], blockages.csv
"""
from __future__ import annotations

import json
import math
import os
from dataclasses import dataclass, field

import numpy as np
import pandas as pd
from scipy import special, stats

BAG_COLUMNS = ["id", "weight_kg", "nominal_transport_s", "observed_transport_s", "inspection_s", "arrival_s",
               "queue_enter_s", "scan_start_s", "complete_s", "queue_wait_s", "total_s", "flagged"]
EVENT_COLUMNS = ["time_s", "event_type", "bag_id", "detail", "scheduled_time_s"]
RUN_COLUMNS = ["run", "seed", "n", "arrival_mode", "flag_probability", "blockage_rate_per_s", "blockage_duration_s",
               "end_time_s", "completed", "approved", "flagged", "blockage_occurrences", "blocked_seconds",
               "mean_queue_wait_s", "mean_total_s", "scanner_utilization"]
BLOCKAGE_COLUMNS = ["seed", "occurrence", "scheduled_time_s", "time_s"]


class ExportFormatError(ValueError):
    """Raised when a file does not follow the exported format."""


# ---------------------------------------------------------------------------------------------------------
# Loading
# ---------------------------------------------------------------------------------------------------------

def _resolve(path: str) -> str:
    if os.path.exists(path):
        return path
    if os.path.exists(path + ".gz"):
        return path + ".gz"
    raise FileNotFoundError(path)


def read_checked_csv(path: str, columns: list[str]) -> pd.DataFrame:
    """Reads a UTF-8 CSV and requires the exact header. Empty fields become NaN (missing values)."""
    path = _resolve(path)
    frame = pd.read_csv(path, dtype={"event_type": "string", "detail": "string", "arrival_mode": "string"},
                        keep_default_na=False, na_values=[""], encoding="utf-8")
    if list(frame.columns) != columns:
        raise ExportFormatError(f"{os.path.basename(path)}: header {list(frame.columns)} != expected {columns}")
    return frame


def _typed_bags(frame: pd.DataFrame) -> pd.DataFrame:
    frame = frame.copy()
    for name in BAG_COLUMNS:
        if name in ("id", "flagged"):
            continue
        frame[name] = pd.to_numeric(frame[name], errors="raise").astype(float)
    frame["id"] = pd.to_numeric(frame["id"], errors="raise").astype("int64")
    flagged = pd.to_numeric(frame["flagged"], errors="raise")
    if not flagged.dropna().isin([0, 1]).all():
        raise ExportFormatError("flagged must be 0, 1 or empty")
    frame["flagged"] = flagged.astype("Int64")
    return frame


@dataclass
class Export:
    path: str
    bags: pd.DataFrame
    events: pd.DataFrame
    config: dict

    @property
    def parameters(self) -> dict:
        return self.config["parameters"]


def load_export(path: str) -> Export:
    bags = _typed_bags(read_checked_csv(os.path.join(path, "bags.csv"), BAG_COLUMNS))
    events = read_checked_csv(os.path.join(path, "events.csv"), EVENT_COLUMNS)
    events["time_s"] = events["time_s"].astype(float)
    events["scheduled_time_s"] = events["scheduled_time_s"].astype(float)
    events["bag_id"] = events["bag_id"].astype("Int64")
    with open(os.path.join(path, "config.json"), encoding="utf-8") as handle:
        config = json.load(handle)
    return Export(path, bags, events, config)


def check_export(export: Export) -> list[str]:
    """Integrity checks between the three files. Returns a list of problems (empty = consistent)."""
    problems = []
    bags, events, summary = export.bags, export.events, export.config["summary"]
    if len(bags) != summary["bag_count"]:
        problems.append("bag_count differs from bags.csv")
    if len(events) != summary["event_count"]:
        problems.append("event_count differs from events.csv")
    if not bags["id"].is_unique:
        problems.append("duplicate bag ids")
    if int(bags["flagged"].sum()) != summary["flagged"]:
        problems.append("flagged column differs from summary")
    if int((events["event_type"] == "BagFlagged").sum()) != int(bags["flagged"].sum()):
        problems.append("BagFlagged events differ from flagged column")
    if int((events["event_type"] == "BlockageOccurrence").sum()) != summary["blockage_occurrences"]:
        problems.append("BlockageOccurrence events differ from summary")
    p = export.parameters
    if not bags["weight_kg"].between(p["MinWeightKg"], p["MaxWeightKg"]).all():
        problems.append("weight outside truncation interval")
    if not bags["nominal_transport_s"].between(p["TransportMinSeconds"], p["TransportMaxSeconds"]).all():
        problems.append("nominal transport outside [a, b]")
    if (bags["queue_wait_s"].dropna() < 0).any() or (bags["total_s"].dropna() < 0).any():
        problems.append("negative derived times")
    return problems


@dataclass
class Batch:
    path: str
    meta: dict
    runs: pd.DataFrame
    blockages: pd.DataFrame
    bags: pd.DataFrame | None = field(default=None)

    @property
    def template(self) -> dict:
        return self.meta["template"]


def load_batch(path: str, with_bags: bool = True) -> Batch:
    with open(os.path.join(path, "batch.json"), encoding="utf-8") as handle:
        meta = json.load(handle)
    runs = read_checked_csv(os.path.join(path, "runs.csv"), RUN_COLUMNS)
    blockages = read_checked_csv(os.path.join(path, "blockages.csv"), BLOCKAGE_COLUMNS)
    bags = None
    if with_bags:
        bags = _typed_bags(read_checked_csv(os.path.join(path, "batch_bags.csv"), ["seed"] + BAG_COLUMNS))
    if len(runs) != meta["runs"] or not runs["seed"].is_unique:
        raise ExportFormatError("runs.csv does not match batch.json")
    return Batch(path, meta, runs, blockages, bags)


# ---------------------------------------------------------------------------------------------------------
# Theoretical laws (docs/MATH_MODELS.md)
# ---------------------------------------------------------------------------------------------------------

@dataclass(frozen=True)
class Triangular:
    a: float
    c: float
    b: float

    def __post_init__(self):
        if not (self.a < self.c < self.b):
            raise ValueError("ordinary triangular requires a < c < b")

    def pdf(self, t):
        t = np.asarray(t, dtype=float)
        a, b, c = self.a, self.b, self.c
        up = 2 * (t - a) / ((b - a) * (c - a))
        down = 2 * (b - t) / ((b - a) * (b - c))
        return np.where((t >= a) & (t < c), up, np.where((t >= c) & (t <= b), down, 0.0))

    def cdf(self, t):
        t = np.asarray(t, dtype=float)
        a, b, c = self.a, self.b, self.c
        left = (t - a) ** 2 / ((b - a) * (c - a))
        right = 1 - (b - t) ** 2 / ((b - a) * (b - c))
        return np.where(t < a, 0.0, np.where(t < c, left, np.where(t < b, right, 1.0)))

    @property
    def mean(self):
        return (self.a + self.b + self.c) / 3

    @property
    def var(self):
        a, b, c = self.a, self.b, self.c
        return (a * a + b * b + c * c - a * b - a * c - b * c) / 18


@dataclass(frozen=True)
class Exponential:
    rate: float

    def __post_init__(self):
        if not self.rate > 0:
            raise ValueError("rate must be > 0")

    def pdf(self, t):
        t = np.asarray(t, dtype=float)
        return np.where(t >= 0, self.rate * np.exp(-self.rate * np.clip(t, 0, None)), 0.0)

    def cdf(self, t):
        t = np.asarray(t, dtype=float)
        return np.where(t >= 0, -np.expm1(-self.rate * np.clip(t, 0, None)), 0.0)

    @property
    def mean(self):
        return 1 / self.rate

    @property
    def var(self):
        return 1 / self.rate ** 2


@dataclass(frozen=True)
class TruncatedNormal:
    """N(mu, sigma^2) of the ORIGINAL normal conditioned on L <= W <= H."""
    mu: float
    sigma: float
    low: float
    high: float

    def __post_init__(self):
        if not (self.sigma > 0 and self.low < self.high):
            raise ValueError("requires sigma > 0 and L < H")
        if not self.z > 0:
            raise ValueError("truncation interval has no probability mass")

    @property
    def alpha(self):
        return (self.low - self.mu) / self.sigma

    @property
    def beta(self):
        return (self.high - self.mu) / self.sigma

    @property
    def z(self):
        # Z = Phi(beta) - Phi(alpha); for an interval in the right tail use survival functions (no cancellation).
        a, b = self.alpha, self.beta
        if a > 0:
            return float(special.ndtr(-a) - special.ndtr(-b))
        return float(special.ndtr(b) - special.ndtr(a))

    @staticmethod
    def _phi(x):
        return np.exp(-0.5 * np.asarray(x, dtype=float) ** 2) / math.sqrt(2 * math.pi)

    def pdf(self, w):
        w = np.asarray(w, dtype=float)
        inside = (w >= self.low) & (w <= self.high)
        return np.where(inside, self._phi((w - self.mu) / self.sigma) / (self.sigma * self.z), 0.0)

    def cdf(self, w):
        w = np.asarray(w, dtype=float)
        x = (np.clip(w, self.low, self.high) - self.mu) / self.sigma
        if self.alpha > 0:
            inner = (special.ndtr(-self.alpha) - special.ndtr(-x)) / self.z
        else:
            inner = (special.ndtr(x) - special.ndtr(self.alpha)) / self.z
        return np.where(w < self.low, 0.0, np.where(w > self.high, 1.0, inner))

    @property
    def mean(self):
        a, b, z = self.alpha, self.beta, self.z
        return self.mu + self.sigma * (self._phi(a) - self._phi(b)) / z

    @property
    def var(self):
        a, b, z = self.alpha, self.beta, self.z
        pa, pb = self._phi(a), self._phi(b)
        return self.sigma ** 2 * (1 + (a * pa - b * pb) / z - ((pa - pb) / z) ** 2)


def binomial_pmf(k, n: int, p: float):
    k = np.asarray(k)
    return stats.binom.pmf(k, n, p)


def poisson_pmf(k, mean: float):
    k = np.asarray(k)
    return stats.poisson.pmf(k, mean)


# ---------------------------------------------------------------------------------------------------------
# Empirical statistics
# ---------------------------------------------------------------------------------------------------------

def density_histogram(x, bins=40, value_range=None):
    """Histogram normalised as a density: sum(density * width) == 1."""
    density, edges = np.histogram(np.asarray(x, dtype=float), bins=bins, range=value_range, density=True)
    return density, edges


def ecdf(x):
    x = np.sort(np.asarray(x, dtype=float))
    return x, np.arange(1, len(x) + 1) / len(x)


def relative_frequencies(counts, support):
    counts = np.asarray(counts)
    return np.array([(counts == k).mean() for k in support])


@dataclass
class MomentComparison:
    name: str
    n: int
    theory_mean: float
    sample_mean: float
    se_mean: float
    theory_var: float
    sample_var: float
    se_var: float

    @property
    def z_mean(self):
        return (self.sample_mean - self.theory_mean) / self.se_mean if self.se_mean > 0 else float("nan")

    @property
    def z_var(self):
        return (self.sample_var - self.theory_var) / self.se_var if self.se_var > 0 else float("nan")

    def row(self) -> dict:
        return {"variável": self.name, "n": self.n, "E teórico": self.theory_mean, "média amostral": self.sample_mean,
                "z(média)": self.z_mean, "Var teórica": self.theory_var, "variância amostral": self.sample_var,
                "z(var)": self.z_var}


def compare_moments(name: str, x, theory_mean: float, theory_var: float) -> MomentComparison:
    """Sample mean/variance (ddof=1) with large-sample standard errors:
    SE(mean)=sqrt(Var/n) with the theoretical variance; SE(s^2)=sqrt((mu4 - s^4)/n) from the sample."""
    x = np.asarray(x, dtype=float)
    n = len(x)
    mean = float(x.mean())
    var = float(x.var(ddof=1))
    mu4 = float(np.mean((x - mean) ** 4))
    return MomentComparison(name, n, theory_mean, mean, math.sqrt(theory_var / n), theory_var, var,
                            math.sqrt(max(mu4 - var ** 2, 0) / n))


def ks_test(x, cdf):
    return stats.kstest(np.asarray(x, dtype=float), cdf)


def chi_square_discrete(counts, pmf, support_max: int, min_expected: float = 5.0):
    """Chi-square goodness of fit for integer counts. Cells 0..support_max with the upper tail pooled into the last cell;
    adjacent cells are merged from the right until every expected count >= min_expected."""
    counts = np.asarray(counts, dtype=int)
    n = len(counts)
    ks = np.arange(support_max + 1)
    expected = n * pmf(ks)
    expected[-1] = n * (1 - pmf(ks[:-1]).sum())   # P(K >= support_max)
    observed = np.array([(counts == k).sum() for k in ks[:-1]] + [(counts >= ks[-1]).sum()], dtype=float)
    obs, exp_ = list(observed), list(expected)
    while len(exp_) > 2 and exp_[-1] < min_expected:
        e, o = exp_.pop(), obs.pop()
        exp_[-1] += e
        obs[-1] += o
    while len(exp_) > 2 and exp_[0] < min_expected:
        e, o = exp_.pop(0), obs.pop(0)
        exp_[0] += e
        obs[0] += o
    obs, exp_ = np.array(obs), np.array(exp_)
    statistic = float(((obs - exp_) ** 2 / exp_).sum())
    dof = len(obs) - 1
    return statistic, dof, float(stats.chi2.sf(statistic, dof))


# ---------------------------------------------------------------------------------------------------------
# Binomial and Poisson from simulation runs
# ---------------------------------------------------------------------------------------------------------

def binomial_counts(runs: pd.DataFrame):
    """One N_S observation per complete run (sum of that run's Bernoulli decisions). Requires constant N and p."""
    if runs["n"].nunique() != 1 or runs["flag_probability"].nunique() != 1:
        raise ValueError("Binomial validation needs the same N and p in every run")
    if not (runs["completed"] == runs["n"]).all():
        raise ValueError("every run must be complete (all N Bernoulli decisions taken)")
    return runs["flagged"].to_numpy(dtype=int), int(runs["n"].iloc[0]), float(runs["flag_probability"].iloc[0])


def poisson_window_counts(blockages: pd.DataFrame, runs: pd.DataFrame, window_s: float, horizon_s: float):
    """Counts of Poisson occurrences (scheduled instants) in the fixed windows [kT, (k+1)T), k < horizon/T,
    for every run. Only windows inside [0, horizon] are used, and every run must last at least the horizon, so
    every window has the same exposure T. Overlapping occurrences (during a stoppage) are counted."""
    if window_s <= 0 or horizon_s < window_s:
        raise ValueError("need 0 < window <= horizon")
    if (runs["end_time_s"] < horizon_s).any():
        raise ValueError("some runs end before the observation horizon; windows would have unequal exposure")
    windows = int(math.floor(horizon_s / window_s + 1e-12))
    by_seed = {seed: group["scheduled_time_s"].to_numpy(float) for seed, group in blockages.groupby("seed")}
    counts = []
    for seed in runs["seed"]:
        times = by_seed.get(seed, np.empty(0))
        index = np.floor(times / window_s).astype(int)
        index = index[(times >= 0) & (index < windows)]
        counts.extend(np.bincount(index, minlength=windows).tolist())
    return np.asarray(counts, dtype=int)


def interarrival_times(blockages: pd.DataFrame):
    """Gaps between consecutive scheduled occurrences inside each run (first gap measured from t=0)."""
    gaps = []
    for _, group in blockages.groupby("seed"):
        times = np.sort(group["scheduled_time_s"].to_numpy(float))
        gaps.extend(np.diff(np.concatenate([[0.0], times])).tolist())
    return np.asarray(gaps)


def stoppage_intervals(events: pd.DataFrame):
    """[start, end] of belt stoppages from BlockageStarted/BlockageEnded (distinct from Poisson occurrences)."""
    starts = events.loc[events.event_type == "BlockageStarted", "scheduled_time_s"].to_numpy(float)
    ends = events.loc[events.event_type == "BlockageEnded", "scheduled_time_s"].to_numpy(float)
    return np.column_stack([starts[:len(ends)], ends])


# ---------------------------------------------------------------------------------------------------------
# Operational reconstruction
# ---------------------------------------------------------------------------------------------------------

def queue_length_series(events: pd.DataFrame):
    """Scanner-queue length as a step function: +1 on ScanQueued, -1 on ScanStarted (both at the tick time_s).
    Returns (times, lengths) where lengths[i] holds on [times[i], times[i+1])."""
    delta = events.event_type.map({"ScanQueued": 1, "ScanStarted": -1}).fillna(0).astype(int)
    changes = pd.DataFrame({"t": events.time_s, "d": delta})[delta != 0]
    per_time = changes.groupby("t", sort=True)["d"].sum()
    times = np.concatenate([[0.0], per_time.index.to_numpy(float)])
    lengths = np.concatenate([[0], np.cumsum(per_time.to_numpy())])
    if (lengths < 0).any():
        raise ValueError("queue length became negative: inconsistent event log")
    return times, lengths


def time_average(times, values, end_time: float) -> float:
    durations = np.diff(np.concatenate([times, [end_time]]))
    return float(np.sum(values * durations) / end_time)
