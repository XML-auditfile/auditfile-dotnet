using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(XmlAuditfile.Auditfile.Benchmarks.ReadBenchmarks).Assembly).Run(args);
