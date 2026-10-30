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

## WrathAccess (ported code and design)

The graph navigation kernel (`src/Access.Core/Graph/`: key graph, builder, path announcer, control identity)
reached this project from **WrathAccess**, the screen-reader mod for *Pathfinder: Wrath of the Righteous*, by way of
RTAccess. The host/module split that makes hot reload possible (`src/Host/`, `module/GwentAccess.Module.csproj`) is
ported from WrathAccess's `ModuleLoader` design. Ported files say so in their header comments.

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
