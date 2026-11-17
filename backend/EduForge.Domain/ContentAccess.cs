namespace EduForge.Domain;
public sealed class ContentAccess
{
    public Guid Id {get;set;} = Guid.NewGuid();
    public Guid TeacherId {get;set;}
    public Guid SourceId {get;set;}
    public string Kind {get;set;} = "material";
    public string Visibility {get;set;} = "Private";
    public string? Token {get;set;}
    public string Snapshot {get;set;} = "";
    public Guid Version {get;set;} = Guid.NewGuid();
}
