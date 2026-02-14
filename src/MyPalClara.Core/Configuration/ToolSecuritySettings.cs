namespace MyPalClara.Core.Configuration;

public sealed class ToolSecuritySettings
{
    public ToolApprovalMode DefaultMode { get; set; } = ToolApprovalMode.Allow;
    public List<string> AllowList { get; set; } = [];
    public List<string> BlockList { get; set; } = [];
    public List<string> ApprovalRequired { get; set; } = [];
    public int MaxExecutionSeconds { get; set; } = 120;
    public bool LogAllCalls { get; set; } = true;
}

public enum ToolApprovalMode { Allow, Block, Approve }
