# Third-Party Notices

Gwent Access is licensed under the MIT License (see `LICENSE`). Parts of it are derived from the projects below,
each under its own license. Those licenses govern only the corresponding code.

---

## WH40KRTAccess / RTAccess

The graph navigation kernel in `src/Access.Core/Graph/` and its tests in `tests/Access.Core.Tests/` are copied from
RTAccess, the *Warhammer 40,000: Rogue Trader* screen-reader mod, which ported them from WrathAccess (below). The
dev server in `src/Dev/` is ported from it as well.

- Copyright © 2026 Alexander Epaneshnikov.
- License: **MIT License** (the same terms as this project's `LICENSE`).

---

## Prism

Native screen-reader / TTS abstraction library (`prism.dll`), used for speech output. Shipped in the player zip
next to `GwentAccess.dll`.

- Copyright © the Prism authors.
- Source: https://github.com/ethindp/prism
- License: **Mozilla Public License 2.0 (MPL-2.0)**: https://mozilla.org/MPL/2.0/

`prism.dll` is compiled from unmodified upstream sources, vendored as the git submodule `third_party/prism` and
pinned to an exact commit. That pin, plus `scripts/build-prism.ps1`, is the complete recipe for reproducing the
binary we ship. Under the MPL-2.0 the corresponding source is also available from the repository linked above.

Prism statically links the following into `prism.dll` on Windows:

- **simdutf**: Apache-2.0
- **{fmt}**: MIT
- **Highway**: Apache-2.0 / BSD-3-Clause (dual)
- **concurrentqueue**: BSD-2-Clause
- **dr_wav** (dr_libs): public domain
- **Moderncom**: MIT
- **NVDA Controller Client** RPC definitions (and the stubs MIDL generates from them): originally LGPL-2.1,
  relicensed to MPL-2.0 by the Prism project with permission
- **SAPI bridge** and the `range_convert` helpers: credited to the [NVGT](https://github.com/samtupy/nvgt) project

Full license texts for Prism and its bundled components are in the `LICENSES/` directory of the Prism repository
(`third_party/prism/LICENSES/`).

Gwent Access no longer ships NVDA's `nvdaControllerClient.dll`: Prism generates the NVDA RPC client stubs at build
time from `idl/nvdaController.idl` and links `rpcrt4`, so it reaches a running NVDA directly.

---

## WrathAccess (ported code and design)

The graph navigation kernel (`src/Access.Core/Graph/`: key graph, builder, path announcer, control identity)
reached this project from **WrathAccess**, the screen-reader mod for *Pathfinder: Wrath of the Righteous*, by way of
RTAccess. The host/module split that makes hot reload possible (`src/Host/`, `module/GwentAccess.Module.csproj`) is
ported from WrathAccess's `ModuleLoader` design. Ported files say so in their header comments.

**Audio.** The sound mixer and spatializer (`src/Shared/Audio/AudioMixer.cs`, `Spatializer.cs`) are ported, trimmed,
from WrathAccess's NAudio engine by way of RTAccess, and **every bundled sound** under `assets/audio/` (object stems
and cue earcons) is a WrathAccess recording, taken from RTAccess's copies unchanged.

- Copyright © 2026 Bradley Renshaw.
- Source: https://github.com/bradjrenshaw/wotr-access
- License: **zlib License.**

```
zlib License

Copyright (c) 2026 Bradley Renshaw

This software is provided 'as-is', without any express or implied
warranty. In no event will the authors be held liable for any damages
arising from the use of this software.

Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not
   claim that you wrote the original software. If you use this software
   in a product, an acknowledgment in the product documentation would be
   appreciated but is not required.

2. Altered source versions must be plainly marked as such, and must not be
   misrepresented as being the original software.

3. This notice may not be removed or altered from any source distribution.
```

---

## NAudio 1.10.0

Managed audio playback and mixing library (`NAudio.dll`, deployed beside the host) behind the mod's own sounds
(`src/Shared/Audio/`).

- Copyright © Mark Heath and contributors.
- Source: https://github.com/naudio/NAudio
- License: **Microsoft Public License (Ms-PL).** NAudio relicensed to MIT starting at version 2.0; the version
  bundled here (1.10.0, the one RTAccess ships) predates that and is Ms-PL.

```
Microsoft Public License (Ms-PL)

This license governs use of the accompanying software. If you use the software,
you accept this license. If you do not accept the license, do not use the software.

1. Definitions

The terms "reproduce," "reproduction," "derivative works," and "distribution" have
the same meaning here as under U.S. copyright law.

A "contribution" is the original software, or any additions or changes to the software.

A "contributor" is any person that distributes its contribution under this license.

"Licensed patents" are a contributor's patent claims that read directly on its
contribution.

2. Grant of Rights

(A) Copyright Grant- Subject to the terms of this license, including the license
conditions and limitations in section 3, each contributor grants you a non-exclusive,
worldwide, royalty-free copyright license to reproduce its contribution, prepare
derivative works of its contribution, and distribute its contribution or any
derivative works that you create.

(B) Patent Grant- Subject to the terms of this license, including the license
conditions and limitations in section 3, each contributor grants you a non-exclusive,
worldwide, royalty-free license under its licensed patents to make, have made, use,
sell, offer for sale, import, and/or otherwise dispose of its contribution in the
software or derivative works of the contribution in the software.

3. Conditions and Limitations

(A) No Trademark License- This license does not grant you rights to use any
contributors' name, logo, or trademarks.

(B) If you bring a patent claim against any contributor over patents that you claim
are infringed by the software, your patent license from such contributor to the
software ends automatically.

(C) If you distribute any portion of the software, you must retain all copyright,
patent, trademark, and attribution notices that are present in the software.

(D) If you distribute any portion of the software in source code form, you may do so
only under this license by including a complete copy of this license with your
distribution. If you distribute any portion of the software in compiled or object
code form, you may only do so under a license that complies with this license.

(E) The software is licensed "as-is." You bear the risk of using it. The contributors
give no express warranties, guarantees or conditions. You may have additional consumer
rights under your local laws which this license cannot change. To the extent permitted
under your local laws, the contributors exclude the implied warranties of
merchantability, fitness for a particular purpose and non-infringement.
```

---

## Tanglebeep (via WrathAccess)

The key graph, the two-tier control identity and the row-based menu builder were ported into WrathAccess, with the
author's permission, from **Tanglebeep**, a roguelike accessibility project, which in turn derives them from
Factorio Access (below).

- Copyright © Austin Hicks.
- Source: https://github.com/ahicks92/tanglebeep

---

## Factorio Access (via Tanglebeep and WrathAccess)

The key-graph navigation model (`key-graph.lua`) and the row-based menu layout (`menu.lua`) that the graph kernel
descends from come from **Factorio Access**, the *Factorio* accessibility mod.

- Copyright © 2024 Crimso.
- Source: https://github.com/Factorio-Access/FactorioAccess
- License: **MIT License.**

```
MIT License

Copyright (c) 2024 Crimso

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
