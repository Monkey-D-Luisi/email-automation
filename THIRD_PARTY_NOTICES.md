# Third-party notices

Original Smart Inbox source is licensed under [MIT](LICENSE). Third-party components retain their own copyright, license, and NOTICE files. This inventory records package metadata reviewed on 2026-10-29; it does not relicense those components.

This repository distributes source and package references, not vendored package binaries or Docker images. Restoring dependencies retains their upstream license files. When distributing a built application, installer, or image, also retain the licenses, copyright notices, NOTICE files, and any source/relinking obligations of the actual bundled components; this table is not a substitute for them. Frameworks and container operating-system packages have their own additional notices.

## Optional runtimes and notable terms

- MediatR is pinned to **12.5.0**, whose package metadata and [versioned upstream license](https://github.com/LuckyPennySoftware/MediatR/blob/v12.5.0/LICENSE) identify Apache-2.0. Do not assume later major versions have identical terms.
- The previously evaluated n8n 2.45.6 Community runtime has its own [Sustainable Use License](https://github.com/n8n-io/n8n/blob/n8n%402.45.6/LICENSE.md) and additional enterprise terms. This source release retains workflow templates but suspends runtime provisioning: no n8n image is bundled or recommended. Smart Inbox's MIT license does not relicense n8n. See [n8n's license guidance](https://support.n8n.io/article/can-i-use-your-license-for-my-use-case) before any future redistribution or embedding.
- PostgreSQL uses the [PostgreSQL License](https://www.postgresql.org/about/licence/). Compose downloads its image separately.
- Next.js transitively references Sharp and platform-specific libvips binaries. The lockfile includes **LGPL-3.0-or-later** libvips packages and a combined-license WASM package. Review the actual platform bundle's notices and [Sharp licensing guidance](https://sharp.pixelplumbing.com/install/#licensing) before distributing native binaries or images.
- `caniuse-lite` data is attributed to its upstream [Can I Use authors](https://github.com/browserslist/caniuse-lite) under CC-BY-4.0.
- Gitleaks, used only by CI/developer checks, is [MIT licensed](https://github.com/gitleaks/gitleaks/blob/v8.30.1/LICENSE). GitHub Actions are pinned to reviewed revisions and retain their upstream licenses.

## NuGet packages

Resolved direct, transitive, test, and build packages from the eight project assets files. NuGet metadata is the source for the license expressions; package pages provide upstream attribution and license details.

| Package | Version | License metadata |
| --- | --- | --- |
| [Castle.Core](https://www.nuget.org/packages/Castle.Core/5.1.1) | 5.1.1 | Apache-2.0 |
| [FluentValidation.DependencyInjectionExtensions](https://www.nuget.org/packages/FluentValidation.DependencyInjectionExtensions/12.2.0) | 12.2.0 | Apache-2.0 |
| [FluentValidation](https://www.nuget.org/packages/FluentValidation/12.2.0) | 12.2.0 | Apache-2.0 |
| [Humanizer.Core](https://www.nuget.org/packages/Humanizer.Core/2.14.1) | 2.14.1 | MIT |
| [MediatR.Contracts](https://www.nuget.org/packages/MediatR.Contracts/2.0.1) | 2.0.1 | Apache-2.0 |
| [MediatR](https://www.nuget.org/packages/MediatR/12.5.0) | 12.5.0 | Apache-2.0 |
| [Microsoft.AspNetCore.Mvc.Testing](https://www.nuget.org/packages/Microsoft.AspNetCore.Mvc.Testing/10.0.13) | 10.0.13 | MIT |
| [Microsoft.AspNetCore.TestHost](https://www.nuget.org/packages/Microsoft.AspNetCore.TestHost/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Build.Framework](https://www.nuget.org/packages/Microsoft.Build.Framework/18.0.2) | 18.0.2 | MIT |
| [Microsoft.CodeAnalysis.Analyzers](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Analyzers/3.11.0) | 3.11.0 | MIT |
| [Microsoft.CodeAnalysis.Common](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Common/5.0.0) | 5.0.0 | MIT |
| [Microsoft.CodeAnalysis.CSharp.Workspaces](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp.Workspaces/5.0.0) | 5.0.0 | MIT |
| [Microsoft.CodeAnalysis.CSharp](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/5.0.0) | 5.0.0 | MIT |
| [Microsoft.CodeAnalysis.Workspaces.Common](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Workspaces.Common/5.0.0) | 5.0.0 | MIT |
| [Microsoft.CodeAnalysis.Workspaces.MSBuild](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Workspaces.MSBuild/5.0.0) | 5.0.0 | MIT |
| [Microsoft.CodeCoverage](https://www.nuget.org/packages/Microsoft.CodeCoverage/18.11.0) | 18.11.0 | MIT |
| [Microsoft.EntityFrameworkCore.Abstractions](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Abstractions/10.0.13) | 10.0.13 | MIT |
| [Microsoft.EntityFrameworkCore.Analyzers](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Analyzers/10.0.13) | 10.0.13 | MIT |
| [Microsoft.EntityFrameworkCore.Design](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Design/10.0.13) | 10.0.13 | MIT |
| [Microsoft.EntityFrameworkCore.Relational](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Relational/10.0.13) | 10.0.13 | MIT |
| [Microsoft.EntityFrameworkCore](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Caching.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Caching.Abstractions/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Caching.Memory](https://www.nuget.org/packages/Microsoft.Extensions.Caching.Memory/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Configuration.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Abstractions/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Configuration.Binder](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Binder/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Configuration.CommandLine](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.CommandLine/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Configuration.EnvironmentVariables](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.EnvironmentVariables/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Configuration.FileExtensions](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.FileExtensions/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Configuration.Json](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Json/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Configuration.UserSecrets](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.UserSecrets/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Configuration](https://www.nuget.org/packages/Microsoft.Extensions.Configuration/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.DependencyInjection.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection.Abstractions/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.DependencyInjection.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection.Abstractions/8.0.0) | 8.0.0 | MIT |
| [Microsoft.Extensions.DependencyInjection](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.DependencyModel](https://www.nuget.org/packages/Microsoft.Extensions.DependencyModel/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Diagnostics.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.Abstractions/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Diagnostics.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.Abstractions/10.0.9) | 10.0.9 | MIT |
| [Microsoft.Extensions.Diagnostics](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.FileProviders.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.FileProviders.Abstractions/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.FileProviders.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.FileProviders.Abstractions/10.0.9) | 10.0.9 | MIT |
| [Microsoft.Extensions.FileProviders.Physical](https://www.nuget.org/packages/Microsoft.Extensions.FileProviders.Physical/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.FileSystemGlobbing](https://www.nuget.org/packages/Microsoft.Extensions.FileSystemGlobbing/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Hosting.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Hosting.Abstractions/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Hosting.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Hosting.Abstractions/10.0.9) | 10.0.9 | MIT |
| [Microsoft.Extensions.Hosting](https://www.nuget.org/packages/Microsoft.Extensions.Hosting/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Logging.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Logging.Configuration](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Configuration/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Logging.Console](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Console/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Logging.Debug](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Debug/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Logging.EventLog](https://www.nuget.org/packages/Microsoft.Extensions.Logging.EventLog/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Logging.EventSource](https://www.nuget.org/packages/Microsoft.Extensions.Logging.EventSource/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Logging](https://www.nuget.org/packages/Microsoft.Extensions.Logging/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Options.ConfigurationExtensions](https://www.nuget.org/packages/Microsoft.Extensions.Options.ConfigurationExtensions/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Options](https://www.nuget.org/packages/Microsoft.Extensions.Options/10.0.13) | 10.0.13 | MIT |
| [Microsoft.Extensions.Primitives](https://www.nuget.org/packages/Microsoft.Extensions.Primitives/10.0.13) | 10.0.13 | MIT |
| [Microsoft.NET.Test.Sdk](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.11.0) | 18.11.0 | MIT |
| [Microsoft.TestPlatform.ObjectModel](https://www.nuget.org/packages/Microsoft.TestPlatform.ObjectModel/18.11.0) | 18.11.0 | MIT |
| [Microsoft.TestPlatform.TestHost](https://www.nuget.org/packages/Microsoft.TestPlatform.TestHost/18.11.0) | 18.11.0 | MIT |
| [Microsoft.VisualStudio.SolutionPersistence](https://www.nuget.org/packages/Microsoft.VisualStudio.SolutionPersistence/1.0.52) | 1.0.52 | MIT |
| [Mono.TextTemplating](https://www.nuget.org/packages/Mono.TextTemplating/3.0.0) | 3.0.0 | MIT |
| [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json/13.0.4) | 13.0.4 | MIT |
| [Npgsql.EntityFrameworkCore.PostgreSQL](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/10.0.3) | 10.0.3 | PostgreSQL |
| [Npgsql](https://www.nuget.org/packages/Npgsql/10.0.3) | 10.0.3 | PostgreSQL |
| [NSubstitute](https://www.nuget.org/packages/NSubstitute/5.3.0) | 5.3.0 | BSD-3-Clause |
| [OpenAI](https://www.nuget.org/packages/OpenAI/2.14.0) | 2.14.0 | MIT |
| [System.ClientModel](https://www.nuget.org/packages/System.ClientModel/1.15.0) | 1.15.0 | MIT |
| [System.CodeDom](https://www.nuget.org/packages/System.CodeDom/6.0.0) | 6.0.0 | MIT |
| [System.Composition.AttributedModel](https://www.nuget.org/packages/System.Composition.AttributedModel/9.0.0) | 9.0.0 | MIT |
| [System.Composition.Convention](https://www.nuget.org/packages/System.Composition.Convention/9.0.0) | 9.0.0 | MIT |
| [System.Composition.Hosting](https://www.nuget.org/packages/System.Composition.Hosting/9.0.0) | 9.0.0 | MIT |
| [System.Composition.Runtime](https://www.nuget.org/packages/System.Composition.Runtime/9.0.0) | 9.0.0 | MIT |
| [System.Composition.TypedParts](https://www.nuget.org/packages/System.Composition.TypedParts/9.0.0) | 9.0.0 | MIT |
| [System.Composition](https://www.nuget.org/packages/System.Composition/9.0.0) | 9.0.0 | MIT |
| [System.Diagnostics.EventLog](https://www.nuget.org/packages/System.Diagnostics.EventLog/10.0.13) | 10.0.13 | MIT |
| [System.Diagnostics.EventLog](https://www.nuget.org/packages/System.Diagnostics.EventLog/6.0.0) | 6.0.0 | MIT |
| [System.Memory.Data](https://www.nuget.org/packages/System.Memory.Data/10.0.9) | 10.0.9 | MIT |
| [xunit.abstractions](https://www.nuget.org/packages/xunit.abstractions/2.0.3) | 2.0.3 | [Upstream license](https://raw.githubusercontent.com/xunit/xunit/master/license.txt) |
| [xunit.analyzers](https://www.nuget.org/packages/xunit.analyzers/1.18.0) | 1.18.0 | Apache-2.0 |
| [xunit.assert](https://www.nuget.org/packages/xunit.assert/2.9.3) | 2.9.3 | Apache-2.0 |
| [xunit.core](https://www.nuget.org/packages/xunit.core/2.9.3) | 2.9.3 | Apache-2.0 |
| [xunit.extensibility.core](https://www.nuget.org/packages/xunit.extensibility.core/2.9.3) | 2.9.3 | Apache-2.0 |
| [xunit.extensibility.execution](https://www.nuget.org/packages/xunit.extensibility.execution/2.9.3) | 2.9.3 | Apache-2.0 |
| [xunit.runner.visualstudio](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.5) | 3.1.5 | Apache-2.0 |
| [xunit](https://www.nuget.org/packages/xunit/2.9.3) | 2.9.3 | Apache-2.0 |

## npm packages

All entries in `web/package-lock.json`, including development and optional packages for other platforms. An optional entry does not mean that binary is installed on every machine. License expressions are taken from the lockfile and installed package metadata.

| Package | Version | License metadata | Scope |
| --- | --- | --- | --- |
| [@emnapi/runtime](https://www.npmjs.com/package/@emnapi/runtime/v/1.11.3) | 1.11.3 | MIT | optional platform/runtime |
| [@img/colour](https://www.npmjs.com/package/@img/colour/v/1.1.0) | 1.1.0 | MIT | optional platform/runtime |
| [@img/sharp-darwin-arm64](https://www.npmjs.com/package/@img/sharp-darwin-arm64/v/0.35.5) | 0.35.5 | Apache-2.0 | optional platform/runtime |
| [@img/sharp-darwin-x64](https://www.npmjs.com/package/@img/sharp-darwin-x64/v/0.35.5) | 0.35.5 | Apache-2.0 | optional platform/runtime |
| [@img/sharp-freebsd-wasm32](https://www.npmjs.com/package/@img/sharp-freebsd-wasm32/v/0.35.5) | 0.35.5 | Apache-2.0 | optional platform/runtime |
| [@img/sharp-libvips-darwin-arm64](https://www.npmjs.com/package/@img/sharp-libvips-darwin-arm64/v/1.3.4) | 1.3.4 | LGPL-3.0-or-later | optional platform/runtime |
| [@img/sharp-libvips-darwin-x64](https://www.npmjs.com/package/@img/sharp-libvips-darwin-x64/v/1.3.4) | 1.3.4 | LGPL-3.0-or-later | optional platform/runtime |
| [@img/sharp-libvips-linux-arm](https://www.npmjs.com/package/@img/sharp-libvips-linux-arm/v/1.3.4) | 1.3.4 | LGPL-3.0-or-later | optional platform/runtime |
| [@img/sharp-libvips-linux-arm64](https://www.npmjs.com/package/@img/sharp-libvips-linux-arm64/v/1.3.4) | 1.3.4 | LGPL-3.0-or-later | optional platform/runtime |
| [@img/sharp-libvips-linux-ppc64](https://www.npmjs.com/package/@img/sharp-libvips-linux-ppc64/v/1.3.4) | 1.3.4 | LGPL-3.0-or-later | optional platform/runtime |
| [@img/sharp-libvips-linux-riscv64](https://www.npmjs.com/package/@img/sharp-libvips-linux-riscv64/v/1.3.4) | 1.3.4 | LGPL-3.0-or-later | optional platform/runtime |
| [@img/sharp-libvips-linux-s390x](https://www.npmjs.com/package/@img/sharp-libvips-linux-s390x/v/1.3.4) | 1.3.4 | LGPL-3.0-or-later | optional platform/runtime |
| [@img/sharp-libvips-linux-x64](https://www.npmjs.com/package/@img/sharp-libvips-linux-x64/v/1.3.4) | 1.3.4 | LGPL-3.0-or-later | optional platform/runtime |
| [@img/sharp-libvips-linuxmusl-arm64](https://www.npmjs.com/package/@img/sharp-libvips-linuxmusl-arm64/v/1.3.4) | 1.3.4 | LGPL-3.0-or-later | optional platform/runtime |
| [@img/sharp-libvips-linuxmusl-x64](https://www.npmjs.com/package/@img/sharp-libvips-linuxmusl-x64/v/1.3.4) | 1.3.4 | LGPL-3.0-or-later | optional platform/runtime |
| [@img/sharp-linux-arm](https://www.npmjs.com/package/@img/sharp-linux-arm/v/0.35.5) | 0.35.5 | Apache-2.0 | optional platform/runtime |
| [@img/sharp-linux-arm64](https://www.npmjs.com/package/@img/sharp-linux-arm64/v/0.35.5) | 0.35.5 | Apache-2.0 | optional platform/runtime |
| [@img/sharp-linux-ppc64](https://www.npmjs.com/package/@img/sharp-linux-ppc64/v/0.35.5) | 0.35.5 | Apache-2.0 | optional platform/runtime |
| [@img/sharp-linux-riscv64](https://www.npmjs.com/package/@img/sharp-linux-riscv64/v/0.35.5) | 0.35.5 | Apache-2.0 | optional platform/runtime |
| [@img/sharp-linux-s390x](https://www.npmjs.com/package/@img/sharp-linux-s390x/v/0.35.5) | 0.35.5 | Apache-2.0 | optional platform/runtime |
| [@img/sharp-linux-x64](https://www.npmjs.com/package/@img/sharp-linux-x64/v/0.35.5) | 0.35.5 | Apache-2.0 | optional platform/runtime |
| [@img/sharp-linuxmusl-arm64](https://www.npmjs.com/package/@img/sharp-linuxmusl-arm64/v/0.35.5) | 0.35.5 | Apache-2.0 | optional platform/runtime |
| [@img/sharp-linuxmusl-x64](https://www.npmjs.com/package/@img/sharp-linuxmusl-x64/v/0.35.5) | 0.35.5 | Apache-2.0 | optional platform/runtime |
| [@img/sharp-wasm32](https://www.npmjs.com/package/@img/sharp-wasm32/v/0.35.5) | 0.35.5 | Apache-2.0 AND LGPL-3.0-or-later AND MIT | optional platform/runtime |
| [@img/sharp-webcontainers-wasm32](https://www.npmjs.com/package/@img/sharp-webcontainers-wasm32/v/0.35.5) | 0.35.5 | Apache-2.0 | optional platform/runtime |
| [@img/sharp-win32-arm64](https://www.npmjs.com/package/@img/sharp-win32-arm64/v/0.35.5) | 0.35.5 | Apache-2.0 AND LGPL-3.0-or-later | optional platform/runtime |
| [@img/sharp-win32-ia32](https://www.npmjs.com/package/@img/sharp-win32-ia32/v/0.35.5) | 0.35.5 | Apache-2.0 AND LGPL-3.0-or-later | optional platform/runtime |
| [@img/sharp-win32-x64](https://www.npmjs.com/package/@img/sharp-win32-x64/v/0.35.5) | 0.35.5 | Apache-2.0 AND LGPL-3.0-or-later | optional platform/runtime |
| [@next/env](https://www.npmjs.com/package/@next/env/v/16.4.2) | 16.4.2 | MIT | runtime/build |
| [@next/swc-darwin-arm64](https://www.npmjs.com/package/@next/swc-darwin-arm64/v/16.4.2) | 16.4.2 | MIT | optional platform/runtime |
| [@next/swc-darwin-x64](https://www.npmjs.com/package/@next/swc-darwin-x64/v/16.4.2) | 16.4.2 | MIT | optional platform/runtime |
| [@next/swc-linux-arm64-gnu](https://www.npmjs.com/package/@next/swc-linux-arm64-gnu/v/16.4.2) | 16.4.2 | MIT | optional platform/runtime |
| [@next/swc-linux-arm64-musl](https://www.npmjs.com/package/@next/swc-linux-arm64-musl/v/16.4.2) | 16.4.2 | MIT | optional platform/runtime |
| [@next/swc-linux-x64-gnu](https://www.npmjs.com/package/@next/swc-linux-x64-gnu/v/16.4.2) | 16.4.2 | MIT | optional platform/runtime |
| [@next/swc-linux-x64-musl](https://www.npmjs.com/package/@next/swc-linux-x64-musl/v/16.4.2) | 16.4.2 | MIT | optional platform/runtime |
| [@next/swc-win32-arm64-msvc](https://www.npmjs.com/package/@next/swc-win32-arm64-msvc/v/16.4.2) | 16.4.2 | MIT | optional platform/runtime |
| [@next/swc-win32-x64-msvc](https://www.npmjs.com/package/@next/swc-win32-x64-msvc/v/16.4.2) | 16.4.2 | MIT | optional platform/runtime |
| [@swc/helpers](https://www.npmjs.com/package/@swc/helpers/v/0.5.23) | 0.5.23 | Apache-2.0 | runtime/build |
| [@types/node](https://www.npmjs.com/package/@types/node/v/22.20.5) | 22.20.5 | MIT | development |
| [@types/react](https://www.npmjs.com/package/@types/react/v/19.3.0) | 19.3.0 | MIT | development |
| [@types/react-dom](https://www.npmjs.com/package/@types/react-dom/v/19.3.0) | 19.3.0 | MIT | development |
| [baseline-browser-mapping](https://www.npmjs.com/package/baseline-browser-mapping/v/2.11.30) | 2.11.30 | Apache-2.0 | runtime/build |
| [caniuse-lite](https://www.npmjs.com/package/caniuse-lite/v/1.0.30001818) | 1.0.30001818 | CC-BY-4.0 | runtime/build |
| [client-only](https://www.npmjs.com/package/client-only/v/0.0.1) | 0.0.1 | MIT | runtime/build |
| [csstype](https://www.npmjs.com/package/csstype/v/3.2.3) | 3.2.3 | MIT | development |
| [detect-libc](https://www.npmjs.com/package/detect-libc/v/2.1.2) | 2.1.2 | Apache-2.0 | optional platform/runtime |
| [nanoid](https://www.npmjs.com/package/nanoid/v/3.3.20) | 3.3.20 | MIT | runtime/build |
| [next](https://www.npmjs.com/package/next/v/16.4.2) | 16.4.2 | MIT | runtime/build |
| [picocolors](https://www.npmjs.com/package/picocolors/v/1.1.1) | 1.1.1 | ISC | runtime/build |
| [postcss](https://www.npmjs.com/package/postcss/v/8.5.23) | 8.5.23 | MIT | runtime/build |
| [react](https://www.npmjs.com/package/react/v/19.3.1) | 19.3.1 | MIT | runtime/build |
| [react-dom](https://www.npmjs.com/package/react-dom/v/19.3.1) | 19.3.1 | MIT | runtime/build |
| [scheduler](https://www.npmjs.com/package/scheduler/v/0.28.0) | 0.28.0 | MIT | runtime/build |
| [semver](https://www.npmjs.com/package/semver/v/7.8.5) | 7.8.5 | ISC | optional platform/runtime |
| [sharp](https://www.npmjs.com/package/sharp/v/0.35.5) | 0.35.5 | Apache-2.0 | optional platform/runtime |
| [source-map-js](https://www.npmjs.com/package/source-map-js/v/1.2.2) | 1.2.2 | BSD-3-Clause | runtime/build |
| [styled-jsx](https://www.npmjs.com/package/styled-jsx/v/5.1.6) | 5.1.6 | MIT | runtime/build |
| [tslib](https://www.npmjs.com/package/tslib/v/2.8.1) | 2.8.1 | 0BSD | runtime/build |
| [typescript](https://www.npmjs.com/package/typescript/v/5.9.3) | 5.9.3 | Apache-2.0 | development |
| [undici-types](https://www.npmjs.com/package/undici-types/v/6.21.0) | 6.21.0 | MIT | development |

Refresh this inventory after dependency changes. The final public source scope removes n8n runtime provisioning and pins the verified PostgreSQL image. The listed application NuGet/npm dependency versions are unchanged.
