# Resolved third-party dependencies

Reviewed 2026-10-11. Direct versions are pinned centrally; every project commits its NuGet lockfile with resolved transitive hashes. The table includes all resolved NuGet packages, including development-only transitives. No GPL/AGPL package was added. VisoLoom’s own source is licensed GPL-3.0-or-later (decided 2026-11-21, [licence status](licensing-status.md)); the dependency review and its rules are unchanged.

| Dependency | Resolved version | License evidence | Purpose / component | Distributed in plug-in? |
| --- | --- | --- | --- | --- |
| [Microsoft.CodeCoverage](https://www.nuget.org/packages/Microsoft.CodeCoverage/17.14.1) | 17.14.1 | MIT; pinned package metadata inspected | Test infrastructure; development only | No |
| [Microsoft.NET.Test.Sdk](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/17.14.1) | 17.14.1 | MIT; pinned package metadata inspected | Test infrastructure; development only | No |
| [Microsoft.TestPlatform.ObjectModel](https://www.nuget.org/packages/Microsoft.TestPlatform.ObjectModel/17.14.1) | 17.14.1 | MIT; pinned package metadata inspected | Test infrastructure; development only | No |
| [Microsoft.TestPlatform.TestHost](https://www.nuget.org/packages/Microsoft.TestPlatform.TestHost/17.14.1) | 17.14.1 | MIT; pinned package metadata inspected | Test infrastructure; development only | No |
| [Microsoft.Win32.SystemEvents](https://www.nuget.org/packages/Microsoft.Win32.SystemEvents/7.0.0) | 7.0.0 | MIT; pinned package metadata inspected | Drawing transitive build reference; host supplies compatible runtime | No |
| [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json/13.0.3) | 13.0.3 | MIT; pinned package metadata inspected | Test infrastructure; development only | No |
| [RhinoCommon](https://www.nuget.org/packages/RhinoCommon/8.0.23304.9001) | 8.0.23304.9001 | See review below; pinned package metadata inspected | Original Rhino 8/Eto build references; runtime supplied by Rhino | No |
| [System.Collections.Immutable](https://www.nuget.org/packages/System.Collections.Immutable/8.0.0) | 8.0.0 | MIT; pinned package metadata inspected | Test infrastructure; development only | No |
| [System.Drawing.Common](https://www.nuget.org/packages/System.Drawing.Common/7.0.0) | 7.0.0 | MIT; pinned package metadata inspected | Explicit compile-only reference; host supplies compatible runtime | No |
| [System.Reflection.Metadata](https://www.nuget.org/packages/System.Reflection.Metadata/8.0.0) | 8.0.0 | MIT; pinned package metadata inspected | Test infrastructure; development only | No |
| [xunit](https://www.nuget.org/packages/xunit/2.9.3) | 2.9.3 | Apache-2.0; pinned package metadata inspected | Test infrastructure; development only | No |
| [xunit.abstractions](https://www.nuget.org/packages/xunit.abstractions/2.0.3) | 2.0.3 | See review below; pinned package metadata inspected | Test infrastructure; development only | No |
| [xunit.analyzers](https://www.nuget.org/packages/xunit.analyzers/1.18.0) | 1.18.0 | Apache-2.0; pinned package metadata inspected | Test infrastructure; development only | No |
| [xunit.assert](https://www.nuget.org/packages/xunit.assert/2.9.3) | 2.9.3 | Apache-2.0; pinned package metadata inspected | Test infrastructure; development only | No |
| [xunit.core](https://www.nuget.org/packages/xunit.core/2.9.3) | 2.9.3 | Apache-2.0; pinned package metadata inspected | Test infrastructure; development only | No |
| [xunit.extensibility.core](https://www.nuget.org/packages/xunit.extensibility.core/2.9.3) | 2.9.3 | Apache-2.0; pinned package metadata inspected | Test infrastructure; development only | No |
| [xunit.extensibility.execution](https://www.nuget.org/packages/xunit.extensibility.execution/2.9.3) | 2.9.3 | Apache-2.0; pinned package metadata inspected | Test infrastructure; development only | No |
| [xunit.runner.visualstudio](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.5) | 3.1.5 | Apache-2.0; pinned package metadata inspected | Test infrastructure; development only | No |

## Host and platform components

The OpenAI integration adds no NuGet package or cloud SDK. Optional credential storage calls the OS-provided Security/CoreFoundation frameworks on macOS and Advapi32 Credential Manager APIs on Windows; these native components are used from the installed OS and are not redistributed. Image processing uses Rhino's existing drawing implementation, not a bundled image library. Native host/credential acceptance is tracked in [ADR 0012](../adr/0012-first-openai-image-edit.md).

- **RhinoCommon 8.0.23304.9001:** the original stable Rhino 8 NuGet package has no license expression or SDK license file; it contains a McNeel copyright notice and an `Eto-LICENSE.txt`. It must not be classified as MIT/BSD by inference. RhinoCommon/Rhino.UI/Eto/Ed.Eto are build references only and are excluded from product output. Confirm the applicable McNeel SDK/distribution terms before any binary release; compilation is not legal clearance.
- **Eto bundled in that exact RhinoCommon artifact:** BSD-3-Clause, read from `Eto-LICENSE.txt`. Host-provided only. Any later redistribution would require its copyright, conditions, disclaimer and non-endorsement provisions. Avoid adding a separate Eto package that could conflict with Rhino.
- **.NET / ASP.NET Core:** MIT plus platform third-party notices. SDK 10.0.201, local .NET 10.0.5 and isolated .NET 8.0.31 were used for verification. The plug-in/shared libraries target .NET 7 for Rhino's default .NET 7 and .NET 8 hosts; a client compatibility probe used Rhino 8.16's existing bundled .NET 7.0.0. The server is framework-dependent; no runtime is bundled or changed in Rhino. [Runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [ASP.NET Core license](https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt). Recheck current servicing patches before distribution.
- **xunit.abstractions 2.0.3:** legacy package metadata points to the shared xUnit license URL rather than an embedded SPDX expression. The [abstractions upstream license](https://github.com/xunit/abstractions.xunit/blob/main/license.txt) is Apache-2.0; the 2.0.3 source tag does not contain that file. Retain this provenance caveat in any development-source redistribution audit. It is not shipped in the plug-in.
- **xUnit family / Visual Studio runner:** exact pinned package expressions are Apache-2.0. Preserve applicable license/NOTICE and separately noted source licenses if redistributing test tools or their source. The production plug-in contains none of them.
- **MIT test transitives:** preserve copyright and permission notices if distributing the test harness; they are not product runtime dependencies.
- **GitHub Actions:** checkout v4 at `11d5960a326750d5838078e36cf38b85af677262`, setup-dotnet v4 at `67a3573c9a986a3f9c594539f4ab511d57bb3ce9`; development infrastructure, not bundled product assets. Their action repositories provide their license/notices.

The first slice uses platform JSON, HTTP, SHA256 and file APIs. PNG encoding/decoding stays in Rhino/Eto; the server only validates bounded PNG metadata and copies bytes. No inference runtime, image-processing package, cloud SDK, Python dependency, model weights or copied upstream sample implementation is shipped.

## Optional managed-engine prototype — 2026-10-15

The new [engine controller](../../engine/README.md) and its fixture tests use Python's standard library. The optional Mac worker imports an external inference stack in a separate process; it is not copied into the plug-in output. An isolated development runtime was installed from a 56-wheel hash lock; the [resolved inventory and conditional-licence review](local-engine-runtime.md) records all package versions and their role. Official model files were explicitly provisioned on the user's external drive and verified against a fixed revision. No wheel or model is vendored, committed or bundled in VisoLoom. The controller does not install packages or fetch weights.

| Candidate | Version / evidence | Terms | Role / distribution status |
| --- | --- | --- | --- |
| [MFLUX](https://github.com/mflux-community/mflux) | Worker requires 0.22.0; PyPI wheel inspected, SHA256 `b3c863f1e64718f1fed657448307d00a5702870b9e8fce18c547f5a16f9851dd` | [Upstream MIT](https://github.com/mflux-community/mflux/blob/main/LICENSE); retain notices if distributed | Optional external Mac development runtime; installed separately, not bundled |
| [FLUX.2 klein 4B](https://huggingface.co/black-forest-labs/FLUX.2-klein-4B) | Official model revision `e7b7dc27f91deacad38e78976d1f2b499d76a294`; 18 verified files | Model card declares Apache-2.0; Qwen3-4B encoder upstream also declares Apache-2.0; retain artifact notices | Provisioned on user's external drive for a local test; not bundled |
| [Diffusers](https://github.com/huggingface/diffusers) / PyTorch | No selected version or dependency | Review exact artifacts/transitives before installation | Proposed Windows/NVIDIA candidate; no implementation or installation |

The Mac development hash lock pins the selected complete Python environment, including MLX, PyTorch, Transformers, Hugging Face Hub and Pillow. Other platforms need separate locks. Package/native redistribution review is not complete; OpenCV's bundled LGPL libraries and the MPL-covered packages are explicitly evaluated for unmodified external development use only. Do not infer clearance of the complete stack from MFLUX's MIT licence. The source adapter/fixture tests do not establish real runtime compatibility or security. There is no runtime/model installer yet and VisoLoom's own licensing remains undecided.

NuGet vulnerability enumeration with `--include-transitive` reported no known vulnerable packages on 2026-10-11. This is advisory-feed evidence, not proof of absence of vulnerabilities or of host/runtime suitability. Capture/drawing behavior and Windows profile ACLs still require host validation.

Before distribution: verify McNeel SDK terms, review actual shipped files, select supported runtime patches, preserve any required third-party notices, validate native compatibility, and separately review future provider/model terms. The project licence was decided 2026-11-21 as GPL-3.0-or-later and the root `LICENSE` is present; the CI guard that previously failed the packaged build on a LICENSE file was removed with the decision.
