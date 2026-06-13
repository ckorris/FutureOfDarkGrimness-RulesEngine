# TempVisuals — RETIRED (work item 071, the presentation beat stream — numbered 052 at retirement time)

This folder is **excluded from compilation** (see `<Compile Remove="TempVisuals/**/*.cs" />`
in `FutureOfDarkGrimness.csproj`). The files are kept on disk for reference, not deleted.

## Why

`TempVisual` was an early, low-level, host-authoritative *imperative* visual primitive system
(`ITempVisualDrawer.AddVisual/UpdateTransform/UpdateColor/Remove`, replicated via the
`AddTempVisualMessage` family). Its one real use was an uncalled debug probe
(`ChooseMapSideStage.TempTestVisuals`) proving a stage could push visuals from outside a resolver.

It is superseded by the **presentation-beat stream** (`FDG.Presentation`): the engine emits paced,
semantic beats via `context.Presenter.Present(beat)`, replicated host→client as `PresentBeatMessage`,
and each front-end renders them however it likes. That carries the "show something happening outside
a resolver" need (e.g. surfacing deployment-zone options while the opponent chooses) far better — as a
typed beat rather than mesh/material transforms chosen by the engine.

## Re-enabling / replacing

The "non-resolver activity" use case (e.g. previewing deployment-zone options during the opponent's
choice) should come back as a **new presentation beat**, not by reviving TempVisual. If you do want
TempVisual back, restore it by deleting the `Compile Remove` line above and re-adding the
`ITempVisualDrawer` property/parameter to `IGameContext` / `IFDGGame` / `IPlayerController` and their
implementations (see git history around work item 071 — committed under its pre-renumbering id, 052).
