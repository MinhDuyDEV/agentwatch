namespace AgentWatch.App;

// Piagent conversations in WSL that wait on the member, or whose turn ended
// (Piagent 1.34+, AgentWatch.Core/PiagentStatus): read every few seconds from
// \\wsl.localhost\<distro><home>\.pi\agent\status while that distribution
// runs; a stopped one is never started for this.
static class PiagentStatusWatch
{
    static readonly TimeSpan Every = TimeSpan.FromSeconds(3), RunningCheck = TimeSpan.FromSeconds(30);

    public static async Task RunAsync(Action<PiagentAlert> notify)
    {
        var tracker = new PiagentStatusTracker();
        string? home = null, homeOf = null;
        var running = false;
        var checkedAt = DateTimeOffset.MinValue;
        while (true)
        {
            try
            {
                if (PiagentWslBinding.Remembered() is { Length: > 0 } distro)
                {
                    if (DateTimeOffset.Now - checkedAt > RunningCheck)
                    {
                        running = await PiagentBindingWatch.RunningDistributionsAsync() is { } list && list.Contains(distro, StringComparer.OrdinalIgnoreCase);
                        checkedAt = DateTimeOffset.Now;
                    }
                    if (running)
                    {
                        if (home is null || homeOf != distro)
                        {
                            home = (await new WslExe().RunAsync(distro, null, "sh", "-c", "printf %s \"$HOME\"")).Trim();
                            homeOf = distro;
                        }
                        if (home.StartsWith('/'))
                        {
                            var agentDir = $@"\\wsl.localhost\{distro}{home.Replace('/', '\\')}\.pi\agent";
                            foreach (var alert in tracker.Update(PiagentStatusReader.Read(agentDir))) notify(alert);
                        }
                    }
                }
            }
            // WSL not answering, or a file share hiccup: read again later.
            catch (Exception) { }
            await Task.Delay(Every);
        }
    }
}
