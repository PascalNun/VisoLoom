# Local engine runtime inventory

Development resolution for Apple Silicon, Python 3.14 and this Mac's macOS wheel set, 2026-10-15. The [hash lock](../../engine/requirements-mac-arm64-py314.lock) records all 56 selected artifacts. This is not the Windows profile or a release installer. Exact wheel metadata was read during resolution; the MFLUX API and selected wheel notices were inspected separately. Runtime provisioning and the live result are tracked in the [implementation review](../development/phase-4-review.md).

Every package is an external engine runtime dependency, never a Rhino plug-in assembly. None of these wheels is committed or bundled in VisoLoom. The isolated environment is user-installed development tooling. No package source is modified. Model weights and their encoder terms are separate.

| Package | Version | Licence evidence / caveat |
| --- | --- | --- |
| [annotated-doc](https://pypi.org/project/annotated-doc/0.0.5/) | 0.0.5 | MIT |
| [anyio](https://pypi.org/project/anyio/4.15.1/) | 4.15.1 | MIT |
| [certifi](https://pypi.org/project/certifi/2026.7.22/) | 2026.7.22 | MPL-2.0 |
| [charset-normalizer](https://pypi.org/project/charset-normalizer/3.5.2/) | 3.5.2 | MIT |
| [click](https://pypi.org/project/click/8.5.0/) | 8.5.0 | BSD-3-Clause |
| [contourpy](https://pypi.org/project/contourpy/1.4.0/) | 1.4.0 | BSD-3-Clause |
| [cycler](https://pypi.org/project/cycler/0.12.1/) | 0.12.1 | BSD-3-Clause |
| [filelock](https://pypi.org/project/filelock/4.1.2/) | 4.1.2 | MIT |
| [fonttools](https://pypi.org/project/fonttools/4.67.0/) | 4.67.0 | MIT |
| [fsspec](https://pypi.org/project/fsspec/2026.9.0/) | 2026.9.0 | BSD-3-Clause |
| [h11](https://pypi.org/project/h11/0.16.0/) | 0.16.0 | MIT |
| [hf-xet](https://pypi.org/project/hf-xet/1.7.0/) | 1.7.0 | Apache-2.0 |
| [hf_transfer](https://pypi.org/project/hf_transfer/0.1.9/) | 0.1.9 | Apache-2.0 (wheel LICENSE inspected) |
| [httpcore](https://pypi.org/project/httpcore/1.0.9/) | 1.0.9 | BSD-3-Clause |
| [httpx](https://pypi.org/project/httpx/0.28.1/) | 0.28.1 | BSD-3-Clause |
| [huggingface_hub](https://pypi.org/project/huggingface_hub/1.33.0/) | 1.33.0 | Apache-2.0 |
| [idna](https://pypi.org/project/idna/3.20/) | 3.20 | BSD-3-Clause |
| [Jinja2](https://pypi.org/project/Jinja2/3.1.6/) | 3.1.6 | BSD-3-Clause |
| [kiwisolver](https://pypi.org/project/kiwisolver/1.5.1/) | 1.5.1 | BSD-3-Clause |
| [markdown-it-py](https://pypi.org/project/markdown-it-py/4.2.0/) | 4.2.0 | MIT |
| [MarkupSafe](https://pypi.org/project/MarkupSafe/3.0.4/) | 3.0.4 | BSD-3-Clause |
| [matplotlib](https://pypi.org/project/matplotlib/3.11.2/) | 3.11.2 | Matplotlib/PSF-style; bundled native notices need release audit |
| [mdurl](https://pypi.org/project/mdurl/0.1.2/) | 0.1.2 | MIT |
| [mflux](https://pypi.org/project/mflux/0.22.0/) | 0.22.0 | MIT |
| [mlx](https://pypi.org/project/mlx/0.32.3/) | 0.32.3 | MIT |
| [mlx-metal](https://pypi.org/project/mlx-metal/0.32.3/) | 0.32.3 | MIT |
| [mpmath](https://pypi.org/project/mpmath/1.3.0/) | 1.3.0 | BSD-3-Clause |
| [networkx](https://pypi.org/project/networkx/3.7/) | 3.7 | BSD-3-Clause |
| [numpy](https://pypi.org/project/numpy/2.5.4/) | 2.5.4 | BSD-3-Clause AND 0BSD AND MIT AND Zlib AND CC0-1.0 |
| [opencv-python](https://pypi.org/project/opencv-python/4.14.0.94/) | 4.14.0.94 | MIT wrapper; Apache-2.0 OpenCV; LGPL-2.1 FFmpeg and listed macOS libraries; FreeType License |
| [packaging](https://pypi.org/project/packaging/26.3/) | 26.3 | Apache-2.0 OR BSD-2-Clause |
| [piexif](https://pypi.org/project/piexif/1.1.3/) | 1.1.3 | MIT |
| [pillow](https://pypi.org/project/pillow/12.3.0/) | 12.3.0 | MIT-CMU |
| [platformdirs](https://pypi.org/project/platformdirs/4.13.0/) | 4.13.0 | MIT |
| [protobuf](https://pypi.org/project/protobuf/7.36.2/) | 7.36.2 | BSD-3-Clause |
| [Pygments](https://pypi.org/project/Pygments/2.21.0/) | 2.21.0 | BSD-2-Clause |
| [pyparsing](https://pypi.org/project/pyparsing/3.3.3/) | 3.3.3 | MIT |
| [python-dateutil](https://pypi.org/project/python-dateutil/2.9.0.post0/) | 2.9.0.post0 | Apache-2.0 OR BSD-3-Clause |
| [PyYAML](https://pypi.org/project/PyYAML/6.0.3/) | 6.0.3 | MIT |
| [regex](https://pypi.org/project/regex/2026.9.29/) | 2026.9.29 | Apache-2.0 AND CNRI-Python |
| [requests](https://pypi.org/project/requests/2.34.2/) | 2.34.2 | Apache-2.0 |
| [rich](https://pypi.org/project/rich/15.0.0/) | 15.0.0 | MIT |
| [safetensors](https://pypi.org/project/safetensors/0.8.0/) | 0.8.0 | Apache-2.0 (wheel LICENSE inspected) |
| [sentencepiece](https://pypi.org/project/sentencepiece/0.2.2/) | 0.2.2 | Apache-2.0 |
| [setuptools](https://pypi.org/project/setuptools/84.0.0/) | 84.0.0 | MIT |
| [shellingham](https://pypi.org/project/shellingham/1.5.4/) | 1.5.4 | ISC |
| [six](https://pypi.org/project/six/1.17.0/) | 1.17.0 | MIT |
| [sympy](https://pypi.org/project/sympy/1.14.0/) | 1.14.0 | BSD-3-Clause |
| [tokenizers](https://pypi.org/project/tokenizers/0.23.3/) | 0.23.3 | Apache-2.0 (upstream Hugging Face tokenizers) |
| [toml](https://pypi.org/project/toml/0.10.2/) | 0.10.2 | MIT |
| [torch](https://pypi.org/project/torch/2.14.1/) | 2.14.1 | Apache-2.0 AND Apache-2.0 WITH LLVM-exception AND BSD-2-Clause AND BSD-3-Clause AND BSL-1.0 AND MIT |
| [tqdm](https://pypi.org/project/tqdm/4.70.1/) | 4.70.1 | MPL-2.0 AND MIT |
| [transformers](https://pypi.org/project/transformers/5.19.0/) | 5.19.0 | Apache-2.0 |
| [typer](https://pypi.org/project/typer/0.27.3/) | 0.27.3 | MIT |
| [typing_extensions](https://pypi.org/project/typing_extensions/4.16.0/) | 4.16.0 | PSF-2.0 |
| [urllib3](https://pypi.org/project/urllib3/2.8.0/) | 2.8.0 | MIT |

## Conditional terms and release boundary

- **OpenCV macOS wheel:** its `LICENSE-3RD-PARTY.txt` explicitly lists FFmpeg and macOS libraries under LGPL-2.1, plus FreeType under its own permissive FreeType License. The development decision permits an unmodified, separate local environment only. No GPL engine is selected and none of these binaries is shipped with Rhino. A future bundled engine needs an artifact-level review of linking, corresponding-source/relinking requirements and notices; this experiment does not clear redistribution. Do not label the entire wheel Apache-2.0 merely because the OpenCV library is.
- **tqdm and certifi:** MPL-2.0 applies to their covered files/data (tqdm also contains MIT contributions). Keep them unmodified in the external environment. Preserve notices and satisfy applicable source availability obligations before redistribution. Their file-level terms do not determine the licence of VisoLoom's separate code.
- **Native/transitive notices:** package metadata is a starting inventory, not a full binary bill of materials. Review bundled native libraries in PyTorch, NumPy, Matplotlib, Pillow, OpenCV, tokenizers and other compiled wheels before release. Missing licence files or incomplete wheel metadata require upstream evidence and notices, not a guessed classification.
- **Model:** the official `black-forest-labs/FLUX.2-klein-4B` model card declares Apache-2.0; its Qwen3-4B encoder upstream also declares Apache-2.0. The selected model revision, hashes and downloaded file inventory stay alongside the model on the external drive. No community quantisation, encoder replacement or gated-account credential is used.

This resolution pins current-macOS arm64 wheels, including MLX Metal 0.32.3. It makes no minimum-macOS, all-Rhino-8-host, Windows GPU or general hardware support claim. Those need separate runtime profiles and native tests. VisoLoom's own licence remains undecided; no project LICENSE file is added.
