namespace Clara.Core.Config;

public class SandboxOptions
{
    public string Mode { get; set; } = "auto";
    public DockerSandboxOptions Docker { get; set; } = new();
}

public class DockerSandboxOptions
{
    public string Image { get; set; } = "python:3.12-slim";
    public int TimeoutSeconds { get; set; } = 900;
    public string Memory { get; set; } = "512m";
    public double Cpu { get; set; } = 1.0;
}
