FROM ghcr.io/spiffe/spire-agent:1.10.0
ENTRYPOINT [ "/opt/spire/bin/spire-agent", "api", "fetch", "-socketPath", "/tmp/spire/agent/public/api.sock" ]