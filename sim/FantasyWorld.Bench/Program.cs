using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(FantasyWorld.Bench.WorldYearBenchmark).Assembly).Run(args);
