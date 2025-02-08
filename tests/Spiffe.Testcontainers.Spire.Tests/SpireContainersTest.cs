﻿using System;
using System.IO;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using HandlebarsDotNet;

namespace Spiffe.Testcontainers.Spire.Tests;

public class SpireContainersTest
{
    [Fact]
    public void RenderConfigTemplate()
    {
        var template = Handlebars.Compile(Defaults.ServerConfigTemplate);
        var model = new
        {
            TrustDomain = "example.com",
            LogLevel = "DEBUG",
            CaBundlePath = Defaults.ServerAgentCertPath,
            KeyFilePath = Defaults.ServerKeyPath,
            CertFilePath = Defaults.ServerCertPath,
            Federation = new dynamic[]
            {
                new { TrustDomain = "example1.org", Host = "spire-server1" },
                new { TrustDomain = "example2.org", Host = "spire-server2" },
            },
        };
        var result = template(model);
        Assert.NotEmpty(result);
        Assert.Contains("example.com", result);
        Assert.Contains("DEBUG", result);
        Assert.Contains(Defaults.ServerAgentCertPath, result);
        Assert.Contains(Defaults.ServerKeyPath, result);
        Assert.Contains(Defaults.ServerCertPath, result);
        Assert.Contains("example1.org", result);
        Assert.Contains("spire-server1", result);
        Assert.Contains("example2.org", result);
        Assert.Contains("spire-server2", result);
    }

    [Fact(Timeout = 60_000)]
    public async Task StartTest()
    {
        var td = "example.com";

        await using var net = new NetworkBuilder().WithName(td + "-" + Guid.NewGuid().ToString("D")).Build();
        await using var vol = new VolumeBuilder().WithName(td + "-" + Guid.NewGuid().ToString("D")).Build();
        var cout = Consume.RedirectStdoutAndStderrToConsole();

        var s = new SpireServerBuilder().WithNetwork(net).WithOutputConsumer(cout).Build();
        await s.StartAsync();

        var a = new SpireAgentBuilder().WithNetwork(net).WithAgentVolume(vol).WithOutputConsumer(cout).Build();
        await a.StartAsync();

        await s.ExecAsync([
            "/opt/spire/bin/spire-server", "entry", "create",
            "-parentID", $"spiffe://{td}/spire/agent/x509pop/cn/agent.example.com",
            "-spiffeID", $"spiffe://{td}/workload",
            "-selector", "docker:label:com.example:workload"
        ]);

        string expr = @$"msg=""SVID updated"" entry=[\w-]+ spiffe_id=""spiffe://{td}/workload"" subsystem_name=cache_manager$";
        await a.AssertLogAsync(expr, 10);
        
        var w = new ContainerBuilder()
                        .WithImage(Defaults.AgentImage)
                        .WithNetwork(net)
                        .WithVolumeMount(vol, "/tmp/spire/agent/public")
                        .WithLabel("com.example", "workload")
                        .WithPrivileged(true)
                        .WithCreateParameterModifier(parameterModifier =>
                        {
                            parameterModifier.HostConfig.PidMode = "host";
                            parameterModifier.HostConfig.CgroupnsMode = "host";
                        })
                        .WithEntrypoint(
                            "/opt/spire/bin/spire-agent", "api", "fetch"
                        )
                        .WithCommand(
                            "-socketPath", "/tmp/spire/agent/public/api.sock"
                        )
                        .WithOutputConsumer(cout)
                        .Build();
        await w.StartAsync();

        await w.AssertLogAsync("Received 1 svid after", 10);
    }
}
