# Element factory registration: one `ElementServices` instead of the triple

(From TODO.md "Abstract element pool factory registration". Investigated 2026-11-07. Approach settled the same day:
`ElementServices`, not a registration helper. One phase, built 2026-11-07; awaiting in-game testing. `UiEngineContext`
builds the services itself rather than the bootstrapper; tests use `TestElementServices.For(pool)`, one per pool.)

## Verdict in one paragraph

The TODO asks for a helper that takes only the type-specific extras. A helper alone can't do that. C# can't partially
apply a constructor, so any registration helper still hands the lambda the font service, the pool and the render
service, and the lambda names all three: `(fonts, pool, render) => new Button(fonts, pool, render, sprites)`. That is
no shorter than today's closure, `() => new Button(fonts, pool, render, sprites)`. `ElementFactoryRegistry` already has
exactly that local helper (`Register<TElement>`), and it saves almost nothing. The repetition is in the constructor's
shape: every element takes the same three services as its first three arguments. So the fix is one value,
`ElementServices` (font service, pool, render service), passed as the one framework argument of every element
constructor. Registration becomes `pool.RegisterFactory(() => new Button(services, sprites))` with no helper, every
element's primary constructor and base call lose two parameters, and tests that build an element directly pass one
test value instead of three. The pool still doesn't hold the render service or the fonts, so the 2026-10-23 rule
("the render service isn't handed on through the pool") stands. Only its wording ("a fourth constructor argument") changes.

## How it works today

- `Element(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)`
  (`Cadence/UIEngine/UI/Element.cs:24`) exposes them as `FontService`, `ElementPoolService` and `Render`. Every subclass
  forwards the three to its base, in UIEngine, both Workshops and Presentation, plus a few test-only elements.
- `ElementPoolService.RegisterFactory<TElement>(Func<TElement>)` (`ElementPoolService.cs:26`) keeps one pool per exact
  type. It knows nothing about fonts or rendering.
- Registration sites, each naming the triple per line:
  - `UiEngineContext.RegisterFrameworkElementFactories` (`Cadence/UIEngine/Bootstrap/UiEngineContext.cs:40`): 21 framework controls.
  - `ElementFactoryRegistry.RegisterAll` (`DungeonCrawlerWorld/GameShell/ElementFactoryRegistry.cs`): the game's elements,
    half through the local `Register<TElement>` helper (`:51`) and half spelled out (MapWindow, the item windows,
    AbilityScore/Health windows).
  - `ShellBootstrapper` (`DiagnosticsWindow`, `:349`), `BuilderTabs.cs:46-54`, `SpritesTab.cs:89-91`,
    `DebugTabServices.cs:16-17`, `WorkshopShell.cs:292`.
  - Tests: `TestElementPoolServiceFactory` (a new `UiRenderService()` per element), `ControlTestHarness`,
    `Workshop.Tests/DebugTabHarness`, and about 25 test classes that register or construct elements directly
    (`UiInputControllerTests`, `ItemWindowCoordinatorTests`, `TradeWindowTests`, `InventoryGridContentShopModeTests`, ...).
- Non-elements that take some of the three (`DropdownController(pool, render)`, `MapRenderer(..., render, fonts, ...)`,
  `UiLayerHost`) take only what they use. They don't change.

## Phase 1: `ElementServices`, every element and every registration

This is one phase because changing `Element`'s constructor breaks every subclass at once. Splitting it would leave
a phase that doesn't build.

### `ElementServices` (new, `Cadence/UIEngine/UI/ElementServices.cs`)
- `public sealed class ElementServices(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)`
  with get-only `FontService`, `ElementPoolService` and `RenderService`. It is a class, not a struct: there is one per
  host, and every element holds a reference to it. It is not a record either, because nothing compares two of them.
- XML summary: "The services every element is built with: fonts, the element pool and the render service." Remarks:
  a non-element takes only the services it uses, never this bundle.

### `Element`
- The primary constructor becomes `Element(ElementServices services)`. `FontService`, `ElementPoolService` and `Render`
  stay as properties (read from `services`), so no element body changes. Add `public ElementServices Services`, the value
  an element passes when it constructs something that takes it. Check in this phase whether anything constructs an
  element directly outside a factory; the pool should be the only place that does.
- Every subclass: `(ElementServices services, <extras>) : Base(services, <extras>)`. This is mechanical. A subclass that
  passes its base something other than what it received is a finding to look at, not something to rewrite blindly.

### `UiEngineContext` / `UiEngineBootstrapper`
- `UiEngineBootstrapper` builds `ElementServices` once, after the pool, fonts and render service, and puts it on the
  context (`UiEngineContext.ElementServices`). `RegisterFrameworkElementFactories` uses it:
  `pool.RegisterFactory(() => new Button(services, sprites))`.

### Hosts
- `ElementFactoryRegistry`: delete the local `Register<TElement>` helper and its comment, and the comment over MapWindow
  that explains why MapWindow doesn't use it. Every line becomes `elementPool.RegisterFactory(() => new X(services, extras))`.
- `ShellBootstrapper`, `BuilderTabs`, `SpritesTab`, `DebugTabServices.RegisterElementFactories` (takes `ElementServices`
  in place of `pool, fonts, render`), `WorkshopShell`.

### Tests
- `TestElementPoolServiceFactory` gains `CreateElementServices(FontService?)`: one pool and one `UiRenderService` per
  call, not one render service per element. Check that no test relied on separate render service instances. None
  should, since a fresh `UiRenderService()` has no device.
- `ControlTestHarness`, `DebugTabHarness` and the test classes that register or construct elements switch to one services
  value per test. This is mechanical, with no new tests: the constructor shape is covered by everything that builds an element.

### Docs
- CLAUDE.md, UIEngine bullet: "every element takes it as a constructor argument beside the font service and the pool"
  becomes "every element takes `ElementServices` (fonts, pool, render service) as its first constructor argument".
- IMPLEMENTATION-NOTES: a short note beside "The render state is its own service" (2026-10-23) that records the bundle,
  why a registration helper couldn't do it, and that the pool still holds neither fonts nor rendering.
- TODO.md: remove "Abstract element pool factory registration".

### Phase 1 checks
- `dotnet build DungeonCrawlerWorld.sln` passes with no warnings beyond today's. `dotnet test Tests/Tests.csproj`,
  `dotnet test Workshop.Tests/Workshop.Tests.csproj` and `dotnet test Analyzers.Tests/Analyzers.Tests.csproj` pass.
- Grep: no `FontService \w+, ElementPoolService` constructor parameter pair remains under `Cadence/`,
  `DungeonCrawlerWorld/` or the test projects, except on non-elements.
- Run the game: open and close the inventory (I), a container's loot window, a shop and its trade, Item Details on hover,
  the Health and Ability Score windows, F3 Diagnostics, and right-click a map tile with Admin Mode on (F12). Expect each
  to look and behave as before. Then reopen each once more to check that pooled reuse still works.
- Run the Workshop: Items, Actions, Auras, Map (preview an area), Sprites and Debug tabs open and draw as before.

## Decisions

1. **Settled (2026-11-07): `ElementServices`.** Promoting `ElementFactoryRegistry`'s local helper into UIEngine was
   rejected because it shortens nothing (see the verdict). Don't re-propose a registration helper.

2. **Settled: the name is `ElementServices`.**
3. **Settled: `Element.Services` exposes the bundle.**
