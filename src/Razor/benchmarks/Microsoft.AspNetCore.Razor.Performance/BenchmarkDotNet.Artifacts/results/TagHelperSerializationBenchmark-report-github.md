``` ini

BenchmarkDotNet=v0.10.13, OS=Windows 10.0.19041
Intel Core i7-8700 CPU 3.20GHz (Coffee Lake), 1 CPU, 12 logical cores and 6 physical cores
.NET Core SDK=5.0.100-preview.6.20310.4
  [Host] : .NET Core 5.0.0-preview.6.20305.6 (CoreCLR 5.0.20.30506, CoreFX 5.0.20.30506), 64bit RyuJIT

Toolchain=InProcessToolchain  RunStrategy=Throughput  

```
|                                    Method |     Mean |     Error |    StdDev |  Op/s |   Gen 0 |   Gen 1 |   Gen 2 | Allocated |
|------------------------------------------ |---------:|----------:|----------:|------:|--------:|--------:|--------:|----------:|
| &#39;Razor TagHelper Roundtrip Serialization&#39; | 5.201 ms | 0.1029 ms | 0.1101 ms | 192.3 | 62.5000 | 54.6875 | 31.2500 |   3.87 MB |
|           &#39;Razor TagHelper Serialization&#39; | 1.884 ms | 0.0376 ms | 0.0658 ms | 530.7 | 35.1563 | 35.1563 | 35.1563 |   1.11 MB |
|         &#39;Razor TagHelper Deserialization&#39; | 4.684 ms | 0.0933 ms | 0.1396 ms | 213.5 | 39.0625 | 15.6250 |       - |    3.3 MB |
