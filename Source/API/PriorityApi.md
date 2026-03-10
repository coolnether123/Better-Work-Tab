# Better Work Tab Priority API

This API exists so other mods can read Better Work Tab's extended priority settings without hardcoding the vanilla max priority of `4`.

## Goals

- No hard dependency required
- Stable reflection target
- Small read-only surface
- Versioned for future extension

## Reflection Contract

- Assembly name: `Better Work Tab`
- Type name: `Better_Work_Tab.API.PriorityApi`
- API version field: `ApiVersion`

## Public Methods

- `GetMaxPriority()`
- `GetDefaultEnabledPriority()`
- `MapPriorityToVanillaDisplay(int priority)`
- `IsDisabledPriority(int priority)`
- `GetSnapshot()`
- `TryGetSnapshot(out PriorityApiSnapshot snapshot)`

## Recommended Integration

If you do not want a compile-time reference to Better Work Tab:

1. Find the loaded assembly named `Better Work Tab`
2. Resolve `Better_Work_Tab.API.PriorityApi`
3. Prefer calling `GetSnapshot()` or `TryGetSnapshot(...)`

`GetSnapshot()` returns a versioned struct with:

- `ApiVersion`
- `MaxPriority`
- `DefaultEnabledPriority`

## Notes

- `0` means disabled
- Higher numbers are lower priority
- `MapPriorityToVanillaDisplay(...)` intentionally compresses extended priorities back into vanilla `1..4` display buckets for compatibility-oriented UI
