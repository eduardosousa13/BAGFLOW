# Independent mathematical reference

`reference_samplers.py` contains independent, pure-Python reference algorithms for the approved distributions. Not part of the Unity app; use it to cross-check C# implementations. The `test_reference.py` suite uses Python standard library only.

Run from this repository root: `python -m unittest discover -s validation -p 'test_reference.py' -v`.

M5 will add a Jupyter notebook generating FDP/FMP/CDF overlays on real exports, with NumPy/SciPy/Matplotlib, separately for sampler results and full simulation results.
