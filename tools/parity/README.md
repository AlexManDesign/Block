# Parity / performance harness

Runs the Unity port's pure-logic code (generation, lighting, meshing) outside Unity and compares it
with the reference implementation (`reference/source/*.js`) executed in Node.

* `shim/UnityShim.cs` – managed re-implementation of the few UnityEngine math types the logic uses.
* `cs/` – .NET 8 harness (`gen`, `genbench`, `pipeline`, `meshparity`, `golden`).
* `mono/` – same harness built for net472 to run under the Mono JIT (close to Unity Editor speed).
  Unity Editor in *Debug* code-optimisation mode can be emulated with
  `mono --debug --debugger-agent=transport=dt_socket,server=y,suspend=n,address=127.0.0.1:55555`.
* `jsdump.mjs`, `jsmeshdump.mjs`, `jsbench.mjs`, `jsmeshbench.mjs` – run genWorker/meshWorker in Node.
* `compare_gen.py`, `compare_mesh.py`, `peek.py` – diff tools.
* `golden_expected.txt` – fingerprint of the current pipeline output; performance work must keep it identical.

`./run_checks.sh` runs everything.
