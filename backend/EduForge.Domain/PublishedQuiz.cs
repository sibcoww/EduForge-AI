namespace EduForge.Domain;
public sealed class PublishedQuiz
{
 public Guid Id {get;set;} public Guid TeacherId {get;set;} public Guid? MaterialId {get;set;}
 public string Title {get;set;}=""; public string AuthorName {get;set;}="";
 public string Subject {get;set;}=""; public string Language {get;set;}=""; public string Level {get;set;}="";
 public string Snapshot {get;set;}=""; public string SearchText {get;set;}="";
 public long PublishedAt {get;set;} public Guid Revision {get;set;}=Guid.NewGuid();
}
