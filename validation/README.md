# Independent mathematical reference

`reference_samplers.py` contains independent, pure-Python reference algorithms for the approved distributions. Not part of the Unity app; use it to cross-check C# implementations. The `test_reference.py` suite uses Python standard library only.

Run from this repository root: `python -m unittest discover -s validation -p 'test_reference.py' -v`.

## M5 validation of Unity exports

* `bagflow_analysis.py` — loaders for the Unity exports, theoretical laws from `docs/MATH_MODELS.md`, empirical statistics.
* `test_analysis.py` — tests (run all: `python -m unittest discover -s validation -p "test_*.py" -v`).
* `BAGFLOW_validation.ipynb` — executed notebook (source: `build_notebook.py`); figures in `figures/`.
* `data/unity/` — datasets exported by Unity (see `docs/M5_VALIDATION_EXPORT.md` to regenerate).
