
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;

// https://www.whexy.com/posts/cgroup-inside-containers

const string hostName = "spire-server";
var network = new NetworkBuilder()
    .WithName(Guid.NewGuid().ToString("D"))
    .Build();
var volume = new VolumeBuilder()
    .WithName(Guid.NewGuid().ToString("D"))
    .Build();

var server = new ContainerBuilder()
    .WithName(Guid.NewGuid().ToString("D"))
    .WithImage("ghcr.io/spiffe/spire-server:1.10.0")
    .WithPortBinding(8081, 8081)
    .WithBindMount("/Users/avurhanau/Projects/testcontainers-spire-dotnet/src/Testcontainers.Spire/conf/server", "/etc/spire/server")
    .WithCommand("-config", "/etc/spire/server/server.conf")
    .WithNetwork(network)
    .WithNetworkAliases(hostName)
    .Build();
await network.CreateAsync();
await server.StartAsync();

var pol = await server.ExecAsync([
    "/opt/spire/bin/spire-server", "entry", "create",
    "-parentID", "spiffe://example.org/myagent",
    "-spiffeID", "spiffe://example.org/myservice",
    "-selector", "docker:image_id:ghcr.io/spiffe/spire-agent:1.10.0"
]);
Console.WriteLine(pol.Stdout);

var jt = await server.ExecAsync([
    "/opt/spire/bin/spire-server", "token", "generate", 
    "-spiffeID", "spiffe://example.org/myagent"
]);
Console.WriteLine(jt.Stdout);

var agent = new ContainerBuilder()
    .WithName(Guid.NewGuid().ToString("D"))
    .WithImage("ghcr.io/spiffe/spire-agent:1.10.0")
    .WithPortBinding(8080, 8080)
    .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
    .WithVolumeMount(volume, "/tmp/spire/agent/public")
    .WithBindMount("/Users/avurhanau/Projects/testcontainers-spire-dotnet/src/Testcontainers.Spire/conf/agent", "/etc/spire/agent")
    .WithPrivileged(true)
    .WithCreateParameterModifier(parameterModifier =>
    {
        parameterModifier.HostConfig.PidMode = "host";
        parameterModifier.HostConfig.CgroupnsMode = "host";
    })
    .WithCommand(
        "-config", "/etc/spire/agent/agent.conf",
        "-serverAddress", "spire-server",
        "-joinToken", jt.Stdout["Token: ".Length..].Trim()
    )
    .WithNetwork(network)
    .Build();
await volume.CreateAsync();
await agent.StartAsync();

Thread.Sleep(5000);
using IOutputConsumer outputConsumer = Consume.RedirectStdoutAndStderrToConsole();

var workload = new ContainerBuilder()
    .WithName(Guid.NewGuid().ToString("D"))
    .WithImage("ghcr.io/spiffe/spire-agent:1.10.0")
    .WithLabel("com.example.service", "myservice")
    .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
    .WithVolumeMount(volume, "/tmp/spire/agent/public")
    .WithBindMount("/Users/avurhanau/Projects/testcontainers-spire-dotnet/src/Testcontainers.Spire/conf/agent", "/etc/spire/agent")
    .WithPrivileged(true)
    .WithCreateParameterModifier(parameterModifier =>
    {
        parameterModifier.HostConfig.PidMode = "host";
        parameterModifier.HostConfig.CgroupnsMode = "host";
    })
    .WithEntrypoint(
        "/opt/spire/bin/spire-agent", "api", "fetch",
        "-socketPath", "/tmp/spire/agent/public/api.sock"
    )
    .WithOutputConsumer(outputConsumer)
    .Build();
await workload.StartAsync();
Thread.Sleep(5000);
