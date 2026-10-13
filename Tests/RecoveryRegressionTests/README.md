Run the synthetic squash regressions with:

```sh
dotnet run --project Tests/RecoveryRegressionTests/RecoveryRegressionTests.csproj
```

The checks cover row-presence voting, rotating contents at identical addresses,
optional handling of nonzero subcodes, virtual-address collisions, parity noise,
partial receptions, header control flags, noisy outliers and cancellation. They do
not require an external test package or a captured broadcast.

Character-set checks also cover Polish mappings, ESC switching and row reset,
manual overrides, X/28 and M/29 precedence, Hamming errors, mosaics and squash
round trips with self-contained character-set designations.

Drop-detection checks cover single and repeated T42 addresses, unique subcodes,
padding, stream alignment, short reads, misleading extensions and VBI routing.

To also run the Eurotext capture regressions (three contents on page 100,
unchanged stable page 132), pass its local path as the first argument:

```sh
dotnet run --project Tests/RecoveryRegressionTests/RecoveryRegressionTests.csproj -- /path/to/eurotext.t42
```
