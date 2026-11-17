namespace EduForge.Domain;
public sealed class MediaAsset { public Guid Id {get;set;}=Guid.NewGuid(); public Guid TeacherId {get;set;} public string ContentType {get;set;}=""; public byte[] Data {get;set;}=[]; }
public sealed class GroupInvitation { public Guid Id {get;set;} public string Token {get;set;}=""; public bool Enabled {get;set;}=true; }
public sealed class EnrollmentRequest { public Guid Id {get;set;}=Guid.NewGuid(); public Guid GroupId {get;set;} public string FirstName {get;set;}=""; public string LastName {get;set;}=""; public string NormalizedName {get;set;}=""; public string Status {get;set;}="Pending"; }

public sealed class AssignmentAsset { public Guid Id {get;set;} public Guid AssignmentId {get;set;} public string Name {get;set;}=""; }
