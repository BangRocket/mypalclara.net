namespace MyPalClara.Modules.Proactive.Notes;

public static class NoteTypes
{
    public const string Observation = "observation";
    public const string Question = "question";
    public const string FollowUp = "follow_up";
    public const string Connection = "connection";
}

public enum NoteValidation
{
    Relevant,
    Resolved,
    Stale,
    Contradicted
}
