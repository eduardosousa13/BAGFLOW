"""Reference-only Python implementations of academic distributions.

NOT the Unity runtime. Used by M2/M5 to cross-check the C# logic.
"""
import math
import random


def uniform_open(rng: random.Random) -> float:
    """Uniform from (0,1), not including endpoints."""
    while True:
        x = rng.random()
        if 0.0 < x < 1.0:
            return x


def triangular(rng: random.Random, a: float, c: float, b: float) -> float:
    if not (math.isfinite(a) and math.isfinite(c) and math.isfinite(b) and a <= c <= b):
        raise ValueError("Require finite a<=c<=b")
    if a == b:
        return a
    u = uniform_open(rng)
    threshold = (c-a)/(b-a)
    if u < threshold:
        return a + math.sqrt(u * (b-a) * (c-a))
    return b - math.sqrt((1-u) * (b-a) * (b-c))


def exponential(rng: random.Random, rate: float) -> float:
    if not (math.isfinite(rate) and rate > 0):
        raise ValueError("rate must be finite and >0")
    return -math.log1p(-uniform_open(rng))/rate


def truncated_normal(rng: random.Random, mu: float, sigma: float,
                     lower: float, upper: float, max_tries: int = 100_000) -> float:
    if not all(map(math.isfinite, (mu, sigma, lower, upper))) or not (sigma > 0 and lower < upper):
        raise ValueError("invalid truncated normal parameters")
    for _ in range(max_tries):
        u1 = uniform_open(rng)
        u2 = uniform_open(rng)
        z = math.sqrt(-2 * math.log(u1)) * math.cos(2 * math.pi * u2)
        v = mu + sigma * z
        if lower <= v <= upper:
            return v
    raise ValueError("Truncation acceptance probability too small")


def bernoulli(rng: random.Random, p: float) -> bool:
    if not math.isfinite(p) or not 0 <= p <= 1:
        raise ValueError("p must be in [0,1]")
    return rng.random() < p


def binomial_batch(rng: random.Random, n: int, p: float) -> int:
    if not isinstance(n, int) or n < 0:
        raise ValueError("n must be a non-negative integer")
    return sum(bernoulli(rng,p) for _ in range(n))


def poisson_events(rng: random.Random, rate: float, horizon: float) -> list[float]:
    if not (math.isfinite(rate) and rate >= 0 and math.isfinite(horizon) and horizon >= 0):
        raise ValueError("invalid rate or horizon")
    if rate == 0:
        return []
    t = 0.0
    values = []
    while True:
        t += exponential(rng,rate)
        if t > horizon:
            break
        values.append(t)
        if len(values)>1_000_000:
            raise ValueError("too many events for reference test")
    return values
