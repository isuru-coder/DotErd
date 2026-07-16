namespace D400.DotErd.Application;

public static class DotErdCommands
{
    public const string Init = "init";
    public const string ListContexts = "list-contexts";
    public const string Inspect = "inspect";
    public const string Generate = "generate";
    public const string Diff = "diff";
    public const string Verify = "verify";
}

public enum DotErdExitCode
{
    Success = 0,
    VerificationFailed = 1,
    InvalidArguments = 2,
    NotImplemented = 3,
    Error = 4
}
