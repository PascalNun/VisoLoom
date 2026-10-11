# Resolved third-party dependencies

Reviewed 2026-10-11. Direct versions are pinned centrally; every project commits its NuGet lockfile with resolved transitive hashes. The table includes all resolved NuGet packages, including development-only transitives. No GPL/AGPL package was added. VisoLoom’s own license remains undecided.

| Dependency | Resolved version | License evidence | Purpose / component | Distributed in plug-in? |
| --- | --- | --- | --- | --- |
| [Microsoft.CodeCoverage](https://www.nuget.org/packages/Microsoft.CodeCoverage/17.14.1) | 17.14.1 | MIT; pinned package metadata inspected | Test infrastructure; development only | No |
| [Microsoft.NET.Test.Sdk](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/17.14.1) | 17.14.1 | MIT; pinned package metadata inspected | Test infrastructure; development only | No |
| [Microsoft.TestPlatform.ObjectModel](https://www.nuget.org/packages/Microsoft.TestPlatform.ObjectModel/17.14.1) | 17.14.1 | MIT; pinned package metadata inspected | Test infrastructure; development only | No |
| [Microsoft.TestPlatform.TestHost](https://www.nuget.org/packages/Microsoft.TestPlatform.TestHost/17.14.1) | 17.14.1 | MIT; pinned package metadata inspected | Test infrastructure; development only | No |
| [Microsoft.Win32.SystemEvents](https://www.nuget.org/packages/Microsoft.Win32.SystemEvents/7.0.0) | 7.0.0 | MIT; pinned package metadata inspected | Rhino SDK transitive build references; host supplies compatible runtime | No |
| [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json/13.0.3) | 13.0.3 | MIT; pinned package metadata inspected | Test infrastructure; development only | No |
| [RhinoCommon](https://www.nuget.org/packages/RhinoCommon/8.21.25188.17001) | 8.21.25188.17001 | See review below; pinned package metadata inspected | Rhino/Eto build references; runtime supplied by Rhino | No |
| [System.Collections.Immutable](https://www.nuget.org/packages/System.Collections.Immutable/8.0.0) | 8.0.0 | MIT; pinned package metadata inspected | Test infrastructure; development only | No |
| [System.Drawing.Common](https://www.nuget.org/packages/System.Drawing.Common/7.0.0) | 7.0.0 | MIT; pinned package metadata inspected | Rhino SDK transitive build references; host supplies compatible runtime | No |
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

- **RhinoCommon 8.21.25188.17001:** the pinned NuGet package has no license expression or SDK license file; it contains a McNeel copyright notice and an `Eto-LICENSE.txt`. It must not be classified as MIT/BSD by inference. RhinoCommon/Rhino.UI/Eto/Ed.Eto are build references only and are excluded from product output. Confirm the applicable McNeel SDK/distribution terms before any binary release; compilation is not legal clearance.
- **Eto bundled in that exact RhinoCommon artifact:** BSD-3-Clause, read from `Eto-LICENSE.txt`. Host-provided only. Any later redistribution would require its copyright, conditions, disclaimer and non-endorsement provisions. Avoid adding a separate Eto package that could conflict with Rhino.
- **.NET / ASP.NET Core:** MIT plus platform third-party notices. SDK 10.0.201, local .NET 10.0.5 and isolated .NET 8.0.31 were used for verification. The server is framework-dependent; no runtime is bundled. [Runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [ASP.NET Core license](https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt). Recheck current servicing patches before distribution.
- **xunit.abstractions 2.0.3:** legacy package metadata points to the shared xUnit license URL rather than an embedded SPDX expression. The [abstractions upstream license](https://github.com/xunit/abstractions.xunit/blob/main/license.txt) is Apache-2.0; the 2.0.3 source tag does not contain that file. Retain this provenance caveat in any development-source redistribution audit. It is not shipped in the plug-in.
- **xUnit family / Visual Studio runner:** exact pinned package expressions are Apache-2.0. Preserve applicable license/NOTICE and separately noted source licenses if redistributing test tools or their source. The production plug-in contains none of them.
- **MIT test transitives:** preserve copyright and permission notices if distributing the test harness; they are not product runtime dependencies.
- **GitHub Actions:** checkout v4 at `11d5960a326750d5838078e36cf38b85af677262`, setup-dotnet v4 at `67a3573c9a986a3f9c594539f4ab511d57bb3ce9`; development infrastructure, not bundled product assets. Their action repositories provide their license/notices.

The first slice uses platform JSON, HTTP, SHA256 and file APIs. PNG encoding/decoding stays in Rhino/Eto; the server only validates bounded PNG metadata and copies bytes. No inference runtime, image-processing package, cloud SDK, Python dependency, model weights or copied upstream sample implementation is shipped.

NuGet vulnerability enumeration with `--include-transitive` reported no known vulnerable packages on 2026-10-11. This is advisory-feed evidence, not proof of absence of vulnerabilities or of host/runtime suitability. Capture/drawing behavior and Windows profile ACLs still require host validation.

Before distribution: verify McNeel SDK terms, review actual shipped files, select supported runtime patches, preserve any required third-party notices, validate native compatibility, and separately review future provider/model terms. Do not create a project LICENSE file until the user chooses the project license.
