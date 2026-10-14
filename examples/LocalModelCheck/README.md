# Local FLUX editing check

This is a development experiment for an existing Draw Things installation, not a required VisoLoom installation step. The production local-model selector, managed engine and home/office server UI remain separate work. See [ADR 0017](../../docs/adr/0017-local-inference-and-model-prompts.md).

Create a separate test project in Draw Things, then select the installed **FLUX.2 klein 4B** (`flux_2_klein_4b_q8p.ckpt`). Confirm cloud/server offloading is disabled. Only enable its HTTP API for the test if it listens on this computer's loopback address; do not expose its unauthenticated HTTP service on your network. Stop the service when finished. The VisoLoom reference server keeps its existing authentication.

Check the selected model without sending an image:

```sh
dotnet run --project examples/LocalModelCheck -- check http://127.0.0.1:7860/
```

Prepare a PNG with each edge divisible by 64, at least 128 and at most 1536 pixels, and at most 1.5 megapixels. Preserve the whole source frame by uniform scaling and documented padding; do not stretch or crop it. This check deliberately rejects unsupported input rather than silently transforming it. Review the preset before editing:

```sh
dotnet run --project examples/LocalModelCheck -- edit http://127.0.0.1:7860/ /absolute/path/prepared.png examples/presets/competition-sketch.json
```

The client sends one PNG, the preset/geometry-preservation instructions and no scene JSON. It uses four steps, guidance 1, strength 1 and seed 1, with additional controls, LoRAs, refiner and upscaler disabled. These are a Klein baseline, not a cross-model fidelity control. Some other settings still come from the app; do not call this a fully reproducible production profile. The 2048-byte prompt budget is not an exact token count, and overlong input is rejected intact.

The result, prepared reference and exact prompt/hash/settings record are saved in a new private, Git-ignored `artifacts/local-flux` directory. Output dimensions must match the prepared input. This tool does not recover a padding/scaling plan, update Rhino, or automatically compare geometry. Compare openings, sections, object placement and frame against the original. Do not interpret matching pixel dimensions as matching architecture.

Generation is submitted once. A lost response or stopped wait does not prove cancellation; check Draw Things before starting another operation. Automated transport checks use a fake engine and never start inference. Private captures/results must not be committed.
