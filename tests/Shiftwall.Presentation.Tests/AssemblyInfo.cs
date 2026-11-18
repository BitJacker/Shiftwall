// Loc.Instance.Culture is process-wide: tests that switch language must not run in parallel.
// One collection for the whole assembly = sequential execution.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]
