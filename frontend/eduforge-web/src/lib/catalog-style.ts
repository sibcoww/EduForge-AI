// A subject keeps its visual spine when cards are filtered or reordered.
export function subjectTone(subject:string):"violet"|"sage"|"sand" {
 const key=subject.trim().toLocaleLowerCase("ru-RU");
 let hash=0;for(const c of key)hash=(hash*31+c.charCodeAt(0))>>>0;
 return (["violet","sage","sand"] as const)[hash%3];
}
