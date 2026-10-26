using FantasyWorld.Broadcast;

return await BroadcastApp.RunAsync(args, Console.Out, () => DateTimeOffset.UtcNow, Environment.GetEnvironmentVariable);
